# Builds a self-contained win-x64 StichidexDotNet and an Inno Setup installer (side-by-side with Python Stitchidex).
param([string]$Version = "0.5.0", [string]$Iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts\win-x64"
dotnet publish (Join-Path $root "src\Stitchidex.Desktop\Stitchidex.Desktop.csproj") -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $out
Copy-Item (Join-Path $out "Stitchidex.exe") (Join-Path $out "stichidexdotnet.exe") -Force
Copy-Item (Join-Path $out "Stitchidex.exe") (Join-Path $out "stitchidexdotnet.exe") -Force
$iss = Join-Path $root "artifacts\stichidexdotnet.iss"
@"
[Setup]
AppId={{7E1C3D5A-5B1F-4C8E-9A1D-57A1C0DE0500}
AppName=Stitchidex (.NET)
AppVersion=$Version
AppPublisher=Stitchidex
DefaultDirName={autopf}\StichidexDotNet
DefaultGroupName=Stitchidex (.NET)
OutputDir=$(Join-Path $root "artifacts")
OutputBaseFilename=StichidexDotNet-Setup-$Version-win64
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
[Files]
Source: "$out\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
[Icons]
Name: "{group}\Stitchidex (.NET)"; Filename: "{app}\stichidexdotnet.exe"
Name: "{autodesktop}\Stitchidex (.NET)"; Filename: "{app}\stichidexdotnet.exe"; Tasks: desktopicon
[Tasks]
Name: "desktopicon"; Description: "Create a desktop icon"; Flags: unchecked
[Run]
Filename: "{app}\stichidexdotnet.exe"; Description: "Launch Stitchidex (.NET)"; Flags: nowait postinstall skipifsilent
"@ | Set-Content -Encoding UTF8 $iss
if (Test-Path $Iscc) { & $Iscc $iss } else { Write-Warning "Inno Setup not found at $Iscc; script written to $iss" }
