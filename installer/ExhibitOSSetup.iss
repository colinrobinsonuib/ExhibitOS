; ExhibitOS Installer Script (Inno Setup)
; Compiles into ExhibitOSSetup.exe

#define MyAppName "ExhibitOS"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "ExhibitOS Team"
#define MyAppURL "https://github.com/exhibitos/exhibitos"
#define MyAppExeName "ExhibitOSManager.exe"

[Setup]
AppId={{D37F291E-4E38-4C82-A512-B60814EE9E5F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName=C:\ExhibitOS
DisableDirPage=yes
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist-installer
OutputBaseFilename=ExhibitOSSetup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Dist directory files
Source: "..\dist\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{app}\artwork"; Flags: uninsneveruninstall
Name: "{app}\config"
Name: "{app}\logs"
Name: "{app}\runtime"
Name: "{app}\runtime\bin\node"
Name: "{app}\runtime\bin\mpv"

[Icons]
Name: "{group}\{#MyAppName} Manager"; Filename: "{app}\{#MyAppExeName}"
Name: "{commondesktop}\{#MyAppName} Manager"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Run restore to normal use on uninstall before removing files
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""& '{app}\{#MyAppExeName}' --restore-normal-use"""; Flags: runhidden

[Code]
var
  DeleteArtworkCheckbox: TNewCheckBox;

procedure InitializeUninstallProgressForm();
begin
  DeleteArtworkCheckbox := TNewCheckBox.Create(UninstallProgressForm);
  DeleteArtworkCheckbox.Parent := UninstallProgressForm;
  DeleteArtworkCheckbox.Left := ScaleX(16);
  DeleteArtworkCheckbox.Top := ScaleY(130);
  DeleteArtworkCheckbox.Width := ScaleX(400);
  DeleteArtworkCheckbox.Height := ScaleY(24);
  DeleteArtworkCheckbox.Caption := 'Also permanently delete artwork files in C:\ExhibitOS\artwork';
  DeleteArtworkCheckbox.Checked := False;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if Assigned(DeleteArtworkCheckbox) and DeleteArtworkCheckbox.Checked then
    begin
      DelTree(ExpandConstant('{app}\artwork'), True, True, True);
    end;
  end;
end;
