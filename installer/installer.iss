; Speck Sequence Helpers — per-user Inno Setup installer.
; Installs the plugin into %localappdata%\NINA\Plugins\3.0.0\SpeckSequenceHelpers.
; Built by CI with: ISCC.exe /DAppVersion=<version> installer\installer.iss
; (expects the staged plugin folder at ..\artifacts\SpeckSequenceHelpers, as produced by scripts/build.sh)

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{8D81FA21-A304-4FCA-B105-11760F2AD855}
AppName=Speck Sequence Helpers
AppVersion={#AppVersion}
AppPublisher=Speck Astro
AppPublisherURL=https://github.com/speckastro/nina-helpers
DefaultDirName={localappdata}\NINA\Plugins\3.0.0\SpeckSequenceHelpers
DisableDirPage=yes
DisableProgramGroupPage=yes
; Inno 6 hides the welcome page by default; keep it so the "close N.I.N.A. first" warning below is seen.
DisableWelcomePage=no
; Per-user install into %localappdata% — no elevation, matching how NINA itself installs plugins.
PrivilegesRequired=lowest
OutputDir=..\artifacts
OutputBaseFilename=SpeckSequenceHelpers-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName=Speck Sequence Helpers

[Files]
Source: "..\artifacts\SpeckSequenceHelpers\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Messages]
SetupWindowTitle=Speck Sequence Helpers Setup
WelcomeLabel2=This will install the Speck Sequence Helpers plugin ([name/ver]) into your local N.I.N.A. plugin folder.%n%nClose N.I.N.A. before continuing — plugin files cannot be replaced while N.I.N.A. is running.%n%nAfter installation, start N.I.N.A. and find the new instructions under the Speck Sequence Helpers category in the Advanced Sequencer.

[UninstallDelete]
; The plugin writes nothing into its own folder at runtime, but remove any stray files on uninstall.
Type: filesandordirs; Name: "{app}"
