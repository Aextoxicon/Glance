import 'package:flutter/material.dart';

import '../view_model.dart';
import '../utils.dart';

class CodePreviewPanel extends StatelessWidget {
  final MainViewModel vm;
  const CodePreviewPanel({required this.vm, super.key});

  @override
  Widget build(BuildContext context) {
    if (vm.messageText != null) {
      return MessageView(message: vm.messageText!);
    }
    if (!vm.hasSelection) {
      return const Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(Icons.insert_drive_file, size: 48, color: Colors.grey),
            SizedBox(height: 8),
            Text('选择文件以预览', style: TextStyle(color: Colors.grey)),
          ],
        ),
      );
    }
    return Column(
      children: [
        CodePreviewToolbar(vm: vm),
        const Divider(height: 1),
        Expanded(
          child: vm.selectedContent != null
              ? CodeContentView(vm: vm)
              : const Center(child: CircularProgressIndicator()),
        ),
        if (vm.previewNotice != null)
          Column(
            children: [
              const Divider(height: 1),
              Padding(
                padding:
                    const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
                child: Text(
                  vm.previewNotice!,
                  style: const TextStyle(fontSize: 12, color: Colors.grey),
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                ),
              ),
            ],
          ),
      ],
    );
  }
}

class CodePreviewToolbar extends StatelessWidget {
  final MainViewModel vm;
  const CodePreviewToolbar({required this.vm, super.key});

  @override
  Widget build(BuildContext context) {
    final artifact = vm.selectedArtifact;
    if (artifact == null) return const SizedBox.shrink();
    return SizedBox(
      height: 40,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
        child: Row(
          children: [
            Expanded(
              child: Text(
                artifact.name,
                style: Theme.of(context).textTheme.titleSmall,
                overflow: TextOverflow.ellipsis,
              ),
            ),
            const SizedBox(width: 8),
            Text(
              formatSize(artifact.size),
              style: Theme.of(context)
                  .textTheme
                  .labelSmall
                  ?.copyWith(
                      color: Theme.of(context).colorScheme.onSurfaceVariant),
            ),
            const SizedBox(width: 8),
            if (vm.currentOutline != null)
              Builder(
                builder: (context) => TextButton(
                  style:
                      TextButton.styleFrom(minimumSize: const Size(28, 28)),
                  onPressed: () => Scaffold.of(context).openEndDrawer(),
                  child: const Text('大纲', style: TextStyle(fontSize: 12)),
                ),
              ),
            const SizedBox(width: 4),
            IconButton(
              iconSize: 14,
              constraints: const BoxConstraints(minWidth: 28, minHeight: 28),
              tooltip: '清除选择',
              onPressed: vm.clearSelection,
              icon: const Icon(Icons.close, size: 14),
            ),
          ],
        ),
      ),
    );
  }
}

class CodeContentView extends StatelessWidget {
  final MainViewModel vm;
  const CodeContentView({required this.vm, super.key});

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    final style = TextStyle(
      fontFamily: 'monospace',
      fontFamilyFallback: const ['Consolas', 'Courier New'],
      fontSize: vm.codeFontSize.toDouble(),
      height: 1.5,
      color: cs.onSurface,
      decoration: TextDecoration.none,
    );
    final lines = (vm.selectedContent ?? '').split('\n');
    return Material(
      color: cs.surface,
      child: SelectionArea(
        child: ListView.builder(
          padding: const EdgeInsets.fromLTRB(8, 8, 16, 16),
          itemCount: lines.length,
          itemBuilder: (context, i) => Text(lines[i], style: style),
        ),
      ),
    );
  }
}

class MessageView extends StatelessWidget {
  final String message;
  const MessageView({required this.message, super.key});

  @override
  Widget build(BuildContext context) {
    return Container(
      color: Theme.of(context).colorScheme.surface,
      padding: const EdgeInsets.all(32),
      child: Center(
        child: Text(
          message,
          style: Theme.of(context)
              .textTheme
              .bodyMedium
              ?.copyWith(
                  color: Theme.of(context).colorScheme.onSurfaceVariant),
        ),
      ),
    );
  }
}
