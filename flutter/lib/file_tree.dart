import 'dart:convert';
import 'dart:io';
import 'dart:isolate';

import 'package:file_picker/file_picker.dart';
import 'package:path/path.dart' as p;
import 'package:saf/saf.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'models.dart';

abstract class FileTreeSource {
  Future<String?> pickRoot();
  Future<List<Artifact>> listChildren(String parentId);
  Future<int> folderSize(String rootId);
  Future<String?> restoreRoot();
  Future<void> forgetRoot();
  Future<String> readFile(String id);
}

FileTreeSource createSource() {
  if (Platform.isAndroid) return AndroidSafSource();
  return DesktopIoSource();
}

class DesktopIoSource implements FileTreeSource {
  @override
  Future<String?> pickRoot() => FilePicker.platform.getDirectoryPath();

  @override
  Future<String?> restoreRoot() async => null;

  @override
  Future<void> forgetRoot() async {}

  @override
  Future<int> folderSize(String rootId) => Isolate.run(() async {
        var total = 0;
        await for (final e
            in Directory(rootId).list(recursive: true, followLinks: false)) {
          if (e is! File) continue;
          try {
            total += await e.length();
          } catch (_) {}
        }
        return total;
      });

  @override
  Future<List<Artifact>> listChildren(String parentId) async {
    final dir = Directory(parentId);
    final entries = await dir.list(followLinks: false).toList();
    entries.sort((a, b) {
      final ad = a is Directory;
      final bd = b is Directory;
      if (ad != bd) return ad ? -1 : 1;
      return p.basename(a.path).compareTo(p.basename(b.path));
    });
    final out = <Artifact>[];
    for (final e in entries) {
      var size = 0;
      int? modified;
      try {
        final stat = await e.stat();
        size = stat.size;
        modified = stat.modified.millisecondsSinceEpoch;
      } catch (_) {}
      out.add(Artifact(
        id: e.path,
        name: p.basename(e.path),
        path: e.path,
        isDirectory: e is Directory,
        size: size,
        lastModified: modified,
      ));
    }
    return out;
  }

  @override
  Future<String> readFile(String id) => File(id).readAsString();
}

class AndroidSafSource implements FileTreeSource {
  static const _rootKey = 'saf_root_uri';

  final Saf _saf = Saf();

  @override
  Future<String?> pickRoot() async {
    final dir = await _saf.pickDirectory();
    if (dir == null) return null;
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_rootKey, dir.uri);
    return dir.uri;
  }

  @override
  Future<String?> restoreRoot() async {
    final prefs = await SharedPreferences.getInstance();
    final saved = prefs.getString(_rootKey);
    if (saved == null) return null;
    final grants = await _saf.persistedPermissions();
    final granted = grants.any((g) => g.uri == saved && g.read);
    if (!granted) {
      await prefs.remove(_rootKey);
      return null;
    }
    return saved;
  }

  @override
  Future<void> forgetRoot() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_rootKey);
  }

  @override
  Future<int> folderSize(String rootId) async {
    var total = 0;
    for (final a in await listChildren(rootId)) {
      total += a.isDirectory ? await folderSize(a.id) : a.size;
    }
    return total;
  }

  @override
  Future<List<Artifact>> listChildren(String parentId) async {
    final docs = await _saf.list(parentId);
    final out = <Artifact>[];
    for (final d in docs) {
      out.add(Artifact(
        id: d.uri,
        name: d.name,
        path: d.uri,
        isDirectory: d.isDir,
        size: d.length,
        lastModified: d.lastModified,
      ));
    }
    out.sort((a, b) {
      if (a.isDirectory != b.isDirectory) return a.isDirectory ? -1 : 1;
      return a.name.compareTo(b.name);
    });
    return out;
  }

  @override
  Future<String> readFile(String id) async {
    final bytes = await _saf.readFileBytes(id);
    return utf8.decode(bytes, allowMalformed: true);
  }
}
