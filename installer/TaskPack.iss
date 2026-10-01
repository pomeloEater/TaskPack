; TaskPack 설치 프로그램 (Inno Setup 6)
; 직접 컴파일하지 말고 저장소 루트의 build-installer.ps1 을 실행한다 (게시·버전 전달을 함께 한다)

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
; 두 가지 설치 파일: 기본은 .NET 포함판, /DLite=1 이면 .NET을 따로 설치해야 하는 lite (build-installer.ps1 이 둘 다 만든다)
#ifdef Lite
  #define OutputSuffix "-lite"
  #define PublishDir "publish-lite"
#else
  #define OutputSuffix ""
  #define PublishDir "publish"
#endif
#define AppName "TaskPack"
#define AppExe "TaskPack.exe"
; 작업표시줄에서 TaskPack 아이콘 아래에 "실행 중"으로 묶이려면 바로가기와 프로그램이 같은 식별자를 써야 한다 (Drawer.AppId)
#define AppUserModelId "TaskPack.Drawer"

[Setup]
AppId={{6B0E4C9A-2F3D-4E7B-9C1A-5D8F7E2B4A61}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=TaskPack
VersionInfoVersion={#AppVersion}
; 기본은 관리자 권한 없이 내 계정에만 설치. 시작할 때 "모든 사용자용"도 고를 수 있다
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupIconFile=..\assets\TaskPack.ico
WizardSmallImageFile=wizard-small.bmp
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
; 실행 중인 TaskPack이 있으면 닫고 설치·제거한다
CloseApplications=yes
OutputDir=Output
OutputBaseFilename=TaskPack-Setup-{#AppVersion}{#OutputSuffix}
Compression=lzma2
SolidCompression=yes
ShowLanguageDialog=auto

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[InstallDelete]
; 판이 다른 설치 파일 위에 덮어 설치해도 실행 방식이 섞이지 않게, 이전 프로그램 파일을 먼저 지운다.
; (.NET 포함판의 런타임 DLL이 lite 설치 폴더에 남는 것을 막는다. 가방 데이터는 %APPDATA%에 있어 영향 없음)
; TaskPack.exe가 있는 폴더일 때만 지우며, 제거 프로그램(unins*)은 건드리지 않는다
Type: files; Name: "{app}\*.dll"; Check: IsExistingInstall
Type: files; Name: "{app}\*.json"; Check: IsExistingInstall
Type: filesandordirs; Name: "{app}\cs"; Check: IsExistingInstall
Type: filesandordirs; Name: "{app}\de"; Check: IsExistingInstall
Type: filesandordirs; Name: "{app}\es"; Check: IsExistingInstall
Type: filesandordirs; Name: "{app}\fr"; Check: IsExistingInstall
Type: filesandordirs; Name: "{app}\it"; Check: IsExistingInstall
Type: filesandordirs; Name: "{app}\ja"; Check: IsExistingInstall
Type: filesandordirs; Name: "{app}\ko"; Check: IsExistingInstall
Type: filesandordirs; Name: "{app}\pl"; Check: IsExistingInstall
Type: filesandordirs; Name: "{app}\pt-BR"; Check: IsExistingInstall
Type: filesandordirs; Name: "{app}\ru"; Check: IsExistingInstall
Type: filesandordirs; Name: "{app}\tr"; Check: IsExistingInstall
Type: filesandordirs; Name: "{app}\zh-Hans"; Check: IsExistingInstall
Type: filesandordirs; Name: "{app}\zh-Hant"; Check: IsExistingInstall

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}\licenses"; DestName: "TaskPack-LICENSE.txt"; Flags: ignoreversion
Source: "..\assets\fonts\OFL.txt"; DestDir: "{app}\licenses"; DestName: "Pretendard-OFL.txt"; Flags: ignoreversion
Source: "..\assets\emoji\LICENSE"; DestDir: "{app}\licenses"; DestName: "FluentEmoji-LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; AppUserModelID: "{#AppUserModelId}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; AppUserModelID: "{#AppUserModelId}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 프로그램 안의 "작업표시줄에 고정하기"가 만든 시작 메뉴 바로가기
Type: files; Name: "{userprograms}\{#AppName}.lnk"

[Messages]
; 완료 화면 문구는 바로가기 폴더를 만든 경우(FinishedLabel)와 아닌 경우(FinishedLabelNoIcons)가 따로 있어 둘 다 바꾼다
korean.FinishedLabel=TaskPack 설치가 끝났습니다.%n%n작업표시줄에 고정하려면: 시작 메뉴에서 TaskPack을 우클릭 → '작업 표시줄에 고정'을 눌러 주세요. (Windows가 프로그램의 자동 고정을 막고 있어 직접 고정해야 합니다.)
korean.FinishedLabelNoIcons=TaskPack 설치가 끝났습니다.%n%n작업표시줄에 고정하려면: 시작 메뉴에서 TaskPack을 우클릭 → '작업 표시줄에 고정'을 눌러 주세요. (Windows가 프로그램의 자동 고정을 막고 있어 직접 고정해야 합니다.)
english.FinishedLabel=TaskPack has been installed.%n%nTo pin it to the taskbar, right-click TaskPack in the Start menu and choose 'Pin to taskbar'. (Windows does not allow programs to pin themselves.)
english.FinishedLabelNoIcons=TaskPack has been installed.%n%nTo pin it to the taskbar, right-click TaskPack in the Start menu and choose 'Pin to taskbar'. (Windows does not allow programs to pin themselves.)

[CustomMessages]
korean.NeedDotNet=TaskPack을 실행하려면 Microsoft의 무료 구성 요소인 .NET 10 데스크톱 런타임(x64)이 필요합니다.%n%n[예]를 누르면 설치를 멈추고 Microsoft 공식 사이트에서 런타임 설치 파일(약 60MB)을 바로 내려받습니다.%n받은 파일을 실행해 런타임을 설치한 뒤, 이 TaskPack 설치 프로그램을 다시 실행해 주세요.%n%n(직접 받으려면 dotnet.microsoft.com 다운로드 페이지에서 ".NET 데스크톱 런타임 10.0.x"의 Windows "설치 관리자 x64"를 받으세요.)
english.NeedDotNet=TaskPack requires the .NET 10 Desktop Runtime (x64), a free component from Microsoft.%n%nChoose [Yes] to stop setup and download the runtime installer (about 60 MB) from Microsoft.%nRun it, then run this TaskPack setup again.%n%n(To get it yourself: on the dotnet.microsoft.com download page, pick ".NET Desktop Runtime 10.0.x" > Windows "Installer x64".)
korean.DeleteData=가방 데이터(가방 목록, 칸 내용, 아이콘, 백업 전 보관본)도 지울까요?%n%n다시 설치해서 쓸 생각이면 '아니요'를 누르세요.
english.DeleteData=Also delete your bags (tabs, items, icons, pre-restore copies)?%n%nChoose 'No' if you plan to reinstall.

[Code]
// 같은 폴더에 이미 TaskPack이 설치되어 있는지 (덮어 설치 정리 대상)
function IsExistingInstall: Boolean;
begin
  Result := FileExists(ExpandConstant('{app}\{#AppExe}'));
end;

#ifdef Lite
// .NET 10 데스크톱 런타임(x64)이 설치되어 있는지: dotnet\shared\Microsoft.WindowsDesktop.App.* 폴더로 판단
function HasDesktopRuntime10(const DotNetDir: String): Boolean;
var
  FindRec: TFindRec;
begin
  Result := FindFirst(DotNetDir + '\shared\Microsoft.WindowsDesktop.App.*', FindRec);
  if Result then
    FindClose(FindRec);
end;

function IsDesktopRuntime10Installed: Boolean;
var
  Dir: String;
begin
  Dir := ExpandConstant('{commonpf64}\dotnet');
  // ARM64 Windows는 x64 런타임을 dotnet\x64 아래에 설치한다
  Result := HasDesktopRuntime10(Dir) or HasDesktopRuntime10(Dir + '\x64');
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not IsDesktopRuntime10Installed then
  begin
    if SuppressibleMsgBox(CustomMessage('NeedDotNet'), mbConfirmation, MB_YESNO, IDYES) = IDYES then
      // Microsoft 공식 단축 주소: 항상 .NET 10 데스크톱 런타임 x64 최신 설치 파일로 연결된다
      ShellExec('open', 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    Result := False;
  end;
end;
#endif

// 제거가 끝나면 가방 데이터도 지울지 묻는다 (기본: 아니요)
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := ExpandConstant('{userappdata}\TaskPack');
    if DirExists(DataDir) then
      if SuppressibleMsgBox(CustomMessage('DeleteData'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES then
        DelTree(DataDir, True, True, True);
  end;
end;
