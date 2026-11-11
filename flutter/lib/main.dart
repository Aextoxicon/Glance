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
  try {
    await RustLib.init();
  } catch (e) {
    debugPrint('RustLib.init failed, fallback to mock content: $e');
  }
  await mainViewModel.loadSettings();
  // 外部VIEW/分享intent: 冷启动经getInitialUri取, 热切换由MainActivity经openUri推送
  const intentChannel = MethodChannel('glance/intent');
  intentChannel.setMethodCallHandler((call) async {
    if (call.method == 'openUri') {
      final uri = call.arguments as String?;
      if (uri != null) await mainViewModel.openSingleFileUri(uri);
    }
  });
  final initialUri = Platform.isAndroid
      ? await intentChannel.invokeMethod<String>('getInitialUri')
      : null;
  if (initialUri != null) {
    await mainViewModel.openSingleFileUri(initialUri);
  } else {
    unawaited(mainViewModel.restoreWorkspace());
  }
  runApp(const GlanceApp());
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
