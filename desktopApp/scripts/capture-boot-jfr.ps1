param(
    [int]$DurationSeconds = 30,
    [string]$JfrOut = "C:\temp\glance-boot.jfr"
)

$ErrorActionPreference = "Stop"

$appRoot = "C:\Users\Lwh20\Documents\GitHub\Collisions\desktopApp\build\compose\binaries\main\app\com.example.glance"
$launcher = Join-Path $appRoot "com.example.glance.exe"
$jfrTool  = "C:\Program Files\JetBrains\IntelliJ IDEA 2026.2.3\jbr\bin\jfr.exe"

if (-not (Test-Path $launcher)) {
    throw "launcher not found: $launcher. run :desktopApp:createDistributable first."
}
if (-not (Test-Path $jfrTool)) {
    throw "jfr.exe not found at $jfrTool"
}

# 设置profile含CPU flame/wall/classload/GC/alloc/safepoint，开销CPU +2-5%
New-Item -ItemType Directory -Path (Split-Path $JfrOut) -Force | Out-Null
$env:JAVA_TOOL_OPTIONS = "-XX:StartFlightRecording=settings=profile,filename=$JfrOut,duration=$DurationSeconds"

Write-Host "JAVA_TOOL_OPTIONS = $env:JAVA_TOOL_OPTIONS"
Write-Host "launcher          = $launcher"
Write-Host "JFR output        = $JfrOut"
Write-Host "duration          = ${DurationSeconds}s (auto-dump, app must stay open until then)"
Write-Host ""
Write-Host "launching now. you have ${DurationSeconds}s to select a folder and open a file if you want that phase captured too."
Write-Host ""

Start-Process -FilePath $launcher

# 等jfr落盘，最长duration+10s
$deadline = (Get-Date).AddSeconds($DurationSeconds + 10)
$written  = $false
while ((Get-Date) -lt $deadline) {
    if (Test-Path $JfrOut) {
        Start-Sleep -Seconds 1
        $size = (Get-Item $JfrOut).Length
        if ($size -gt 1024) {
            Write-Host "JFR written: $JfrOut ($size bytes)"
            $written = $true
            break
        }
    }
    Start-Sleep -Milliseconds 500
}

if (-not $written) {
    Write-Warning "JFR not generated at $JfrOut. check launcher inherited JAVA_TOOL_OPTIONS."
    exit 1
}

taskkill /IM com.example.glance.exe /T /F 2>$null | Out-Null

Write-Host ""
Write-Host "open in IntelliJ JFR viewer:"
Write-Host "  `"$jfrTool`" view `"$JfrOut`""
