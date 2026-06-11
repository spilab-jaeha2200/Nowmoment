@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM ============================================================
REM NowMoment v4.1 Installer Build Script - EXTERNAL EDITION
REM (Core-Excluded / Self-Contained Folder)
REM ------------------------------------------------------------
REM Location: <project>\installer\build-installer-EXT-SC.bat
REM Output:   installer\Output\NowMoment-v4.1.0-Setup.exe (~80 MB+)
REM
REM 개선 개발계획서 6.2 / 6.4 / 8장:
REM   외부 배포본 전용 빌드. .NET 8 런타임을 산출물에 내장하는
REM   Self-Contained 방식이며, dotnet publish 산출물에서 SPILab Core
REM   페이로드(kg_builder\)를 물리적으로 제거한 뒤 인스톨러를 만든다.
REM
REM   SC 빌드(build-installer-SC.bat)와의 차이는 두 단계뿐:
REM     [4/6] publish 후 kg_builder\ 강제 삭제
REM     [5/6] Core 페이로드 잔존 검증 게이트 (남아 있으면 빌드 중단)
REM
REM   ISS 파일도 NowMoment-EXT-SC.iss (kg_builder 미포함 + Code 게이트)
REM   를 사용한다 - 2중 방어.
REM
REM Target PC requirement:
REM   None - .NET 8 runtime is bundled inside the installer.
REM
REM   All output ASCII-only (no Korean) to avoid cp949 issues.
REM ============================================================

cd /d "%~dp0\.."

set "ISS_FILE=installer\NowMoment-EXT-SC.iss"
set "ISCC_PATH=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
set "PROJECT_FILE=SPILab.NowMoment.csproj"
set "WINX64_DIR=bin\Release\net8.0-windows\win-x64"
set "PUBLISH_DIR=%WINX64_DIR%\publish"

if not exist "%ISCC_PATH%" goto :no_iscc
if not exist "%ISS_FILE%" goto :no_script
if not exist "%PROJECT_FILE%" goto :no_csproj

echo ============================================================
echo   NowMoment Installer Build - EXTERNAL EDITION
echo   (Self-Contained Folder / SPILab Core EXCLUDED)
echo ============================================================
echo   Project root: %CD%
echo   csproj:       %PROJECT_FILE%
echo   Publish dir:  %PUBLISH_DIR%
echo   ISS script:   %ISS_FILE%
echo   .NET 8 runtime is bundled - no prerequisite on target PC
echo ============================================================
echo.

echo [1/6] Cleaning previous publish output and Output folder...
if exist "%WINX64_DIR%" rmdir /s /q "%WINX64_DIR%"
if exist "installer\Output" rmdir /s /q "installer\Output"
echo   Done.
echo.

echo [2/6] Running dotnet publish... (self-contained folder, 1-3 min)
dotnet publish "%PROJECT_FILE%" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=embedded -p:PublishReadyToRun=true
if errorlevel 1 goto :publish_fail
echo   Done.
echo.

echo [3/6] Removing intermediate build artifacts from win-x64\ root...
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

echo [4/6] Removing SPILab Core payload from publish output...
REM -- csproj already excludes kg_builder\*.cs from compile, but the
REM    folder itself can still be copied by publish/content rules.
REM    EXTERNAL edition must ship NONE of it.
if exist "%PUBLISH_DIR%\kg_builder" (
    rmdir /s /q "%PUBLISH_DIR%\kg_builder"
    echo   Removed: %PUBLISH_DIR%\kg_builder\
) else (
    echo   Note: %PUBLISH_DIR%\kg_builder\ not present ^(already clean^).
)
REM -- defensive: stray builder scripts scattered in publish root
del /q "%PUBLISH_DIR%\build_kg_*.py" >nul 2>&1
del /q "%PUBLISH_DIR%\dump_*_to_csharp.py" >nul 2>&1
echo   Done.
echo.

echo [5/6] Core payload leak gate ^(verifying exclusion^)...
set "LEAK=0"
if exist "%PUBLISH_DIR%\kg_builder\build_kg_cs.py"        set "LEAK=1"
if exist "%PUBLISH_DIR%\kg_builder\build_kg_photo.py"     set "LEAK=1"
if exist "%PUBLISH_DIR%\kg_builder\build_kg_cmp.py"       set "LEAK=1"
if exist "%PUBLISH_DIR%\kg_builder\build_kg_etch.py"      set "LEAK=1"
if exist "%PUBLISH_DIR%\kg_builder\build_kg_thinfilm.py"  set "LEAK=1"
for /r "%PUBLISH_DIR%" %%F in (build_kg_*.py) do set "LEAK=1"
if "!LEAK!"=="1" goto :core_leak
echo   PASS - no Core payload (.py builders) found in publish output.
echo.

echo [6/6] Compiling EXTERNAL installer...
"%ISCC_PATH%" "%ISS_FILE%"
if errorlevel 1 goto :iscc_fail
echo   Done.
echo.

echo [cleanup] Removing intermediate publish folder...
if exist "%WINX64_DIR%" (
    rmdir /s /q "%WINX64_DIR%"
    echo   Removed: %WINX64_DIR%\
)
echo.

echo ============================================================
echo   BUILD COMPLETE!  (EXTERNAL EDITION - Self-Contained)
echo ============================================================
echo.
echo Installer output:
dir /b "installer\Output\*.exe" 2>nul
echo.
echo Location: %CD%\installer\Output\
echo Expected: NowMoment-v4.1.0-Setup.exe
echo.
echo Reminder: this build ships NO SPILab Core, but bundles the
echo           .NET 8 runtime. On the target PC, KG build /
echo           auto-classification show as "disabled".
echo.
goto :end

:core_leak
echo.
echo ============================================================
echo [ERROR] CORE PAYLOAD LEAK DETECTED - BUILD ABORTED
echo ============================================================
echo.
echo SPILab Core builders (build_kg_*.py) are still present in:
echo   %PUBLISH_DIR%
echo.
echo The EXTERNAL edition must NEVER ship Core IP.
echo Step [4/6] should have removed them - inspect the publish
echo folder and your csproj content/publish rules.
echo.
echo Note: win-x64\ folder is KEPT for inspection.
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
goto :end

:publish_fail
echo.
echo ============================================================
echo [ERROR] dotnet publish failed.
echo ============================================================
echo.
echo Possible causes:
echo   1. dotnet SDK 8.x not installed   (dotnet --list-sdks)
echo   2. NuGet restore failed           (dotnet restore)
echo   3. Compilation error in source    (dotnet build -c Release)
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
