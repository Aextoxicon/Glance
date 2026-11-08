$nativeDir = "$PSScriptRoot/../native"
$jniLibs = "$PSScriptRoot/android/app/src/main/jniLibs"

Push-Location $nativeDir
cargo ndk -t arm64-v8a -t armeabi-v7a -t x86_64 -o $jniLibs build --release
Pop-Location

flutter build apk --release --split-per-abi

$desktopApkDir = "$env:USERPROFILE\Desktop\Glance\Android"
New-Item -ItemType Directory -Force -Path $desktopApkDir | Out-Null
Copy-Item "$PSScriptRoot\build\app\outputs\flutter-apk\*.apk" -Destination $desktopApkDir -Force
