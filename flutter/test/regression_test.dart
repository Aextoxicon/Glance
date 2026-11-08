import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:glance/file_tree.dart';
import 'package:glance/generated/frb_generated.dart';
import 'package:glance/models.dart';
import 'package:glance/settings_store.dart';
import 'package:glance/view_model.dart';
import 'package:glance/views/code_view.dart';
import 'package:glance/views/file_tree.dart';
import 'package:glance/views/main_view.dart';

/// 固定结构的假源: root -> [fileA(文件), dirA]; dirA -> [fileB, dirB];
/// dirB -> [fileC]
class FakeSource implements FileTreeSource {
  final Map<String, List<Artifact>> _tree;
  final Map<String, String> _files;
  final Set<String> _binary;

  FakeSource(this._tree, [this._files = const {}, this._binary = const {}]);

  @override
  Future<String?> pickRoot() async => 'root';
  @override
  Future<String?> restoreRoot() async => 'root';
  @override
  Future<void> forgetRoot() async {}

  @override
  Future<int> folderSize(String rootId) async => 0;

  @override
  Future<List<Artifact>> listChildren(String parentId) async =>
      _tree[parentId] ?? const [];

  @override
  Future<String> readFile(String id) async => _files[id] ?? 'fn main() {}\n';

  @override
  Future<bool> isBinary(String id) async => _binary.contains(id);
}

Artifact file(String id) =>
    Artifact(id: id, name: id, path: id, isDirectory: false, size: 100);

Artifact dir(String id) =>
    Artifact(id: id, name: id, path: id, isDirectory: true, size: 0);

