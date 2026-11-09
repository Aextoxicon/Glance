import 'dart:math' as math;
import 'dart:typed_data';

import 'package:flutter/material.dart';

import 'package:glance/generated/lib.dart' as rust;

// 各字段是扁平double[], stride见native/src/lib.rs
// 几何算法逐段对齐Avalonia版CadCanvas.cs(叉积定弧向/bulge转弧/参数采样椭圆)

class CadView extends StatelessWidget {
  const CadView({required this.geometry, super.key});

  final rust.CadGeometry geometry;

  @override
  Widget build(BuildContext context) {
    final foreground = Theme.of(context).colorScheme.onSurface;
    return ClipRect(
      child: CustomPaint(
        painter: _CadPainter(geometry: geometry, foreground: foreground),
        size: Size.infinite,
      ),
    );
  }
}

bool isCadFileName(String name) {
  final dot = name.lastIndexOf('.');
  if (dot < 0 || dot == name.length - 1) return false;
  final ext = name.substring(dot + 1).toLowerCase();
  return ext == 'dwg' || ext == 'dxf';
}

class _CadPainter extends CustomPainter {
  _CadPainter({required this.geometry, required this.foreground});

  final rust.CadGeometry geometry;
  final Color foreground;

  static const double _padding = 12.0;
  static const double _twoPi = math.pi * 2;

  @override
  bool shouldRepaint(_CadPainter oldDelegate) =>
      !identical(geometry, oldDelegate.geometry) ||
      foreground != oldDelegate.foreground;

  @override
  void paint(Canvas canvas, Size size) {
    final width = size.width;
    final height = size.height;
    if (width <= 0 || height <= 0) return;

    final g = geometry;
    final minX = _at(g.boundsMin, 0);
    final minY = _at(g.boundsMin, 1);
    final maxX = _at(g.boundsMax, 0);
    final maxY = _at(g.boundsMax, 1);
    final dx = maxX - minX;
    final dy = maxY - minY;

    // 无图元时bounds退化, 给个恒定比例避免除零
    var scale = 1.0;
    if (dx > 1e-9 && dy > 1e-9) {
      scale = math.min(
        (width - _padding * 2) / dx,
        (height - _padding * 2) / dy,
      );
      if (scale <= 0) scale = 1;
    }

    double offsetX = (width - dx * scale) / 2 - minX * scale;
    double offsetY = (height + dy * scale) / 2 + minY * scale;

    double toSx(double x) => offsetX + x * scale;
    double toSy(double y) => offsetY - y * scale;

    final paintCache = <int, Paint>{};

    Paint paintFor(int rgb, {bool fill = false}) {
      final cached = paintCache[(fill ? 0x1000000 : 0) | rgb];
      if (cached != null) return cached;
      final color = rgb == 0
          ? foreground
          : Color(0xFF000000 | rgb);
      final p = Paint()
        ..color = color
        ..style = fill ? PaintingStyle.fill : PaintingStyle.stroke
        ..strokeWidth = 1;
      paintCache[(fill ? 0x1000000 : 0) | rgb] = p;
      return p;
    }

    _drawLines(canvas, g, paintFor, toSx, toSy);
    _drawCircles(canvas, g, paintFor, toSx, toSy, scale);
    _drawArcs(canvas, g, paintFor, toSx, toSy, scale);
    _drawEllipses(canvas, g, paintFor, toSx, toSy, scale);
    _drawPolylines(canvas, g, paintFor, toSx, toSy);
    _drawTexts(canvas, g, paintFor, toSx, toSy, scale);
  }

  void _drawLines(
    Canvas canvas,
    rust.CadGeometry g,
    Paint Function(int, {bool fill}) paintFor,
    double Function(double) toSx,
    double Function(double) toSy,
  ) {
    final data = g.lineData;
    for (var i = 0; i + 7 <= data.length; i += 7) {
      canvas.drawLine(
        Offset(toSx(data[i]), toSy(data[i + 1])),
        Offset(toSx(data[i + 3]), toSy(data[i + 4])),
        paintFor(_colorAt(g.lineColors, i ~/ 7)),
      );
    }
  }

  void _drawCircles(
    Canvas canvas,
    rust.CadGeometry g,
    Paint Function(int, {bool fill}) paintFor,
    double Function(double) toSx,
    double Function(double) toSy,
    double scale,
  ) {
    final data = g.circleData;
    for (var i = 0; i + 8 <= data.length; i += 8) {
      final radius = data[i + 3] * scale;
      if (radius <= 0) continue;
      final center = Offset(toSx(data[i]), toSy(data[i + 1]));
      canvas.drawCircle(center, radius, paintFor(_colorAt(g.circleColors, i ~/ 8)));
    }
  }

