@echo off
echo ============================================
echo Building FileTagger Single-File Release
echo ============================================

:: Clean previous builds
echo Cleaning previous builds...
if exist "bin\Release" rmdir /s /q "bin\Release"
if exist "obj\Release" rmdir /s /q "obj\Release"
if exist "release-single" rmdir /s /q "release-single"

:: Restore packages
echo Restoring NuGet packages...
dotnet restore

:: Build and publish single-file executable
echo Building single-file release...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o release-single

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ============================================
    echo Build completed successfully!
    echo ============================================
    echo.
    echo Single file executable created at:
    echo %cd%\release-single\FileTagger.exe
    echo.
    echo File size:
    for %%A in ("release-single\FileTagger.exe") do echo %%~zA bytes
    echo.
    echo Ready for distribution!
    echo ============================================
    
    :: Open the release folder
    explorer "release-single"
) else (
    echo.
    echo ============================================
    echo Build failed with error code %ERRORLEVEL%
    echo ============================================
)

pause
