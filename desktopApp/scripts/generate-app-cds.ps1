param(
    [Parameter(Mandatory)][string]$JavaBin,
    [Parameter(Mandatory)][string]$ClassPath,
    [Parameter(Mandatory)][string]$ResourcesDir,
    [Parameter(Mandatory)][string]$SkikoPath,
    [Parameter(Mandatory)][string]$JsaOut,
    [Parameter(Mandatory)][string]$ClassListPath
)

$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Path (Split-Path $ClassListPath) -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path $JsaOut) -Force | Out-Null

# Phase 1: launch the real app so the JVM records every class it loads.
# -XX:DumpLoadedClassList writes incrementally, so a hard kill loses nothing.
$launchArgs = @(
    "-Xshare:off",
    "-XX:DumpLoadedClassList=$ClassListPath",
    "-Dskiko.library.path=$SkikoPath",
    "-Dcompose.application.resources.dir=$ResourcesDir"
) + @("-cp", $ClassPath, "com.example.glance.MainKt")

$proc = Start-Process -FilePath $JavaBin -ArgumentList $launchArgs -PassThru

$deadline = (Get-Date).AddSeconds(25)
$lastSize = -1
$stableRounds = 0
while ((Get-Date) -lt $deadline) {
    if ($proc.HasExited) { break }
    Start-Sleep -Milliseconds 500
    $size = if (Test-Path $ClassListPath) { (Get-Item $ClassListPath).Length } else { 0 }
    if ($size -gt 0 -and $size -eq $lastSize) {
        $stableRounds++
        if ($stableRounds -ge 3) { break }
    } else {
        $stableRounds = 0
    }
    $lastSize = $size
}
if (-not $proc.HasExited) {
    Stop-Process -Id $proc.Id -Force
    Start-Sleep -Milliseconds 300
}

if (-not (Test-Path $ClassListPath) -or (Get-Item $ClassListPath).Length -eq 0) {
    throw "Phase 1 failed: class list empty at $ClassListPath (exit=$($proc.ExitCode))"
}
Write-Host "Class list: $((Get-Item $ClassListPath).Length) bytes"

# Phase 2: offline dump of the shared class archive.
$dumpArgs = @(
    "-Xshare:dump",
    "-XX:SharedArchiveFile=$JsaOut",
    "-XX:SharedClassListFile=$ClassListPath"
) + @("-cp", $ClassPath)

& $JavaBin $dumpArgs
if ($LASTEXITCODE -ne 0) {
    throw "Phase 2 failed: java -Xshare:dump exited $LASTEXITCODE"
}

if (-not (Test-Path $JsaOut)) {
    throw "Archive not created: $JsaOut"
}
Write-Host "Archive: $JsaOut ($((Get-Item $JsaOut).Length) bytes)"
