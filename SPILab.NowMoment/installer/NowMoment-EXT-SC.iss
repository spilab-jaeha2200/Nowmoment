; ============================================================
; NowMoment v4.1 Installer Script (Inno Setup 6.x)
; Edition: EXTERNAL (Core-Excluded / 외부 배포본)
;          Self-Contained Folder (.NET 8 런타임 내장)
; ------------------------------------------------------------
; 개선 개발계획서 6.2 / 6.4 / 8장:
;   외부 배포본은 SPILab Core 페이로드(kg_builder/)를 물리적으로
;   제외한다. CoreProviderLoader 가 페이로드 부재를 감지해 자동으로
;   Core 를 비활성화하므로(NullKgBuilder/AssetClassifierFallback),
;   코드 분기 없이 이 스크립트에서 kg_builder/ 를 빼기만 하면 된다.
;
;   배포 방식: Self-Contained
;     - .NET 8 Desktop Runtime 을 산출물에 내장한다.
;     - 외부 고객 PC 에 별도 런타임 설치가 필요 없다.
;     - 산출물 크기는 커지지만(~80MB+) 설치 실패 요인이 줄어든다.
;
;   Core 검출 게이트는 두 곳에 있다 (계획서 8장 — 실수에 의한 IP 유출 차단):
;     1) [Files] 아래 #if/#pragma error — 컴파일 시점 게이트(본 방어선).
;        publish 에 build_kg_*.py 가 섞이면 ISCC 컴파일을 중단한다.
;     2) [Code] 의 InitializeSetup — 설치 실행 시점 게이트(보조).
;        만에 하나 누출 산출물이 설치 실행될 때 설치를 중단한다.
; ============================================================

#define MyAppName        "NowMoment"
#define MyAppVersion     "4.1.0"
#define MyAppPublisher   "SPILab Co., Ltd."
#define MyAppURL         "https://spilab.ai"
#define MyAppExeName     "SPILab.NowMoment.exe"
; EXT 본은 별도 AppId — 내부본과 동시 설치/업그레이드 충돌 방지
#define MyAppId          "{{C4F5B6D7-8E9A-6BAC-BD3E-4F5A6B7C8D9E}"

; Self-Contained publish 출력 경로 (win-x64\publish)
#define PublishDir       "..\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}

