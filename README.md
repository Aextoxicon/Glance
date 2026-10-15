# Glance

一个以工件为核心的小工具（自用也占用了一部分原因）
A small tool centered on workpieces (partly for personal use as well)

正在开发中，Android 后续补，iOS 暂不考虑
Currently under development; Android to follow, iOS is not being considered for the time being

目前基本上要加的功能参考自我自己的实际需求
Right now, the features I need to add are basically based on my own actual needs

因为这个鬼地方大概率没什么人看，文档等我做完以后补
Because this damn place probably doesn’t have many people looking, I’ll fill in the documentation after I finish it

## 技术栈 / Tech Stack

Avalonia 12 + .NET 10（原 KMP+CMP 实现已重写为 Avalonia）
DesktopApp（`Glance/`，.NET 10，WinExe）
Rust 底层库（`native/`），通过 **UniFFI** 生成 C# 绑定（`LibraryImport` P/Invoke）
Android 后续补（`LocalFileSystem` 走 SAF），iOS 暂不考虑

## 快速开始 / Quick Start

### Desktop App

```sh
# 先构建 Rust 原生库（生成 cdylib；CopyNativeLib 会在 dotnet build 时拷到输出目录）
cd native && cargo build --release
# 运行 Glance
cd Glance.Desktop && dotnet run -c Debug
# 发布
cd Glance.Desktop && dotnet publish -c Release
```

### Android App（计划中 / Planned）

note: `LocalFileSystem` 的 SAF 版本 + `.so` 入 jniLibs + `MainActivity` 尚未实现。

### 测试 / Tests

note: 单元测试尚未接入（xUnit 待补）；Rust 侧测试在 `native/tests/`。

Rust 库构建后，UniFFI 会生成 C# 绑定到 `Glance/Native/uniffi_code_parser.cs`，由 P/Invoke（`LibraryImport("uniffi_code_parser")`）加载 `native/target/release/` 下的 cdylib

note
```
C# UI (Avalonia / XAML)
  MainViewModel.SelectFile(path)
    ILocalFileSystem.TryReadText(path)      [System.IO, desktop]
    ICodeParser.ParseCode(content, ext)     [UniffiCodeParser.cs]
      UniFFI 桥接 (uniffi_code_parser.parse_code)
        Rust lib.rs: parse_code()
          lang::get_grammar(ext) -> GrammarDef { language, highlight_query }
            Parser::set_language(&language)  <- ABI 版本检查在此
              Parser::parse(source_bytes)
                Query + QueryCursor
                  返回 CodeParseResult { content, highlights, outline }
  HighlightIndex (12B/条目 小端打包) -> AvaloniaEdit IVisualLineTransformer 上色
```
