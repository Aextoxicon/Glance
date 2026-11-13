import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';

import 'file_tree.dart';
import 'settings_store.dart';
import 'generated/flutter_api.dart';
import 'generated/lib.dart' as rust;
import 'models.dart';
import 'trace.dart';
import 'trace_destination.dart';
import 'utils.dart';
import 'views/cad_view.dart';

enum AppThemeMode { light, dark, system }

class ThemeListenable extends ChangeNotifier {
  void changed() => notifyListeners();
}

class MainViewModel extends ChangeNotifier {
  MainViewModel([FileTreeSource? source, SettingsStore? settings])
      : _source = source ?? createSource(),
        settingsStore = settings ?? SettingsStore();

  final FileTreeSource _source;

  AppThemeMode themeMode = AppThemeMode.system;
  final ThemeListenable themeListenable = ThemeListenable();
  int codeFontSize = 13;
  static const List<int> codeFontSizes = [11, 12, 13, 14, 15, 16, 18, 20];

  bool rememberLastDir = true;
  final SettingsStore settingsStore;

  // 启动时在runApp前调用, 避免首帧闪默认主题
  Future<void> loadSettings() async {
    final s = await settingsStore.load();
    for (final m in AppThemeMode.values) {
      if (m.name == s.themeMode) themeMode = m;
    }
    if (codeFontSizes.contains(s.codeFontSize)) codeFontSize = s.codeFontSize;
    rememberLastDir = s.rememberLastDir;
    themeListenable.changed();
  }

  Future<void> _persistSettings() {
    return settingsStore.save(AppSettings(
      themeMode: themeMode.name,
      codeFontSize: codeFontSize,
      rememberLastDir: rememberLastDir,
    ));
  }

  bool hasWorkspace = false;
  String currentFolderName = '';
  String totalSizeReadable = '';
  bool isComputingSize = false;

  List<TreeItem> treeItems = [];
  TreeItem? selectedItem;
  Artifact? selectedArtifact;
  String? selectedContent;
  List<OutlineNode>? currentOutline;
  rust.CodeParseResult? codeParseResult;
  rust.CadGeometry? selectedCad;
  bool get isCadPreview => selectedCad != null;
  String? messageText;
  String? previewNotice;

  bool _benchmarkMode = false;
  bool get benchmarkMode => _benchmarkMode;
  set benchmarkMode(bool v) {
    if (_benchmarkMode == v) return;
    _benchmarkMode = v;
    Trace.enabled = v;
    messageText = v
        ? '诊断基准模式：开启（打开文件夹将自动展开全部+逐文件计时，跑完自动导出日志）'
        : '诊断基准模式：关闭';
    notifyListeners();
  }

  void toggleBenchmarkMode() => benchmarkMode = !benchmarkMode;

  void stopBenchmark() {
    _benchCancel = true;
    benchmarkMode = false;
    messageText = '诊断基准模式：已取消';
    notifyListeners();
  }

  bool _benchCancel = false;
  // 测试可注入导出目录, 避免path_provider在纯VM测试环境无平台实现
  String? traceDirOverride;

  // 诊断滚动标志: benchmark序列要求代码区匀速滚到底部, 与大纲定位的瞬跳区分
  bool _diagnosticScrollToBottom = false;
  Completer<void>? _diagnosticScrollCompleter;

  bool consumeDiagnosticScrollToBottom() {
    final v = _diagnosticScrollToBottom;
    _diagnosticScrollToBottom = false;
    return v;
  }

  // 置标志并通知UI, 滚动动画完成时complete对应future
  Future<void> requestDiagnosticScrollToBottom() {
    _diagnosticScrollToBottom = true;
    _diagnosticScrollCompleter = Completer<void>();
    notifyListeners();
    return _diagnosticScrollCompleter!.future;
  }

  void notifyDiagnosticScrollDone() {
    _diagnosticScrollCompleter?.complete();
    _diagnosticScrollCompleter = null;
  }

  int? outlineScrollTargetLine;

  bool get hasSelection => selectedArtifact != null;

    String? _rootId;
    int _sizeToken = 0;
    int _loadToken = 0;
    Future<void>? _pendingLoad;

  Future<void> restoreWorkspace() async {
    if (!rememberLastDir) return;
    try {
      final rootId = await _source.restoreRoot();
      if (rootId != null) await _openRoot(rootId);
    } catch (_) {}
  }

  Future<void> pickFolder() async {
    final rootId = await _source.pickRoot();
    if (rootId == null) return;
    await _openRoot(rootId);
  }

