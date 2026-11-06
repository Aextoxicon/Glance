import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';

import 'file_tree.dart';
import 'generated/flutter_api.dart';
import 'generated/lib.dart' as rust;
import 'models.dart';
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

  int? outlineScrollTargetLine;

  bool get hasSelection => selectedArtifact != null;

  String? _rootId;
  int _sizeToken = 0;
  int _loadToken = 0;

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
      final token = ++_loadToken;
      unawaited(_loadFile(item.artifact, token));
    }
    notifyListeners();
  }

  Future<void> _loadFile(Artifact a, int token) async {
    try {
      final content = await _source.readFile(a.id);
      if (token != _loadToken) return;
      final result = parseCodeFrb(source: content, filename: a.name);
      if (token != _loadToken) return;
      codeParseResult = result;
      selectedContent = content;
      currentOutline = result.outline.map(_toModelNode).toList();
      messageText = null;
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
