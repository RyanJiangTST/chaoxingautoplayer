#define MyAppName "超星视频连播器"
#define MyAppVersion "0.1.5"
#define MyAppPublisher "ChaoxingAutoPlayer"
#define MyAppExeName "ChaoxingAutoPlayer.exe"
#define MyPortableDir "portable-" + MyAppVersion

[Setup]
AppId={{6A19D73D-9197-4E49-BF20-7D4A0490FA33}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\ChaoxingAutoPlayer
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\ChaoxingAutoPlayer
OutputBaseFilename=ChaoxingAutoPlayer-Setup-{#MyAppVersion}
SetupIconFile=..\src\ChaoxingAutoPlayer\assets\branding\chaoxing-autoplayer.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupLogging=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
Source: "..\artifacts\ChaoxingAutoPlayer\{#MyPortableDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
  if (not DirExists(ExpandConstant('{pf32}\Microsoft\EdgeWebView\Application')))
     and (not DirExists(ExpandConstant('{localappdata}\Microsoft\EdgeWebView\Application'))) then
  begin
    MsgBox(
      '没有检测到 Microsoft Edge WebView2 Runtime。程序仍会安装，但首次启动前需要从 Microsoft 官网安装 WebView2 Runtime。',
      mbInformation,
      MB_OK);
  end;
end;
