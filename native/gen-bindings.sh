#!/usr/bin/env bash
# 生成 C# 绑定。必须在 native/ 下运行：bindgen 内部要走 cargo metadata 读 crate 元数据
set -euo pipefail

cd "$(dirname "$0")"

LIB="target/release/uniffi_code_parser.dll"
OUT="../Glance/Native"

[ -f "$LIB" ] || { echo "缺少 $LIB，先跑 cargo build --release" >&2; exit 1; }

command -v uniffi-bindgen-cs >/dev/null 2>&1 || {
  echo "缺少 uniffi-bindgen-cs，安装：cargo install uniffi-bindgen-cs --version 0.11.0+v0.31.0" >&2
  exit 1
}

uniffi-bindgen-cs --library "$LIB" -o "$OUT" -n

# 都在 public 面上引用 OutlineNode，不改会 CS0051/CS0053
sed -i 's/^internal record CodeParseResult (/public record CodeParseResult (/' "$OUT/uniffi_code_parser.cs"
sed -i 's/^internal record OutlineNode (/public record OutlineNode (/' "$OUT/uniffi_code_parser.cs"

echo "已生成 $OUT/uniffi_code_parser.cs"
