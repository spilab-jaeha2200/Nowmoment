; ============================================================
; NowMoment v4.1 Installer Script (Inno Setup 6.x)
; Edition: Framework-Dependent (Default / Standard)
; ============================================================
; Strategy: explicit file list to avoid wildcard path issues.
;           Each file uses skipifsourcedoesntexist for safety.
; ============================================================

#define MyAppName        "NowMoment"
#define MyAppVersion     "4.1.0"
#define MyAppPublisher   "SPILab Co., Ltd."
#define MyAppURL         "https://spilab.ai"
#define MyAppExeName     "SPILab.NowMoment.exe"
#define MyAppId          "{{B3E4A5C6-7D8F-5A9B-AC2D-3E4F5A6B7C8D}"

#define PublishDir       "..\bin\Release\net8.0-windows\publish"

#define DotNetCheckURL   "https://dotnet.microsoft.com/download/dotnet/8.0/runtime?cid=getdotnetcore&arch=x64&os=win&runtime=desktop"

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
OutputBaseFilename={#MyAppName}-v{#MyAppVersion}-Setup

; --- Compression: simpler LZMA (no separate process) ---
Compression=lzma
SolidCompression=yes

WizardStyle=modern
ShowLanguageDialog=yes

[Languages]
Name: "korean";  MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
korean.DotNetMissing=.NET 8 Desktop Runtime(x64)이 설치되지 않았습니다.%n%n이 프로그램은 .NET 8 Desktop Runtime이 필요합니다.%n다운로드 페이지를 지금 열까요?%n%n(런타임 설치 후 본 인스톨러를 다시 실행해 주세요.)
english.DotNetMissing=.NET 8 Desktop Runtime (x64) is not installed.%n%nThis application requires .NET 8 Desktop Runtime to run.%nOpen the download page now?%n%n(Please re-run this installer after installing the runtime.)

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; --- Main executable (REQUIRED) ---
Source: "{#PublishDir}\SPILab.NowMoment.exe"; DestDir: "{app}"; Flags: ignoreversion

; --- Main app DLL (REQUIRED) ---
Source: "{#PublishDir}\SPILab.NowMoment.dll"; DestDir: "{app}"; Flags: ignoreversion

; --- .NET runtime config files (REQUIRED) ---
Source: "{#PublishDir}\SPILab.NowMoment.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\SPILab.NowMoment.deps.json"; DestDir: "{app}"; Flags: ignoreversion

; --- NuGet dependencies ---
Source: "{#PublishDir}\CommunityToolkit.Mvvm.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#PublishDir}\Microsoft.Data.Sqlite.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#PublishDir}\Microsoft.Extensions.DependencyInjection.Abstractions.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#PublishDir}\Microsoft.Extensions.DependencyInjection.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#PublishDir}\SQLitePCLRaw.batteries_v2.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#PublishDir}\SQLitePCLRaw.core.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#PublishDir}\SQLitePCLRaw.provider.e_sqlite3.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

; --- SQLite native interop (CRITICAL for runtime) ---
; --- May be in publish root OR runtimes\win-x64\native\ ---
Source: "{#PublishDir}\e_sqlite3.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#PublishDir}\runtimes\win-x64\native\e_sqlite3.dll"; DestDir: "{app}\runtimes\win-x64\native"; Flags: ignoreversion skipifsourcedoesntexist

; --- KG builder scripts (.py only) — output goes to %APPDATA%\SPILab\NowMoment\kg_builder\ ---
; 빌더 스크립트만 설치 폴더에 둔다. 출력 파일(.cs/.json/.ttl) 은
; 사용자 쓰기 가능한 데이터 폴더에 NowMoment 가 자동 생성한다.
Source: "..\kg_builder\build_kg_cs.py";          DestDir: "{app}\kg_builder"; Flags: ignoreversion
Source: "..\kg_builder\build_kg_photo.py";       DestDir: "{app}\kg_builder"; Flags: ignoreversion
Source: "..\kg_builder\dump_photo_to_csharp.py"; DestDir: "{app}\kg_builder"; Flags: ignoreversion
Source: "..\kg_builder\build_kg_cmp.py";        DestDir: "{app}\kg_builder"; Flags: ignoreversion
Source: "..\kg_builder\build_kg_etch.py";       DestDir: "{app}\kg_builder"; Flags: ignoreversion
Source: "..\kg_builder\build_kg_thinfilm.py";   DestDir: "{app}\kg_builder"; Flags: ignoreversion

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
function IsDotNetDesktopRuntimeInstalled(): Boolean;
var
  Output: AnsiString;
  ResultCode: Integer;
  TempFile: String;
  Cmd: String;
begin
  Result := False;
  TempFile := ExpandConstant('{tmp}\dotnet_runtimes.txt');
  Cmd := ExpandConstant('/C dotnet --list-runtimes > "' + TempFile + '" 2>&1');
  if Exec(ExpandConstant('{cmd}'), Cmd, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if LoadStringFromFile(TempFile, Output) then
    begin
      if Pos('Microsoft.WindowsDesktop.App 8.', String(Output)) > 0 then
        Result := True;
    end;
  end;
  DeleteFile(TempFile);
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not IsDotNetDesktopRuntimeInstalled() then
  begin
    if MsgBox(ExpandConstant('{cm:DotNetMissing}'), mbConfirmation, MB_YESNO) = IDYES then
    begin
      ShellExec('open', '{#DotNetCheckURL}', '', '', SW_SHOW, ewNoWait, ErrorCode);
    end;
    Result := False;
  end;
end;
