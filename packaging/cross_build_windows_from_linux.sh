#!/usr/bin/env bash
# Cross-compiles self-contained Windows x64 stichidexdotnet.exe directly on Linux (z8g4)
# and copies it into ~/stichidex/packaging/windows_vm/shared with Windows CRLF batch launcher.
set -euo pipefail
VERSION="${VERSION:-0.5.0}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/artifacts/win-x64"
VM_SHARED="${VM_SHARED:-$HOME/stichidex/packaging/windows_vm/shared}"

if [ -x "$HOME/.dotnet/dotnet" ]; then
  export DOTNET_ROOT="$HOME/.dotnet"
  export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
  DOTNET_BIN="$HOME/.dotnet/dotnet"
else
  DOTNET_BIN="$(command -v dotnet)"
fi

echo "[Stitchidex .NET] Cross-compiling self-contained win-x64 stichidexdotnet.exe using $DOTNET_BIN..."
rm -rf "$OUT"
"$DOTNET_BIN" publish "$ROOT/src/Stitchidex.Desktop/Stitchidex.Desktop.csproj" \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:IncludeAllContentForSelfExtract=true \
  -o "$OUT"

ISS_FILE="$OUT/stichidexdotnet.iss"
cat > "$ISS_FILE" <<ISS
[Setup]
AppId={{7E1C3D5A-5B1F-4C8E-9A1D-57A1C0DE0500}
AppName=Stitchidex (.NET)
AppVersion=${VERSION}
AppPublisher=Stitchidex
DefaultDirName={autopf}\StichidexDotNet
DefaultGroupName=Stitchidex (.NET)
OutputDir=C:\StichidexDotNet
OutputBaseFilename=StichidexDotNet-Setup-${VERSION}-win64
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible

[Files]
Source: "C:\StichidexDotNet\stichidexdotnet.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "C:\StichidexDotNet\*.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\Stitchidex (.NET)"; Filename: "{app}\stichidexdotnet.exe"
Name: "{autodesktop}\Stitchidex (.NET)"; Filename: "{app}\stichidexdotnet.exe"

[Run]
Filename: "{app}\stichidexdotnet.exe"; Description: "Launch Stitchidex (.NET)"; Flags: nowait postinstall skipifsilent
ISS

python3 - <<'PY' "$OUT/install_and_run_dotnet.bat"
import sys
bat = """@echo off
setlocal
rem pushd maps a temporary drive letter so UNC shares (\\\\host.lan\\Data) work as the current directory
pushd "%~dp0"
set "TARGET=C:\\StichidexDotNet"
set "DOTNET_BUNDLE_EXTRACT_BASE_DIR=%TARGET%\\extract"
echo [Stitchidex .NET] Copying build from Shared folder to %TARGET%...
if not exist "%TARGET%" mkdir "%TARGET%"
if not exist "%DOTNET_BUNDLE_EXTRACT_BASE_DIR%" mkdir "%DOTNET_BUNDLE_EXTRACT_BASE_DIR%"
xcopy /Y /E /I /Q "%CD%\\*" "%TARGET%\\" >nul
popd
rem drop Mark-of-the-Web so SmartScreen does not block the copied exe
powershell -NoProfile -Command "Get-ChildItem -Recurse '%TARGET%' | Unblock-File" >nul 2>&1
cd /d "%TARGET%"
echo [Stitchidex .NET] Launching %TARGET%\\stichidexdotnet.exe...
"%TARGET%\\stichidexdotnet.exe"
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" (
    echo [Stitchidex .NET] stichidexdotnet.exe exited with code %RC%.
    if exist "%TARGET%\\stichidexdotnet-crash.log" type "%TARGET%\\stichidexdotnet-crash.log"
    pause
)
exit /b %RC%
"""
with open(sys.argv[1], "wb") as f:
    f.write(bat.replace("\r\n", "\n").replace("\n", "\r\n").encode("utf-8"))
PY

mkdir -p "$VM_SHARED"
cp -f "$OUT/stichidexdotnet.exe" "$VM_SHARED/stichidexdotnet.exe"
cp -f "$OUT/stichidexdotnet.iss" "$VM_SHARED/stichidexdotnet.iss"
cp -f "$OUT/install_and_run_dotnet.bat" "$VM_SHARED/install_and_run_dotnet.bat"
cp -f "$OUT"/*.dll "$VM_SHARED/" 2>/dev/null || true
cp -rf "$OUT/samples" "$VM_SHARED/" 2>/dev/null || true

echo "[Stitchidex .NET] Built and staged to $VM_SHARED:"
ls -lh "$VM_SHARED/stichidexdotnet.exe" "$VM_SHARED/install_and_run_dotnet.bat"
