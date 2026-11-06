import 'package:flutter/material.dart';

import '../view_model.dart';
import '../models.dart';

class FileTreePanel extends StatelessWidget {
  final MainViewModel vm;
  const FileTreePanel({required this.vm, super.key});

  @override
  Widget build(BuildContext context) {
    final flat = flattenTree(vm.treeItems);
    if (vm.treeItems.isEmpty) {
      return const Center(
        child: Text('空目录', style: TextStyle(color: Colors.grey)),
      );
    }
    return ListView.builder(
      itemCount: flat.length,
      itemBuilder: (context, i) {
        final (depth, item) = flat[i];
        return TreeItemRow(
          key: ValueKey(item.artifact.id),
          vm: vm,
          item: item,
          depth: depth,
        );
      },
    );
  }
}

List<(int, TreeItem)> flattenTree(List<TreeItem> items, [int depth = 0]) {
  final out = <(int, TreeItem)>[];
  for (final item in items) {
    out.add((depth, item));
    if (item.isExpanded && item.children.isNotEmpty) {
      out.addAll(flattenTree(item.children, depth + 1));
    }
  }
  return out;
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
        padding:
            EdgeInsets.only(left: indent, right: 8, top: 2, bottom: 2),
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
              color: item.artifact.isDirectory
                  ? cs.primary
                  : cs.onSurfaceVariant,
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
