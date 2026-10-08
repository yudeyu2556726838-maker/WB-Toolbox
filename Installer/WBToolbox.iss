#define MyAppName "WB Toolbox"
#define MyAppVersion "4.0.0 Alpha 40"
#define MyAppExeName "WB-Toolbox-v4.0.0-alpha.40.exe"

#ifndef MyAppSource
  #define MyAppSource AddBackslash(SourcePath) + "..\..\release\WB-Toolbox-v4.0.0-alpha.40"
#endif

#ifndef MyOutputDir
  #define MyOutputDir AddBackslash(SourcePath) + "..\..\release"
#endif

[Setup]
AppId={{93DA55C1-8132-4B56-B247-DDA8D8FD207D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=WB Toolbox
DefaultDirName={localappdata}\Programs\WB Toolbox
DefaultGroupName=WB Toolbox
DisableProgramGroupPage=yes
OutputDir={#MyOutputDir}
OutputBaseFilename=WB-Toolbox-v4.0.0-alpha.40-Setup
SetupIconFile=..\Assets\WBToolbox.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
AppMutex=Local\WBToolbox.Native.4.SingleInstance
CloseApplications=yes
RestartApplications=no
VersionInfoVersion=4.0.0.39
VersionInfoProductName={#MyAppName}
VersionInfoDescription=WB Toolbox 安装程序

[Languages]
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式："; Flags: unchecked

[Files]
Source: "{#MyAppSource}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyAppSource}\{#MyAppExeName}.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyAppSource}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyAppSource}\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
Type: files; Name: "{app}\WB工具箱-v*.exe"
Type: files; Name: "{app}\WB工具箱-v*.exe.config"

[Icons]
Name: "{group}\WB Toolbox"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 WB Toolbox"; Filename: "{uninstallexe}"
Name: "{autodesktop}\WB Toolbox"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 WB Toolbox"; Flags: nowait postinstall skipifsilent
