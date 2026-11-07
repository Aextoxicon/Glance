#!/usr/bin/env bash

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

pushd "$SCRIPT_DIR/../native" >/dev/null
cargo ndk -t arm64-v8a -t armeabi-v7a -t x86_64 -o "$SCRIPT_DIR/android/app/src/main/jniLibs" build --release
popd >/dev/null

flutter build apk --release --split-per-abi
