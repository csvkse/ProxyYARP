#!/usr/bin/env pwsh
# build-linux-aot.ps1 - Cross-compile AOT binary for Linux x64 using Docker

$ImageName = "proxyyarp-linux-builder"
$ProjectDir = "$PSScriptRoot/src/ProxyYARP"
$OutputDir = "$PSScriptRoot/publish/linux-x64"

Write-Host "======================================================"
Write-Host " ProxyYARP - Linux AOT Cross-Compile (Docker)"
Write-Host "======================================================"

# Build using Docker
docker build `
    -f "$ProjectDir/Dockerfile" `
    -t $ImageName `
    "$PSScriptRoot"

if ($LASTEXITCODE -ne 0) {
    Write-Error "Docker build failed!"
    exit 1
}

# Extract the binary
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$ContainerId = docker create $ImageName
docker cp "${ContainerId}:/app/." "$OutputDir/"
docker rm $ContainerId

Write-Host ""
Write-Host "======================================================"
Write-Host " Build complete! Output: $OutputDir"
Write-Host " Run: $OutputDir/ProxyYARP -p 8080 -k MyKey"
Write-Host "======================================================"
