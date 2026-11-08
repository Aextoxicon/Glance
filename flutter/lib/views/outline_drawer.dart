import 'package:flutter/material.dart';

import '../utils.dart';
import '../models.dart';

class OutlineDrawer extends StatelessWidget {
  final List<OutlineNode> outline;
  final void Function(OutlineNode) onSelectNode;
  const OutlineDrawer({
    required this.outline,
    required this.onSelectNode,
    super.key,
  });

  @override
  Widget build(BuildContext context) {
    final flat = flattenOutline(outline);
    final scaffold = Scaffold.of(context);
    return Drawer(
      width: outlineDrawerWidth,
      child: Column(
        children: [
          SizedBox(
            height: 44,
            child: Padding(
              padding: const EdgeInsets.only(left: 12, right: 4),
              child: Row(
                children: [
                  Expanded(
                    child: Text('大纲',
                        style: Theme.of(context).textTheme.titleSmall),
                  ),
                  TextButton(
                    style: TextButton.styleFrom(
                        minimumSize: const Size(28, 28)),
                    onPressed: scaffold.closeEndDrawer,
                    child: const Icon(Icons.keyboard_arrow_right, size: 14),
                  ),
                ],
              ),
            ),
          ),
          const Divider(height: 1),
          Expanded(
            child: flat.isEmpty
                ? const Center(
                    child: Text('该文件没有大纲',
                        style: TextStyle(color: Colors.grey)))
                : ListView.builder(
                    padding: EdgeInsets.zero,
                    itemCount: flat.length,
                    itemBuilder: (context, i) {
                      final (depth, node) = flat[i];
                      return OutlineRow(
                        node: node,
                        depth: depth,
                        onClick: () {
                          onSelectNode(node);
                          scaffold.closeEndDrawer();
                        },
                      );
                    },
                  ),
          ),
        ],
      ),
    );
  }
}

List<(int, OutlineNode)> flattenOutline(List<OutlineNode> nodes,
    [int depth = 0]) {
  final out = <(int, OutlineNode)>[];
  for (final n in nodes) {
    out.add((depth, n));
    if (n.children.isNotEmpty) {
      out.addAll(flattenOutline(n.children, depth + 1));
    }
  }
  return out;
}

class OutlineRow extends StatelessWidget {
  final OutlineNode node;
  final int depth;
  final VoidCallback onClick;
  const OutlineRow({
    required this.node,
    required this.depth,
    required this.onClick,
    super.key,
  });

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    final indent = 12.0 + depth * 14.0;
    return InkWell(
      onTap: onClick,
      child: Container(
        padding:
            EdgeInsets.only(left: indent, right: 12, top: 3, bottom: 3),
        constraints: const BoxConstraints(minHeight: 30),
        child: Row(
          children: [
            SizedBox(
              width: 48,
              child: Text(
                outlineKindLabel(node.kind),
                style: Theme.of(context)
                    .textTheme
                    .labelSmall
                    ?.copyWith(color: cs.primary),
                overflow: TextOverflow.ellipsis,
              ),
            ),
            const SizedBox(width: 6),
            Expanded(
              child: Text(
                node.name.isEmpty ? '（无名）' : node.name,
                style: Theme.of(context).textTheme.bodySmall,
                overflow: TextOverflow.ellipsis,
              ),
            ),
          ],
        ),
      ),
    );
  }
}
