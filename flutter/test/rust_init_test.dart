import 'package:flutter_test/flutter_test.dart';

import 'package:glance/generated/flutter_api.dart';
import 'package:glance/generated/frb_generated.dart';

void main() {
  test('RustLib.init加载原生库并解析', () async {
    await RustLib.init();
    final result = parseCodeFrb(source: 'fn main() {}', filename: 'main.rs');
    expect(result.outline.any((n) => n.name == 'main'), isTrue,
        reason: '应解析出main函数大纲');
  });
}
