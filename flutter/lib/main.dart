import 'dart:async';

import 'package:flutter/material.dart';

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

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  unawaited(mainViewModel.restoreWorkspace());
  runApp(const GlanceApp());
}

class GlanceApp extends StatelessWidget {
  const GlanceApp({super.key});

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: mainViewModel,
      builder: (context, _) {
        final vm = mainViewModel;
        final brightness = vm.themeMode == AppThemeMode.system
            ? WidgetsBinding.instance.platformDispatcher.platformBrightness
            : (vm.themeMode == AppThemeMode.dark
                ? Brightness.dark
                : Brightness.light);
        return MaterialApp(
          title: 'Glance',
          theme: ThemeData(
            useMaterial3: true,
            fontFamilyFallback: _cjkFontFallback,
          ),
          darkTheme: ThemeData(
            useMaterial3: true,
            brightness: Brightness.dark,
            fontFamilyFallback: _cjkFontFallback,
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
