import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'package:glance/generated/lib.dart' as rust;

// 每张表一卡, 列行给出类型与约束标记, 索引附在表卡尾部

bool isSqlFileName(String name) {
  final dot = name.lastIndexOf('.');
  if (dot < 0 || dot == name.length - 1) return false;
  return name.substring(dot + 1).toLowerCase() == 'sql';
}

// 列名与类型各按最宽一行定宽, 使两列在卡片内对齐
List<double> _measureColumns(
    TextStyle? mono, rust.SqlTableSchema table, double maxWidth) {
  double widestOf(String Function(rust.SqlColumnSchema) pick) {
    var pixels = 0.0;
    for (final column in table.columns) {
      final text = pick(column);
      if (text.isEmpty) continue;
      final painter = TextPainter(
        text: TextSpan(text: text, style: mono),
        textDirection: TextDirection.ltr,
        maxLines: 1,
      )..layout();
      pixels = math.max(pixels, painter.width);
    }
    return pixels;
  }

  final budget = math.max(maxWidth * 0.55, 100.0);
  var name = widestOf((c) => c.name) + 10;
  var type = widestOf((c) => c.typeName) + 10;
  if (name + type > budget) {
    final scale = budget / (name + type);
    name *= scale;
    type *= scale;
  }
  return [name.clamp(56.0, 240.0), type.clamp(48.0, 180.0)];
}

class SqlSchemaView extends StatelessWidget {
  const SqlSchemaView({required this.schema, super.key});

  final rust.SqlSchemaResult schema;

  @override
  Widget build(BuildContext context) {
    final indexesByTable = <String, List<rust.SqlIndexSchema>>{};
    for (final idx in schema.indexes) {
      indexesByTable.putIfAbsent(idx.table, () => <rust.SqlIndexSchema>[]).add(idx);
    }
    return ListView.builder(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
      itemCount: schema.tables.length,
      itemBuilder: (context, i) {
        final table = schema.tables[i];
        return _TableCard(
          table: table,
          indexes: indexesByTable[table.name] ?? const <rust.SqlIndexSchema>[],
        );
      },
    );
  }
}

class _TableCard extends StatelessWidget {
  const _TableCard({required this.table, required this.indexes});

  final rust.SqlTableSchema table;
  final List<rust.SqlIndexSchema> indexes;

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    final mono = Theme.of(context)
        .textTheme
        .bodySmall
        ?.copyWith(fontFamily: 'monospace');
    return LayoutBuilder(builder: (context, constraints) {
      final widths = _measureColumns(mono, table, constraints.maxWidth);
      return Card(
        margin: const EdgeInsets.only(bottom: 10),
        elevation: 0,
        color: colors.surfaceContainerHighest,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(8),
          side: BorderSide(color: colors.outlineVariant),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(12, 10, 12, 6),
              child: Row(
                children: [
                  Icon(Icons.table_chart_outlined,
                      size: 16, color: colors.onSurface),
                  const SizedBox(width: 6),
                  Expanded(
                    child: Text(
                      table.name,
                      style: Theme.of(context).textTheme.titleSmall,
                      overflow: TextOverflow.ellipsis,
                    ),
                  ),
                  Text(
                    '${table.columns.length} 列',
                    style: Theme.of(context)
                        .textTheme
                        .labelSmall
                        ?.copyWith(color: colors.onSurfaceVariant),
                  ),
                ],
              ),
            ),
            Divider(height: 1, color: colors.outlineVariant),
            for (final col in table.columns)
              _ColumnRow(
                column: col,
                mono: mono,
                nameWidth: widths[0],
                typeWidth: widths[1],
              ),
            if (indexes.isNotEmpty) ...[
              Divider(height: 1, color: colors.outlineVariant),
              for (final idx in indexes)
                Padding(
                  padding: const EdgeInsets.fromLTRB(12, 6, 12, 0),
                  child: Row(
                    children: [
                      Icon(Icons.bolt,
                          size: 13, color: colors.onSurfaceVariant),
                      const SizedBox(width: 4),
                      Expanded(
                        child: Text(
                          '${idx.name}(${idx.columns.join(', ')})',
                          style: mono?.copyWith(color: colors.onSurfaceVariant),
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                      if (idx.unique) const _Badge(text: 'UNIQUE'),
                    ],
                  ),
                ),
              const SizedBox(height: 8),
            ] else
              const SizedBox(height: 6),
          ],
        ),
      );
    });
  }
}

class _ColumnRow extends StatelessWidget {
  const _ColumnRow({
    required this.column,
    required this.nameWidth,
    required this.typeWidth,
    this.mono,
  });

  final rust.SqlColumnSchema column;
  final double nameWidth;
  final double typeWidth;
  final TextStyle? mono;

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.fromLTRB(12, 5, 12, 5),
      child: Row(
        children: [
          SizedBox(
            width: 18,
            child: column.primaryKey
                ? Icon(Icons.vpn_key, size: 13, color: colors.onSurface)
                : null,
          ),
          SizedBox(
            width: nameWidth,
            child: Text(
              column.name,
              style: mono?.copyWith(color: colors.onSurface),
              overflow: TextOverflow.ellipsis,
            ),
          ),
          const SizedBox(width: 10),
          SizedBox(
            width: typeWidth,
            child: Text(
              column.typeName,
              style: mono?.copyWith(color: colors.onSurfaceVariant),
              overflow: TextOverflow.ellipsis,
            ),
          ),
          if (column.notNull) const _Badge(text: 'NN'),
          if (column.unique) const _Badge(text: 'UQ'),
          if (column.autoIncrement) const _Badge(text: 'AI'),
          Expanded(
            child: Padding(
              padding: const EdgeInsets.only(left: 10),
              child: Text.rich(
                TextSpan(
                  style: mono?.copyWith(color: colors.onSurfaceVariant),
                  children: [
                    if (column.defaultText.isNotEmpty)
                      TextSpan(text: '= ${column.defaultText}'),
                    if (column.foreignKey.isNotEmpty)
                      TextSpan(
                        text: column.defaultText.isNotEmpty
                            ? '  → ${column.foreignKey}'
                            : '→ ${column.foreignKey}',
                      ),
                  ],
                ),
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _Badge extends StatelessWidget {
  const _Badge({required this.text});

  final String text;

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    return Container(
      margin: const EdgeInsets.only(left: 4),
      padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 1),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(3),
        border: Border.all(color: colors.outlineVariant),
      ),
      child: Text(
        text,
        style: TextStyle(fontSize: 9, color: colors.onSurfaceVariant),
      ),
    );
  }
}
