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

// fromSeed(黑/白)会把未覆写的色染成青绿或粉白, 所以把用到的色一次铺满
const _lightSurfaceContainerLowest = _white;
const _lightSurfaceContainerLow = Color(0xFFFAFAFA);
const _lightSurfaceContainer = Color(0xFFF5F5F5);
const _lightSurfaceContainerHigh = Color(0xFFEFEFEF);
const _lightSurfaceContainerHighest = Color(0xFFE9E9E9);
const _lightPrimaryContainer = Color(0xFFD8D8D8);
const _lightOnPrimaryContainer = Color(0xFF111111);
const _lightSecondaryContainer = Color(0xFFE4E4E4);
const _lightOnSurfaceVariant = Color(0xFF4A4A4A);
const _lightOutline = Color(0xFF8C8C8C);
const _lightOutlineVariant = Color(0xFFE0E0E0);

const _darkSurfaceContainerLowest = Color(0xFF050505);
const _darkSurfaceContainerLow = Color(0xFF0F0F0F);
const _darkSurfaceContainer = Color(0xFF161616);
const _darkSurfaceContainerHigh = Color(0xFF1E1E1E);
const _darkSurfaceContainerHighest = Color(0xFF262626);
const _darkPrimaryContainer = Color(0xFF3A3A3A);
const _darkOnPrimaryContainer = Color(0xFFF0F0F0);
const _darkSecondaryContainer = Color(0xFF2E2E2E);
const _darkOnSurfaceVariant = Color(0xFFAFAFAF);
const _darkOutline = Color(0xFF6E6E6E);
const _darkOutlineVariant = Color(0xFF2A2A2A);

ColorScheme _lightScheme() => ColorScheme.fromSeed(seedColor: _black).copyWith(
      primary: _black,
      onPrimary: _white,
      primaryContainer: _lightPrimaryContainer,
      onPrimaryContainer: _lightOnPrimaryContainer,
      secondary: _lightOnSurfaceVariant,
      onSecondary: _white,
      secondaryContainer: _lightSecondaryContainer,
      onSecondaryContainer: _lightOnPrimaryContainer,
      surface: _white,
      onSurface: _black,
      onSurfaceVariant: _lightOnSurfaceVariant,
      surfaceContainerLowest: _lightSurfaceContainerLowest,
      surfaceContainerLow: _lightSurfaceContainerLow,
      surfaceContainer: _lightSurfaceContainer,
      surfaceContainerHigh: _lightSurfaceContainerHigh,
      surfaceContainerHighest: _lightSurfaceContainerHighest,
      outline: _lightOutline,
      outlineVariant: _lightOutlineVariant,
      surfaceTint: _white,
    );

ColorScheme _darkScheme() =>
    ColorScheme.fromSeed(seedColor: _white, brightness: Brightness.dark)
        .copyWith(
      primary: _white,
      onPrimary: _black,
      primaryContainer: _darkPrimaryContainer,
      onPrimaryContainer: _darkOnPrimaryContainer,
      secondary: _darkOnSurfaceVariant,
      onSecondary: _black,
      secondaryContainer: _darkSecondaryContainer,
      onSecondaryContainer: _darkOnPrimaryContainer,
      surface: _black,
      onSurface: _white,
      onSurfaceVariant: _darkOnSurfaceVariant,
      surfaceContainerLowest: _darkSurfaceContainerLowest,
      surfaceContainerLow: _darkSurfaceContainerLow,
      surfaceContainer: _darkSurfaceContainer,
      surfaceContainerHigh: _darkSurfaceContainerHigh,
      surfaceContainerHighest: _darkSurfaceContainerHighest,
      outline: _darkOutline,
      outlineVariant: _darkOutlineVariant,
      surfaceTint: _black,
    );

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  // 不等会先闪默认主题再跳变
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
            colorScheme: _lightScheme(),
            scaffoldBackgroundColor: _white,
          ),
          darkTheme: ThemeData(
            useMaterial3: true,
            brightness: Brightness.dark,
            fontFamilyFallback: _cjkFontFallback,
            colorScheme: _darkScheme(),
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
