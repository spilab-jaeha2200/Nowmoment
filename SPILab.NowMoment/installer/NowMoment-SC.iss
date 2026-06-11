; ============================================================
; NowMoment v4.1 Installer Script (Inno Setup 6.x)
; Edition: Self-Contained Folder
;
; Folder-based self-contained publish:
;   - QuestPDF.dll and other managed DLLs are installed alongside
;     the EXE under {app}, avoiding in-memory load failures
;     observed on user PCs with the SingleFile self-extract model.
;   - All DLLs / runtime files are tracked by the installer
;     and removed automatically when the user uninstalls.
; ============================================================

#define MyAppName        "NowMoment"
#define MyAppVersion     "4.1.0"
#define MyAppPublisher   "SPILab Co., Ltd."
#define MyAppURL         "https://spilab.ai"
#define MyAppExeName     "SPILab.NowMoment.exe"
#define MyAppId          "{{A2D3F4B5-6C7E-4F8A-9B1C-2D3E4F5A6B7C}"

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
OutputBaseFilename={#MyAppName}-v{#MyAppVersion}-Setup

Compression=lzma
SolidCompression=yes

WizardStyle=modern
ShowLanguageDialog=yes

[Languages]
Name: "korean";  MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; --- Self-contained publish output (EXE + .NET runtime + all dependent DLLs) ---
; recursesubdirs preserves runtimes\, locale folders, etc.
; createallsubdirs ensures empty folders (if any) are recreated.
; Inno Setup tracks every file copied here and removes them at uninstall.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; --- KG builder scripts (.py only) — output goes to %APPDATA%\SPILab\NowMoment\kg_builder\ ---
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
