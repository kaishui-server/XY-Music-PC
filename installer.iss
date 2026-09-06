; XY Music Inno Setup 安装脚本
#define MyAppName "XY Music"
#define MyAppNameEn "XY Music"
#define MyAppVersion "1.0.0"
#define MyAppExeName "XY Music.exe"
#define MyAppPublisher "XYMusic"

[Setup]
AppId={{A3C5E7F9-1B2D-4F60-8A9C-E0D2F4B6A8C0}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppNameEn}
DefaultGroupName={#MyAppName}
UninstallDisplayName={#MyAppName}
OutputDir=D:\Github\original-sound-hq-player\installer-output
OutputBaseFilename=XYMusic_Setup_{#MyAppVersion}
SetupIconFile=Assets\icon.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
PrivilegesRequiredOverridesAllowed=dialog
; 无签名,关闭UAC页面签名提示的无意义警告文案
DisableProgramGroupPage=yes

[Languages]
; Inno Setup 6.5+ 已移除官方简体中文, 使用仓库内本地语言文件(支持6.5.0+)
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "release-publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 卸载时清理日志等运行时生成数据(用户目录下的数据不动)
Type: filesandordirs; Name: "{app}\Logs"
