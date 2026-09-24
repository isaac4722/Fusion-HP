; ============================================================================
;  LuminaPresentation Suite - installer/AppHibrida.iss
;  Copyright (c) 2026 Isaac. Licencia View-Only.
; ----------------------------------------------------------------------------
;  DUAL-ARCHITECTURE INSTALLER (fixture F6.06, spec v1.1 [SPEC section 4.4])
;
;  Normative requirements implemented here (F6.06):
;    1. Detect the CPU architecture at runtime.
;    2. Install ONLY the variant matching that architecture:
;       - x64 machine  -> native core LuminaCore.dll (x64)  + managed layer
;       - x86 machine  -> native core LuminaCore.dll (x86)  + managed layer
;       Pattern follows the official Inno Setup example "64BitTwoArch.iss":
;       one [Files] entry per architecture guarded with
;       Check: Is64BitInstallMode / Check: not Is64BitInstallMode.
;    3. Verify the .NET Framework 4.7.2+ presence (NDP "Release" >= 461808)
;       with READ-ONLY registry inspection (Pascal Script).
;    4. Offer the OFFICIAL offline .NET installer as an OPTIONAL component
;       (checkbox, checked by default when .NET is missing). If the redist is
;       not bundled, show the official Microsoft URL and continue: the app
;       degrades gracefully (profiles B/C). NO automatic download, EVER.
;    5. Do NOT require elevation for normal use: per-user install is the
;       default (see PRIVILEGE MODEL below).
;    6. The portable mode is NOT part of the installer: it ships as separate
;       ZIP archives built by scripts/package_portable.sh.
;
;  PRIVILEGE MODEL (documented decision):
;      PrivilegesRequired=lowest + PrivilegesRequiredOverridesAllowed=dialog
;      commandline. Default = per-user install (no UAC) into
;      {localappdata}\Programs\AppHibrida, because {autopf} resolves to
;      {userpf} for non-elevated installs. The wizard dialog (or /ALLUSERS on
;      the command line) still allows an all-users install into Program Files
;      - {autopf} then resolves to {commonpf} - with the standard UAC prompt.
;      This satisfies "no elevation for normal use" WITHOUT breaking a
;      Program Files deployment. The ONLY other elevation that can ever be
;      requested is the user-approved UAC to run the OPTIONAL .NET offline
;      installer (it needs admin by Microsoft's own requirement).
;
;  REGISTRY POLICY (documented decision):
;      The [Registry] section is INTENTIONALLY ABSENT. The application NEVER
;      reads/writes the registry (F6.05.5). The only registry writes in the
;      whole lifecycle are Inno Setup's standard uninstall bookkeeping
;      (UninstallString/DisplayName/etc. under
;      HKCU\...\Uninstall\{AppId}_is1 for per-user installs or HKLM for
;      all-users installs). The NDP v4 check below is READ-ONLY.
;
;  CONFIG PATHS (F6.05): installed mode -> %APPDATA%\AppHibrida (JSON config
;      + logs/, written by the app at runtime, not by this installer).
;      Portable mode -> folder of the program. Never the registry.
;
;  SOURCE LAYOUT (staging contract, overridable with /D on the ISCC command
;  line; defaults point to the same per-arch layout that
;  scripts/package_portable.sh builds into dist/portable/):
;      dist\portable\x86\*   dist\portable\x64\*   (flat layout per arch)
;      dist\redist\ndp48-x86-x64-allos-enu.exe     (optional .NET 4.8 redist)
;  NOTE: the official Microsoft ENU file name is "ndp48-x86-x64-alos-enu.exe"
;  (single "l"); this project standardizes on the fixture's name
;  "ndp48-x86-x64-allos-enu.exe" - rename the downloaded file accordingly.
;
;  Requires: Inno Setup 6.3 or later (x86compatible/x64compatible and {autopf}).
;  Compile:  iscc installer\AppHibrida.iss   (from the repository root)
;            -> dist\installer\LuminaPresentation-1.0.0-beta.1-setup.exe
; ============================================================================

#define AppName "LuminaPresentation Suite"
#define AppVersion "1.0.0-beta.1"
#define AppVersionNumeric "1.0.0.1"
#define AppPublisher "Isaac"
#define AppUrl "https://github.com/isaac4722/Fusion-HP"

; --- Staging overrides (iscc /DStagingX86="..." /DStagingX64="..." ) --------
#ifndef StagingX86
#define StagingX86 "..\dist\portable\x86"
#endif
#ifndef StagingX64
#define StagingX64 "..\dist\portable\x64"
#endif

; --- Optional .NET Framework 4.8 offline redist -----------------------------
; Compile-time probe (#ifexist): when the redist was staged next to this
; script, the "redist" component is compiled in; otherwise the component is
; omitted entirely and the fallback message with the official URL is shown
; at post-install when .NET is missing. No download is ever performed.
#define RedistDir "..\dist\redist"
#define RedistFileName "ndp48-x86-x64-allos-enu.exe"
#define RedistSourcePath RedistDir + "\" + RedistFileName
#define RedistBundledFlag "0"
#ifexist RedistSourcePath
#define RedistBundledFlag "1"
#endif

[Setup]
AppId={{7E1F2A38-4C6B-4D95-9A03-B8F2C5D61E47}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
VersionInfoVersion={#AppVersionNumeric}
VersionInfoTextVersion={#AppVersion}

; --- Dual architecture -------------------------------------------------------
; x86compatible: allowed on any x86 or x64 machine (blocks ARM64-only).
; x64compatible : run in 64-bit install mode on x64 -> {app} lands in the real
;                 "Program Files" and the 64-bit registry view is used; on x86
;                 machines the setup runs in 32-bit mode. Exactly one of the
;                 two LuminaCore.dll variants is installed (see [Files]).
ArchitecturesAllowed=x86compatible
ArchitecturesInstallIn64BitMode=x64compatible

; --- Privilege model: per-user by default, no elevation for normal use ------
; See "PRIVILEGE MODEL" in the header. {autopf} resolves per final mode:
;   per-user  -> C:\Users\<user>\AppData\Local\Programs\AppHibrida
;   all-users -> C:\Program Files\AppHibrida (64-bit view on x64)
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
DefaultDirName={autopf}\AppHibrida
DefaultGroupName=LuminaPresentation Suite
AllowNoIcons=yes

; Windows 7 SP1 is the normative floor (Win7 SP1 x86 -> Win11 x64).
MinVersion=6.1sp1
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
SetupLogging=yes
UninstallDisplayIcon={app}\LuminaPresentation.exe
SetupIconFile=..\resources\img\app.ico
OutputDir=..\dist\installer
OutputBaseFilename=LuminaPresentation-{#AppVersion}-setup

[Languages]
; Base language file shipped with every Inno Setup install (always present).
; All user-visible strings of this script (descriptions, messages) are in
; Spanish (es-VE) and the most visible wizard labels are overridden in
; [Messages] below. If the unofficial Spanish.isl is installed, CI may switch
; MessagesFile to "compiler:Languages\Spanish.isl" without other changes.
Name: "es"; MessagesFile: "compiler:Default.isl"

[Messages]
; Spanish (es-VE) overrides for the core wizard strings (Default.isl is EN).
SetupAppTitle=Instalación - %1
SetupWindowTitle=Instalación de %1
WelcomeLabel1=Bienvenido al Asistente de Instalación de [name].
WelcomeLabel2=Este programa instalará [name/ver] en su equipo.%n%nSe recomienda cerrar todas las demás aplicaciones antes de continuar.
SelectDirDesc=¿Dónde debe instalarse [name]?
SelectDirLabel3=El programa se instalará en la carpeta indicada a continuación.
SelectDirBrowseLabel=Para continuar, haga clic en Siguiente. Si prefiere otra carpeta, haga clic en Examinar.
SelectComponentsDesc=¿Qué componentes deben instalarse?
SelectComponentsLabel2=Seleccione los componentes que desea instalar; desmarque los que no desea instalar. Haga clic en Siguiente para continuar.
InstallingLabel=Instalando [name]...
FinishedHeadingLabel=Completada la instalación de [name]
FinishedLabelNoIcons=[name/ver] se instaló correctamente en su equipo.%n%nPuede ejecutar la aplicación seleccionando el icono correspondiente en el menú de inicio.
ExitSetupTitle=¿Salir de la instalación?
ExitSetupMessage=La instalación no se ha completado. Si sale ahora, el programa no se instalará.%n%nPuede ejecutar de nuevo el Asistente para completar la instalación en otro momento.%n%n¿Salir de la instalación?
ButtonBack=< &Atrás
ButtonNext=&Siguiente >
ButtonInstall=&Instalar
ButtonFinish=Terminar
ButtonCancel=Cancelar

[Types]
; First type listed is the default selection.
Name: "full"; Description: "Instalación completa (recomendada)"
Name: "custom"; Description: "Personalizada"

[Components]
Name: "managed"; Description: "Capa gestionada C# — interfaz WPF (.NET 4.8) y baseline WinForms (.NET 3.5) con las DLL Lumina.*"; Types: full custom; Flags: fixed
Name: "native_x64"; Description: "Núcleo nativo C++ para Windows x64 (LuminaCore.dll)"; Types: full custom; Check: Is64BitInstallMode
Name: "native_x86"; Description: "Núcleo nativo C++ para Windows x86 (LuminaCore.dll)"; Types: full custom; Check: not Is64BitInstallMode
Name: "redist"; Description: "Instalador offline oficial de .NET Framework 4.8 (~111 MB, opcional — solo se ofrece si falta .NET)"; Types: full custom; Flags: unchecked; ExtraDiskSpaceRequired: 120000000; Check: DotNetMissing472

[Files]
; ============================================================================
; DUAL-ARCHITECTURE FILE SELECTION (official "64BitTwoArch.iss" pattern).
; Each entry exists in BOTH arch stagings; the Check: guard selects exactly
; one at install time. All core sources are REQUIRED at compile time on
; purpose: a missing staged binary must fail the ISCC build loudly instead of
; silently producing a broken installer. Only genuinely optional items carry
; the "skipifsourcedoesntexist" flag.
; ============================================================================

; ---- x64 staging (installed only in 64-bit install mode) -------------------
Source: "{#StagingX64}\LuminaPresentation.exe"; DestDir: "{app}"; Components: managed; Check: Is64BitInstallMode; Flags: ignoreversion
Source: "{#StagingX64}\LuminaPresentation.exe.config"; DestDir: "{app}"; Components: managed; Check: Is64BitInstallMode; Flags: ignoreversion
Source: "{#StagingX64}\LuminaPresentation35.exe"; DestDir: "{app}"; Components: managed; Check: Is64BitInstallMode; Flags: ignoreversion
Source: "{#StagingX64}\LuminaPresentation35.exe.config"; DestDir: "{app}"; Components: managed; Check: Is64BitInstallMode; Flags: ignoreversion
Source: "{#StagingX64}\Lumina.Core.dll"; DestDir: "{app}"; Components: managed; Check: Is64BitInstallMode; Flags: ignoreversion
Source: "{#StagingX64}\Lumina.Api.dll"; DestDir: "{app}"; Components: managed; Check: Is64BitInstallMode; Flags: ignoreversion
Source: "{#StagingX64}\Lumina.Bridge.dll"; DestDir: "{app}"; Components: managed; Check: Is64BitInstallMode; Flags: ignoreversion
Source: "{#StagingX64}\LuminaCore.dll"; DestDir: "{app}"; Components: native_x64; Check: Is64BitInstallMode; Flags: ignoreversion

; ---- x86 staging (installed only in 32-bit install mode, i.e. x86 CPUs) ----
Source: "{#StagingX86}\LuminaPresentation.exe"; DestDir: "{app}"; Components: managed; Check: not Is64BitInstallMode; Flags: ignoreversion
Source: "{#StagingX86}\LuminaPresentation.exe.config"; DestDir: "{app}"; Components: managed; Check: not Is64BitInstallMode; Flags: ignoreversion
Source: "{#StagingX86}\LuminaPresentation35.exe"; DestDir: "{app}"; Components: managed; Check: not Is64BitInstallMode; Flags: ignoreversion
Source: "{#StagingX86}\LuminaPresentation35.exe.config"; DestDir: "{app}"; Components: managed; Check: not Is64BitInstallMode; Flags: ignoreversion
Source: "{#StagingX86}\Lumina.Core.dll"; DestDir: "{app}"; Components: managed; Check: not Is64BitInstallMode; Flags: ignoreversion
Source: "{#StagingX86}\Lumina.Api.dll"; DestDir: "{app}"; Components: managed; Check: not Is64BitInstallMode; Flags: ignoreversion
Source: "{#StagingX86}\Lumina.Bridge.dll"; DestDir: "{app}"; Components: managed; Check: not Is64BitInstallMode; Flags: ignoreversion
Source: "{#StagingX86}\LuminaCore.dll"; DestDir: "{app}"; Components: native_x86; Check: not Is64BitInstallMode; Flags: ignoreversion

; ---- Optional managed bits (kept optional for staging flexibility) ---------
; Lumina.WPF.dll: the v5.4.0 build merges the WPF shell INTO
; LuminaPresentation.exe; a future split build may stage it separately.
Source: "{#StagingX64}\Lumina.WPF.dll"; DestDir: "{app}"; Components: managed; Check: Is64BitInstallMode; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#StagingX86}\Lumina.WPF.dll"; DestDir: "{app}"; Components: managed; Check: not Is64BitInstallMode; Flags: ignoreversion skipifsourcedoesntexist
; Lumina.PocFacade.dll: PoC interop facade — ships only when staged by CI.
Source: "{#StagingX64}\Lumina.PocFacade.dll"; DestDir: "{app}"; Components: managed; Check: Is64BitInstallMode; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#StagingX86}\Lumina.PocFacade.dll"; DestDir: "{app}"; Components: managed; Check: not Is64BitInstallMode; Flags: ignoreversion skipifsourcedoesntexist
; Native launcher (optional): picks the right exe variant at runtime.
Source: "{#StagingX64}\LuminaLauncher.exe"; DestDir: "{app}"; Components: managed; Check: Is64BitInstallMode; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#StagingX86}\LuminaLauncher.exe"; DestDir: "{app}"; Components: managed; Check: not Is64BitInstallMode; Flags: ignoreversion skipifsourcedoesntexist
; Template configs (reference copies; the operative ones are *.exe.config).
Source: "{#StagingX64}\app.net48.config"; DestDir: "{app}"; Components: managed; Check: Is64BitInstallMode; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#StagingX64}\app.net35.config"; DestDir: "{app}"; Components: managed; Check: Is64BitInstallMode; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#StagingX86}\app.net48.config"; DestDir: "{app}"; Components: managed; Check: not Is64BitInstallMode; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#StagingX86}\app.net35.config"; DestDir: "{app}"; Components: managed; Check: not Is64BitInstallMode; Flags: ignoreversion skipifsourcedoesntexist

; ---- Docs, factory data, license -------------------------------------------
Source: "{#StagingX64}\README.txt"; DestDir: "{app}"; Components: managed; Check: Is64BitInstallMode; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#StagingX64}\LICENSE.md"; DestDir: "{app}"; Components: managed; Check: Is64BitInstallMode; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#StagingX64}\data\*"; DestDir: "{app}\data"; Components: managed; Check: Is64BitInstallMode; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#StagingX86}\README.txt"; DestDir: "{app}"; Components: managed; Check: not Is64BitInstallMode; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#StagingX86}\LICENSE.md"; DestDir: "{app}"; Components: managed; Check: not Is64BitInstallMode; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#StagingX86}\data\*"; DestDir: "{app}\data"; Components: managed; Check: not Is64BitInstallMode; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

; ---- OPTIONAL .NET Framework 4.8 offline installer (no download, ever) -----
; Compiled in only when the file was staged (RedistBundledFlag above). It is
; installed into {app}\redist\ and executed at post-install ONLY if the user
; selected the component AND .NET is still missing. ~111 MB, excluded from
; the product size budget (spec section 10.2, "componente separado opcional").
Source: "{#RedistSourcePath}"; DestDir: "{app}\redist"; Components: redist; Flags: ignoreversion skipifsourcedoesntexist

[Dirs]
; Factory data folder exists even when no sample resources were staged.
Name: "{app}\data"; Components: managed

[Icons]
; Start-menu entries. Dual variants (common vs user) selected by the final
; install mode via the built-in IsAdminInstallMode — no {autopf} magic here
; so the script stays explicit and audit-friendly.
Name: "{commonprograms}\{groupname}\LuminaPresentation (WPF)"; Filename: "{app}\LuminaPresentation.exe"; Comment: "Interfaz principal (WPF, .NET Framework 4.8)"; Check: IsAdminInstallMode
Name: "{commonprograms}\{groupname}\LuminaPresentation35 (baseline)"; Filename: "{app}\LuminaPresentation35.exe"; Comment: "Baseline WinForms para Windows 7 SP1 con .NET 3.5"; Check: IsAdminInstallMode
Name: "{commonprograms}\{groupname}\Desinstalar LuminaPresentation Suite"; Filename: "{uninstallexe}"; Check: IsAdminInstallMode
Name: "{userprograms}\{groupname}\LuminaPresentation (WPF)"; Filename: "{app}\LuminaPresentation.exe"; Comment: "Interfaz principal (WPF, .NET Framework 4.8)"; Check: not IsAdminInstallMode
Name: "{userprograms}\{groupname}\LuminaPresentation35 (baseline)"; Filename: "{app}\LuminaPresentation35.exe"; Comment: "Baseline WinForms para Windows 7 SP1 con .NET 3.5"; Check: not IsAdminInstallMode
Name: "{userprograms}\{groupname}\Desinstalar LuminaPresentation Suite"; Filename: "{uninstallexe}"; Check: not IsAdminInstallMode

[Run]
; The WPF shell requires .NET 4.8; when it is missing the app still boots and
; degrades (spec section 4.2), so launching it is always safe to offer.
Filename: "{app}\LuminaPresentation.exe"; Description: "Ejecutar LuminaPresentation Suite ahora"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Nothing extra: user data (%APPDATA%\AppHibrida) is deliberately PRESERVED
; by default; the optional removal below is user-confirmed at uninstall time.
; Installed files (including the optional {app}\redist redist) are removed by
; Inno Setup's standard uninstaller.

[Code]
; ============================================================================
;  .NET Framework verification — READ-ONLY registry inspection.
;  Checks the standard NDP v4 key: HKLM\SOFTWARE\Microsoft\NET Framework
;  Setup\NDP\v4\Full  ("Release" DWORD >= 461808 == .NET Framework 4.7.2).
;  NOTE: 528040 would be the exact .NET 4.8 release value; 461808 is the
;  normative minimum fixed by the spec/task. If the check fails, the app
;  still runs (degradation profiles B/C, spec section 4.2) — nothing blocks.
;  The application NEVER touches the registry itself (F6.05.5); this is the
;  only registry access in the product lifecycle and it never writes.
; ============================================================================
const
  NdpV4FullKey = 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full';
  NdpReleaseValue = 'Release';
  MinRelease = 461808;  // .NET Framework 4.7.2
  OfficialRedistUrl = 'https://go.microsoft.com/fwlink/?linkid=2088631'; // official .NET Framework 4.8 offline installer
  RedistInApp = 'redist\ndp48-x86-x64-allos-enu.exe';

var
  NdpReleaseCached: Cardinal;
  NdpChecked: Boolean;

function NdpRelease(): Cardinal;
// Reads the NDP v4 "Release" DWORD. Tries the default HKLM view first, then
// the alternate view (HKLM32/HKLM64) in case the framework registered itself
// in the other registry view under WoW64. READ-ONLY in all cases.
var
  value: Cardinal;
begin
  if NdpChecked then
  begin
    Result := NdpReleaseCached;
    Exit;
  end;
  NdpReleaseCached := 0;
  value := 0;
  if RegQueryDWordValue(HKLM, NdpV4FullKey, NdpReleaseValue, NdpReleaseCached) then
    // found in the default view
  else if Is64BitInstallMode then
  begin
    if not RegQueryDWordValue(HKLM32, NdpV4FullKey, NdpReleaseValue, value) then
      RegQueryDWordValue(HKLM64, NdpV4FullKey, NdpReleaseValue, value);
    NdpReleaseCached := value;
  end
  else
  begin
    if not RegQueryDWordValue(HKLM64, NdpV4FullKey, NdpReleaseValue, value) then
      RegQueryDWordValue(HKLM32, NdpV4FullKey, NdpReleaseValue, value);
    NdpReleaseCached := value;
  end;
  NdpChecked := True;
  Result := NdpReleaseCached;
  Log('.NET Framework NDP v4 Release detected: ' + IntToStr(NdpReleaseCached) +
      ' (minimum ' + IntToStr(MinRelease) + ')');
end;

function DotNet472OrLater(): Boolean;
begin
  Result := NdpRelease() >= MinRelease;
end;

function DotNetMissing472(): Boolean;
// Used as the Check: of the "redist" component: the component is hidden
// entirely when .NET 4.7.2+ is already present.
begin
  Result := not DotNet472OrLater();
end;

function RedistBundledAtCompileTime(): Boolean;
// Value injected by ISPP at compile time ("1" when the offline redist was
// staged next to this script, "0" otherwise).
begin
  Result := ('{#RedistBundledFlag}' = '1');
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  Log(Format('LuminaPresentation Suite setup — 64-bit install mode: %s', [IntToStr(Ord(Is64BitInstallMode))]));
  Log('.NET offline redist bundled at compile time: ' + IntToStr(Ord(RedistBundledAtCompileTime())));
end;

procedure CurPageChanged(CurPageID: Integer);
// Default-check the optional redist component (checkbox) when .NET is
// missing — the exact behavior required by F6.06.4 (checked by default).
begin
  if CurPageID = wpSelectComponents then
  begin
    if DotNetMissing472() and RedistBundledAtCompileTime() then
      WizardSelectComponents('redist');
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
// Early, non-blocking notice before the files are installed (F6.06.3).
begin
  Result := True;
  if CurPageID = wpSelectComponents then
  begin
    if DotNetMissing472() then
      MsgBox(
        'No se detectó Microsoft .NET Framework 4.7.2 o posterior en este equipo.' #13#10 +
        'La aplicación puede instalarse igualmente: se ejecutará con funcionalidad degradada (perfiles B/C).' #13#10 #13#10 +
        'Componente opcional disponible:' #13#10 +
        '  • "Instalador offline oficial de .NET Framework 4.8" — si está marcado, se ofrecerá' #13#10 +
        '    al final de la instalación. NUNCA se descarga nada automáticamente.' #13#10 +
        '  • Si el instalador offline no viaja con este paquete, se mostrará el enlace oficial' #13#10 +
        '    de Microsoft para obtenerlo manualmente.',
        mbInformation, MB_OK);
  end;
end;

procedure ShowUrlFallback();
// No redist bundled (or user declined): show the official URL clearly and
// CONTINUE — the app degrades to profiles B/C (F6.06.4, spec section 4.2).
begin
  MsgBox('Microsoft .NET Framework 4.7.2 o posterior no fue detectado y este paquete ' +
    'no incluye el instalador offline opcional.' #13#10 #13#10 +
    'Descargue el instalador offline oficial de Microsoft (sin registro, gratuito) en:' #13#10 +
    OfficialRedistUrl + #13#10 #13#10 +
    'La aplicación se instaló correctamente y arrancará con funcionalidad degradada ' +
    '(perfiles B/C) hasta que instale .NET. Puede continuar usando el equipo con normalidad.',
    mbInformation, MB_OK);
end;

procedure RunRedistIfRequested();
// Post-install: honor the optional redist component. Elevation is requested
// ONLY through the user-approved UAC prompt (verb "runas") because the
// Microsoft .NET installer itself requires admin. Error codes handled:
//   0 = OK · 3010/1641 = OK, restart required · anything else -> URL hint.
var
  redistPath: String;
  resultCode: Integer;
  executed: Boolean;
begin
  if DotNet472OrLater() then
    Exit; // nothing to do — .NET present (or installed in the meantime)

  redistPath := ExpandConstant('{app}\') + RedistInApp;

  if IsComponentSelected('redist') and FileExists(redistPath) then
  begin
    if MsgBox(
        'Falta Microsoft .NET Framework 4.7.2 o posterior.' #13#10 +
        '¿Desea ejecutar ahora el instalador offline oficial incluido en este paquete?' #13#10 +
        '(Requiere confirmar el permiso de administrador en el control de cuentas de usuario.)',
        mbConfirmation, MB_YESNO) = IDYES then
    begin
      executed := ShellExec('runas', redistPath, '/passive /norestart /showrmui',
        ExpandConstant('{app}'), SW_SHOWNORMAL, ewWaitUntilTerminated, resultCode);
      if executed and ((resultCode = 0) or (resultCode = 3010) or (resultCode = 1641)) then
      begin
        if resultCode <> 0 then
          MsgBox('La instalación de .NET Framework terminó correctamente.' #13#10 +
            'Se recomienda REINICIAR el equipo antes de usar la aplicación (código ' +
            IntToStr(resultCode) + ').', mbInformation, MB_OK)
        else
          MsgBox('Microsoft .NET Framework se instaló correctamente.', mbInformation, MB_OK);
      end
      else
        MsgBox('No se pudo completar la instalación de .NET Framework (código ' +
          IntToStr(resultCode) + ').' #13#10 #13#10 +
          'Puede descargar el instalador offline oficial en:' #13#10 +
          OfficialRedistUrl + #13#10 #13#10 +
          'La aplicación se instaló correctamente y arrancará con funcionalidad degradada hasta que instale .NET.',
          mbError, MB_OK);
    end
    else
      ShowUrlFallback();
  end
  else
    ShowUrlFallback();
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    RunRedistIfRequested();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
// Optional user-data removal (F6.05: %APPDATA%\AppHibrida holds config, data
// and logs in installed mode). Inno Setup's uninstaller cannot render a real
// checkbox, so a confirmed Yes/No question is the documented equivalent.
// Default behavior = PRESERVE user data (only binaries under {app} removed).
var
  userDataDir: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    userDataDir := ExpandConstant('{userappdata}\AppHibrida');
    if DirExists(userDataDir) then
    begin
      if MsgBox(
          '¿Desea eliminar TAMBIÉN los datos de usuario de LuminaPresentation Suite?' #13#10 #13#10 +
          'Carpeta: ' + userDataDir + #13#10 +
          '(Configuración, proyectos, biblias importadas y logs.)' #13#10 #13#10 +
          'Si responde "No", sus datos se conservan para una futura reinstalación.',
          mbConfirmation, MB_YESNO) = IDYES then
      begin
        DelTree(userDataDir, False, True, True);
        if DirExists(userDataDir) then
          MsgBox('No fue posible eliminar completamente la carpeta de datos (algunos archivos pueden estar en uso):' #13#10 +
            userDataDir, mbError, MB_OK)
        else
          Log('User data folder removed: ' + userDataDir);
      end;
    end;
  end;
end;
