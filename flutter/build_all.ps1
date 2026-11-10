$nativeDir = "$PSScriptRoot/../native"
$jniLibs = "$PSScriptRoot/android/app/src/main/jniLibs"
$outDir = "$PSScriptRoot/build/windows/x64/runner/Release"
$dll = "$nativeDir/target/release/uniffi_code_parser.dll"
$desktopApkDir = "$env:USERPROFILE/Desktop/Glance/Android"
$desktopWinDir = "$env:USERPROFILE/Desktop/Glance/Windows"

Push-Location $nativeDir
cargo ndk -t arm64-v8a -t armeabi-v7a -t x86_64 -o $jniLibs build --release
cargo build --release
Pop-Location

Push-Location $PSScriptRoot
flutter build apk --release --split-per-abi

New-Item -ItemType Directory -Force -Path $desktopApkDir | Out-Null
Copy-Item "$PSScriptRoot/build/app/outputs/flutter-apk/*.apk" -Destination $desktopApkDir -Force

flutter build windows --release

Copy-Item $dll $outDir -Force
Pop-Location

New-Item -ItemType Directory -Force -Path $desktopWinDir | Out-Null
Copy-Item "$outDir/*" -Destination $desktopWinDir -Recurse -Force

$deviceOnline = (adb devices | Select-String -Pattern "\tdevice").Count -gt 0
if ($deviceOnline) {
    adb install -r "$PSScriptRoot/build/app/outputs/flutter-apk/app-arm64-v8a-release.apk"
}
