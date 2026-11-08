import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';

import 'file_tree.dart';
import 'generated/flutter_api.dart';
import 'generated/lib.dart' as rust;
import 'models.dart';
import 'package:path_provider/path_provider.dart';
import 'trace.dart';
import 'utils.dart';

enum AppThemeMode { light, dark, system }

class ThemeListenable extends ChangeNotifier {
  void changed() => notifyListeners();
}

class MainViewModel extends ChangeNotifier {
  MainViewModel([FileTreeSource? source]) : _source = source ?? createSource();

  final FileTreeSource _source;

  AppThemeMode themeMode = AppThemeMode.system;
  final ThemeListenable themeListenable = ThemeListenable();
  int codeFontSize = 13;
  static const List<int> codeFontSizes = [11, 12, 13, 14, 15, 16, 18, 20];

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

  int? outlineScrollTargetLine;

  bool get hasSelection => selectedArtifact != null;

    String? _rootId;
    int _sizeToken = 0;
    int _loadToken = 0;
    Future<void>? _pendingLoad;

  Future<void> restoreWorkspace() async {
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

  Future<void> _openRoot(String rootId) async {
    _rootId = rootId;
    currentFolderName = _nameOf(rootId);
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
    if (_benchmarkMode) unawaited(runBenchmarkSequence());
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
      messageText = '已选择目录：${item.artifact.name}';
    } else {
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

  Future<void> _loadFile(Artifact a, int token) async {
    try {
      if (await _source.isBinary(a.id)) {
        if (token != _loadToken) return;
        codeParseResult = null;
        selectedContent = null;
        currentOutline = null;
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

  // 展开过程中不逐目录notifyListeners，大目录树会触发数百次全量重建导致
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
  }

  void selectCodeFontSize(int size) {
    codeFontSize = size;
    notifyListeners();
  }

  void clearSelection() {
    for (final it in _flatten(treeItems)) {
      it.isSelected = false;
    }
    selectedItem = null;
    selectedArtifact = null;
    selectedContent = null;
    currentOutline = null;
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
        Trace.mark('bench.scroll.start', 'name=${node.artifact.name}');
        final lineCount = (selectedContent ?? '').split('\n').length;
        requestScrollToLine(lineCount > 0 ? lineCount - 1 : 0);
        await Future.delayed(const Duration(milliseconds: 16));
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

  // 导出诊断追踪日志到应用文档目录下的Glance/, 返回文件路径; 无可导出时返回null
  Future<String?> exportTraceLog() async {
    final path = Trace.exportTo(await _traceDir());
    messageText =
        path == null ? '暂无可导出日志' : '日志已导出: $path';
    notifyListeners();
    return path;
  }

  Future<String> _traceDir() async {
    if (traceDirOverride != null) return traceDirOverride!;
    final base = await getApplicationDocumentsDirectory();
    final dir = Directory('${base.path}/Glance');
    await dir.create(recursive: true);
    return dir.path;
  }

  Future<List<TreeItem>> _childrenToItems(String parentId) async {
    final artifacts = await _source.listChildren(parentId);
    return [for (final a in artifacts) TreeItem(artifact: a)];
  }

  List<TreeItem> _flatten(List<TreeItem> items) {
    final out = <TreeItem>[];
    for (final it in items) {
      out.add(it);
      if (it.isExpanded) out.addAll(_flatten(it.children));
    }
    return out;
  }

  String _nameOf(String id) {
    if (id.startsWith('content://')) {
      final seg = id.split('/').where((s) => s.isNotEmpty).toList();
      return seg.isNotEmpty ? seg.last : id;
    }
    final parts = id.split(Platform.pathSeparator).where((s) => s.isNotEmpty).toList();
    return parts.isNotEmpty ? parts.last : id;
  }
}

final mainViewModel = MainViewModel();
