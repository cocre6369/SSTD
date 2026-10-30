#define AppName "SSTD"
#ifndef AppVersion
#define AppVersion "1.0.1"
#endif
#define ExeName "SSTD.exe"
[Setup]
AppId={{8A77F9D2-5E36-4D01-A4C3-9F5051D6C8B2}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName=SSTD {#AppVersion}
AppPublisher=SSTD contributors
AppSupportURL=https://github.com/cocre6369/SSTD
DefaultDirName={localappdata}\Programs\SSTD
DefaultGroupName=SSTD
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts
OutputBaseFilename=SSTD-Setup
SetupIconFile=..\assets\SSTD.ico
UninstallDisplayIcon={app}\SSTD.exe
UninstallDisplayName=SSTD — Super Terminal of Deepthinking
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}.0
VersionInfoDescription=SSTD — Super Terminal of Deepthinking installer
LicenseFile=..\LICENSE
InfoBeforeFile=..\installer\BEFORE_INSTALL.txt
[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked
[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs
[Icons]
Name: "{autoprograms}\SSTD"; Filename: "{app}\SSTD.exe"; WorkingDir: "{app}"; Comment: "Super Terminal of Deepthinking"
Name: "{autodesktop}\SSTD"; Filename: "{app}\SSTD.exe"; Tasks: desktopicon; WorkingDir: "{app}"
[Run]
Filename: "{app}\SSTD.exe"; Description: "Launch SSTD"; Flags: nowait postinstall skipifsilent
; Deliberately no recursive user-profile cleanup, registry edits, Ollama removal, or system changes.
; Only files installed by this installer and its shortcuts are removed.