void main() {
  setUpAll(() async {
    await RustLib.init();
  });

  final fakeTree = <String, List<Artifact>>{
    'root': [file('main.rs'), dir('dirA')],
    'dirA': [file('fileB'), dir('dirB')],
    'dirB': [file('fileC')],
  };

  test('expandAll展开从未手动展开过的目录', () async {
    final vm = MainViewModel(FakeSource(fakeTree));
    await vm.restoreWorkspace();

    expect(vm.treeItems, hasLength(2));
    // 初始全部未展开、未加载
    expect(vm.treeItems[1].artifact.name, 'dirA');
    expect(vm.treeItems[1].isExpanded, isFalse);
    expect(vm.treeItems[1].loaded, isFalse);

    await vm.expandAll();

    final dirA = vm.treeItems[1];
    expect(dirA.isExpanded, isTrue);
    expect(dirA.loaded, isTrue);
    expect(dirA.children, hasLength(2));
    final dirB = dirA.children[1];
    expect(dirB.artifact.name, 'dirB');
    expect(dirB.isExpanded, isTrue);
    expect(dirB.loaded, isTrue);
    expect(dirB.children, hasLength(1));
  });

  test('refreshTree保留已展开目录态并反映子目录文件变化', () async {
    final tree = <String, List<Artifact>>{
      'root': [file('main.rs'), dir('dirA')],
      'dirA': [file('fileB'), dir('dirB')],
      'dirB': [file('fileC')],
    };
    final vm = MainViewModel(FakeSource(tree));
    await vm.restoreWorkspace();
    await vm.expandAll();

    final dirA = vm.treeItems[1];
    expect(dirA.isExpanded, isTrue);
    expect(dirA.loaded, isTrue);

    // 在已展开目录dirA下新增文件, 刷新后应可见(证明深刷新生效)
    tree['dirA'] = [...tree['dirA']!, file('newFile')];
    await vm.refreshTree();

    final dirA2 = vm.treeItems[1];
    expect(dirA2.isExpanded, isTrue, reason: '刷新后dirA应保持展开');
    expect(dirA2.loaded, isTrue, reason: '刷新后dirA应保持已加载');
    expect(dirA2.children.any((c) => c.artifact.id == 'newFile'), isTrue,
        reason: '新增文件应出现在刷新后的子列表');
    final dirB2 = dirA2.children.firstWhere((c) => c.artifact.id == 'dirB');
    expect(dirB2.isExpanded, isTrue, reason: '嵌套目录dirB应保持展开');
    expect(dirB2.loaded, isTrue, reason: '嵌套目录dirB应保持已加载');
  });

  test('refreshTree保留选中态并重新挂接selectedItem', () async {
    final vm = MainViewModel(FakeSource(fakeTree));
    await vm.restoreWorkspace();
    vm.selectItem(vm.treeItems[0]); // 选中main.rs
    expect(vm.selectedItem?.artifact.id, 'main.rs');

    await vm.refreshTree();

    final mainRs = vm.treeItems[0];
    expect(mainRs.isSelected, isTrue, reason: '选中态应按id保留');
    expect(vm.selectedItem, same(mainRs), reason: 'selectedItem应挂回新树对象');
    expect(vm.selectedItem?.artifact.id, 'main.rs');
  });

  testWidgets('点开文件后CodeContentView无主题色块/下划线装饰', (tester) async {
    final vm = MainViewModel(FakeSource(fakeTree));
    await vm.restoreWorkspace();
    // 选中一个文件
    vm.selectItem(vm.treeItems[0]);

    await tester.pumpWidget(MaterialApp(
      theme: ThemeData(useMaterial3: true),
      home: Scaffold(body: CodePreviewPanel(vm: vm)),
    ));
    await tester.pumpAndSettle();

    // 应显示真实解析内容(fn main)
    expect(find.textContaining('fn main'), findsOneWidget);

    // 检查是否存在带颜色背景的DecoratedBox/Container(主题色块)
    final colored = tester.widgetList<Container>(find.byType(Container)).where((c) {
      final deco = c.decoration;
      return deco is BoxDecoration && deco.color != null && deco.color != Colors.transparent;
    });
    // 非透明色背景块不应出现在代码内容区
    expect(colored, isEmpty, reason: '代码区不应出现主题色背景块');

    // 检查是否有下划线装饰的文字
    final underlined = tester.widgetList<Text>(find.byType(Text)).where((t) {
      final deco = t.style?.decoration;
      return deco == TextDecoration.underline || deco == TextDecoration.lineThrough;
    });
    expect(underlined, isEmpty, reason: '代码区不应出现下划线装饰文字');
  });

  testWidgets('高亮渲染: fn关键字与main函数出现非默认颜色span', (tester) async {
    final vm = MainViewModel(FakeSource(fakeTree));
    await vm.restoreWorkspace();
    vm.selectItem(vm.treeItems[0]); // fileA -> 'fn main() {}\n'

    await tester.pumpWidget(MaterialApp(
      theme: ThemeData(useMaterial3: true),
      home: Scaffold(body: CodePreviewPanel(vm: vm)),
    ));
    await tester.pumpAndSettle();

    // 至少应有一个span的颜色不同于该Text的基础色(证明高亮生效)
    final texts = tester.widgetList<Text>(find.byType(Text));
    var colored = false;
    for (final t in texts) {
      final base = t.style?.color;
      final span = t.textSpan;
      if (span == null) continue;
      for (final s in _flattenSpans(span)) {
        final c = s.style?.color;
        if (c != null && c != base) colored = true;
      }
    }
    expect(colored, isTrue, reason: '应出现高亮颜色span');
  });

  testWidgets('expandAll后文件树UI显示出从未展开过的目录内容', (tester) async {
    final vm = MainViewModel(FakeSource(fakeTree));
    await vm.restoreWorkspace();
    await vm.expandAll();

    await tester.pumpWidget(MaterialApp(
      theme: ThemeData(useMaterial3: true),
      home: Scaffold(body: FileTreePanel(vm: vm)),
    ));
    await tester.pumpAndSettle();

    // dirA展开后应看到fileB与dirB；dirB展开后应看到fileC
    expect(find.text('main.rs'), findsOneWidget);
    expect(find.text('dirA'), findsOneWidget);
    expect(find.text('fileB'), findsOneWidget);
    expect(find.text('dirB'), findsOneWidget);
    expect(find.text('fileC'), findsOneWidget);
  });

  testWidgets('WideLayout选中文件后无黄色下划线装饰文字', (tester) async {
    final vm = MainViewModel(FakeSource(fakeTree));
    await vm.restoreWorkspace();
    vm.selectItem(vm.treeItems[0]);

    await tester.pumpWidget(MaterialApp(
      theme: ThemeData(useMaterial3: true),
      home: WideLayout(vm: vm),
    ));
    await tester.pumpAndSettle();

    // 全界面检查: 不应有黄色(amber/yellow)系颜色的文字或下划线装饰
    // 遍历Text的textSpan树(高亮span在children里), 基础style也要查
    final texts = tester.widgetList<Text>(find.byType(Text));
    for (final t in texts) {
      _checkStyle(t.style, '${t.data}');
      final span = t.textSpan;
      if (span != null) {
        for (final s in _flattenSpans(span)) {
          _checkStyle(s.style, s.toPlainText());
        }
      }
    }
  });
  test('requestScrollToOutline按UTF-8字节偏移换算行号', () {
    final vm = MainViewModel(FakeSource(fakeTree));
    // 三个中文字符各3字节, 第二行含多字节字符
    vm.selectedContent = 'fn a() {}\nlet s = "中文中";\nfn b() {}\n';
    vm.requestScrollToOutline(const OutlineNode(
        kind: '', name: '', detail: '', startByte: 15, children: []));
    // "fn a() {}\n" = 10字节, 第二行前缀 'let s= "' 9字节 => 19 > 15, 命中第二行
    expect(vm.outlineScrollTargetLine, 1);

    vm.selectedContent = 'a\nb\nc\n';
    vm.requestScrollToOutline(const OutlineNode(
        kind: '', name: '', detail: '', startByte: 4, children: []));
    expect(vm.outlineScrollTargetLine, 2);

    vm.selectedContent = 'a\nb\nc\n';
    vm.requestScrollToOutline(const OutlineNode(
        kind: '', name: '', detail: '', startByte: 999, children: []));
    expect(vm.outlineScrollTargetLine, 2);
  });

  testWidgets('二进制文件被探针拦截并显示无法预览', (tester) async {
    // 把main.rs标记为二进制(这是故意为之), 验证探针拦截+不喂给解析
    final vm = MainViewModel(FakeSource(fakeTree, const {}, {'main.rs'}));
    await vm.restoreWorkspace();
    vm.selectItem(vm.treeItems[0]); // main.rs

    await tester.pumpWidget(MaterialApp(
      theme: ThemeData(useMaterial3: true),
      home: Scaffold(body: CodePreviewPanel(vm: vm)),
    ));
    await tester.pumpAndSettle();

    expect(find.text('二进制文件，无法预览'), findsOneWidget,
        reason: '应显示无法预览提示而非代码内容');
    expect(vm.selectedContent, isNull, reason: '二进制文件不应被读入内容');
    expect(vm.codeParseResult, isNull, reason: '二进制文件不应喂给解析');
    expect(vm.currentOutline, isNull, reason: '二进制文件不应生成大纲');

    // 选中普通文本文件后仍可正常预览(fn main)
    final textVm = MainViewModel(FakeSource(fakeTree));
    await textVm.restoreWorkspace();
    textVm.selectItem(textVm.treeItems[0]);
    await tester.pumpWidget(MaterialApp(
      theme: ThemeData(useMaterial3: true),
      home: Scaffold(body: CodePreviewPanel(vm: textVm)),
    ));
    await tester.pumpAndSettle();
    expect(find.textContaining('fn main'), findsOneWidget,
        reason: '普通文本文件应正常预览');
  });

  testWidgets('点大纲节点后CodeContentView滚动到对应行', (tester) async {
    final lines = List.generate(200, (i) => 'line $i');
    final src = FakeSource(fakeTree, {
      'main.rs': '${lines.join('\n')}\n',
    });
    final vm = MainViewModel(src);
    await vm.restoreWorkspace();
    vm.selectItem(vm.treeItems[0]);

    await tester.pumpWidget(MaterialApp(
      theme: ThemeData(useMaterial3: true),
      home: Scaffold(
        body: ListenableBuilder(
          listenable: vm,
          builder: (context, _) => CodePreviewPanel(vm: vm),
        ),
      ),
    ));
    await tester.pumpAndSettle();

    // 设定滚动目标后重建, 应跳到第150行
    vm.requestScrollToLine(150);
    await tester.pumpAndSettle();

    // 第150行应出现在视口内(视口约几十行, 距目标行的行号差应很小)
    expect(vm.outlineScrollTargetLine, isNull, reason: '目标行应被消费');
    final textWidgets = tester.widgetList<Text>(find.byType(Text));
    var found = false;
    for (final t in textWidgets) {
      if (t.data == 'line 150' ||
          t.textSpan?.toPlainText() == 'line 150') {
        found = true;
      }
    }
    expect(found, isTrue, reason: '视口内应出现line 150');
  });

  testWidgets('长行不折行, 通过横向滚动可见', (tester) async {
    final longLine = 'x' * 300;
    final content = 'short\n$longLine\nend\n';
    final src = FakeSource(fakeTree, {'main.rs': content});
    final vm = MainViewModel(src);
    await vm.restoreWorkspace();
    vm.selectItem(vm.treeItems[0]);

    await tester.pumpWidget(MaterialApp(
      theme: ThemeData(useMaterial3: true),
      home: Scaffold(
        body: SizedBox(
          width: 400,
          child: ListenableBuilder(
            listenable: vm,
            builder: (context, _) => CodePreviewPanel(vm: vm),
          ),
        ),
      ),
    ));
    await tester.pumpAndSettle();

    // 存在横向滚动结构
    final hScroll = tester
        .widgetList<SingleChildScrollView>(find.byType(SingleChildScrollView))
        .where((s) => s.scrollDirection == Axis.horizontal);
    expect(hScroll, isNotEmpty, reason: '代码区应有横向滚动');

    // 内容宽度应超过 400 视口(长行未折行), 且能横向拖出末尾字符
    final sizing = tester.widget<SizedBox>(
        find.descendant(
            of: find.byType(SingleChildScrollView),
            matching: find.byType(SizedBox)));
    expect(sizing.width, greaterThan(400), reason: '内容宽度应超过视口');

    await tester.drag(find.byType(SingleChildScrollView).first,
        const Offset(-2000, 0));
    await tester.pumpAndSettle();
    // 拖到最右后应可见行尾(x*300 的末尾) —— 通过行文本未被截成多行验证:
    // 若折行, 'end' 行的y位置会下移超出固定行高; 这里直接验证渲染树中长行span仍是一行
    final lineText = tester.widgetList<Text>(find.byType(Text));
    final target = lineText.where((t) =>
        t.textSpan?.toPlainText() == longLine ||
        t.data == longLine);
    expect(target, isNotEmpty);
  });

  test('诊断基准模式开启后开文件夹自动遍历并逐文件计时并导出日志', () async {
    TestWidgetsFlutterBinding.ensureInitialized();
    final vm = MainViewModel(FakeSource(fakeTree));
    vm.traceDirOverride = Directory.systemTemp.path;
    vm.toggleBenchmarkMode();
    await vm.restoreWorkspace(); // _openRoot末尾自动触发runBenchmarkSequence

    // 自动序列跑完会调用exportTraceLog, 将messageText置为 "日志已导出: ..."
    final deadline = DateTime.now().add(const Duration(seconds: 15));
    while (!(vm.messageText ?? '').startsWith('日志已导出') &&
        DateTime.now().isBefore(deadline)) {
      // 测试无 UI 消费者, 手动驱动诊断滚动完成以推进序列
      vm.notifyDiagnosticScrollDone();
      await Future.delayed(const Duration(milliseconds: 20));
    }
    expect(vm.messageText, startsWith('日志已导出'),
        reason: '诊断基准序列跑完并自动导出');

    final path = vm.messageText!.substring('日志已导出: '.length);
    final log = File(path).readAsStringSync();
    for (final need in [
      'bench.start',
      'bench.expand.start',
      'bench.expand.end',
      'bench.end',
      'select.start',
      'select.painted',
    ]) {
      expect(log, contains(need), reason: '日志应含标记 $need');
    }

    final jsonPath = path.replaceAll('.csv', '.summary.json');
    final summary = File(jsonPath).readAsStringSync();
    expect(summary, contains('"opens"'), reason: '应导出汇总json');
  });

  test('设置持久化: JSON文件保存后loadSettings恢复主题/字号/记住目录, 关闭后不恢复工作区', () async {
    final dir = await Directory.systemTemp.createTemp('glance_settings');
    final store = SettingsStore(directoryOverride: dir.path);
    final vm = MainViewModel(FakeSource(fakeTree), store);
    await vm.loadSettings();
    expect(vm.themeMode, AppThemeMode.system);
    expect(vm.rememberLastDir, isTrue);

    vm.selectThemeMode(AppThemeMode.dark);
    vm.selectCodeFontSize(16);
    vm.selectRememberLastDir(false);
    await vm.settingsStore.save(AppSettings(
      themeMode: vm.themeMode.name,
      codeFontSize: vm.codeFontSize,
      rememberLastDir: vm.rememberLastDir,
    ));

    final vm2 = MainViewModel(FakeSource(fakeTree), store);
    await vm2.loadSettings();
    expect(vm2.themeMode, AppThemeMode.dark);
    expect(vm2.codeFontSize, 16);
    expect(vm2.rememberLastDir, isFalse);
    await vm2.restoreWorkspace();
    expect(vm2.hasWorkspace, isFalse, reason: '关闭记住目录后不应恢复工作区');
  });

  test('SAF目录名解码: URL编码的tree uri取末段目录名', () {
    expect(
        nameOfId(
            'content://com.android.externalstorage.documents/tree/primary%3ADownload%2Fcollisions-kmp-src'),
        'collisions-kmp-src');
    expect(
        nameOfId(
            'content://com.android.externalstorage.documents/tree/primary%3A%E5%86%99%E5%AD%98'),
        '写存');
  });
}

void _checkStyle(TextStyle? style, String label) {
  final deco = style?.decoration;
  expect(
    deco == TextDecoration.underline || deco == TextDecoration.lineThrough,
    isFalse,
    reason: '不应出现下划线装饰文字: $label',
  );
  final color = style?.color;
  if (color != null) {
    expect(
      color.toARGB32() == 0xFFFFEB3B ||
          color.toARGB32() == 0xFFFFC107 ||
          color.toARGB32() == 0xFFFFF9C4 ||
          color.toARGB32() == 0xFFFFF59D,
      isFalse,
      reason: '不应出现黄色文字: $label',
    );
  }
}

Iterable<TextSpan> _flattenSpans(InlineSpan root) sync* {
  if (root is! TextSpan) return;
  for (final child in root.children ?? const <InlineSpan>[]) {
    if (child is TextSpan) {
      yield child;
      yield* _flattenSpans(child);
    }
  }
}
