; 版本与发布目录由 scripts/package.ps1 传入。
[Setup]
AppId={{C4B65DDD-AB40-40BD-8B72-422B90E13403}
AppName=高效雅雅·卫戍协议助手
AppVersion={#AppVersion}
AppPublisher=LBEILC
DefaultDirName={localappdata}\Programs\StrongholdAssistant
DisableDirPage=no
DefaultGroupName=高效雅雅·卫戍协议助手
UsePreviousGroup=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
AppMutex=Local\StrongholdAssistant
CloseApplications=yes
RestartApplications=no
OutputDir={#ReleaseDir}
OutputBaseFilename=StrongholdAssistant-v{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
SetupIconFile=..\branding\icon.ico
UninstallDisplayIcon={app}\StrongholdAssistant.exe

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Messages]
SetupAppRunningError=高效雅雅还开着（可能是以管理员身份运行的），请先在托盘或任务管理器里退出它，再点重试。

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[InstallDelete]
; 改名后清理旧快捷方式，旧程序组仅在为空时删除。
Type: files; Name: "{autoprograms}\卫戍协议助手\卫戍协议助手.lnk"
Type: files; Name: "{autodesktop}\卫戍协议助手.lnk"
Type: dirifempty; Name: "{autoprograms}\卫戍协议助手"
; 仅清理应用拥有的文件，不删除用户选定目录里的其他文件和卸载记录。
Type: files; Name: "{app}\StrongholdAssistant.exe"
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\StrongholdAssistant.*.json"
Type: files; Name: "{app}\LICENSE"
Type: files; Name: "{app}\LICENSE.upstream"
Type: files; Name: "{app}\NOTICE"
Type: files; Name: "{app}\ASSETS.md"
Type: filesandordirs; Name: "{app}\data"
Type: filesandordirs; Name: "{app}\ui"
Type: filesandordirs; Name: "{app}\runtimes"
Type: filesandordirs; Name: "{app}\plugins"

Type: filesandordirs; Name: "{app}\app"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\app"
Type: filesandordirs; Name: "{app}\ui"
Type: filesandordirs; Name: "{app}\data"

[Files]
; 覆盖安装期间即使有人点快捷方式，新宿主能被启动时也已复制完数据。
Source: "{#PublishDir}\data\*"; DestDir: "{app}\data"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDir}\*"; Excludes: "\data\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "installed.json"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
Root: HKCU; Subkey: "Software\Classes\stronghold"; ValueType: string; ValueData: "URL:StrongholdAssistant"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\stronghold"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKCU; Subkey: "Software\Classes\stronghold\shell\open\command"; ValueType: string; ValueData: """{app}\StrongholdAssistant.exe"" ""%1"""

[Icons]
Name: "{group}\高效雅雅·卫戍协议助手"; Filename: "{app}\StrongholdAssistant.exe"
Name: "{autodesktop}\高效雅雅·卫戍协议助手"; Filename: "{app}\StrongholdAssistant.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\StrongholdAssistant.exe"; Description: "运行高效雅雅·卫戍协议助手"; Flags: nowait postinstall skipifsilent

[Code]
#include "InstallGuard.iss"

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := CheckAssistantFiles(ExpandConstant('{app}'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    if SuppressibleMsgBox('是否同时删除阵容和设置（%AppData%\StrongholdAssistant）？',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
      DelTree(ExpandConstant('{userappdata}\StrongholdAssistant'), True, True, True);
end;
