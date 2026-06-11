@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM ============================================================
REM  setup-secure-verify.bat
REM  NowMoment v4.1 Secure-Verify 운영 셋업 - 대화형 실행 도우미
REM ------------------------------------------------------------
REM  SECURE_VERIFY_SETUP_GUIDE.md 의 2-A ~ 2-D 절차를 메뉴에서
REM  하나씩 골라 실행한다. 명령어를 외울 필요가 없다.
REM
REM  위치: kg_builder\build_pipeline\setup-secure-verify.bat
REM  실행: 이 파일을 더블클릭하거나, 명령창에서 실행.
REM
REM  ※ Core-Owner 권한 PC 에서 실행할 것.
REM  ※ Python 3 와 cryptography 패키지가 필요하다.
REM ============================================================

cd /d "%~dp0"

REM --- Python 확인 ---
where python >nul 2>&1
if errorlevel 1 (
    echo [오류] python 을 찾을 수 없습니다.
    echo        Python 3 를 설치하고 PATH 에 추가하세요: https://www.python.org/
    pause
    exit /b 1
)

REM --- 배치 대상 폴더 (%APPDATA%\SPILab\NowMoment) ---
set "TARGET=%APPDATA%\SPILab\NowMoment"

:MENU
cls
echo ============================================================
echo   NowMoment v4.1  Secure-Verify 운영 셋업 도우미
echo ============================================================
echo   배치 대상 폴더 : %TARGET%
echo   현재 작업 폴더 : %CD%
echo ------------------------------------------------------------
echo.
echo   [1] 권한 매트릭스 만들기      (빈 core_access.json 생성)
echo   [2] 사용자 추가              (사번/이름/역할/비밀번호 등록)
echo   [3] 사용자 목록 보기
echo   [4] 비밀번호 검증 테스트
echo.
echo   [5] Core 키/번들을 APPDATA 에 배치   (setup_core_keys deploy)
echo   [6] DPAPI 보호 전환                  (Windows 배포 PC 전용)
echo   [7] 배치 상태 점검                   (6개 파일 확인)
echo.
echo   [8] core_access.json 을 APPDATA 로 복사
echo.
echo   [0] 종료
echo ============================================================
set /p SEL="실행할 번호를 입력하세요: "

if "%SEL%"=="1" goto INIT
if "%SEL%"=="2" goto ADD
if "%SEL%"=="3" goto LIST
if "%SEL%"=="4" goto VERIFY
if "%SEL%"=="5" goto DEPLOY
if "%SEL%"=="6" goto DPAPI
if "%SEL%"=="7" goto CHECK
if "%SEL%"=="8" goto COPYACC
if "%SEL%"=="0" goto END
echo 잘못된 입력입니다.
pause
goto MENU

REM ------------------------------------------------------------
:INIT
echo.
echo [1] 빈 권한 매트릭스(core_access.json)를 만듭니다.
if exist "core_access.json" (
    echo     core_access.json 이 이미 있습니다.
    set /p OW="    덮어쓸까요? (Y/N): "
    if /I not "!OW!"=="Y" goto MENUWAIT
    python manage_access.py init --out core_access.json --force
) else (
    python manage_access.py init --out core_access.json
)
goto MENUWAIT

REM ------------------------------------------------------------
:ADD
echo.
echo [2] 사용자를 추가합니다.
echo     역할: CoreOwner / CoreDeveloper / CoreRunner / ShellOnly
echo.
set /p UID="    사번 (예: SPL-001): "
set /p UNAME="    이름 (예: 홍길동): "
set /p UROLE="    역할: "
if "%UID%"=="" ( echo 사번이 비었습니다. & goto MENUWAIT )
if "%UNAME%"=="" ( echo 이름이 비었습니다. & goto MENUWAIT )
if "%UROLE%"=="" ( echo 역할이 비었습니다. & goto MENUWAIT )
echo.
echo     비밀번호는 다음 화면에서 입력합니다 (화면에 표시되지 않음).
python manage_access.py add --file core_access.json --id "%UID%" --name "%UNAME%" --role "%UROLE%"
goto MENUWAIT

REM ------------------------------------------------------------
:LIST
echo.
python manage_access.py list --file core_access.json
goto MENUWAIT

REM ------------------------------------------------------------
:VERIFY
echo.
set /p UID="    검증할 사번: "
if "%UID%"=="" ( echo 사번이 비었습니다. & goto MENUWAIT )
python manage_access.py verify --file core_access.json --id "%UID%"
goto MENUWAIT

REM ------------------------------------------------------------
:DEPLOY
echo.
echo [5] Core 키/번들을 %TARGET% 에 배치합니다.
echo     build-core-bundle.bat 산출 폴더(build_out)에서 자동으로 찾습니다.
echo.
REM build-core-bundle.bat 의 산출 폴더(build_out)에서 4개 파일을 자동으로 찾는다.
set "BOUT=%~dp0build_out"
set "P_SPC=%BOUT%\SPILab.Core.spc"
set "P_BK=%BOUT%\bundle.key"
set "P_CK=%BOUT%\core.key"
set "P_PUB=%BOUT%\sign_ed25519.pub"

set "MISSING="
if not exist "%P_SPC%" set "MISSING=!MISSING! SPILab.Core.spc"
if not exist "%P_BK%"  set "MISSING=!MISSING! bundle.key"
if not exist "%P_CK%"  set "MISSING=!MISSING! core.key"
if not exist "%P_PUB%" set "MISSING=!MISSING! sign_ed25519.pub"
if defined MISSING (
    echo [오류] build_out 폴더에서 다음 파일을 찾지 못했습니다:!MISSING!
    echo        폴더: %BOUT%
    echo        먼저 build-core-bundle.bat 을 실행해 번들/키를 생성하세요.
    goto MENUWAIT
)
echo     build_out 에서 4개 파일을 찾았습니다. 배치를 시작합니다.
echo       %P_SPC%
echo       %P_BK%
echo       %P_CK%
echo       %P_PUB%
echo.
python setup_core_keys.py deploy --spc "%P_SPC%" --bundle-key "%P_BK%" --core-key "%P_CK%" --sign-pub "%P_PUB%" --pipeline-dir .
goto MENUWAIT

REM ------------------------------------------------------------
:DPAPI
echo.
echo [6] 평문 .raw 키를 DPAPI 보호로 전환합니다 (Windows 배포 PC 전용).
echo     비Windows 에서 배치한 경우에만 필요합니다.
python setup_core_keys.py dpapi-protect
goto MENUWAIT

REM ------------------------------------------------------------
:CHECK
echo.
python setup_core_keys.py check
goto MENUWAIT

REM ------------------------------------------------------------
:COPYACC
echo.
echo [8] core_access.json 을 %TARGET% 로 복사합니다.
if not exist "core_access.json" (
    echo [오류] core_access.json 이 없습니다. 먼저 [1][2] 로 만드세요.
    goto MENUWAIT
)
if not exist "%TARGET%" mkdir "%TARGET%"
copy /Y "core_access.json" "%TARGET%\core_access.json" >nul
if errorlevel 1 (
    echo [오류] 복사에 실패했습니다.
) else (
    echo [완료] %TARGET%\core_access.json 로 복사했습니다.
)
goto MENUWAIT

REM ------------------------------------------------------------
:MENUWAIT
echo.
pause
goto MENU

:END
echo.
echo 종료합니다.
endlocal
