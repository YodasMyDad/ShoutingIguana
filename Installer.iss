; Shouting Iguana Installer Script for Inno Setup 6
; Supports both x86 and x64 builds

#define MyAppName "Shouting Iguana"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Your Company"
#define MyAppExeName "ShoutingIguana.exe"

; Platform will be defined via command line: /DPlatform=x64 or /DPlatform=x86
#ifndef Platform
  #define Platform "x64"
#endif

#if Platform == "x64"
  #define PlatformName "x64"
  #define ArchitecturesMode "x64compatible"
  #define ProgramFilesFolder "pf64"
#else
  #define PlatformName "x86"
  #define ArchitecturesMode ""
  #define ProgramFilesFolder "pf32"
#endif

[Setup]
AppId={{8F7A9B3C-2E1D-4F5C-9A8B-7D6E5F4C3B2A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={#ProgramFilesFolder}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=publish\installer
OutputBaseFilename=ShoutingIguana-{#PlatformName}
SetupIconFile=Assets\logo.ico
LicenseFile=LICENSE
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
#if Platform == "x64"
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64compatible
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "publish\{#PlatformName}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\logo.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\logo.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function IsDotNetInstalled(): Boolean;
var
  ResultCode: Integer;
begin
  // Check if .NET 9 runtime is installed (basic check)
  Result := Exec('dotnet', '--list-runtimes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsDotNetInstalled() then
  begin
    if MsgBox('.NET 9 Runtime does not appear to be installed. The application may not run without it. Continue anyway?', 
              mbConfirmation, MB_YESNO) = IDNO then
    begin
      Result := False;
    end;
  end;
end;

