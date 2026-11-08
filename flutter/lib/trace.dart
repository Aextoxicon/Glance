import 'dart:io';

// 环形缓冲诊断追踪: 记录label/extra与相对耗时, 导出CSV+同名.summary.json

class TraceEvent {
  TraceEvent(this.totalMs, this.deltaMs, this.label, this.extra);

  final int totalMs;
  final int deltaMs;
  final String label;
  final String extra;

  String toRow() =>
      '$totalMs,$deltaMs,${Trace._csvField(label)},${Trace._csvField(extra)}';
}

class Trace {
  static bool enabled = false;

  static const int capacity = 20000;

  static final List<TraceEvent> _events = [];
  static final Stopwatch _base = Stopwatch()..start();
  static int _lastMs = 0;

  static String? _streamPath;

  static void mark(String label, [String extra = '']) {
    if (!enabled) return;
    final now = _base.elapsedMilliseconds;
    final delta = now - _lastMs;
    _lastMs = now;
    _events.add(TraceEvent(now, delta, label, extra));
    if (_events.length > capacity) _events.removeAt(0);
  }

  // 开始一段追踪: 清空缓冲并准备导出路径(实落盘由exportTo完成)
  static String start(String directory) {
    _events.clear();
    _lastMs = _base.elapsedMilliseconds;
    _streamPath = '$directory/glance_trace_${_timestamp()}.csv';
    return _streamPath!;
  }

  // 导出至指定目录CSV(表头total_ms,delta_ms,label,extra)+同名.summary.json, 无事件返回null
  static String? exportTo(String directory) {
    if (_events.isEmpty) return null;
    final path = _streamPath ?? '$directory/glance_trace_${_timestamp()}.csv';
    final file = File(path);
    file.parent.createSync(recursive: true);
    final buffer = StringBuffer()..writeln('total_ms,delta_ms,label,extra');
    for (final e in _events) {
      buffer.writeln(e.toRow());
    }
    file.writeAsStringSync(buffer.toString());
    _writeSummary(path);
    _events.clear();
    _streamPath = null;
    return path;
  }

  // 跨所有select.start→select.painted区间汇总 平均/中位/最快/最慢/样本数
  static void _writeSummary(String csvPath) {
    final opens = <int>[];
    int? startTotal;
    for (final e in _events) {
      if (e.label == 'select.start') {
        startTotal = e.totalMs;
      } else if (e.label == 'select.painted' && startTotal != null) {
        opens.add(e.totalMs - startTotal);
        startTotal = null;
      }
    }
    if (opens.isEmpty) return;
    opens.sort();
    final avg = opens.reduce((a, b) => a + b) / opens.length;
    final median = opens.length % 2 == 0
        ? (opens[opens.length ~/ 2 - 1] + opens[opens.length ~/ 2]) / 2
        : opens[opens.length ~/ 2];
    final summary = <String, dynamic>{
      'opens': opens.length,
      'averageMs': _round1(avg),
      'medianMs': _round1(median.toDouble()),
      'fastestMs': opens.first,
      'slowestMs': opens.last,
      'allMs': opens,
    };
    File('${csvPath.replaceAll('.csv', '')}.summary.json')
        .writeAsStringSync(_jsonEncode(summary));
  }

  static String _jsonEncode(Map<String, dynamic> m) {
    final parts = <String>[];
    m.forEach((k, v) {
      if (v is List) {
        parts.add('"$k":[${v.join(',')}]');
      } else {
        parts.add('"$k":$v');
      }
    });
    return '{${parts.join(',')}}';
  }

  static double _round1(double v) => (v * 10).round() / 10;

  static String _timestamp() {
    final t = DateTime.now();
    return '${t.year}${_pad(t.month)}${_pad(t.day)}_'
        '${_pad(t.hour)}${_pad(t.minute)}${_pad(t.second)}';
  }

  static String _pad(int n) => n.toString().padLeft(2, '0');

  static String _csvField(String s) {
    if (s.contains(',') || s.contains('"') || s.contains('\n')) {
      return '"${s.replaceAll('"', '""')}"';
    }
    return s;
  }
}
