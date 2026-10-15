; 몽볼륨 설치 스크립트 (Inno Setup 6). release.bat 이 /DSrcDir=<몽볼륨.exe 가 있는 폴더> /DAppVersion=<src\MainWindow.cs 의 Version> 으로 실행한다.
#ifndef SrcDir
  #define SrcDir "..\bin"
#endif
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppName "몽볼륨"
#define AppExe "몽볼륨.exe"
#define ProtoId "mongvolume"

[Setup]
AppId={{839DCF52-1EF5-416B-AAE6-95AEBB4D5C1E}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=NoAd
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\setup
OutputBaseFilename=몽볼륨-Setup-{#AppVersion}
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}.0
VersionInfoProductName={#AppName}
VersionInfoCompany=NoAd

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SrcDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; 홈페이지(설치 확인 페이지)의 [실행] 버튼이 쓰는 프로토콜. 제거할 때 함께 지운다.
Root: HKA; Subkey: "Software\Classes\{#ProtoId}"; ValueType: string; ValueName: ""; ValueData: "URL:{#AppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\{#ProtoId}"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\{#ProtoId}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"""

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
