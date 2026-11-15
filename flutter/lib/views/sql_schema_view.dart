import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'package:glance/generated/lib.dart' as rust;

// 每张表渲染成一张卡，卡内为列名、类型、约束、默认值四栏表格，索引附在表尾

bool isSqlFileName(String name) {
  final dot = name.lastIndexOf('.');
  if (dot < 0 || dot == name.length - 1) return false;
  return name.substring(dot + 1).toLowerCase() == 'sql';
}

const double _iconSlot = 20;
const double _cellGap = 14;
const double _cardInset = 26;
const double _inlineTailMinWidth = 420;
const double _badgeFontSize = 10;
const double _badgePadding = 4;
const double _badgeSpacing = 4;

// 各栏宽度，表头行与数据行共用同一组值
class _Slots {
  const _Slots({
    required this.name,
    required this.type,
    required this.constraint,
    required this.inlineTail,
  });

  final double name;
  final double type;
  final double constraint;

  // 默认值与引用是否与前三栏并排，窄屏放不下时改为列名下方另起一行
  final bool inlineTail;
}

double _textWidth(TextStyle? style, String text) {
  if (text.isEmpty) return 0;
  final painter = TextPainter(
    text: TextSpan(text: text, style: style),
    textDirection: TextDirection.ltr,
    maxLines: 1,
  )..layout();
  return painter.width;
}

// 单枚约束徽标的占位宽，含左右内边距与边框
double _badgeWidth(String text) {
  return _textWidth(
        const TextStyle(fontSize: _badgeFontSize),
        text,
      ) +
      _badgePadding * 2 +
      2;
}

List<String> _badgesOf(rust.SqlColumnSchema column) => [
      if (column.notNull) 'NN',
      if (column.unique) 'UQ',
      if (column.autoIncrement) 'AI',
    ];

double _badgesWidth(List<String> badges) {
  if (badges.isEmpty) return 0;
  var sum = 0.0;
  for (final badge in badges) {
    sum += _badgeWidth(badge);
  }
  return sum + _badgeSpacing * (badges.length - 1);
}

// 按表头文字与表内最长内容取大值定栏宽，空间不够时按各栏可压空间等比收缩
_Slots _measureSlots(
  TextStyle? mono,
  TextStyle? head,
  rust.SqlTableSchema table,
  double maxWidth,
) {
  var name = _textWidth(head, '列名');
  var type = _textWidth(head, '类型');
  var constraint = _textWidth(head, '约束');
  var badges = 0.0;
  for (final column in table.columns) {
    name = math.max(name, _textWidth(mono, column.name));
    type = math.max(type, _textWidth(mono, column.typeName));
    badges = math.max(badges, _badgesWidth(_badgesOf(column)));
  }
  final wantName = name + _cellGap;
  final wantType = type + _cellGap;
  final wantConstraint = math.max(constraint, badges) + _cellGap;

  // 预算要扣掉卡片边框与左右内边距
  final room = math.max(maxWidth - _cardInset - _iconSlot, 120.0);
  final inlineTail = room >= _inlineTailMinWidth;
  final budget = inlineTail ? room * 0.9 : room;
  final total = wantName + wantType + wantConstraint;
  if (total <= budget) {
    return _Slots(
      name: wantName,
      type: wantType,
      constraint: wantConstraint,
      inlineTail: inlineTail,
    );
  }

  final minName = math.min(wantName, 56.0);
  final minType = math.min(wantType, 48.0);
  final minConstraint = math.min(wantConstraint, 36.0);
  final shrinkable = (wantName - minName) +
      (wantType - minType) +
      (wantConstraint - minConstraint);
  final ratio =
      shrinkable <= 0 ? 1.0 : math.min((total - budget) / shrinkable, 1.0);
  return _Slots(
    name: wantName - (wantName - minName) * ratio,
    type: wantType - (wantType - minType) * ratio,
    constraint: wantConstraint - (wantConstraint - minConstraint) * ratio,
    inlineTail: inlineTail,
  );
}

class SqlSchemaView extends StatelessWidget {
  const SqlSchemaView({required this.schema, super.key});

  final rust.SqlSchemaResult schema;

  @override
  Widget build(BuildContext context) {
    final indexesByTable = <String, List<rust.SqlIndexSchema>>{};
    for (final idx in schema.indexes) {
      indexesByTable
          .putIfAbsent(idx.table, () => <rust.SqlIndexSchema>[])
          .add(idx);
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
    final theme = Theme.of(context);
    final colors = theme.colorScheme;
    final mono = theme.textTheme.bodySmall?.copyWith(fontFamily: 'monospace');
    final head = theme.textTheme.labelSmall?.copyWith(
      color: colors.onSurfaceVariant,
    );
    return LayoutBuilder(builder: (context, constraints) {
      final slots = _measureSlots(mono, head, table, constraints.maxWidth);
      return Card(
        margin: const EdgeInsets.only(bottom: 10),
        elevation: 0,
        color: colors.surfaceContainerHighest,
        clipBehavior: Clip.antiAlias,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(8),
          side: BorderSide(color: colors.outlineVariant),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            _TitleBar(table: table, theme: theme),
            ColoredBox(
              color: colors.onSurface.withValues(alpha: 0.05),
              child: _HeaderRow(slots: slots, style: head),
            ),
            for (var i = 0; i < table.columns.length; i++) ...[
              if (i > 0)
                Divider(
                  height: 1,
                  thickness: 1,
                  color: colors.outlineVariant.withValues(alpha: 0.45),
                ),
              _ColumnRow(
                column: table.columns[i],
                slots: slots,
                mono: mono,
              ),
            ],
            if (indexes.isNotEmpty) ...[
              Divider(height: 1, thickness: 1, color: colors.outlineVariant),
              Padding(
                padding: const EdgeInsets.fromLTRB(12, 6, 12, 0),
                child: Text('索引', style: head),
              ),
              for (final idx in indexes) _IndexRow(index: idx, mono: mono),
              const SizedBox(height: 6),
            ] else
              const SizedBox(height: 6),
          ],
        ),
      );
    });
  }
}

