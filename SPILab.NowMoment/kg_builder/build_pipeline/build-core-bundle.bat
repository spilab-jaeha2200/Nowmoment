@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM ============================================================
REM  build-core-bundle.bat
REM  NowMoment v4.1 - SPILab Core 번들/키 생성 (Core-Owner 전용)
REM ------------------------------------------------------------
REM  SECURE_VERIFY_SETUP_GUIDE.md 의 2-B 단계를 자동화한다.
REM  extract_rules -> build_cython -> build_spc pack -> verify
REM  4단계를 순서대로 실행하여 다음을 생성한다:
REM
REM     SPILab.Core.spc      Core 암호화 번들
REM     bundle.key           번들 AES-256 키
REM     core.key             RULES 복호화 키
REM     sign_ed25519.key     서명 개인키  (비공개 보관)
REM     sign_ed25519.pub     서명 검증 공개키
REM
REM  생성 후 setup-secure-verify.bat 의 [5] 로 %APPDATA% 에 배치한다.
REM
REM  위치: kg_builder\build_pipeline\build-core-bundle.bat
REM
REM  ※ Core-Owner 권한 PC 에서만 실행할 것 (계획서 5.2).
REM  ※ 필요 환경:
REM     - Python 3 + cryptography 패키지
REM     - Cython + C 컴파일러 (Windows: Visual Studio Build Tools)
REM  ※ SPILab Core 소스(kg_builder\build_kg_*.py)가 있어야 한다.
REM    외부 배포본 트리에서는 실행 불가(소스 자체가 없음).
REM ============================================================

cd /d "%~dp0"

REM --- Python 확인 ---
where python >nul 2>&1
if errorlevel 1 (
    echo [오류] python 을 찾을 수 없습니다. Python 3 설치 후 PATH 추가.
    pause
    exit /b 1
)

REM --- Core 소스 존재 확인 (kg_builder\build_kg_*.py) ---
set "KGDIR=%~dp0.."
set "HAS_SRC="
for %%F in ("%KGDIR%\build_kg_*.py") do set "HAS_SRC=1"
if not defined HAS_SRC (
    echo [오류] SPILab Core 소스를 찾을 수 없습니다:
    echo        %KGDIR%\build_kg_*.py
    echo        이 스크립트는 Core 소스가 있는 내부 개발 트리에서만 동작합니다.
    pause
    exit /b 1
)

REM --- 산출물 폴더 ---
set "OUTDIR=%~dp0build_out"
set "NATIVEDIR=%OUTDIR%\native"
set "RULESDIR=%OUTDIR%\rules"

echo ============================================================
echo   SPILab Core 번들/키 생성  (Core-Owner 전용)
echo ============================================================
echo   Core 소스 : %KGDIR%
echo   산출 폴더 : %OUTDIR%
echo ------------------------------------------------------------
echo   진행할 단계:
echo     [1/4] extract_rules  - RULES 분리/암호화 (core.key 생성)
echo     [2/4] build_cython   - .py - .pyd 네이티브 컴파일
echo     [3/4] build_spc pack - .spc 번들 봉인 (bundle.key/서명키 생성)
echo     [4/4] build_spc verify - 번들 무결성 자체 검증
echo ============================================================
echo.
set /p GO="진행하시겠습니까? (Y/N): "
if /I not "%GO%"=="Y" (
    echo 취소했습니다.
    pause
    exit /b 0
)
echo.

REM -- [1/4] RULES 분리/암호화 ----------------------------------
echo [1/4] extract_rules.py 실행 중...
python extract_rules.py --out-dir "%OUTDIR%" --key-file "%OUTDIR%\core.key"
if errorlevel 1 goto FAIL_EXTRACT
echo   완료 - stripped.py + rules\*.enc + core.key
echo.

