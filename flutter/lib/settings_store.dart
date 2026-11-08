import 'dart:convert';
import 'dart:io';

import 'package:path_provider/path_provider.dart';

// 设置持久化: 应用数据目录/Glance/settings.json, 对齐Avalonia的SettingsStore
class AppSettings {
  AppSettings({
    this.themeMode = 'system',
    this.codeFontSize = 13,
    this.rememberLastDir = true,
  });

  factory AppSettings.fromJson(Map<String, dynamic> j) => AppSettings(
        themeMode: j['themeMode'] as String? ?? 'system',
        codeFontSize: j['codeFontSize'] as int? ?? 13,
        rememberLastDir: j['rememberLastDir'] as bool? ?? true,
      );

  String themeMode;
  int codeFontSize;
  bool rememberLastDir;

  Map<String, dynamic> toJson() => {
        'themeMode': themeMode,
        'codeFontSize': codeFontSize,
        'rememberLastDir': rememberLastDir,
      };
}

class SettingsStore {
  SettingsStore({String? directoryOverride}) : _directoryOverride = directoryOverride;

  final String? _directoryOverride;

  Future<String> _filePath() async {
    if (_directoryOverride != null) {
      return '$_directoryOverride/settings.json';
    }
    final base = await getApplicationSupportDirectory();
    return '${base.path}/Glance/settings.json';
  }

  Future<AppSettings> load() async {
    try {
      final file = File(await _filePath());
      if (await file.exists()) {
        return AppSettings.fromJson(
          jsonDecode(await file.readAsString()) as Map<String, dynamic>,
        );
      }
    } catch (_) {}
    return AppSettings();
  }

  Future<void> save(AppSettings settings) async {
    final path = await _filePath();
    await File(path).parent.create(recursive: true);
    await File(path).writeAsString(jsonEncode(settings.toJson()));
  }
}
