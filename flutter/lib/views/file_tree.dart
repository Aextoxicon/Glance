import 'package:flutter/material.dart';

import '../view_model.dart';
import '../models.dart';

class FileTreePanel extends StatefulWidget {
  final MainViewModel vm;
  const FileTreePanel({required this.vm, super.key});

  @override
  State<FileTreePanel> createState() => _FileTreePanelState();
}

class _FileTreePanelState extends State<FileTreePanel> {
  // 与TreeItemRow的minHeight对齐
  static const double _rowHeight = 28;

  final ScrollController _scroll = ScrollController();
  String? _lastSelectedId;
  int _lastRevealToken = 0;
  bool _revealing = false;

  MainViewModel get vm => widget.vm;

  @override
  void initState() {
    super.initState();
    _lastSelectedId = vm.selectedArtifact?.id;
    _lastRevealToken = vm.revealToken;
    vm.addListener(_onVmChanged);
    // 抽屉每次打开都是新实例, 首帧后把当前文件滚进视野
    WidgetsBinding.instance
        .addPostFrameCallback((_) => _reveal(animate: false));
  }

  @override
  void dispose() {
    vm.removeListener(_onVmChanged);
    _scroll.dispose();
    super.dispose();
  }

  void _onVmChanged() {
    if (vm.revealToken != _lastRevealToken) {
      _lastRevealToken = vm.revealToken;
      WidgetsBinding.instance
          .addPostFrameCallback((_) => _reveal(animate: true));
      return;
    }
    final id = vm.selectedArtifact?.id;
    if (id == _lastSelectedId) return;
    _lastSelectedId = id;
    if (id == null) return;
    WidgetsBinding.instance
        .addPostFrameCallback((_) => _reveal(animate: false));
  }

  // 展开目标所在路径后滚到它, 已在视野内则不动
  Future<void> _reveal({required bool animate}) async {
    if (_revealing || !mounted) return;
    _revealing = true;
    try {
      if (!await vm.revealSelection()) return;
      if (!mounted) return;
      // 新行要等这一帧布局完才有坐标
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (!mounted) return;
        _scrollToSelection(animate: animate);
      });
      WidgetsBinding.instance.scheduleFrame();
    } finally {
      _revealing = false;
    }
  }

  void _scrollToSelection({required bool animate}) {
    final id = vm.selectedArtifact?.id;
    if (id == null || !_scroll.hasClients) return;
    final flat = flattenTree(vm.treeItems);
    var index = -1;
    for (var i = 0; i < flat.length; i++) {
      final row = flat[i];
      if (row is ItemTreeRow && row.item.artifact.id == id) {
        index = i;
        break;
      }
    }
    if (index < 0) return;
    final pos = _scroll.position;
    if (!pos.hasContentDimensions) return;
    final top = index * _rowHeight;
    final viewTop = pos.pixels;
    final viewBottom = viewTop + pos.viewportDimension;
    if (top >= viewTop && top + _rowHeight <= viewBottom) return;
    // 停在视口上三分之一处, 保留上下文
    final target =
        (top - pos.viewportDimension / 3).clamp(0.0, pos.maxScrollExtent);
    if (animate) {
      _scroll.animateTo(target,
          duration: const Duration(milliseconds: 220),
          curve: Curves.easeOutCubic);
    } else {
      _scroll.jumpTo(target);
    }
  }

  @override
  Widget build(BuildContext context) {
    final flat = flattenTree(vm.treeItems);
    if (vm.treeItems.isEmpty) {
      final cs = Theme.of(context).colorScheme;
      return Center(
        child: Text('空目录', style: TextStyle(color: cs.onSurfaceVariant)),
      );
    }
    return ListView.builder(
      controller: _scroll,
      padding: EdgeInsets.zero,
      itemCount: flat.length,
      itemBuilder: (context, i) {
        final row = flat[i];
        if (row is LoadingTreeRow) {
          return _LoadingRowWidget(depth: row.depth);
        }
        final itemRow = row as ItemTreeRow;
        return TreeItemRow(
          key: ValueKey(itemRow.item.artifact.id),
          vm: vm,
          item: itemRow.item,
          depth: itemRow.depth,
        );
      },
    );
  }
}

class TreeItemRow extends StatelessWidget {
  final MainViewModel vm;
  final TreeItem item;
  final int depth;
  const TreeItemRow(
      {required this.vm, required this.item, required this.depth, super.key});

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    final indent = 8.0 + depth * 20.0;
    return InkWell(
      onTap: () {
        if (item.artifact.isDirectory) {
          vm.toggleExpand(item);
        } else {
          vm.selectItem(item);
        }
      },
      child: Container(
        color: item.isSelected ? cs.primaryContainer : null,
        padding: EdgeInsets.only(left: indent, right: 8, top: 2, bottom: 2),
        constraints: const BoxConstraints(minHeight: 28),
        child: Row(
          children: [
            if (item.artifact.isDirectory)
              Icon(
                item.isExpanded
                    ? Icons.keyboard_arrow_down
                    : Icons.keyboard_arrow_right,
                size: 16,
                color: cs.onSurfaceVariant,
              )
            else
              const SizedBox(width: 16),
            const SizedBox(width: 4),
            Icon(
              item.icon,
              size: 16,
              color:
                  item.artifact.isDirectory ? cs.primary : cs.onSurfaceVariant,
            ),
            const SizedBox(width: 8),
            Expanded(
              child: Text(
                item.artifact.name,
                style: Theme.of(context).textTheme.bodySmall,
                overflow: TextOverflow.ellipsis,
              ),
            ),
            if (!item.artifact.isDirectory) ...[
              const SizedBox(width: 4),
              Text(
                item.sizeDisplay,
                style: Theme.of(context)
                    .textTheme
                    .labelSmall
                    ?.copyWith(color: cs.onSurfaceVariant),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class _LoadingRowWidget extends StatelessWidget {
  final int depth;
  const _LoadingRowWidget({required this.depth});

  @override
  Widget build(BuildContext context) {
    final indent = 8.0 + depth * 20.0;
    final cs = Theme.of(context).colorScheme;
    return Container(
      padding: EdgeInsets.only(left: indent, right: 8, top: 2, bottom: 2),
      constraints: const BoxConstraints(minHeight: 28),
      child: Row(
        children: [
          const SizedBox(width: 16),
          const SizedBox(width: 4),
          const SizedBox(
            width: 14,
            height: 14,
            child: CircularProgressIndicator(strokeWidth: 2),
          ),
          const SizedBox(width: 12),
          Text('加载中…',
              style: TextStyle(color: cs.onSurfaceVariant, fontSize: 12)),
        ],
      ),
    );
  }
}
