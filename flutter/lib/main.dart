import 'dart:async';
import 'dart:io' show Platform;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'generated/frb_generated.dart';
import 'view_model.dart';
import 'views/main_view.dart';

const _cjkFontFallback = [
  'PingFang SC',
  'Microsoft YaHei',
  'Hiragino Sans GB',
  'Heiti SC',
  'SimHei',
  'sans-serif',
];

const _black = Color(0xFF000000);
const _white = Color(0xFFFFFFFF);
const _lightSurfaceVariant = Color(0xFFF2F2F2);
const _lightOutline = Color(0xFFE0E0E0);
const _darkSurfaceVariant = Color(0xFF1A1A1A);
const _darkOutline = Color(0xFF2A2A2A);

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  // 首帧只等设置: 它决定主题, 不等会先闪默认主题再跳变
  await mainViewModel.loadSettings();

  // 加载Rust库不改变首帧外观, 挪到首帧后, 解析前由VM自等
  mainViewModel.attachRustInit(_initRust());

  // 热切换的openUri推送要尽早接上, 否则启动期丢事件
  const intentChannel = MethodChannel('glance/intent');
  intentChannel.setMethodCallHandler((call) async {
    if (call.method == 'openUri') {
      final uri = call.arguments as String?;
      if (uri != null) await mainViewModel.openSingleFileUri(uri);
    }
  });

  // 外部intent决定首帧该显示什么, 先取到再runApp
  final initialUri = await _initialIntentUri(intentChannel);
  if (initialUri != null) await mainViewModel.openSingleFileUri(initialUri);

  runApp(const GlanceApp());
  if (initialUri == null) unawaited(mainViewModel.restoreWorkspace());
}

Future<void> _initRust() async {
  try {
    await RustLib.init();
  } catch (e) {
    debugPrint('RustLib.init failed, fallback to mock content: $e');
  }
}

// 外部VIEW/分享uri
Future<String?> _initialIntentUri(MethodChannel intentChannel) async {
  if (!Platform.isAndroid) return null;
  try {
    return await intentChannel.invokeMethod<String>('getInitialUri');
  } catch (_) {
    return null;
  }
}

class GlanceApp extends StatelessWidget {
  const GlanceApp({super.key});

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: mainViewModel.themeListenable,
      builder: (context, _) {
        final brightness = mainViewModel.themeMode == AppThemeMode.system
            ? WidgetsBinding.instance.platformDispatcher.platformBrightness
            : (mainViewModel.themeMode == AppThemeMode.dark
                ? Brightness.dark
                : Brightness.light);
        return MaterialApp(
          title: 'Glance',
          theme: ThemeData(
            useMaterial3: true,
            fontFamilyFallback: _cjkFontFallback,
            colorScheme: ColorScheme.fromSeed(seedColor: _black).copyWith(
              primary: _black,
              onPrimary: _white,
              surface: _white,
              onSurface: _black,
              surfaceContainerHighest: _lightSurfaceVariant,
              outlineVariant: _lightOutline,
            ),
            scaffoldBackgroundColor: _white,
          ),
          darkTheme: ThemeData(
            useMaterial3: true,
            brightness: Brightness.dark,
            fontFamilyFallback: _cjkFontFallback,
            colorScheme: ColorScheme.fromSeed(
              seedColor: _white,
              brightness: Brightness.dark,
            ).copyWith(
              primary: _white,
              onPrimary: _black,
              surface: _black,
              onSurface: _white,
              surfaceContainerHighest: _darkSurfaceVariant,
              outlineVariant: _darkOutline,
            ),
            scaffoldBackgroundColor: _black,
          ),
          themeMode:
              brightness == Brightness.dark ? ThemeMode.dark : ThemeMode.light,
          home: const MainView(),
          debugShowCheckedModeBanner: false,
        );
      },
    );
  }
}
