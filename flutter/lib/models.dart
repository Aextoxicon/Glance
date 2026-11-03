import 'package:flutter/material.dart';

import 'utils.dart';

class Artifact {
  final String id;
  final String name;
  final String path;
  final bool isDirectory;
  final int size;
  final int? lastModified;

  const Artifact({
    required this.id,
    required this.name,
    required this.path,
    required this.isDirectory,
    required this.size,
    this.lastModified,
  });
}

class TreeItem {
  final Artifact artifact;
  bool isExpanded;
  bool isSelected;
  List<TreeItem> children;
  bool loaded;

  TreeItem({
    required this.artifact,
    this.isExpanded = false,
    this.isSelected = false,
    this.children = const [],
    this.loaded = false,
  });

  String get sizeDisplay => formatSize(artifact.size);

  IconData get icon {
    if (artifact.isDirectory) {
      return isExpanded ? Icons.folder_open : Icons.folder;
    }
    return iconForFile(artifact.name);
  }
}

class OutlineNode {
  final String kind;
  final String name;
  final String detail;
  final int startByte;
  final int endByte;
  final List<OutlineNode> children;

  const OutlineNode({
    required this.kind,
    required this.name,
    this.detail = '',
    this.startByte = 0,
    this.endByte = 0,
    this.children = const [],
  });
}
