@echo off
setlocal EnableExtensions

REM ============================================================
REM NowMoment v4.0 Installer Build Script - Framework-Dependent
REM ------------------------------------------------------------
REM v4.0 patch:
REM   - All output ASCII-only (no Korean) to avoid cp949 issues.
REM   - Auto-cleanup: only the publish\ folder is deleted after
REM     successful ISCC compile. bin\ and obj\ are kept for
REM     incremental compilation (faster rebuild).
REM ============================================================

cd /d "%~dp0\.."

set "ISS_FILE=installer\NowMoment.iss"
set "ISCC_PATH=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
set "PROJECT_FILE=SPILab.NowMoment.csproj"
set "PUBLISH_DIR=bin\Release\net8.0-windows\publish"

if not exist "%ISCC_PATH%" goto :no_iscc
if not exist "%ISS_FILE%" goto :no_script
if not exist "%PROJECT_FILE%" goto :no_csproj

echo ============================================================
echo   NowMoment Installer Build (Framework-Dependent)
echo ============================================================
echo   Project root: %CD%
echo   csproj:       %PROJECT_FILE%
echo   Publish dir:  %PUBLISH_DIR%
echo   Requires .NET 8 Desktop Runtime on target PC
echo ============================================================
echo.

echo [1/4] Cleaning previous publish and Output folders...
if exist "%PUBLISH_DIR%" rmdir /s /q "%PUBLISH_DIR%"
if exist "installer\Output" rmdir /s /q "installer\Output"
echo   Done.
echo.

echo [2/4] Running dotnet publish ... (framework-dependent, ~30 sec)
REM -- v4.0: -p:DebugType=embedded embeds .pdb debug symbols into the
REM    assembly so error message boxes on user PCs include source file
REM    paths and line numbers in stack traces.
dotnet publish "%PROJECT_FILE%" -c Release --no-self-contained -p:DebugType=embedded -o "%PUBLISH_DIR%"
if errorlevel 1 goto :publish_fail
echo   Done.
echo.

echo ============================================================
echo   Publish folder contents (for diagnosis):
echo ============================================================
dir /b "%PUBLISH_DIR%"
echo ------------------------------------------------------------
echo   Subfolders (if any):
dir /ad /b "%PUBLISH_DIR%" 2>nul
echo ============================================================
echo.

echo [3/4] Compiling installer...
"%ISCC_PATH%" "%ISS_FILE%"
if errorlevel 1 goto :iscc_fail
echo   Done.
echo.

REM --- v4.0: cleanup ONLY the publish folder (no longer needed) ---
REM     bin\ and obj\ are KEPT for incremental compile next time.
echo [4/4] Cleaning up intermediate publish folder...
if exist "%PUBLISH_DIR%" (
    rmdir /s /q "%PUBLISH_DIR%"
    echo   Removed: %PUBLISH_DIR%\
) else (
    echo   Skipped: %PUBLISH_DIR%\ not found.
)
echo.

echo ============================================================
echo   BUILD COMPLETE!
echo ============================================================
echo.
echo Installer output:
dir /b "installer\Output\*.exe" 2>nul
echo.
echo Location: %CD%\installer\Output\
echo.
goto :end

:no_iscc
echo [ERROR] Inno Setup not found at:
echo   %ISCC_PATH%
goto :end

:no_script
echo [ERROR] Script file not found: %ISS_FILE%
goto :end

:no_csproj
echo [ERROR] csproj not found: %PROJECT_FILE%
echo Current dir: %CD%
goto :end

:publish_fail
echo.
echo ============================================================
echo [ERROR] dotnet publish failed.
echo ============================================================
echo.
echo Possible causes:
echo   1. dotnet SDK 8.x not installed
echo      Run: dotnet --list-sdks
echo   2. NuGet restore failed (network issue)
echo      Run: dotnet restore "%PROJECT_FILE%"
echo   3. Compilation error in source code
echo      Run: dotnet build "%PROJECT_FILE%" -c Release
echo.
echo Note: publish\ folder is KEPT for debugging.
echo.
goto :end

:iscc_fail
echo.
echo ============================================================
echo [ERROR] Inno Setup compilation failed.
echo ============================================================
echo.
echo Note: publish\ folder is KEPT for debugging.
echo       Inspect: %PUBLISH_DIR%
echo.
goto :end

:end
pause
endlocal
