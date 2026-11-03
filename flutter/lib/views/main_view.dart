import 'package:flutter/material.dart';

import '../view_model.dart';
import 'code_view.dart';
import 'file_tree.dart';
import 'outline_drawer.dart';

class MainView extends StatelessWidget {
  const MainView({super.key});

  @override
  Widget build(BuildContext context) {
    final vm = mainViewModel;
    return ListenableBuilder(
      listenable: vm,
      builder: (context, _) {
        if (!vm.hasWorkspace) {
          return WelcomeScreen(onOpenFolder: vm.pickFolder);
        }
        final isWide = MediaQuery.sizeOf(context).width > 600;
        return isWide ? WideLayout(vm: vm) : NarrowLayout(vm: vm);
      },
    );
  }
}

class WelcomeScreen extends StatelessWidget {
  final VoidCallback onOpenFolder;
  const WelcomeScreen({required this.onOpenFolder, super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Center(
        child: FilledButton.tonal(
          onPressed: onOpenFolder,
          child: const Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(Icons.folder_open, size: 18),
              SizedBox(width: 8),
              Text('打开文件夹'),
            ],
          ),
        ),
      ),
    );
  }
}

class WideLayout extends StatelessWidget {
  final MainViewModel vm;
  const WideLayout({required this.vm, super.key});

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    return Scaffold(
      endDrawer: OutlineDrawer(
        outline: vm.currentOutline ?? [],
        onSelectNode: (_) => vm.requestScrollToLine(0),
      ),
      body: Row(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SizedBox(
            width: 300,
            child: Material(
              color: cs.surfaceContainerHighest,
              child: Column(
                children: [
                  WorkspaceHeader(vm: vm),
                  Expanded(child: FileTreePanel(vm: vm)),
                ],
              ),
            ),
          ),
          VerticalDivider(width: 1, color: cs.outlineVariant),
          Expanded(child: CodePreviewPanel(vm: vm)),
        ],
      ),
    );
  }
}

class NarrowLayout extends StatelessWidget {
  final MainViewModel vm;
  const NarrowLayout({required this.vm, super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: Text(
          vm.selectedArtifact?.name ??
              (vm.currentFolderName.isEmpty ? 'Glance' : vm.currentFolderName),
          overflow: TextOverflow.ellipsis,
        ),
        actions: [
          if (vm.totalSizeReadable.isNotEmpty)
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 8),
              child: Text(
                vm.totalSizeReadable,
                style: Theme.of(context)
                    .textTheme
                    .labelMedium
                    ?.copyWith(color: Theme.of(context).colorScheme.onSurfaceVariant),
              ),
            ),
        ],
      ),
      drawer: Drawer(
        width: 300,
        child: Column(
          children: [
            WorkspaceHeader(vm: vm),
            Expanded(child: FileTreePanel(vm: vm)),
          ],
        ),
      ),
      endDrawer: OutlineDrawer(
        outline: vm.currentOutline ?? [],
        onSelectNode: (_) => vm.requestScrollToLine(0),
      ),
      body: CodePreviewPanel(vm: vm),
    );
  }
}

class WorkspaceHeader extends StatelessWidget {
  final MainViewModel vm;
  const WorkspaceHeader({required this.vm, super.key});

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    return Column(
      children: [
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
          child: Row(
            children: [
              Expanded(
                child: Text(
                  vm.currentFolderName.isEmpty ? '工作区' : vm.currentFolderName,
                  style: Theme.of(context).textTheme.titleSmall,
                  overflow: TextOverflow.ellipsis,
                ),
              ),
              if (vm.isComputingSize)
                const SizedBox(
                  width: 16,
                  height: 16,
                  child: CircularProgressIndicator(strokeWidth: 2),
                ),
              IconButton(
                iconSize: 14,
                constraints: const BoxConstraints(minWidth: 28, minHeight: 28),
                tooltip: '关闭工作区',
                onPressed: vm.closeWorkspace,
                icon: const Icon(Icons.close, size: 14),
              ),
            ],
          ),
        ),
        const SizedBox(height: 4),
        Row(
          children: [
            Padding(
              padding: const EdgeInsets.only(left: 12),
              child: Text(
                vm.totalSizeReadable,
                style: Theme.of(context)
                    .textTheme
                    .labelSmall
                    ?.copyWith(color: cs.onSurfaceVariant),
              ),
            ),
            const Spacer(),
            TextButton(
              onPressed: vm.expandAll,
              child: Text('展开', style: Theme.of(context).textTheme.bodySmall),
            ),
            TextButton(
              onPressed: vm.collapseAll,
              child: Text('折叠', style: Theme.of(context).textTheme.bodySmall),
            ),
            IconButton(
              iconSize: 14,
              constraints: const BoxConstraints(minWidth: 28, minHeight: 28),
              tooltip: '刷新',
              onPressed: vm.refreshTree,
              icon: const Icon(Icons.refresh, size: 14),
            ),
            IconButton(
              iconSize: 14,
              constraints: const BoxConstraints(minWidth: 28, minHeight: 28),
              tooltip: '设置',
              onPressed: () => showSettingsDialog(context),
              icon: const Icon(Icons.settings, size: 14),
            ),
          ],
        ),
        const Divider(height: 1),
      ],
    );
  }
}

Future<void> showSettingsDialog(BuildContext context) {
  final vm = mainViewModel;
  return showDialog<void>(
    context: context,
    builder: (context) => ListenableBuilder(
      listenable: vm,
      builder: (context, _) => SettingsDialog(vm: vm),
    ),
  );
}

class SettingsDialog extends StatelessWidget {
  final MainViewModel vm;
  const SettingsDialog({required this.vm, super.key});

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: const Text('设置'),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('主题', style: Theme.of(context).textTheme.labelLarge),
          const SizedBox(height: 8),
          Wrap(
            spacing: 8,
            children: [
              ChoiceChip(
                label: const Text('浅色'),
                selected: vm.themeMode == AppThemeMode.light,
                onSelected: (_) => vm.selectThemeMode(AppThemeMode.light),
              ),
              ChoiceChip(
                label: const Text('深色'),
                selected: vm.themeMode == AppThemeMode.dark,
                onSelected: (_) => vm.selectThemeMode(AppThemeMode.dark),
              ),
              ChoiceChip(
                label: const Text('跟随系统'),
                selected: vm.themeMode == AppThemeMode.system,
                onSelected: (_) => vm.selectThemeMode(AppThemeMode.system),
              ),
            ],
          ),
          const Divider(height: 24),
          Text('字号', style: Theme.of(context).textTheme.labelLarge),
          const SizedBox(height: 8),
          DropdownMenu<int>(
            initialSelection: vm.codeFontSize,
            enableFilter: false,
            requestFocusOnTap: false,
            onSelected: (v) {
              if (v != null) vm.selectCodeFontSize(v);
            },
            dropdownMenuEntries: [
              for (final s in MainViewModel.codeFontSizes)
                DropdownMenuEntry(value: s, label: fontSizeLabel(s)),
            ],
          ),
        ],
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('完成'),
        ),
      ],
    );
  }
}

String fontSizeLabel(int size) => '$size${size == 13 ? '(默认)' : ''}';
