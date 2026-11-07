import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:glance/file_tree.dart';
import 'package:glance/generated/frb_generated.dart';
import 'package:glance/models.dart';
import 'package:glance/view_model.dart';
import 'package:glance/views/code_view.dart';
import 'package:glance/views/file_tree.dart';
import 'package:glance/views/main_view.dart';

/// 固定结构的假源: root -> [fileA(文件), dirA]; dirA -> [fileB, dirB];
/// dirB -> [fileC]
class FakeSource implements FileTreeSource {
  final Map<String, List<Artifact>> _tree;
  final Map<String, String> _files;

  FakeSource(this._tree, [this._files = const {}]);

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

  test('expandAll 展开从未手动展开过的目录', () async {
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

  test('refreshTree 保留已展开目录态并反映子目录文件变化', () async {
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

    // 在已展开目录 dirA 下新增文件, 刷新后应可见(证明深刷新生效)
    tree['dirA'] = [...tree['dirA']!, file('newFile')];
    await vm.refreshTree();

    final dirA2 = vm.treeItems[1];
    expect(dirA2.isExpanded, isTrue, reason: '刷新后 dirA 应保持展开');
    expect(dirA2.loaded, isTrue, reason: '刷新后 dirA 应保持已加载');
    expect(dirA2.children.any((c) => c.artifact.id == 'newFile'), isTrue,
        reason: '新增文件应出现在刷新后的子列表');
    final dirB2 = dirA2.children.firstWhere((c) => c.artifact.id == 'dirB');
    expect(dirB2.isExpanded, isTrue, reason: '嵌套目录 dirB 应保持展开');
    expect(dirB2.loaded, isTrue, reason: '嵌套目录 dirB 应保持已加载');
  });

  test('refreshTree 保留选中态并重新挂接 selectedItem', () async {
    final vm = MainViewModel(FakeSource(fakeTree));
    await vm.restoreWorkspace();
    vm.selectItem(vm.treeItems[0]); // 选中 main.rs
    expect(vm.selectedItem?.artifact.id, 'main.rs');

    await vm.refreshTree();

    final mainRs = vm.treeItems[0];
    expect(mainRs.isSelected, isTrue, reason: '选中态应按 id 保留');
    expect(vm.selectedItem, same(mainRs), reason: 'selectedItem 应挂回新树对象');
    expect(vm.selectedItem?.artifact.id, 'main.rs');
  });

  testWidgets('点开文件后 CodeContentView 无主题色块/下划线装饰', (tester) async {
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

    // 检查是否存在带颜色背景的 DecoratedBox/Container(主题色块)
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

  testWidgets('高亮渲染: fn 关键字与 main 函数出现非默认颜色 span', (tester) async {
    final vm = MainViewModel(FakeSource(fakeTree));
    await vm.restoreWorkspace();
    vm.selectItem(vm.treeItems[0]); // fileA -> 'fn main() {}\n'

    await tester.pumpWidget(MaterialApp(
      theme: ThemeData(useMaterial3: true),
      home: Scaffold(body: CodePreviewPanel(vm: vm)),
    ));
    await tester.pumpAndSettle();

    // 至少应有一个 span 的颜色不同于该 Text 的基础色(证明高亮生效)
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
    expect(colored, isTrue, reason: '应出现高亮颜色 span');
  });

  testWidgets('expandAll 后文件树 UI 显示出从未展开过的目录内容', (tester) async {
    final vm = MainViewModel(FakeSource(fakeTree));
    await vm.restoreWorkspace();
    await vm.expandAll();

    await tester.pumpWidget(MaterialApp(
      theme: ThemeData(useMaterial3: true),
      home: Scaffold(body: FileTreePanel(vm: vm)),
    ));
    await tester.pumpAndSettle();

    // dirA 展开后应看到 fileB 与 dirB；dirB 展开后应看到 fileC
    expect(find.text('main.rs'), findsOneWidget);
    expect(find.text('dirA'), findsOneWidget);
    expect(find.text('fileB'), findsOneWidget);
    expect(find.text('dirB'), findsOneWidget);
    expect(find.text('fileC'), findsOneWidget);
  });

  testWidgets('WideLayout 选中文件后无黄色下划线装饰文字', (tester) async {
    final vm = MainViewModel(FakeSource(fakeTree));
    await vm.restoreWorkspace();
    vm.selectItem(vm.treeItems[0]);

    await tester.pumpWidget(MaterialApp(
      theme: ThemeData(useMaterial3: true),
      home: WideLayout(vm: vm),
    ));
    await tester.pumpAndSettle();

    // 全界面检查: 不应有黄色(amber/yellow)系颜色的文字或下划线装饰
    // 遍历 Text 的 textSpan 树(高亮 span 在 children 里), 基础 style 也要查
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
  test('requestScrollToOutline 按 UTF-8 字节偏移换算行号', () {
    final vm = MainViewModel(FakeSource(fakeTree));
    // 三个中文字符各3字节, 第二行含多字节字符
    vm.selectedContent = 'fn a() {}\nlet s = "中文中";\nfn b() {}\n';
    vm.requestScrollToOutline(const OutlineNode(
        kind: '', name: '', detail: '', startByte: 15, children: []));
    // "fn a() {}\n" = 10字节, 第二行前缀 'let s = "' 9字节 => 19 > 15, 命中第二行
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

  testWidgets('点大纲节点后 CodeContentView 滚动到对应行', (tester) async {
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
    expect(found, isTrue, reason: '视口内应出现 line 150');
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
    // 若折行, 'end' 行的 y 位置会下移超出固定行高; 这里直接验证渲染树中长行 span 仍是一行
    final lineText = tester.widgetList<Text>(find.byType(Text));
    final target = lineText.where((t) =>
        t.textSpan?.toPlainText() == longLine ||
        t.data == longLine);
    expect(target, isNotEmpty);
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
