============================================================
NowMoment v2.0 Installer - SPILab AMS
============================================================

이 폴더는 NowMoment v2.0 의 Windows 인스톨러를 빌드하는
스크립트와 설정 파일을 포함합니다.

폴더 구조 가정 (★ 기존 v2 와 다름)
----------------------------------

  <project>\
  |- SPILab.NowMoment.csproj        ★ csproj 가 이 위치에 있어야 함
  |- App.xaml(.cs)
  |- Models\, Services\, ViewModels\, Views\
  |- Converters\, Helpers\
  `- installer\                      <-- 이 폴더
     |- build-installer.bat          일반(FD) 빌드  - 기본
     |- build-installer-SC.bat       SC 빌드        - .NET 내장
     |- NowMoment.iss                FD 용 Inno Setup 스크립트
     |- NowMoment-SC.iss             SC 용 Inno Setup 스크립트
     |- README.txt                   본 파일
     `- Output\                      빌드 산출물 (자동 생성)
        `- NowMoment-v2.0.0-Setup.exe        ★ 둘 다 같은 파일명

  실제 위치 예시:
  C:\...\SPILab_NowMoment_WPF\SPILab_NowMoment_WPF\
       wpf_nowmoment\SPILab.NowMoment\
                                      <-- 이 폴더가 <project>
  C:\...\wpf_nowmoment\SPILab.NowMoment\installer\
                                      <-- 여기에 bat 들이 있음


두 가지 배포 방식 비교
----------------------

  +--------+----------+----------------+---------------+
  | 방식   | 설치파일 | 대상 PC 요구   | 빌드 타임     |
  +--------+----------+----------------+---------------+
  | 일반   | ~2-3 MB  | .NET 8 Desktop | 30 초~1 분    |
  | (FD)   |          | Runtime 필요   |               |
  +--------+----------+----------------+---------------+
  | SC     | ~150 MB  | 추가 설치 없음 | 1~3 분        |
  |        |          | (.NET 내장)    |               |
  +--------+----------+----------------+---------------+


★ 출력 파일명 통일에 관한 중요 안내 ★
-------------------------------------

  두 빌드의 결과물은 모두 동일한 이름으로 생성됩니다:

      installer\Output\NowMoment-v2.0.0-Setup.exe

  의도된 동작:
    - FD 빌드 후 다시 SC 빌드하면 .exe 가 SC 로 덮어써짐
    - 두 인스톨러를 모두 보관하려면 빌드 직후 .exe 를
      다른 폴더로 이동 또는 이름 변경

  파일 크기로 구분:
    ~2-3 MB  -> FD
    ~150 MB  -> SC


사용 절차
---------

1. Inno Setup 6.x 설치 (1회만):
   https://jrsoftware.org/isdl.php
   기본 경로: C:\Program Files (x86)\Inno Setup 6\

2. .NET 8 SDK 설치 (1회만):
   https://dotnet.microsoft.com/download/dotnet/8.0

3. 빌드 (Windows cmd, Git Bash 아님):

   [기본 - 일반(FD) 빌드]
       cd C:\...\wpf_nowmoment\SPILab.NowMoment
       installer\build-installer.bat
   결과: installer\Output\NowMoment-v2.0.0-Setup.exe (~2-3 MB)

   [SC 빌드 - .NET 내장]
       cd C:\...\wpf_nowmoment\SPILab.NowMoment
       installer\build-installer-SC.bat
   결과: installer\Output\NowMoment-v2.0.0-Setup.exe (~150 MB)

   * .bat 파일을 탐색기에서 더블클릭해도 동일 동작
     (자동으로 부모 폴더로 이동).


산출 인스톨러 동작
------------------

  * 설치 경로 기본값: %ProgramFiles%\SPILab\NowMoment\
  * 사용자 데이터 (DB) 위치: %APPDATA%\SPILab\NowMoment\nowmoment.db
    (인스톨러는 사용자 데이터를 건드리지 않음)
  * 시작 메뉴: SPILab > NowMoment
  * 바탕화면 아이콘: 옵션
  * 한국어/영어 설치 언어 선택


트러블슈팅
----------

  [ERROR] csproj not found
     -> installer\ 폴더의 부모에 SPILab.NowMoment.csproj 가
        있어야 합니다. 폴더 구조 가정 섹션 참조.

  [ERROR] Inno Setup not found
     -> Inno Setup 6.x 가 설치되지 않았거나 경로가 다름.
        .bat 파일의 ISCC_PATH 변수 수정.

  [ERROR] dotnet publish failed
     -> .NET 8 SDK 미설치 또는 코드 빌드 오류.
        먼저 dotnet build 로 수동 검증.

  Windows Defender SmartScreen 경고:
     -> 코드 서명 미적용 단계 정상 동작.
        [추가 정보] -> [실행].