  // 单文件模式: 外部VIEW/分享intent传来的content uri直接当根打开, 授权是临时的不持久化
  Future<void> openSingleFileUri(String uriStr) async {
    final seg = Uri.decodeComponent(uriStr.split('/').last);
    final name = seg.split(':').last;
    final item = TreeItem(
      artifact: Artifact(
        id: uriStr,
        name: name,
        path: uriStr,
        isDirectory: false,
        size: 0,
      ),
    );
    treeItems = [item];
    currentFolderName = name;
    hasWorkspace = true;
    messageText = null;
    totalSizeReadable = '';
    notifyListeners();
    selectItem(item);
  }

  Future<void> _openRoot(String rootId) async {
    _rootId = rootId;
    currentFolderName = nameOfId(rootId);
    hasWorkspace = true;
    messageText = null;
    totalSizeReadable = '';
    notifyListeners();
    try {
      treeItems = await _childrenToItems(rootId);
    } catch (e) {
      treeItems = [];
      messageText = '读取目录失败：$e';
    }
    notifyListeners();
    unawaited(_computeTotalSize(rootId));
    if (_benchmarkMode) {
      // 基准序列与目录大小统计严格串行, 避免隔离线程抢占UI干扰计时
      await _computeTotalSize(rootId);
      unawaited(runBenchmarkSequence());
    }
  }

  Future<void> _computeTotalSize(String rootId) async {
    final token = ++_sizeToken;
    isComputingSize = true;
    notifyListeners();
    var total = 0;
    try {
      total = await _source.folderSize(rootId);
    } catch (_) {}
    if (token != _sizeToken) return;
    totalSizeReadable = formatSize(total);
    isComputingSize = false;
    notifyListeners();
  }

  Future<void> toggleExpand(TreeItem item) async {
    if (!item.artifact.isDirectory) return;
    if (item.isExpanded) {
      item.isExpanded = false;
      notifyListeners();
      return;
    }
    if (!item.loaded) {
      item.isExpanded = true;
      item.isLoading = true;
      notifyListeners();
      try {
        item.children = await _childrenToItems(item.artifact.id);
        item.loaded = true;
      } catch (e) {
        messageText = '读取目录失败：$e';
      }
      item.isLoading = false;
      notifyListeners();
      return;
    }
    item.isExpanded = true;
    notifyListeners();
  }

  void selectItem(TreeItem item) {
    for (final it in _flatten(treeItems)) {
      it.isSelected = false;
    }
    item.isSelected = true;
    selectedItem = item;
    if (item.artifact.isDirectory) {
      selectedArtifact = null;
      selectedContent = null;
      currentOutline = null;
      codeParseResult = null;
      selectedCad = null;
      messageText = '已选择目录：${item.artifact.name}';
    } else {
      selectedCad = null;
      selectedArtifact = item.artifact;
      currentOutline = null;
      messageText = null;
      Trace.mark('select.start', 'name=${item.artifact.name}');
      final token = ++_loadToken;
      _pendingLoad = _loadFile(item.artifact, token);
      unawaited(_pendingLoad);
    }
    notifyListeners();
  }

  // 按扩展名分流: CAD文件(dwg/dxf)读字节走Rust几何解析不做二进制嗅探, 其余读文本走代码解析
  Future<void> _loadFile(Artifact a, int token) async {
    try {
      if (isCadFileName(a.name)) {
        final bytes = await _source.readFileBytes(a.id);
        if (token != _loadToken) return;
        final geometry = parseCadGeometryFrb(
          fileBytes: bytes,
          filename: a.name,
        );
        if (token != _loadToken) return;
        selectedCad = geometry;
        codeParseResult = null;
        selectedContent = null;
        currentOutline = null;
        messageText = null;
        Trace.mark('select.painted', 'name=${a.name} cad=true');
        notifyListeners();
        return;
      }
      if (await _source.isBinary(a.id)) {
        if (token != _loadToken) return;
        codeParseResult = null;
        selectedContent = null;
        currentOutline = null;
        selectedCad = null;
        messageText = '二进制文件，无法预览';
        notifyListeners();
        return;
      }
      final content = await _source.readFile(a.id);
      if (token != _loadToken) return;
      if (content.contains('\u0000')) {
        // 前16KB嗅探未命中但全文含NUL(极少见), 仍按二进制处理
        codeParseResult = null;
        selectedContent = null;
        currentOutline = null;
        selectedCad = null;
        messageText = '二进制文件，无法预览';
        notifyListeners();
        return;
      }
      final result = parseCodeFrb(source: content, filename: a.name);
      if (token != _loadToken) return;
      codeParseResult = result;
      selectedContent = content;
      currentOutline = result.outline.map(_toModelNode).toList();
      messageText = null;
      Trace.mark('select.painted', 'name=${a.name}');
    } catch (e) {
      if (token != _loadToken) return;
      codeParseResult = null;
      selectedContent = null;
      currentOutline = null;
      selectedCad = null;
      messageText = '读取/解析失败：$e';
    }
    notifyListeners();
  }

