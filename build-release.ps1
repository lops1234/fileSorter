#!/usr/bin/env pwsh

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "Building FileTagger Single-File Release" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan

# Clean previous builds
Write-Host "Cleaning previous builds..." -ForegroundColor Yellow
if (Test-Path "bin\Release") { Remove-Item -Recurse -Force "bin\Release" }
if (Test-Path "obj\Release") { Remove-Item -Recurse -Force "obj\Release" }
if (Test-Path "release-single") { Remove-Item -Recurse -Force "release-single" }

# Restore packages
Write-Host "Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore

# Build and publish single-file executable
Write-Host "Building single-file release..." -ForegroundColor Yellow
$buildResult = dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o release-single

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Green
    Write-Host "Build completed successfully!" -ForegroundColor Green
    Write-Host "============================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "Single file executable created at:" -ForegroundColor White
    Write-Host "$PWD\release-single\FileTagger.exe" -ForegroundColor Cyan
    Write-Host ""
    
    $fileInfo = Get-Item "release-single\FileTagger.exe"
    $fileSizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
    Write-Host "File size: $($fileInfo.Length) bytes ($fileSizeMB MB)" -ForegroundColor White
    Write-Host ""
    Write-Host "Ready for distribution!" -ForegroundColor Green
    Write-Host "============================================" -ForegroundColor Green
    
    # Open the release folder
    Start-Process "release-single"
} else {
    Write-Host ""
    Write-Host "============================================" -ForegroundColor Red
    Write-Host "Build failed with error code $LASTEXITCODE" -ForegroundColor Red
    Write-Host "============================================" -ForegroundColor Red
}

Read-Host "Press Enter to continue"
