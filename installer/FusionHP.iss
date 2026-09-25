; ============================================================================
;  Fusion-HP · installer/FusionHP.iss — instalador dual x86/x64 (Inno Setup 6)
;  [SPEC §4.4]: instala el binario de la arquitectura detectada, sin permisos
;  elevados obligatorios y SIN tocar el Registro para funciones [SPEC §11.4].
;  El staging lo prepara el CI: staging/ con FusionHP.x86.exe, FusionHP.x64.exe,
;  FusionStudio.exe, FusionStudio.Lite.exe, FusionShared.dll, resources/.
; ============================================================================

#define AppName "Fusion HP"
#define AppVersion "2.0.0-beta.1"
#define AppPublisher "Fusion HP"
#define AppExeName "FusionHP.exe"
#define DotNetUrl "https://go.microsoft.com/fwlink/?linkid=2088631"

[Setup]
AppId={{6F55B1A2-9C4D-4E8A-B7F3-FUSIONHP001}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=dist\installer
OutputBaseFilename=FusionHP-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
; Instalar por usuario cuando sea posible (sin exigir elevación) [SPEC §11.4]
PrivilegesRequiredOverridesAllowed=dialog
LicenseFile=..\LICENSE.md
SetupIconFile=..\resources\img\app.ico

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[CustomMessages]
es.Escritorio=Acceso directo en el escritorio
es.Lanzar=Ejecutar {#AppName}
es.DescargaNet=Preparando el instalador de .NET Framework 4.8 (opcional)

[Tasks]
Name: "desktopicon"; Description: "{cm:Escritorio}"; GroupDescription: "Iconos:"
Name: "dotnet"; Description: "Instalar .NET Framework 4.8 desde Internet (opcional; el programa funciona sin él)"; Flags: unchecked

[Files]
; Núcleo y capa administrada (elección por arquitectura)
Source: "staging\FusionHP.x86.exe"; DestDir: "{app}"; DestName: "FusionHP.exe"; Check: NotX64; Flags: ignoreversion
Source: "staging\FusionHP.x64.exe"; DestDir: "{app}"; DestName: "FusionHP.exe"; Check: IsX64; Flags: ignoreversion
Source: "staging\FusionShared.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "staging\FusionStudio.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "staging\FusionStudio.Lite.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "staging\resources\*"; DestDir: "{app}\resources"; Flags: recursesubdirs ignoreversion
Source: "..\resources\data\bible_rvr1909.json"; DestDir: "{app}\resources\data"; Flags: skipifsourcedoesntexist ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Desinstalar {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:Lanzar}"; Flags: nowait postinstall skipifsilent
; .NET 4.8 opcional: componente separado, consentimiento explícito [SPEC §4.1]
Filename: "{tmp}\ndp48-web.exe"; Parameters: "/passive /norestart"; Check: WantDotNet; Flags: skipifdoesntexist runhidden

[UninstallDelete]
Type: filesandordirs; Name: "{app}\datos"

[Code]
function IsX64: Boolean;
begin
  Result := IsWin64;
end;

function NotX64: Boolean;
begin
  Result := not IsWin64;
end;

function WantDotNet: Boolean;
begin
  Result := WizardIsTaskSelected('dotnet');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Dest: String;
begin
  Result := True;
  // Descarga del instalador web de .NET SOLO si la tarea opcional está marcada
  if (CurPageID = wpReady) and WantDotNet then
  begin
    Dest := ExpandConstant('{tmp}\ndp48-web.exe');
    try
      DownloadTemporaryFile('{#DotNetUrl}', 'ndp48-web.exe', nil);
      Log('Descargado instalador opcional de .NET 4.8');
    except
      // Sin Internet o descarga fallida: la instalación continúa (perfil C/B)
      MsgBox('No se pudo descargar .NET Framework 4.8. ' +
             'Fusion HP se instalará de todos modos y funcionará con lo que haya ' +
             'incluso sin .NET (modo nativo).', mbInformation, MB_OK);
    end;
  end;
end;

procedure InitializeWizard;
begin
  // Diálogo claro sobre .NET [SPEC §4.1]: nada se instala sin consentimiento
end;
