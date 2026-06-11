@echo off
setlocal EnableExtensions

REM ============================================================
REM NowMoment v4.0 Installer Build Script - Self-Contained Folder
REM (.NET runtime + all DLLs published as a folder under {app})
REM ------------------------------------------------------------
REM Location: <project>\installer\build-installer-SC.bat
REM Output:   installer\Output\NowMoment-v4.0.0-Setup.exe (~80 MB)
REM
REM v4.0 patch:
REM   - All output ASCII-only (no Korean) to avoid cp949 issues.
REM   - Auto-cleanup: only the win-x64\ folder (publish + intermediate)
REM     is deleted after successful ISCC compile. bin\Release\net8.0-windows\
REM     and obj\ are kept for incremental compilation.
REM
REM WARNING: Output filename is identical to the standard (FD) build.
REM          Running this batch will OVERWRITE any FD installer
REM          that already exists in installer\Output\.
REM ============================================================
REM Target PC requirement:
REM   None - .NET 8 runtime is installed alongside the EXE
REM ============================================================

cd /d "%~dp0\.."

set "ISS_FILE=installer\NowMoment-SC.iss"
set "ISCC_PATH=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
set "PROJECT_FILE=SPILab.NowMoment.csproj"
set "WINX64_DIR=bin\Release\net8.0-windows\win-x64"
set "PUBLISH_DIR=%WINX64_DIR%\publish"

if not exist "%ISCC_PATH%" goto :no_iscc
if not exist "%ISS_FILE%" goto :no_script
if not exist "%PROJECT_FILE%" goto :no_csproj

echo ============================================================
echo   NowMoment Installer Build (Self-Contained Folder)
echo ============================================================
echo   Project root: %CD%
echo   csproj:       %PROJECT_FILE%
echo.

echo [1/5] Cleaning previous publish output and Output folder...
if exist "%WINX64_DIR%" rmdir /s /q "%WINX64_DIR%"
if exist "installer\Output" rmdir /s /q "installer\Output"
echo   Done.
echo.

echo [2/5] Running dotnet publish... (self-contained folder, 1-3 min)
dotnet publish "%PROJECT_FILE%" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=embedded -p:PublishReadyToRun=true
if errorlevel 1 goto :publish_fail
echo   Done.
echo.

echo [3/5] Removing intermediate build artifacts from win-x64\ root...
if exist "%WINX64_DIR%" (
    pushd "%WINX64_DIR%"
    for %%F in (*) do del /q "%%F" 2>nul
    for /D %%D in (*) do if /I not "%%D"=="publish" rmdir /s /q "%%D" 2>nul
    popd
    echo   Done. Only publish\ folder remains.
) else (
    echo   [WARN] %WINX64_DIR% not found, skipping.
)
echo.

echo [4/5] Compiling installer...
"%ISCC_PATH%" "%ISS_FILE%"
if errorlevel 1 goto :iscc_fail
echo   Done.
echo.

REM --- v4.0: cleanup ONLY the win-x64\ folder (no longer needed) ---
REM     bin\Release\net8.0-windows\ root and obj\ are KEPT.
echo [5/5] Cleaning up intermediate publish folder...
if exist "%WINX64_DIR%" (
    rmdir /s /q "%WINX64_DIR%"
    echo   Removed: %WINX64_DIR%\
) else (
    echo   Skipped: %WINX64_DIR%\ not found.
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
echo Download: https://jrsoftware.org/isdl.php
goto :end

:no_script
echo [ERROR] Script file not found: %ISS_FILE%
goto :end

:no_csproj
echo [ERROR] csproj not found: %PROJECT_FILE%
echo Current dir: %CD%
echo.
echo Expected layout:
echo   ^<project^>\
echo     SPILab.NowMoment.csproj   ^<-- here
echo     App.xaml, Models\, Views\, ...
echo     installer\
echo       build-installer-SC.bat ^<-- this file
goto :end

:publish_fail
echo.
echo ============================================================
echo [ERROR] dotnet publish failed.
echo ============================================================
echo.
echo Note: win-x64\ folder is KEPT for debugging.
echo.
goto :end

:iscc_fail
echo.
echo ============================================================
echo [ERROR] Inno Setup compilation failed.
echo ============================================================
echo.
echo Note: win-x64\ folder is KEPT for debugging.
echo       Inspect: %PUBLISH_DIR%
echo.
goto :end

:end
pause
endlocal