  OutlineNode _toModelNode(rust.OutlineNode n) => OutlineNode(
        kind: n.kind,
        name: n.name,
        detail: n.detail,
        startByte: n.startByte.toInt(),
        endByte: n.endByte.toInt(),
        children: n.children.map(_toModelNode).toList(),
      );

  Future<void> expandAll() async {
    await _expandDirs(treeItems, <String>{});
    notifyListeners();
  }

  // 展开过程不逐目录notifyListeners，大目录树会触发数百次全量重建
  Future<void> _expandDirs(List<TreeItem> items, Set<String> visited) async {
    for (final it in items) {
      if (!it.artifact.isDirectory) continue;
      if (!visited.add(it.artifact.id)) continue;
      try {
        if (!it.loaded) {
          it.children = await _childrenToItems(it.artifact.id);
          it.loaded = true;
        }
        it.isExpanded = true;
      } catch (_) {
        continue;
      }
      await _expandDirs(it.children, visited);
    }
  }

  void collapseAll() {
    for (final it in _flatten(treeItems)) {
      it.isExpanded = false;
    }
    notifyListeners();
  }

  Future<void> refreshTree() async {
    final rootId = _rootId;
    if (rootId == null) return;
    try {
      final oldById = <String, TreeItem>{};
      for (final it in _flatten(treeItems)) {
        oldById[it.artifact.id] = it;
      }
      treeItems = await _refreshLevel(rootId, oldById);
      if (selectedItem != null) {
        selectedItem = _findById(treeItems, selectedItem!.artifact.id) ?? selectedItem;
        selectedArtifact = selectedItem?.artifact;
      }
    } catch (e) {
      messageText = '读取目录失败：$e';
    }
    notifyListeners();
    unawaited(_computeTotalSize(rootId));
  }

  Future<List<TreeItem>> _refreshLevel(String parentId, Map<String, TreeItem> oldById) async {
    final artifacts = await _source.listChildren(parentId);
    final out = <TreeItem>[];
    for (final a in artifacts) {
      final item = TreeItem(artifact: a);
      final old = oldById[a.id];
      if (old != null) {
        item.isExpanded = old.isExpanded;
        item.isSelected = old.isSelected;
        if (old.loaded) {
          item.loaded = true;
          item.children = await _refreshLevel(a.id, oldById);
        }
      }
      out.add(item);
    }
    return out;
  }

  TreeItem? _findById(List<TreeItem> items, String id) {
    for (final it in items) {
      if (it.artifact.id == id) return it;
      final found = _findById(it.children, id);
      if (found != null) return found;
    }
    return null;
  }

  void requestScrollToLine(int line) {
    outlineScrollTargetLine = line;
    notifyListeners();
  }

  void requestScrollToOutline(OutlineNode node) {
    final content = selectedContent;
    if (content == null) return;
    final lines = content.split('\n');
    var acc = 0;
    var target = lines.length - 1;
    for (var i = 0; i < lines.length; i++) {
      if (node.startByte < acc + utf8.encode(lines[i]).length + 1) {
        target = i;
        break;
      }
      acc += utf8.encode(lines[i]).length + 1;
    }
    if (target > 0 && lines[target].isEmpty) target--;
    outlineScrollTargetLine = target;
    notifyListeners();
  }

  // 避免在构建期间触发markNeedsBuild
  void consumeScrollTargetLine() {
    outlineScrollTargetLine = null;
  }

  void selectThemeMode(AppThemeMode mode) {
    themeMode = mode;
    notifyListeners();
    themeListenable.changed();
    unawaited(_persistSettings());
  }

  void selectCodeFontSize(int size) {
    codeFontSize = size;
    notifyListeners();
    unawaited(_persistSettings());
  }

  void selectRememberLastDir(bool value) {
    if (rememberLastDir == value) return;
    rememberLastDir = value;
    notifyListeners();
    // 关闭时丢弃已记住的目录授权, 下次启动不再恢复
    if (!value) unawaited(_source.forgetRoot());
    unawaited(_persistSettings());
  }

