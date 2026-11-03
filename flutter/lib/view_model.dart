import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';

import 'file_tree.dart';
import 'models.dart';
import 'utils.dart';

enum AppThemeMode { light, dark, system }

class MainViewModel extends ChangeNotifier {
  MainViewModel() : _source = createSource();

  final FileTreeSource _source;

  AppThemeMode themeMode = AppThemeMode.system;
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
  String? messageText;
  String? previewNotice;

  int? outlineScrollTargetLine;

  bool get hasSelection => selectedArtifact != null;

  String? _rootId;
  int _sizeToken = 0;

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
    try {
      if (item.children.isEmpty && !item.loaded) {
        item.children = await _childrenToItems(item.artifact.id);
        item.loaded = true;
      }
      item.isExpanded = true;
    } catch (e) {
      messageText = '读取目录失败：$e';
    }
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
      messageText = '已选择目录：${item.artifact.name}';
    } else {
      selectedArtifact = item.artifact;
      selectedContent = _mockContent(item.artifact);
      currentOutline = _mockOutline();
      messageText = null;
    }
    notifyListeners();
  }

  Future<void> expandAll() async {
    await _expandDirs(treeItems, <String>{});
    notifyListeners();
  }

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
      notifyListeners();
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
      treeItems = await _childrenToItems(rootId);
    } catch (e) {
      messageText = '读取目录失败：$e';
    }
    notifyListeners();
    unawaited(_computeTotalSize(rootId));
  }

  void requestScrollToLine(int line) {
    outlineScrollTargetLine = line;
    notifyListeners();
  }

  void consumeScrollTargetLine() {
    outlineScrollTargetLine = null;
    notifyListeners();
  }

  void selectThemeMode(AppThemeMode mode) {
    themeMode = mode;
    notifyListeners();
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

  String _mockContent(Artifact a) {
    final ext = a.name.contains('.') ? a.name.split('.').last : '';
    return [
      '// 占位内容（骨架版未接入 Rust 解析）',
      '// 文件：${a.name}  (${formatSize(a.size)}, 类型: $ext)',
      '',
      'void main() {',
      '  print("Glance Flutter 布局骨架");',
      '}',
      '',
      '// 接入 parseCodeFrb 后即可显示真实高亮与大纲',
    ].join('\n');
  }

  List<OutlineNode> _mockOutline() => const [
        OutlineNode(kind: 'function', name: 'main', startByte: 40),
        OutlineNode(kind: 'class', name: 'GlanceApp', startByte: 120),
      ];
}

final mainViewModel = MainViewModel();
