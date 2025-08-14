# FileTagger - Single File Release Build Instructions

## Overview
This document explains how to build FileTagger as a single, self-contained executable file that requires no additional dependencies or .NET runtime installation.

## Prerequisites
- .NET 8.0 SDK installed
- Windows 10/11 (for building and running)

## Build Methods

### Method 1: Using Build Scripts (Recommended)

#### Option A: Batch Script
```bash
build-release.bat
```

#### Option B: PowerShell Script
```powershell
.\build-release.ps1
```

### Method 2: Manual dotnet CLI

```bash
# Clean previous builds
dotnet clean

# Restore packages
dotnet restore

# Build single-file release
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o release-single
```

## Output

After successful build, you'll find:
- **Single file**: `release-single/FileTagger.exe`
- **Size**: Approximately 80-120 MB (includes .NET runtime and all dependencies)
- **Dependencies**: None - completely self-contained

## Features of the Single-File Build

✅ **Self-contained**: No .NET runtime installation required  
✅ **Single executable**: Just one .exe file  
✅ **No PDB files**: Debug symbols are excluded  
✅ **Compressed**: Built-in compression for smaller file size  
✅ **Full functionality**: All features work identically to the regular build  

## Configuration Details

The single-file build is configured in `FileTagger.csproj` with these key settings:

```xml
<PublishSingleFile>true</PublishSingleFile>
<SelfContained>true</SelfContained>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
<DebugType>none</DebugType>
<DebugSymbols>false</DebugSymbols>
<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
```

## Distribution

The resulting `FileTagger.exe` can be distributed as-is:
- Copy to any Windows machine
- No installation required
- No additional files needed
- Runs directly from any folder

## Troubleshooting

### Build Fails
- Ensure .NET 8.0 SDK is installed: `dotnet --version`
- Clean the project: `dotnet clean`
- Try building in Debug first: `dotnet build`

### Runtime Issues
- The executable is built for Windows x64 only
- First run may be slightly slower (self-extraction)
- Antivirus software may scan the large executable

### File Size Concerns
- The large file size is normal for self-contained apps
- Includes the entire .NET runtime (~60MB)
- SQLite native libraries
- WPF and Windows Forms frameworks
- Can't be reduced much further without losing functionality

## Notes

- The single-file build only applies to Release configuration
- Debug builds will remain as normal multi-file deployments
- All application features work identically in both build types
- Database files and configuration are stored in the user's AppData folder (unchanged)