  void clearSelection() {
    for (final it in _flatten(treeItems)) {
      it.isSelected = false;
    }
    selectedItem = null;
    selectedArtifact = null;
    selectedContent = null;
    currentOutline = null;
    selectedCad = null;
    messageText = null;
    notifyListeners();
  }

  void closeWorkspace() {
    hasWorkspace = false;
    _rootId = null;
    currentFolderName = '';
    totalSizeReadable = '';
    isComputingSize = false;
    _sizeToken++;
    treeItems = [];
    unawaited(_source.forgetRoot());
    clearSelection();
    notifyListeners();
  }

  // 诊断基准模式: 工作区加载完成后逐步展开目录并逐个打开可高亮文件
  // 逐步而非一次性铺开, 避免整树ExpandAll+全量打开把UI/IO一次性打满
  Future<void> runBenchmarkSequence() async {
    if (!_benchmarkMode) return;
    _benchCancel = false;
    final dir = await _traceDir();
    Trace.start(dir);
    try {
      Trace.mark('bench.start');
      var dirs = 0;
      var files = 0;
      final queue = <TreeItem>[];
      for (final root in treeItems) {
        queue.add(root);
      }
      Future<void> openAndScroll(TreeItem node) async {
        selectItem(node);
        await _pendingLoad;
        // 等新内容那一帧真正绘制完再开始滚动, 保证打开与滚动逐个串行
        // 测试环境无帧调度, 超时兜底直接继续
        await WidgetsBinding.instance.endOfFrame
            .timeout(const Duration(milliseconds: 100), onTimeout: () {});
        Trace.mark('bench.scroll.start', 'name=${node.artifact.name}');
        await requestDiagnosticScrollToBottom();
        Trace.mark('bench.scroll.end', 'name=${node.artifact.name}');
        files++;
      }

      while (queue.isNotEmpty) {
        if (_benchCancel) break;
        final item = queue.removeAt(0);
        if (item.artifact.isDirectory) {
          Trace.mark('bench.expand.start', 'name=${item.artifact.name}');
          await toggleExpand(item);
          Trace.mark('bench.expand.end', 'name=${item.artifact.name}');
          dirs++;
          for (final child in item.children) {
            if (_benchCancel) break;
            if (child.artifact.isDirectory) {
              queue.add(child);
            } else if (!await _source.isBinary(child.artifact.id)) {
              await openAndScroll(child);
            }
          }
        } else if (!await _source.isBinary(item.artifact.id)) {
          await openAndScroll(item);
        }
      }
      Trace.mark('bench.end', 'dirs=$dirs files=$files');
    } finally {
      await exportTraceLog();
    }
  }

  // 导出诊断追踪日志: Android先落私有目录再经MediaStore发布到公共Download/Glance-trace, 桌面直接写下载目录
  Future<String?> exportTraceLog() async {
    final path = Trace.exportTo(await _traceDir());
    if (path == null) {
      messageText = '暂无可导出日志';
    } else {
      try {
        final published = await TraceDestination.publish(path);
        messageText = '日志已导出: ${published ?? path}';
      } catch (_) {
        messageText = '日志已导出: $path';
      }
    }
    notifyListeners();
    return path;
  }

  Future<String> _traceDir() async {
    if (traceDirOverride != null) return traceDirOverride!;
    if (Platform.isAndroid) return TraceDestination.stagingDirectory();
    return TraceDestination.downloadsDirectory();
  }

  Future<List<TreeItem>> _childrenToItems(String parentId) async {
    final artifacts = await _source.listChildren(parentId);
    return [for (final a in artifacts) TreeItem(artifact: a)];
  }

  List<TreeItem> _flatten(List<TreeItem> items) =>
      flattenTree(items).whereType<ItemTreeRow>().map((r) => r.item).toList();

}

String nameOfId(String id) {
  if (id.startsWith('content://')) {
    final decoded = Uri.decodeComponent(id);
    final seg = decoded.split('/').where((s) => s.isNotEmpty).toList();
    var name = seg.isNotEmpty ? seg.last : id;
    final colon = name.indexOf(':');
    if (colon >= 0 && colon < name.length - 1) name = name.substring(colon + 1);
    return name;
  }
  final parts = id.split(Platform.pathSeparator).where((s) => s.isNotEmpty).toList();
  return parts.isNotEmpty ? parts.last : id;
}

final mainViewModel = MainViewModel();
