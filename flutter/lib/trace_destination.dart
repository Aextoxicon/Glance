import 'dart:io';

import 'package:flutter/services.dart';
import 'package:path_provider/path_provider.dart';

// trace日志落点: 桌面=下载目录/Glance-trace; Android写私有目录暂存, 发布时经MediaStore复制到公共Download/Glance-trace
class TraceDestination {
  static const _channel = MethodChannel('glance/trace');

  static Future<String> stagingDirectory() async {
    final base = await getApplicationDocumentsDirectory();
    final dir = Directory('${base.path}/Glance-trace');
    await dir.create(recursive: true);
    return dir.path;
  }

  // 导出后把产物挪到用户可见位置; Android发布失败或API<29返回null
  static Future<String?> publish(String filePath) async {
    if (Platform.isAndroid) {
      return _androidPublish(filePath);
    }
    return filePath;
  }

  static Future<String?> _androidPublish(String filePath) async {
    try {
      final published = await _channel.invokeMethod<String>('publish', {
        'path': filePath,
        'mime': 'text/csv',
      });
      if (published == null) return null;
      final summary = File(
        filePath.replaceAll(RegExp(r'\.csv$'), '.summary.json'),
      );
      if (await summary.exists()) {
        await _channel.invokeMethod<String>('publish', {
          'path': summary.path,
          'mime': 'application/json',
        });
        await summary.delete();
      }
      await File(filePath).delete();
      return published;
    } on PlatformException {
      return null;
    }
  }

  static Future<String> downloadsDirectory() async {
    final dir = await getDownloadsDirectory();
    if (dir == null) {
      throw const FileSystemException('下载目录不可用');
    }
    final target = Directory('${dir.path}/Glance-trace');
    await target.create(recursive: true);
    return target.path;
  }
}
