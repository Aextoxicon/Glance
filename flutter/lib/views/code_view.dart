import 'dart:typed_data';

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

class CodeContentView extends StatefulWidget {
  final MainViewModel vm;
  const CodeContentView({required this.vm, super.key});

  @override
  State<CodeContentView> createState() => _CodeContentViewState();
}

class _CodeContentViewState extends State<CodeContentView> {
  final ScrollController _controller = ScrollController();
  static const int _outlineScrollContextLines = 3;

  double get _lineExtent => widget.vm.codeFontSize * 1.5;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  void didUpdateWidget(covariant CodeContentView oldWidget) {
    super.didUpdateWidget(oldWidget);
    final target = widget.vm.outlineScrollTargetLine;
    if (target != null && _controller.hasClients) {
      final maxOffset = _controller.position.maxScrollExtent;
      _controller.jumpTo(
          ((target - _outlineScrollContextLines) * _lineExtent).clamp(0.0, maxOffset));
      widget.vm.consumeScrollTargetLine();
    }
  }

  double _measureCharWidth(TextStyle style, String sample) {
    final tp = TextPainter(
      text: TextSpan(text: sample, style: style),
      textDirection: TextDirection.ltr,
    )..layout();
    final w = tp.width;
    tp.dispose();
    return w;
  }

  // 等宽字体下逐行按字符宽度估最长行宽
  double _contentWidth(List<String> lines, TextStyle style) {
    if (lines.isEmpty) return 0;
    final latin = _measureCharWidth(style, 'M');
    final cjk = _measureCharWidth(style, '中');
    if (latin <= 0) return 0;
    var maxW = 0.0;
    for (final line in lines) {
      var w = 0.0;
      for (final rune in line.runes) {
        w += rune > 0x2E7F ? cjk : latin;
      }
      if (w > maxW) maxW = w;
    }
    return maxW;
  }

  @override
  Widget build(BuildContext context) {
    final vm = widget.vm;
    final cs = Theme.of(context).colorScheme;
    final dark = Theme.of(context).brightness == Brightness.dark;
    final style = TextStyle(
      fontFamily: 'monospace',
      fontFamilyFallback: const ['Consolas', 'Courier New'],
      fontSize: vm.codeFontSize.toDouble(),
      height: 1.5,
      color: cs.onSurface,
      decoration: TextDecoration.none,
    );
    final lines = (vm.selectedContent ?? '').split('\n');
    final contentWidth = _contentWidth(lines, style);
    return Material(
      color: cs.surface,
      child: SelectionArea(
        child: SingleChildScrollView(
          scrollDirection: Axis.horizontal,
          child: SizedBox(
            width: contentWidth + 24,
            child: ListView.builder(
              controller: _controller,
              padding: const EdgeInsets.fromLTRB(8, 8, 16, 16),
              itemExtent: _lineExtent,
              itemCount: lines.length,
              itemBuilder: (context, i) => Text.rich(
                _lineSpan(vm, i, lines[i], style, dark),
                style: style,
                softWrap: false,
                overflow: TextOverflow.clip,
              ),
            ),
          ),
        ),
      ),
    );
  }

  // 每个token12字节
  // UTF-16与Dart substring一致, 直接切分
  TextSpan _lineSpan(
      MainViewModel vm, int lineIdx, String line, TextStyle style, bool dark) {
    final result = vm.codeParseResult;
    if (result == null || result.lineIndex.isEmpty) {
      return TextSpan(text: line, style: style);
    }
    final li = ByteData.sublistView(result.lineIndex);
    final tokenCount = li.lengthInBytes ~/ 4;
    if (lineIdx + 1 >= tokenCount) return TextSpan(text: line, style: style);
    final hd = ByteData.sublistView(result.highlightData);
    final startTok = li.getUint32(lineIdx * 4, Endian.little);
    final endTok = li.getUint32((lineIdx + 1) * 4, Endian.little);
    if (startTok == endTok || endTok * 12 > hd.lengthInBytes) {
      return TextSpan(text: line, style: style);
    }
    final children = <InlineSpan>[];
    var cursor = 0;
    for (var j = startTok; j < endTok; j++) {
      final off = j * 12;
      final relStart = hd.getUint32(off, Endian.little);
      final relEnd = hd.getUint32(off + 4, Endian.little);
      final kindIdx = hd.getUint32(off + 8, Endian.little);
      if (relStart < cursor) continue;
      if (relStart > line.length) break;
      final end = relEnd > line.length ? line.length : relEnd;
      if (relStart > cursor) {
        children.add(TextSpan(text: line.substring(cursor, relStart), style: style));
      }
      final kind = kindIdx < result.kinds.length ? result.kinds[kindIdx] : '';
      final color = _kindColor(kind, dark);
      children.add(TextSpan(
        text: line.substring(relStart, end),
        style: color == null ? style : style.copyWith(color: color),
      ));
      cursor = end;
    }
    if (cursor < line.length) {
      children.add(TextSpan(text: line.substring(cursor), style: style));
    }
    return TextSpan(children: children);
  }

  Color? _kindColor(String kind, bool dark) {
    if (kind.contains('comment')) {
      return dark ? const Color(0xFF7F848E) : const Color(0xFF6A737D);
    }
    if (kind.contains('string') || kind.contains('escape')) {
      return dark ? const Color(0xFF98C379) : const Color(0xFF22863A);
    }
    if (kind.contains('keyword')) {
      return dark ? const Color(0xFFC678DD) : const Color(0xFFD73A49);
    }
    if (kind.contains('function') ||
        kind.contains('constructor') ||
        kind.contains('module')) {
      return dark ? const Color(0xFF61AFEF) : const Color(0xFF6F42C1);
    }
    if (kind.contains('type')) {
      return dark ? const Color(0xFFE5C07B) : const Color(0xFF005CC5);
    }
    if (kind.contains('number') || kind.contains('constant')) {
      return dark ? const Color(0xFFD19A66) : const Color(0xFF005CC5);
    }
    if (kind.contains('operator')) {
      return dark ? const Color(0xFF56B6C2) : const Color(0xFFD73A49);
    }
    if (kind.contains('property') ||
        kind.contains('attribute') ||
        kind.contains('label')) {
      return dark ? const Color(0xFFE06C75) : const Color(0xFF005CC5);
    }
    return null;
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