class _TitleBar extends StatelessWidget {
  const _TitleBar({required this.table, required this.theme});

  final rust.SqlTableSchema table;
  final ThemeData theme;

  @override
  Widget build(BuildContext context) {
    final colors = theme.colorScheme;
    return Padding(
      padding: const EdgeInsets.fromLTRB(12, 10, 12, 8),
      child: Row(
        children: [
          Icon(Icons.table_chart_outlined, size: 15, color: colors.onSurface),
          const SizedBox(width: 6),
          Expanded(
            child: Text(
              table.name,
              style: theme.textTheme.titleSmall,
              overflow: TextOverflow.ellipsis,
            ),
          ),
          Text(
            '${table.columns.length} 列',
            style: theme.textTheme.labelSmall?.copyWith(
              color: colors.onSurfaceVariant,
            ),
          ),
        ],
      ),
    );
  }
}

class _HeaderRow extends StatelessWidget {
  const _HeaderRow({required this.slots, this.style});

  final _Slots slots;
  final TextStyle? style;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.fromLTRB(12, 6, 12, 6),
      child: Row(
        children: [
          const SizedBox(width: _iconSlot),
          SizedBox(width: slots.name, child: Text('列名', style: style)),
          SizedBox(width: slots.type, child: Text('类型', style: style)),
          SizedBox(width: slots.constraint, child: Text('约束', style: style)),
          if (slots.inlineTail)
            Expanded(
              child: Text(
                '默认值 / 引用',
                style: style,
                overflow: TextOverflow.ellipsis,
              ),
            ),
        ],
      ),
    );
  }
}

class _ColumnRow extends StatelessWidget {
  const _ColumnRow({
    required this.column,
    required this.slots,
    this.mono,
  });

  final rust.SqlColumnSchema column;
  final _Slots slots;
  final TextStyle? mono;

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    final badges = _badgesOf(column);
    final tail = <String>[
      if (column.defaultText.isNotEmpty) '= ${column.defaultText}',
      if (column.foreignKey.isNotEmpty) '→ ${column.foreignKey}',
    ].join('  ');
    final cells = <Widget>[
      SizedBox(
        width: _iconSlot,
        child: column.primaryKey
            ? Icon(Icons.vpn_key, size: 12, color: colors.onSurface)
            : null,
      ),
      SizedBox(
        width: slots.name,
        child: Text(
          column.name,
          style: mono?.copyWith(color: colors.onSurface),
          overflow: TextOverflow.ellipsis,
        ),
      ),
      SizedBox(
        width: slots.type,
        child: Text(
          column.typeName,
          style: mono?.copyWith(color: colors.onSurfaceVariant),
          overflow: TextOverflow.ellipsis,
        ),
      ),
      SizedBox(
        width: slots.constraint,
        child: FittedBox(
          fit: BoxFit.scaleDown,
          alignment: Alignment.centerLeft,
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              for (var i = 0; i < badges.length; i++)
                Padding(
                  padding: EdgeInsets.only(left: i == 0 ? 0 : _badgeSpacing),
                  child: _Badge(text: badges[i]),
                ),
            ],
          ),
        ),
      ),
    ];
    final tailStyle = mono?.copyWith(color: colors.onSurfaceVariant);
    return Padding(
      padding: const EdgeInsets.fromLTRB(12, 6, 12, 6),
      child: slots.inlineTail
          ? Row(
              children: [
                ...cells,
                Expanded(
                  child: Text(
                    tail,
                    style: tailStyle,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
              ],
            )
          : Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Row(children: cells),
                if (tail.isNotEmpty)
                  Padding(
                    padding: const EdgeInsets.only(left: _iconSlot, top: 2),
                    child: Text(
                      tail,
                      style: tailStyle,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                    ),
                  ),
              ],
            ),
    );
  }
}

class _IndexRow extends StatelessWidget {
  const _IndexRow({required this.index, this.mono});

  final rust.SqlIndexSchema index;
  final TextStyle? mono;

  @override
  Widget build(BuildContext context) {
    final colors = Theme.of(context).colorScheme;
    return Padding(
      padding: const EdgeInsets.fromLTRB(12, 4, 12, 0),
      child: Row(
        children: [
          Icon(Icons.bolt, size: 13, color: colors.onSurfaceVariant),
          const SizedBox(width: 4),
          Expanded(
            child: Text(
              '${index.name}(${index.columns.join(', ')})',
              style: mono?.copyWith(color: colors.onSurfaceVariant),
              overflow: TextOverflow.ellipsis,
            ),
          ),
          if (index.unique) const _Badge(text: 'UNIQUE'),
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
      padding: const EdgeInsets.symmetric(
        horizontal: _badgePadding,
        vertical: 1,
      ),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(3),
        border: Border.all(color: colors.outlineVariant),
      ),
      child: Text(
        text,
        maxLines: 1,
        softWrap: false,
        overflow: TextOverflow.clip,
        style: TextStyle(fontSize: _badgeFontSize, color: colors.onSurfaceVariant),
      ),
    );
  }
}
