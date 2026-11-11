; 版本与发布目录由 scripts/package.ps1 传入。
[Setup]
AppId={{C4B65DDD-AB40-40BD-8B72-422B90E13403}
AppName=卫戍协议助手
AppVersion={#AppVersion}
AppPublisher=LBEILC
DefaultDirName={localappdata}\Programs\StrongholdAssistant
DisableDirPage=no
DefaultGroupName=卫戍协议助手
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
UninstallDisplayIcon={app}\StrongholdAssistant.exe

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[InstallDelete]
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

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "installed.json"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\卫戍协议助手"; Filename: "{app}\StrongholdAssistant.exe"
Name: "{autodesktop}\卫戍协议助手"; Filename: "{app}\StrongholdAssistant.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\StrongholdAssistant.exe"; Description: "运行卫戍协议助手"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    if SuppressibleMsgBox('是否同时删除阵容和设置（%AppData%\StrongholdAssistant）？',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
      DelTree(ExpandConstant('{userappdata}\StrongholdAssistant'), True, True, True);
end;