  // 已知起止点+圆心+法向: 叉积定符号算扫掠角, 避免优弧画成劣弧
  void _drawArcs(
    Canvas canvas,
    rust.CadGeometry g,
    Paint Function(int, {bool fill}) paintFor,
    double Function(double) toSx,
    double Function(double) toSy,
    double scale,
  ) {
    final data = g.arcData;
    for (var i = 0; i + 14 <= data.length; i += 14) {
      final start = _Vec(data[i], data[i + 1], data[i + 2]);
      final end = _Vec(data[i + 3], data[i + 4], data[i + 5]);
      final center = _Vec(data[i + 6], data[i + 7], data[i + 8]);
      final radius = data[i + 9] * scale;
      final normal = _Vec(data[i + 10], data[i + 11], data[i + 12]);
      if (radius <= 0) continue;

      final u = start - center;
      final v = end - center;
      var sweep = math.atan2(u.cross(v).dot(normal), u.dot(v));
      if (sweep <= 0) sweep += _twoPi;

      // 方向语义对齐Avalonia: normal.z>=0取CounterClockwise(屏幕系), Flutter正sweep为顺时针故取负
      final startAngle = math.atan2(-(start.y - center.y), start.x - center.x);
      final screenSweep = normal.z >= 0 ? -sweep : sweep;
      final rect = Rect.fromCircle(
        center: Offset(toSx(center.x), toSy(center.y)),
        radius: radius,
      );
      canvas.drawArc(rect, startAngle, screenSweep, false,
          paintFor(_colorAt(g.arcColors, i ~/ 14)));
    }
  }

  // 整椭圆走drawEllipse+旋转变换; 带参数区间的按参数取样画折线
  void _drawEllipses(
    Canvas canvas,
    rust.CadGeometry g,
    Paint Function(int, {bool fill}) paintFor,
    double Function(double) toSx,
    double Function(double) toSy,
    double scale,
  ) {
    final data = g.ellipseData;
    for (var i = 0; i + 13 <= data.length; i += 13) {
      final center = _Vec(data[i], data[i + 1], data[i + 2]);
      final major = _Vec(data[i + 3], data[i + 4], data[i + 5]);
      final ratio = data[i + 6];
      final startParameter = data[i + 7];
      final endParameter = data[i + 8];
      final normal = _Vec(data[i + 9], data[i + 10], data[i + 11]);
      final majorLength = math.sqrt(major.dot(major));
      if (majorLength <= 0 || ratio <= 0) continue;

      final paint = paintFor(_colorAt(g.ellipseColors, i ~/ 13));
      var span = (endParameter - startParameter) % _twoPi;
      if (span < 0) span += _twoPi;

      if (span < 1e-6) {
        final cx = toSx(center.x);
        final cy = toSy(center.y);
        // 屏幕Y翻转已在角度计算中处理, 旋转值与Avalonia同
        final rotation = math.atan2(-major.y, major.x);
        canvas.save();
        canvas.translate(cx, cy);
        canvas.rotate(rotation);
        final rx = majorLength * scale;
        final ry = majorLength * ratio * scale;
        canvas.drawPath(
          Path()
            ..addOval(Rect.fromCenter(
              center: Offset.zero,
              width: rx * 2,
              height: ry * 2,
            )),
          paint,
        );
        canvas.restore();
        continue;
      }

      final minor = normal.cross(major) * ratio;
      final steps = ((span / _twoPi) * 96).round().clamp(8, 192);
      final path = Path();
      for (var s = 0; s <= steps; s++) {
        final t = startParameter + span * s / steps;
        final point = center + major * math.cos(t) + minor * math.sin(t);
        final screen = Offset(toSx(point.x), toSy(point.y));
        if (s == 0) {
          path.moveTo(screen.dx, screen.dy);
        } else {
          path.lineTo(screen.dx, screen.dy);
        }
      }
      canvas.drawPath(path, paint);
    }
  }