REM -- [2/4] Cython 컴파일 --------------------------------------
echo [2/4] build_cython.py 실행 중... (수 분 소요 가능)
python build_cython.py --src-dir "%OUTDIR%" --out-dir "%NATIVEDIR%"
if errorlevel 1 goto FAIL_CYTHON
echo   완료 - native\build_kg_*.pyd
echo.

REM -- [3/4] .spc 번들 봉인 -------------------------------------
echo [3/4] build_spc.py pack 실행 중...
python build_spc.py pack ^
    --native-dir "%NATIVEDIR%" ^
    --rules-dir "%RULESDIR%" ^
    --loader rules_loader.py ^
    --out "%OUTDIR%\SPILab.Core.spc" ^
    --bundle-key "%OUTDIR%\bundle.key" ^
    --sign-key "%OUTDIR%\sign_ed25519.key"
if errorlevel 1 goto FAIL_PACK
echo   완료 - SPILab.Core.spc + bundle.key + sign_ed25519.key/.pub
echo.

REM -- [4/4] 번들 무결성 자체 검증 ------------------------------
echo [4/4] build_spc.py verify 실행 중...
python build_spc.py verify ^
    --spc "%OUTDIR%\SPILab.Core.spc" ^
    --bundle-key "%OUTDIR%\bundle.key" ^
    --verify-key "%OUTDIR%\sign_ed25519.pub"
if errorlevel 1 goto FAIL_VERIFY
echo   완료 - 번들 무결성 검증 통과
echo.

echo ============================================================
echo   생성 완료!  산출 폴더: %OUTDIR%
echo ============================================================
echo.
echo   생성된 파일:
if exist "%OUTDIR%\SPILab.Core.spc"     echo     SPILab.Core.spc       Core 암호화 번들
if exist "%OUTDIR%\bundle.key"          echo     bundle.key            번들 AES-256 키
if exist "%OUTDIR%\core.key"            echo     core.key              RULES 복호화 키
if exist "%OUTDIR%\sign_ed25519.key"    echo     sign_ed25519.key      서명 개인키 (비공개!)
if exist "%OUTDIR%\sign_ed25519.pub"    echo     sign_ed25519.pub      서명 검증 공개키
echo.
echo   다음 단계:
echo     setup-secure-verify.bat 의 [5] 를 실행해 위 파일들을
echo     %%APPDATA%%\SPILab\NowMoment 에 배치하세요.
echo.
echo   * 주의 - 키 파일 보관:
echo     sign_ed25519.key (서명 개인키) 와 bundle.key/core.key 는
echo     .spc 와 분리하여 키 저장소에 안전하게 보관하십시오.
echo     배포 PC 에는 setup_core_keys 가 만드는 .dpapi 형태만 둡니다.
echo.
pause
exit /b 0

REM ------------------------------------------------------------
:FAIL_EXTRACT
echo.
echo [실패] [1/4] extract_rules 단계에서 오류가 발생했습니다.
echo        - cryptography 패키지 설치 확인:  pip install cryptography
echo        - build_kg_*.py 소스가 온전한지 확인
goto FAILEND

:FAIL_CYTHON
echo.
echo [실패] [2/4] build_cython 단계에서 오류가 발생했습니다.
echo        - Cython 설치 확인:  pip install cython
echo        - C 컴파일러 확인 (Windows: Visual Studio Build Tools 필요)
goto FAILEND

:FAIL_PACK
echo.
echo [실패] [3/4] build_spc pack 단계에서 오류가 발생했습니다.
echo        - native\*.pyd 와 rules\*.enc 가 생성됐는지 확인
goto FAILEND

:FAIL_VERIFY
echo.
echo [실패] [4/4] 번들 무결성 검증에 실패했습니다.
echo        - 번들/키 생성이 중간에 손상됐을 수 있습니다. 다시 실행하세요.
goto FAILEND

:FAILEND
echo        산출 폴더(%OUTDIR%)는 점검을 위해 보존됩니다.
echo.
pause
exit /b 1
