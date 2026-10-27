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

# Native DLLs side-by-side too (in addition to the single-file bundle) so loading never fails on Win10/11.
NUGET="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
for d in "$NUGET"/microsoft.ml.onnxruntime/*/runtimes/win-x64/native "$NUGET"/skiasharp.nativeassets.win32/*/runtimes/win-x64/native \
         "$NUGET"/harfbuzzsharp.nativeassets.win32/*/runtimes/win-x64/native "$NUGET"/avalonia.angle.windows.natives/*/runtimes/win-x64/native \
         "$NUGET"/sqlitepclraw.lib.e_sqlite3/*/runtimes/win-x64/native; do
  [ -d "$d" ] && cp -f "$d"/*.dll "$OUT/" 2>/dev/null || true
done
cp -f "$OUT/stichidexdotnet.exe" "$OUT/stitchidexdotnet.exe"
cp -f "$OUT/stichidexdotnet.exe" "$OUT/Stitchidex.exe"

# Bundle bge-base-en-v1.5 for 100% offline neural search.
bash "$ROOT/packaging/fetch_model.sh" "$OUT/models/bge-base-en-v1.5"
cp -f "$ROOT/docs/Stitchidex-Windows-User-Guide.html" "$OUT/User-Guide.html"
cp -f "$ROOT/src/Stitchidex.Desktop/Assets/stitchidex.ico" "$OUT/stitchidex.ico"
cp -f "$ROOT/LICENSE" "$OUT/LICENSE.txt"; cp -f "$ROOT/THIRD-PARTY-NOTICES.md" "$OUT/"

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
LicenseFile=C:\StichidexDotNet\LICENSE.txt
SetupIconFile=C:\StichidexDotNet\stitchidex.ico
UninstallDisplayIcon={app}\stichidexdotnet.exe

[Files]
Source: "C:\StichidexDotNet\stichidexdotnet.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "C:\StichidexDotNet\stitchidexdotnet.exe"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "C:\StichidexDotNet\*.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "C:\StichidexDotNet\User-Guide.html"; DestDir: "{app}"; Flags: ignoreversion
Source: "C:\StichidexDotNet\stitchidex.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "C:\StichidexDotNet\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "C:\StichidexDotNet\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "C:\StichidexDotNet\models\*"; DestDir: "{app}\models"; Flags: recursesubdirs ignoreversion
Source: "C:\StichidexDotNet\samples\*"; DestDir: "{app}\samples"; Flags: recursesubdirs ignoreversion skipifsourcedoesntexist

[Icons]
Name: "{group}\Stitchidex (.NET)"; Filename: "{app}\stichidexdotnet.exe"
Name: "{autodesktop}\Stitchidex (.NET)"; Filename: "{app}\stichidexdotnet.exe"
Name: "{group}\Stitchidex User Guide"; Filename: "{app}\User-Guide.html"
Name: "{autodesktop}\Stitchidex User Guide"; Filename: "{app}\User-Guide.html"

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
rem includes models\\ (bge-base-en-v1.5) and samples\\
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

python3 - <<'PY' "$OUT/build_installer_windows.bat" "$VERSION"
import sys
v = sys.argv[2]
bat = r"""@echo off
setlocal
pushd "%~dp0"
set "SHARE=%CD%"
set "TARGET=C:\StichidexDotNet"
if not exist "%TARGET%" mkdir "%TARGET%"
echo [Stitchidex .NET] Copying build (exe, dlls, models, samples) to %TARGET%...
xcopy /Y /E /I /Q "%SHARE%\*" "%TARGET%\" >nul
set "ISCC="
for %%P in ("%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" "%ProgramFiles%\Inno Setup 6\ISCC.exe" "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe") do if exist %%P set "ISCC=%%~P"
if not defined ISCC (
  echo [Stitchidex .NET] Inno Setup 6 not found - installing via winget...
  winget install -e --id JRSoftware.InnoSetup --accept-package-agreements --accept-source-agreements --silent
  for %%P in ("%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" "%ProgramFiles%\Inno Setup 6\ISCC.exe" "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe") do if exist %%P set "ISCC=%%~P"
)
if not defined ISCC ( echo [Stitchidex .NET] ISCC.exe still not found. & popd & pause & exit /b 1 )
"%ISCC%" "%TARGET%\stichidexdotnet.iss" || ( popd & pause & exit /b 1 )
copy /Y "%TARGET%\StichidexDotNet-Setup-VER-win64.exe" "%SHARE%\StichidexDotNet-Setup-VER-win64.exe"
echo [Stitchidex .NET] Installer: %SHARE%\StichidexDotNet-Setup-VER-win64.exe
popd
exit /b 0
""".replace("VER", v)
open(sys.argv[1], "wb").write(bat.replace("\r\n", "\n").replace("\n", "\r\n").encode())
PY

# Linux-built NSIS installer (same contents as Inno Setup) when makensis is available.
MAKENSIS="${MAKENSIS:-$(command -v makensis || echo "$HOME/opt/nsis/root/usr/bin/makensis")}"
[ -z "${NSISDIR:-}" ] && [ -d "$HOME/opt/nsis/root/usr/share/nsis" ] && export NSISDIR="$HOME/opt/nsis/root/usr/share/nsis"
SETUP="$ROOT/artifacts/StichidexDotNet-Setup-${VERSION}-win64.exe"
if [ -x "$MAKENSIS" ]; then
  "$MAKENSIS" -V2 -DVERSION="$VERSION" -DSRC="$OUT" -DOUTFILE="$SETUP" "$ROOT/packaging/stichidexdotnet.nsi"
fi

# Portable zip
ZIP="$ROOT/artifacts/StichidexDotNet-${VERSION}-win64-portable.zip"
rm -f "$ZIP"
(cd "$OUT" && python3 -c "import zipfile,os,sys
z=zipfile.ZipFile(sys.argv[1],'w',zipfile.ZIP_DEFLATED)
for r,_,fs in os.walk('.'):
  for f in fs:
    if not f.endswith(('.pdb','.iss','.bat')) and f not in ('Stitchidex.exe','stitchidexdotnet.exe'): z.write(os.path.join(r,f),os.path.join('StichidexDotNet',os.path.relpath(os.path.join(r,f))))" "$ZIP")

mkdir -p "$VM_SHARED"
cp -f "$OUT/stitchidexdotnet.exe" "$OUT/build_installer_windows.bat" "$VM_SHARED/"
cp -rf "$OUT/models" "$VM_SHARED/"
cp -f "$ZIP" "$VM_SHARED/"
[ -f "$SETUP" ] && cp -f "$SETUP" "$VM_SHARED/"
cp -f "$OUT/User-Guide.html" "$OUT/stitchidex.ico" "$VM_SHARED/"
cp -f "$OUT/stichidexdotnet.exe" "$VM_SHARED/stichidexdotnet.exe"
cp -f "$OUT/stichidexdotnet.iss" "$VM_SHARED/stichidexdotnet.iss"
cp -f "$OUT/install_and_run_dotnet.bat" "$VM_SHARED/install_and_run_dotnet.bat"
cp -f "$OUT"/*.dll "$VM_SHARED/" 2>/dev/null || true
cp -rf "$OUT/samples" "$VM_SHARED/" 2>/dev/null || true

echo "[Stitchidex .NET] Built and staged to $VM_SHARED:"
ls -lh "$VM_SHARED"/*.exe "$VM_SHARED"/*.zip "$VM_SHARED"/*.bat
