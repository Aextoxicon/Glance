# Glance

一个以工件为核心的小工具（自用也占用了一部分原因）
A small tool centered on workpieces (partly for personal use as well)

正在开发中，暂时不考虑 iOS
Currently under development, iOS is not being considered for the time being

目前基本上要加的功能参考自我自己的实际需求
Right now, the features I need to add are basically based on my own actual needs

因为这个鬼地方大概率没什么人看，文档等我做完以后补
Because this damn place probably doesn’t have many people looking, I’ll fill in the documentation after I finish it

## 技术栈 / Tech Stack

KMP+CMP
AndroidApp（`androidApp/`）
DesktopApp（`desktopApp/`，JVM）
Rust 底层库（`native/`），通过 **UniFFI** 生成 Kotlin 绑定
iOS 暂不考虑

## 快速开始 / Quick Start

### Desktop App

```sh
# 运行（会自动 cargo build 并生成 uniffi 绑定）
./gradlew :desktopApp:run
# 生成app
./gradlew :desktopApp:createDistributable
#生成exe
./gradlew :desktopApp:packageExe
```

### Android App

```sh
# 构建 debug APK
./gradlew :androidApp:assembleDebug
# 产物在 androidApp/build/outputs/apk/debug/androidApp-debug.apk
```

### 测试

```sh
# 运行 shared 模块的 JVM 测试（会加载 Rust 原生库）
./gradlew :shared:jvmTest
```

Rust 库构建后，UniFFI 会生成 JNA 绑定到 `shared/build/generated/uniffi/kotlin/`，由 `jvmMain` 和 `androidMain` 共用

note
```
Kotlin UI->parseCode(source, ext) [expect/actual]
  CodeParser.android.kt / .jvm.kt / .ios.kt [platform actual]
    UniFFI 桥接 (uniffi.uniffi_code_parser.parseCode)
      Rust lib.rs: parse_code()
        lang::get_grammar(ext) → GrammarDef { language, highlight_query }
          Parser::set_language(&language)  <-ABI 版本检查在此
            Parser::parse(source_bytes)
              Query::new() + QueryCursor::matches()
                返回 CodeParseResult
```