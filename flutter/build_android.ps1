$ErrorActionPreference = "Stop"

$nativeDir = "$PSScriptRoot/../native"
$jniLibs = "$PSScriptRoot/android/app/src/main/jniLibs"

if (-not (Get-Command cargo-ndk -ErrorAction SilentlyContinue)) {
    Write-Error "cargo-ndk未安装, 先跑cargo install cargo-ndk"
}

Write-Host "cross-compile 3 abi for jniLibs"
Push-Location $nativeDir
cargo ndk -t arm64-v8a -t armeabi-v7a -t x86_64 -o $jniLibs build --release
Pop-Location

Write-Host "build apk (release)"
flutter build apk --release
