; =====================================================================
; PCActivityLog Inno Setup 安装脚本
; 构建方式：powershell -ExecutionPolicy Bypass -File installer\build.ps1
;
; 设计要点：
;   * PrivilegesRequired=lowest —— 全程按当前用户安装到 {localappdata}\Programs，
;     与程序本身"仅写 HKCU、无需管理员"的设计一致
;   * AppId=PCActivityLog（非 GUID）—— 卸载键名固定为 PCActivityLog_is1，
;     程序端 AppRegistrationService.IsInstallerManaged() 据此判断"由安装器管理"，
;     跳过自我登记，避免「设置 → 应用」出现重复条目
;   * 升级路径 —— 同一 AppId 重新运行新版本安装包即原位升级：
;     沿用旧安装目录，用户数据在 %LOCALAPPDATA%\PCActivityLog 不受影响
;   * 升级/卸载前通过程序自身的单实例退出信号优雅关闭（冲写数据库队列），不强杀；
;     静默卸载不弹任何窗，默认保留用户数据
; =====================================================================

#ifndef MyAppVersion
#define MyAppVersion "2.6.1"
#endif

#define MyAppName "电脑日志记录"
#define MyAppExeName "PCActivityLog.exe"

[Setup]
AppId=PCActivityLog
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=PCActivityLog
DefaultDirName={localappdata}\Programs\PCActivityLog
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=dist
OutputBaseFilename=PCActivityLog-Setup-{#MyAppVersion}
SetupIconFile=..\PCActivityLog\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  MutexName = 'Local\PCActivityLog_SingleInstance';
  ExitEventName = 'Local\PCActivityLog_Exit';
  SYNCHRONIZE = $00100000;
  EVENT_MODIFY_STATE = $0002;

function OpenMutexW(dwDesiredAccess: DWORD; bInheritHandle: BOOL; lpName: string): THandle; external 'OpenMutexW@kernel32.dll stdcall';
function CloseHandle(hObject: THandle): BOOL; external 'CloseHandle@kernel32.dll stdcall';
function OpenEventW(dwDesiredAccess: DWORD; bInheritHandle: BOOL; lpName: string): THandle; external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(hEvent: THandle): BOOL; external 'SetEvent@kernel32.dll stdcall';
procedure Sleep(ms: DWORD); external 'Sleep@kernel32.dll stdcall';

// 程序在运行则请求其优雅退出（复用程序自身的单实例退出信号，数据库队列会被冲写），
// 最多等 10 秒。返回 True = 已退出或本就未运行。
function CloseRunningApp(): Boolean;
var
  h, ev: THandle;
  i: Integer;
begin
  Result := True;

  h := OpenMutexW(SYNCHRONIZE, False, MutexName);
  if h = 0 then Exit; // 没在运行
  CloseHandle(h);

  // 先礼貌请求退出，绝不强杀（强杀会丢掉未冲写的记录队列）
  ev := OpenEventW(EVENT_MODIFY_STATE, False, ExitEventName);
  if ev <> 0 then
  begin
    SetEvent(ev);
    CloseHandle(ev);
  end;

  // 互斥体消失 = 程序已退出
  for i := 1 to 20 do
  begin
    h := OpenMutexW(SYNCHRONIZE, False, MutexName);
    if h = 0 then Exit;
    CloseHandle(h);
    Sleep(500);
  end;
  Result := False;
end;

// 安装/升级前：正在运行则先优雅退出
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not CloseRunningApp() then
    Result := '电脑日志记录正在运行且未能自动退出，请从托盘菜单退出后重试。';
end;

// 卸载前：正在运行则先优雅退出。返回 False 取消卸载
function InitializeUninstall(): Boolean;
begin
  Result := True;
  if not CloseRunningApp() then
  begin
    MsgBox('电脑日志记录正在运行且未能自动退出，请从托盘菜单退出后重试卸载。', mbError, MB_OK);
    Result := False;
  end;
end;

// 卸载完成后：比照程序自带卸载流程，询问是否同时删除应用数据（默认保留）。
// 静默卸载（/VERYSILENT）不弹窗，默认保留数据。
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  dataDir: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if UninstallSilent then Exit;
    dataDir := ExpandConstant('{localappdata}\PCActivityLog');
    if DirExists(dataDir) then
      if MsgBox('是否同时删除应用数据（历史记录数据库、设置与日志）？' #13#10 #13#10 +
                '选“否”仅卸载程序，数据保留在：' + dataDir,
                mbConfirmation, MB_YESNO) = IDYES then
        DelTree(dataDir, True, True, True);
  end;
end;
