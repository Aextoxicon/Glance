import 'package:flutter/material.dart';

String formatSize(int bytes) {
  if (bytes <= 0) return '0 B';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  var i = 0;
  double v = bytes.toDouble();
  while (v >= 1024 && i < units.length - 1) {
    v /= 1024;
    i++;
  }
  final s = (v >= 10 || i == 0) ? v.toStringAsFixed(0) : v.toStringAsFixed(1);
  return '$s ${units[i]}';
}

IconData iconForFile(String name) {
  final ext = name.contains('.') ? name.split('.').last.toLowerCase() : '';
  switch (ext) {
    case 'dart':
    case 'kt':
    case 'kts':
    case 'java':
    case 'c':
    case 'cpp':
    case 'h':
    case 'hpp':
    case 'rs':
    case 'py':
    case 'js':
    case 'ts':
    case 'go':
    case 'rb':
      return Icons.code;
    case 'json':
    case 'yaml':
    case 'yml':
    case 'toml':
    case 'xml':
    case 'html':
    case 'css':
      return Icons.data_object;
    case 'md':
    case 'txt':
    case 'rst':
      return Icons.description;
    case 'png':
    case 'jpg':
    case 'jpeg':
    case 'gif':
    case 'webp':
    case 'bmp':
      return Icons.image;
    case 'pdf':
      return Icons.picture_as_pdf;
    case 'zip':
    case 'tar':
    case 'gz':
    case '7z':
    case 'rar':
      return Icons.archive;
    default:
      return Icons.insert_drive_file;
  }
}

const double outlineDrawerWidth = 300;

String outlineKindLabel(String kind) {
  if (kind.startsWith('class') ||
      kind.startsWith('record') ||
      kind.startsWith('annotation_type')) return '类';
  if (kind.startsWith('interface')) return '接口';
  if (kind.startsWith('struct')) return '结构体';
  if (kind.startsWith('enum')) return '枚举';
  if (kind.startsWith('trait')) return 'trait';
  if (kind.startsWith('impl')) return 'impl';
  if (kind.startsWith('method')) return '方法';
  if (kind.startsWith('function') || kind.startsWith('generator_function')) return '函数';
  if (kind.startsWith('namespace') || kind.startsWith('mod')) return '命名空间';
  if (kind.startsWith('type_alias') ||
      kind.startsWith('type_item') ||
      kind == 'type_declaration') return '类型';
  if (kind.startsWith('static') || kind.startsWith('const')) return '常量';
  if (kind == 'package_clause') return '包';
  final idx = kind.indexOf('_');
  return idx < 0 ? kind : kind.substring(0, idx);
}
