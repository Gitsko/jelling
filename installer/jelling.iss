; Per-user installer around the self-contained publish output. CI compiles it once per architecture:
;   ISCC /DAppVersion=1.2.3 /DArch=x64 /DPublishDir=<publish\win-x64> /O<output dir> installer\jelling.iss
; Both paths absolute: relative ones count from this folder, not from where ISCC runs

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-" + Arch
#endif

#if Arch == "x64"
  ; Also runs on ARM64 Windows under emulation; the arm64 build is the better fit there
  #define InstallArch "x64compatible"
#elif Arch == "arm64"
  #define InstallArch "arm64"
#else
  #error Arch must be x64 or arm64
#endif

; Never change: Windows finds an installed version by it, so a new one would install beside the old
#define AppGuid "4448D9D6-B701-424E-A67A-CFC4A1BCFEDF"
#define AppExe "Jelling.App.exe"
#define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"

[Setup]
AppId={{{#AppGuid}}
AppName=Jelling Battery Monitor
AppVersion={#AppVersion}
AppVerName=Jelling Battery Monitor {#AppVersion}
AppPublisher=Sebastian Koschel
AppPublisherURL=https://github.com/Gitsko/jelling
AppSupportURL=https://github.com/Gitsko/jelling/issues
AppUpdatesURL=https://github.com/Gitsko/jelling/releases
VersionInfoVersion={#AppVersion}
; No admin prompt: installs for the current user only, like the unzipped release
PrivilegesRequired=lowest
; The folder the README named for the ZIP, so an install takes over an unzipped copy in place. The tray
; keeps icon positions per exe path; a different folder would reset them
DefaultDirName={userpf}\Jelling
; Fixed, because an update clears out old program files from it (see [InstallDelete])
DisableDirPage=yes
DisableProgramGroupPage=yes
ArchitecturesAllowed={#InstallArch}
ArchitecturesInstallIn64BitMode={#InstallArch}
; The app's own minimum (TargetPlatformMinVersion)
MinVersion=10.0.22000
SetupIconFile=..\src\Jelling.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName=Jelling Battery Monitor
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
; No version in the name, so releases/latest/download/Jelling-Setup-win-x64.exe always points to the newest
OutputBaseFilename=Jelling-Setup-win-{#Arch}
; Beside the publish output, which git ignores; CI passes its own with /O
OutputDir=..\artifacts\installer
; The tray app has no window to close; force ends it so its files can be replaced
CloseApplications=force

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[CustomMessages]
; German.isl titles the group "Beginn des Setups", which reads as the start of this setup
german.AutoStartProgramGroupDescription=Autostart:
german.AutoStartProgram=%1 mit Windows starten

[Tasks]
; Offered on a first install only; an update keeps what the user chose in the app since (see [Registry])
Name: "autostart"; Description: "{cm:AutoStartProgram,Jelling}"; GroupDescription: "{cm:AutoStartProgramGroupDescription}"; Check: not IsUpgrade

[InstallDelete]
; Files a newer version no longer ships would stay behind otherwise. Only the kinds the publish output
; has at its top level; the language folders hold the same files in every version
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.winmd"
Type: files; Name: "{app}\*.pri"
Type: files; Name: "{app}\*.json"
Type: files; Name: "{app}\*.pdb"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Jelling"; Filename: "{app}\{#AppExe}"

[Registry]
; The same quoted path AutoStart writes, so the app shows "Start with Windows" as ticked
Root: HKCU; Subkey: "{#RunKey}"; ValueType: string; ValueName: "Jelling"; ValueData: """{app}\{#AppExe}"""; Tasks: autostart
; Left unticked on a first install, an entry an unzipped copy set must go too, or it starts anyway
Root: HKCU; Subkey: "{#RunKey}"; ValueType: none; ValueName: "Jelling"; Flags: deletevalue; Check: not IsUpgrade and not WizardIsTaskSelected('autostart')
; On an update the entry is only refreshed where it exists; turned off in the app, it stays off
Root: HKCU; Subkey: "{#RunKey}"; ValueType: string; ValueName: "Jelling"; ValueData: """{app}\{#AppExe}"""; Check: IsUpgrade and AutoStartEnabled
; Removed on uninstall however it was set, by the installer or in the app; it would start nothing after
Root: HKCU; Subkey: "{#RunKey}"; ValueType: none; ValueName: "Jelling"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,Jelling}"; Flags: nowait postinstall skipifsilent
; A silent update closes the app too; without this it would stay off until the next sign-in
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: WizardSilent and IsUpgrade

[UninstallRun]
; Its files cannot be removed while it runs
Filename: "{sys}\taskkill.exe"; Parameters: "/im {#AppExe} /f"; Flags: runhidden; RunOnceId: "StopJelling"

[Code]
var
  Upgrade: Boolean;

// Read once at the start: during the install the uninstall entry may already be rewritten
function InitializeSetup(): Boolean;
begin
  Upgrade := RegKeyExists(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{{#AppGuid}}_is1');
  Result := True;
end;

function IsUpgrade(): Boolean;
begin
  Result := Upgrade;
end;

function AutoStartEnabled(): Boolean;
begin
  Result := RegValueExists(HKCU, '{#RunKey}', 'Jelling');
end;