DefaultDirName={autopf}\SPILab\{#MyAppName}
DefaultGroupName=SPILab\{#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#MyAppVersion}

PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog

MinVersion=10.0.17763
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

OutputDir=Output
; ★ 요청 사항 — 산출물 파일명: NowMoment-v4.1.0-Setup.exe
OutputBaseFilename={#MyAppName}-v{#MyAppVersion}-Setup

Compression=lzma
SolidCompression=yes

WizardStyle=modern
ShowLanguageDialog=yes

[Languages]
Name: "korean";  MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
korean.CorePayloadLeak=[빌드 중단] 외부 배포본에 SPILab Core 페이로드가 포함되어 있습니다.%n%nkg_builder 폴더의 물리 규칙 빌더(.py)가 publish 산출물에서 발견되었습니다.%n외부 배포본에는 Core IP 가 절대 포함되어서는 안 됩니다.%n%n조치: publish 폴더에서 kg_builder\ 를 제거한 뒤 다시 빌드하십시오.%n(build-installer-EXT-SC.bat 은 이를 자동으로 처리합니다.)
english.CorePayloadLeak=[BUILD ABORTED] SPILab Core payload detected in the EXTERNAL package.%n%nPhysics-rule builders (.py) from kg_builder\ were found in the publish output.%nThe external edition must NEVER ship Core IP.%n%nAction: remove kg_builder\ from the publish folder and rebuild.%n(build-installer-EXT-SC.bat handles this automatically.)

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; --- Self-contained publish 산출물 (EXE + .NET 8 런타임 + 모든 의존 DLL) ---
; recursesubdirs : runtimes\, 로케일 폴더 등 하위 폴더 보존
; createallsubdirs : 빈 폴더(있다면)까지 재생성
; Inno Setup 이 복사한 모든 파일을 추적하여 제거 시 함께 삭제한다.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; ════════════════════════════════════════════════════════════
; ★ EXTERNAL EDITION — kg_builder/*.py 는 의도적으로 제외한다.
;   표준본/SC본에 있던 build_kg_*.py 6줄을 넣지 않는다.
;   CoreProviderLoader 가 kg_builder/ 부재를 감지해 KG 빌드·자동
;   분류를 자동으로 "비활성" 처리하므로 앱은 정상 동작한다.
;   (Self-Contained 라도 Core 페이로드는 포함하지 않는다.)
; ════════════════════════════════════════════════════════════

; ════════════════════════════════════════════════════════════
; ★ Core 누출 게이트 (컴파일 시점 — 전처리기)
;   #if/#error 는 ISPP 전처리 단계에서 평가되므로, 누출이 있으면
;   ISCC 가 컴파일을 시작하기 전에 [BUILD ABORTED] 로 중단된다.
;   (아래 [Code] 의 InitializeSetup 게이트는 "설치 실행 시점" 에만
;    동작하므로 컴파일을 막지 못한다. 이 전처리기 게이트가 본 방어선.)
;
;   재귀 검사가 필요하면 build-installer-EXT-SC.bat 의 [5/6]
;   for /r 게이트가 이를 보완한다(이중 방어).
; ════════════════════════════════════════════════════════════
; EXT 본은 publish 에 kg_builder 폴더가 통째로 없어야 정상이다.
; 폴더 존재 검사(DirExists)만으로 누출을 잡는다 — FindFirst 핸들
; 관리가 불필요하므로 가장 단순·안전하다.
#if DirExists(AddBackslash(PublishDir) + "kg_builder")
  #pragma error "[BUILD ABORTED] SPILab Core leak: kg_builder\ folder found in publish output. Remove kg_builder\ and rebuild."
#endif

[Icons]
Name: "{group}\{#MyAppName}";                       Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}";                 Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\logs"
Type: filesandordirs; Name: "{app}\cache"

[Code]
{ ─────────────────────────────────────────────────────────────
  Core 검출 게이트 (보조 — 설치 실행 시점).
  주의: InitializeSetup 은 Setup.exe 를 "실행" 할 때 동작한다.
  ISCC "컴파일" 은 막지 못한다 → 컴파일 차단은 [Files] 위의
  전처리기 게이트(DirExists + pragma error)가 담당한다.
  이 게이트는 만약을 대비한 설치 실행 단계의 2차 방어선이다.
  ───────────────────────────────────────────────────────────── }
function CorePayloadLeaked(): Boolean;
var
  PubRoot: String;
  FindRec: TFindRec;
begin
  Result := False;
  PubRoot := ExpandConstant('{#PublishDir}');

  { publish 루트의 kg_builder 폴더 }
  if FindFirst(PubRoot + '\kg_builder\build_kg_*.py', FindRec) then
  begin
    try
      Result := True;
    finally
      FindClose(FindRec);
    end;
    exit;
  end;

  { publish 루트에 직접 흩어진 경우까지 방어 }
  if FindFirst(PubRoot + '\build_kg_*.py', FindRec) then
  begin
    try
      Result := True;
    finally
      FindClose(FindRec);
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;

  { ── Core 페이로드 누출 검사 (EXT 전용) ── }
  if CorePayloadLeaked() then
  begin
    MsgBox(ExpandConstant('{cm:CorePayloadLeak}'), mbCriticalError, MB_OK);
    Result := False;
    exit;
  end;

  { Self-Contained 배포본이므로 .NET 런타임 사전 확인은 불필요 —
    런타임이 산출물에 내장되어 있다. }
end;
