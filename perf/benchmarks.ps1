#Requires -Version 5.1
<#
.SYNOPSIS
  Practical local performance checks for the MapLarge file manager.

.DESCRIPTION
  Measures directory listing for the current root (page size 100) and a small
  resumable upload (256 KB). Does not fabricate results.

  Optional -LargeListing creates (once) and times a 100,000-file directory.

  Prerequisites:
    - Start the file manager first (for example: dotnet run --urls http://localhost:5120).

.PARAMETER BaseUrl
  Base URL of the running app. Default: http://localhost:5120

.PARAMETER StorageRoot
  Absolute path to FileData (or another storage root). Required with -LargeListing.

.PARAMETER LargeListing
  When set with -StorageRoot, times GET /api/files against a 100k-file folder.

.PARAMETER SkipUpload
  When set, skips the 256 KB resumable upload timing.

.EXAMPLE
  # Default: list current root + 256 KB upload against localhost:5120
  .\perf\benchmarks.ps1

.EXAMPLE
  # Custom base URL (app already running on another port)
  .\perf\benchmarks.ps1 -BaseUrl "http://localhost:7146"

.EXAMPLE
  # Listing only (no upload)
  .\perf\benchmarks.ps1 -SkipUpload

.EXAMPLE
  # Include the optional 100k-file directory benchmark
  .\perf\benchmarks.ps1 `
    -BaseUrl "http://localhost:5120" `
    -StorageRoot "C:\Development\MapLarge\TestProject_2025\TestProject\FileData" `
    -LargeListing

.EXAMPLE
  # 100k listing only, skip upload
  .\perf\benchmarks.ps1 `
    -StorageRoot "C:\Development\MapLarge\TestProject_2025\TestProject\FileData" `
    -LargeListing `
    -SkipUpload
#>
param(
    [string]$BaseUrl = "http://localhost:5120",
    [string]$StorageRoot = "",
    [switch]$LargeListing,
    [switch]$SkipUpload
)

$ErrorActionPreference = "Stop"

function Write-Result($name, $ms, $detail) {
    Write-Host ("{0,-40} {1,8:N0} ms  {2}" -f $name, $ms, $detail)
}

Write-Host "Base URL: $BaseUrl"
Write-Host "Ensure the app is running before executing this script."

# --- Listing with ~100 entries ---
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$list = Invoke-RestMethod -Uri "$BaseUrl/api/files?pageSize=100" -Method Get
$sw.Stop()
Write-Result "GET /api/files (current dir)" $sw.ElapsedMilliseconds ("items=$($list.totalItems)")

if ($LargeListing -and $StorageRoot) {
    $dir = Join-Path $StorageRoot "perf100k"
    if (-not (Test-Path $dir)) {
        Write-Host "Creating 100k tiny files under $dir (this can take several minutes)..."
        New-Item -ItemType Directory -Path $dir | Out-Null
        1..100000 | ForEach-Object {
            [System.IO.File]::WriteAllText((Join-Path $dir ("f{0:D6}.txt" -f $_)), "x")
        }
    }
    $sw.Restart()
    $big = Invoke-RestMethod -Uri "$BaseUrl/api/files?path=perf100k&pageSize=100&sort=Name" -Method Get
    $sw.Stop()
    Write-Result "GET /api/files 100k dir page" $sw.ElapsedMilliseconds ("totalItems=$($big.totalItems)")
}

if (-not $SkipUpload) {
    $bytes = New-Object byte[] (256KB)
    (New-Object Random).NextBytes($bytes)
    $sha = [System.BitConverter]::ToString([System.Security.Cryptography.SHA256]::Create().ComputeHash($bytes)).Replace("-", "").ToLowerInvariant()

    $sw.Restart()
    $session = Invoke-RestMethod -Uri "$BaseUrl/api/uploads" -Method Post -ContentType "application/json" -Body (@{
        destinationDirectory = ""
        fileName = ("perf-{0}.bin" -f [guid]::NewGuid().ToString("N"))
        fileSize = $bytes.Length
    } | ConvertTo-Json)
    $chunkUri = "$BaseUrl/api/uploads/$($session.uploadId)/chunk?offset=0"
    Invoke-RestMethod -Uri $chunkUri -Method Put -Headers @{ "X-Chunk-Checksum" = $sha } -ContentType "application/octet-stream" -Body $bytes | Out-Null
    Invoke-RestMethod -Uri "$BaseUrl/api/uploads/$($session.uploadId)/complete" -Method Post -ContentType "application/json" -Body "{}" | Out-Null
    $sw.Stop()
    Write-Result "Resumable upload 256KB" $sw.ElapsedMilliseconds ("uploadId=$($session.uploadId)")
}

Write-Host "Done."
