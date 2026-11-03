$ErrorActionPreference = "Stop"

$nativeDir = "$PSScriptRoot/../native"
$outDir = "$PSScriptRoot/build/windows/x64/runner/Release"
$dll = "$nativeDir/target/release/uniffi_code_parser.dll"

Write-Host "run cargo build --release"
Push-Location $nativeDir
cargo build --release
Pop-Location

Write-Host "copy $dll to $outDir"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Copy-Item $dll $outDir -Force

Write-Host "build for Windows"
flutter build windows --release
