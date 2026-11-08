#!/usr/bin/env bash

export PATH="/Users/lwhs/.cargo/bin:/Users/lwhs/flutter-3.38.1/flutter/bin:$PATH"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

pushd "$SCRIPT_DIR/../native" >/dev/null
cargo ndk -t arm64-v8a -t armeabi-v7a -t x86_64 -o "$SCRIPT_DIR/android/app/src/main/jniLibs" build --release
popd >/dev/null

flutter build apk --release --split-per-abi

DESKTOP_APK_DIR="$HOME/Desktop/Glance/Android"
mkdir -p "$DESKTOP_APK_DIR"
cp -f "$SCRIPT_DIR/build/app/outputs/flutter-apk"/*.apk "$DESKTOP_APK_DIR"
