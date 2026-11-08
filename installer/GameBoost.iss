; GameBoost installer (Inno Setup 6)
; Built by build.ps1:  ISCC.exe /DMyAppVersion=<version> /DSourceDir=<publish folder> installer\GameBoost.iss

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

#define MyAppName "GameBoost"
#define MyAppExe "GameBoost.exe"
#define MyAppPublisher "GameBoost"
#define MyAppUrl "https://github.com/calvinistkrass/GameBoost"

[Setup]
; Never change the AppId – it identifies the app for updates and uninstall.
AppId={{5F3C2A9E-8B1D-4E6F-9A27-6C4D1B8E7F30}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
VersionInfoVersion={#MyAppVersion}
; Installs per user without admin rights by default; the user may choose "all users" (needs admin).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
DisableReadyPage=no
OutputDir={#SourceDir}
OutputBaseFilename=GameBoost-Setup-{#MyAppVersion}
SetupIconFile=..\src\GameBoost\Assets\GameBoost.ico
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
; Ask to close a running GameBoost before installing/uninstalling.
AppMutex=GameBoost.SingleInstance
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\{#MyAppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\README.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
; In-app updates run the setup silently with /UPDATE=1 – start GameBoost again afterwards.
Filename: "{app}\{#MyAppExe}"; Flags: nowait; Check: IsUpdate

[UninstallRun]
; Remove the "Start with Windows" entry the app may have created.
Filename: "{sys}\reg.exe"; Parameters: "delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Run"" /v GameBoost /f"; Flags: runhidden; RunOnceId: "RemoveAutostart"

[Code]
function IsUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;
