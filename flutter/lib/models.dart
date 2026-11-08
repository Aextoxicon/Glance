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
  bool isLoading;

  TreeItem({
    required this.artifact,
    this.isExpanded = false,
    this.isSelected = false,
    this.children = const [],
    this.loaded = false,
    this.isLoading = false,
  });

  String get sizeDisplay => formatSize(artifact.size);

  IconData get icon {
    if (artifact.isDirectory) {
      return isExpanded ? Icons.folder_open : Icons.folder;
    }
    return iconForFile(artifact.name);
  }
}

sealed class FlatTreeRow {
  final int depth;
  const FlatTreeRow(this.depth);
}

class ItemTreeRow extends FlatTreeRow {
  final TreeItem item;
  const ItemTreeRow(super.depth, this.item);
}

class LoadingTreeRow extends FlatTreeRow {
  const LoadingTreeRow(super.depth);
}

List<FlatTreeRow> flattenTree(List<TreeItem> items, [int depth = 0]) {
  final out = <FlatTreeRow>[];
  for (final item in items) {
    out.add(ItemTreeRow(depth, item));
    if (item.isExpanded) {
      if (item.isLoading) {
        out.add(LoadingTreeRow(depth + 1));
      } else if (item.loaded && item.children.isNotEmpty) {
        out.addAll(flattenTree(item.children, depth + 1));
      }
    }
  }
  return out;
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
