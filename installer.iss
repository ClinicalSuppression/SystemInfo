; SystemInfo installer (Inno Setup 6)
; Keep AppVersion in sync with <Version> in SystemInfo.csproj.

#ifndef PublishDir
  #define PublishDir "bin\Release\net8.0-windows\win-x64\publish"
#endif

[Setup]
AppId={{7F2E1A4B-9C3D-4E5F-8A6B-2C3D4E5F6A7B}
AppName=SystemInfo
AppVersion=1.0.2
AppPublisher=ClinicalSuppression
; --- Code Signing (uncomment when cert is available) ---
; SignCommand="signtool sign /fd SHA256 /a /f cert.pfx /p password /t http://timestamp.digicert.com"
; [Signing] section registers the command for use below
; SignToolArgs is optional; use the [Signing] section instead for clarity.
DefaultDirName={autopf}\SystemInfo
DefaultGroupName=SystemInfo
OutputDir=Output
OutputBaseFilename=SystemInfo_Setup_1.0.2
SetupIconFile=Assets\SystemInfo.ico
Compression=lzma
SolidCompression=yes
PrivilegesRequired=lowest
MinVersion=10.0.17763
ArchitecturesInstallIn64BitMode=x64os
ArchitecturesAllowed=x64compatible
WizardStyle=modern
DisableProgramGroupPage=yes
UninstallDisplayName=SystemInfo

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\SystemInfo"; Filename: "{app}\SystemInfo.exe"
Name: "{autodesktop}\SystemInfo"; Filename: "{app}\SystemInfo.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Run]
Filename: "{app}\SystemInfo.exe"; Description: "Launch SystemInfo now"; Flags: nowait postinstall skipifsilent

[Code]
{ Framework-dependent publishes need the .NET 8 Desktop Runtime. }
function IsDotNet8DesktopRuntimeInstalled: Boolean;
var
  Found: Boolean;
begin
  Found :=
    RegKeyExists(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost') or
    RegKeyExists(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App') or
    RegKeyExists(HKCU, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost') or
    RegKeyExists(HKCU, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App');
  Result := Found;
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  if not IsDotNet8DesktopRuntimeInstalled then
  begin
    if MsgBox('The .NET 8 Desktop Runtime was not found. SystemInfo needs it to run.' + #13#10 + #13#10 +
      'Download it from https://dotnet.microsoft.com/download/dotnet/8.0 (x64 Desktop Runtime), then run the installer again.' + #13#10 + #13#10 +
      'Alternatively, use the self-contained build, which does not need the runtime.' + #13#10 + #13#10 +
      'Continue with the installation anyway?',
      mbConfirmation, MB_YESNO) = IDNO then
      Result := False;
  end;
end;