  // 每顶点4个值(x,y,z,bulge), bulge=tan(包含角/4), 正为逆时针
  void _drawPolylines(
    Canvas canvas,
    rust.CadGeometry g,
    Paint Function(int, {bool fill}) paintFor,
    double Function(double) toSx,
    double Function(double) toSy,
  ) {
    final data = g.polylineData;
    final counts = g.polylineCounts;
    final closedFlags = g.polylineClosed;
    var cursor = 0;
    for (var i = 0; i < counts.length; i++) {
      final count = counts[i];
      if (count <= 0) continue;
      if (cursor + count * 4 > data.length) break;

      final isClosed = i < closedFlags.length && closedFlags[i];
      final paint = paintFor(_colorAt(g.polylineColors, i));

      final path = Path();
      final fx = toSx(data[cursor]);
      final fy = toSy(data[cursor + 1]);
      path.moveTo(fx, fy);
      var previous = Offset(fx, fy);
      for (var v = 1; v < count; v++) {
        final offset = cursor + v * 4;
        final next = Offset(toSx(data[offset]), toSy(data[offset + 1]));
        _addSegment(path, previous, next, data[offset - 1]);
        previous = next;
      }
      if (isClosed) {
        _addSegment(
            path, previous, Offset(fx, fy), data[cursor + (count - 1) * 4 + 3]);
        path.close();
      }
      canvas.drawPath(path, paint);
      cursor += count * 4;
    }
  }

  // 每条8个值(x,y,z,height,rotation,halign,valign,layer), 锚点是基线左端
  void _drawTexts(
    Canvas canvas,
    rust.CadGeometry g,
    Paint Function(int, {bool fill}) paintFor,
    double Function(double) toSx,
    double Function(double) toSy,
    double scale,
  ) {
    final data = g.textData;
    final values = g.textValues;
    for (var i = 0; i + 8 <= data.length; i += 8) {
      final index = i ~/ 8;
      if (index >= values.length) break;
      final text = values[index];
      if (text.trim().isEmpty) continue;

      final emSize = data[i + 3] * scale;
      // 屏幕上太小的字会糊成一团
      if (emSize < 4) continue;

      final anchorX = toSx(data[i]);
      final anchorY = toSy(data[i + 1]);
      final rotation = data[i + 4];
      final horizontal = data[i + 5].round();
      final vertical = data[i + 6].round();

      final tp = TextPainter(
        text: TextSpan(
          text: text,
          style: TextStyle(
            color: paintFor(_colorAt(g.textColors, index)).color,
            fontSize: emSize,
          ),
        ),
        textDirection: TextDirection.ltr,
      )..layout();

      final x = switch (horizontal) {
        1 => anchorX - tp.width / 2,
        2 => anchorX - tp.width,
        _ => anchorX,
      };
      final baseline =
          tp.computeDistanceToActualBaseline(TextBaseline.alphabetic);
      final y = switch (vertical) {
        1 => anchorY - tp.height,
        2 => anchorY - tp.height / 2,
        3 => anchorY,
        _ => anchorY - baseline,
      };

      if (rotation.abs() < 1e-6) {
        tp.paint(canvas, Offset(x, y));
        continue;
      }

      // 屏幕Y轴翻转, 旋转角取反
      canvas.save();
      canvas.translate(anchorX, anchorY);
      canvas.rotate(-rotation);
      tp.paint(canvas, Offset(x - anchorX, y - anchorY));
      canvas.restore();
    }
  }

  void _addSegment(Path path, Offset from, Offset to, double bulge) {
    if (bulge.abs() < 1e-9) {
      path.lineTo(to.dx, to.dy);
      return;
    }
    final dx = to.dx - from.dx;
    final dy = to.dy - from.dy;
    final chord = math.sqrt(dx * dx + dy * dy);
    if (chord < 1e-9) {
      path.lineTo(to.dx, to.dy);
      return;
    }
    final included = 4 * math.atan(bulge);
    final radius = (chord / (2 * math.sin(included / 2))).abs();
    // 方向对齐Avalonia: bulge>0取CounterClockwise
    path.arcToPoint(
      to,
      radius: Radius.circular(radius),
      largeArc: included.abs() > math.pi,
      clockwise: bulge < 0,
    );
  }

  static int _colorAt(Uint32List colors, int index) =>
      index < colors.length ? colors[index] : 0;

  static double _at(Float64List array, int index) =>
      index < array.length ? array[index] : 0;
}

class _Vec {
  const _Vec(this.x, this.y, this.z);

  final double x;
  final double y;
  final double z;

  _Vec operator +(_Vec o) => _Vec(x + o.x, y + o.y, z + o.z);
  _Vec operator -(_Vec o) => _Vec(x - o.x, y - o.y, z - o.z);
  _Vec operator *(double k) => _Vec(x * k, y * k, z * k);
  double dot(_Vec o) => x * o.x + y * o.y + z * o.z;
  _Vec cross(_Vec o) =>
      _Vec(y * o.z - z * o.y, z * o.x - x * o.z, x * o.y - y * o.x);
}
