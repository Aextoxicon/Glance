// 纯文本行号搜索, 不依赖Flutter
List<int> findMatchingLines(
  String text,
  String query, {
  bool caseSensitive = false,
}) {
  if (query.isEmpty || text.isEmpty) return const [];
  final needle = caseSensitive ? query : query.toLowerCase();
  final lines = text.split('\n');
  final hits = <int>[];
  for (var i = 0; i < lines.length; i++) {
    final hay = caseSensitive ? lines[i] : lines[i].toLowerCase();
    if (hay.contains(needle)) hits.add(i);
  }
  return hits;
}
