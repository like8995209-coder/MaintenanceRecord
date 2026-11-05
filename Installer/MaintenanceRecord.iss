#define MyAppName "管网维修"
#define MyAppVersion "4.1.0"
#define MyAppPublisher "Pipe Maintenance Contributors"
#define MyAppExeName "MaintenanceRecord.exe"

[Setup]
AppId={{0CC0C6E9-CB67-4EC7-A7B5-92EE4A1A7613}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\MaintenanceRecord
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\Builds
OutputBaseFilename=MaintenanceRecord-Setup-{#MyAppVersion}
SetupIconFile=..\MaintenanceRecordApp\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=yes
DirExistsWarning=no
VersionInfoVersion=4.1.0.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} 安装程序
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
SetupLogging=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Files]
Source: "..\MaintenanceRecordApp\Publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\MaintenanceRecordApp\Assets\app.ico"; DestDir: "{app}"; DestName: "app-icon-{#MyAppVersion}.ico"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app-icon-{#MyAppVersion}.ico"; IconIndex: 0
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\app-icon-{#MyAppVersion}.ico"; IconIndex: 0

[InstallDelete]
Type: files; Name: "{autoprograms}\管网维修记录.lnk"
Type: files; Name: "{autodesktop}\管网维修记录.lnk"
Type: files; Name: "{autoprograms}\管网维修.lnk"
Type: files; Name: "{autodesktop}\管网维修.lnk"

[Registry]
Root: HKCU; Subkey: "Software\M4099\MaintenanceRecord"; ValueType: string; ValueName: "InstallPath"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\M4099\MaintenanceRecord"; ValueType: string; ValueName: "Version"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletekey

[Run]
Filename: "{win}\System32\ie4uinit.exe"; Parameters: "-show"; Flags: runhidden nowait
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\data-path.txt"

[Code]
var
  DataDirectoryPage: TInputDirWizardPage;

function GetDefaultDataDirectory(): String;
var
  DesktopPath: String;
begin
  if DirExists('D:\') then
    Result := 'D:\管网维修数据'
  else
  begin
    if WizardSilent() then
    begin
      DesktopPath := GetEnv('USERPROFILE');
      if DesktopPath <> '' then
        DesktopPath := DesktopPath + '\Desktop'
      else
        DesktopPath := ExpandConstant('{userdesktop}');
    end
    else
      DesktopPath := ExpandConstant('{userdesktop}');
    Result := DesktopPath + '\管网维修数据';
  end;
end;

procedure InitializeWizard();
var
  SavedPath: String;
  Description: String;
begin
  Description :=
    '程序将在下列文件夹创建并保存本地维修台账数据库。' + #13#10 +
    '检测到 D 盘时建议使用 D:\管网维修数据；没有 D 盘时默认使用桌面。' + #13#10 +
    '请确认路径后点击“下一步”。';
  if not DirExists('D:\') then
    Description := Description + #13#10 +
      '当前未检测到 D 盘，已自动将默认位置设置为桌面。';

  DataDirectoryPage := CreateInputDirPage(
    wpSelectDir,
    '数据库存储位置',
    '请确认本地数据库文件夹',
    Description,
    False,
    '新建文件夹');
  DataDirectoryPage.Add('数据库文件夹：');

  SavedPath := GetPreviousData('DataDirectory', '');
  if SavedPath = '' then
    RegQueryStringValue(HKCU, 'Software\M4099\MaintenanceRecord',
      'DataPath', SavedPath);

  if (SavedPath = '') or
     ((CompareText(Copy(SavedPath, 1, 3), 'D:\') = 0) and not DirExists('D:\')) then
    SavedPath := GetDefaultDataDirectory();

  DataDirectoryPage.Values[0] := SavedPath;
  if WizardSilent() then
    DataDirectoryPage.Values[0] := GetDefaultDataDirectory();
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  DataDirectory: String;
begin
  Result := True;
  if CurPageID = DataDirectoryPage.ID then
  begin
    if WizardSilent() then
      Exit;

    DataDirectory := Trim(DataDirectoryPage.Values[0]);
    if DataDirectory = '' then
    begin
      MsgBox('请选择本地数据库文件夹。', mbError, MB_OK);
      Result := False;
      Exit;
    end;

    if not ForceDirectories(DataDirectory) then
    begin
      MsgBox('无法创建以下数据库文件夹，请选择其他位置：' + #13#10 + DataDirectory,
        mbError, MB_OK);
      Result := False;
      Exit;
    end;

    Result :=
      MsgBox('将使用以下路径生成本地数据库文件夹：' + #13#10#13#10 +
        DataDirectory + #13#10#13#10 + '是否确定？',
        mbConfirmation, MB_YESNO) = IDYES;
  end;
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  SetPreviousData(PreviousDataKey, 'DataDirectory', DataDirectoryPage.Values[0]);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  DataDirectory: String;
begin
  if CurStep = ssPostInstall then
  begin
    DataDirectory := Trim(DataDirectoryPage.Values[0]);
    ForceDirectories(DataDirectory);
    ForceDirectories(DataDirectory + '\今日');
    ForceDirectories(DataDirectory + '\维修记录');
    ForceDirectories(DataDirectory + '\库');
    ForceDirectories(DataDirectory + '\图片');
    RegWriteStringValue(HKCU, 'Software\M4099\MaintenanceRecord',
      'DataPath', DataDirectory);
  end;
end;
