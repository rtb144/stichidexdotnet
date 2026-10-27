#!/usr/bin/env bash
# Builds a self-contained linux-x64 Stitchidex .NET package (stichidexdotnet) that installs cleanly alongside the Python stichidex package.
set -euo pipefail
VERSION="${VERSION:-0.5.0}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/artifacts/linux-x64"

if [ -x "$HOME/.dotnet/dotnet" ]; then
  export DOTNET_ROOT="$HOME/.dotnet"
  export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
  DOTNET_BIN="$HOME/.dotnet/dotnet"
else
  DOTNET_BIN="$(command -v dotnet)"
fi

echo "[Stitchidex .NET] Using dotnet binary: $DOTNET_BIN ($("$DOTNET_BIN" --version))"
"$DOTNET_BIN" publish "$ROOT/src/Stitchidex.Desktop/Stitchidex.Desktop.csproj" -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$OUT"

PKG_NAME="stichidexdotnet"
PKG="$ROOT/artifacts/deb/${PKG_NAME}_${VERSION}_amd64"
rm -rf "$PKG"
mkdir -p "$PKG/DEBIAN" "$PKG/opt/$PKG_NAME" "$PKG/usr/bin" "$PKG/usr/share/applications"
bash "$ROOT/packaging/fetch_model.sh" "$OUT/models/bge-base-en-v1.5"
cp -f "$ROOT/docs/Stitchidex-Windows-User-Guide.html" "$OUT/User-Guide.html"
cp -r "$OUT"/. "$PKG/opt/$PKG_NAME/"
chmod +x "$PKG/opt/$PKG_NAME/stichidexdotnet"
ln -sf "/opt/$PKG_NAME/stichidexdotnet" "$PKG/usr/bin/stichidexdotnet"
ln -sf "/opt/$PKG_NAME/stichidexdotnet" "$PKG/usr/bin/stitchidexdotnet"

cat > "$PKG/DEBIAN/control" <<CTL
Package: ${PKG_NAME}
Version: ${VERSION}
Section: graphics
Priority: optional
Architecture: amd64
Depends: libfontconfig1, libice6, libsm6
Maintainer: Stitchidex <noreply@stitchidex.local>
Description: Stitchidex embroidery pattern catalog (.NET 8 / Avalonia side-by-side edition)
CTL

cat > "$PKG/usr/share/applications/${PKG_NAME}.desktop" <<DSK
[Desktop Entry]
Name=Stitchidex (.NET)
Exec=/opt/${PKG_NAME}/stichidexdotnet
Type=Application
Categories=Graphics;
DSK

dpkg-deb --build --root-owner-group "$PKG" "$ROOT/artifacts/${PKG_NAME}_${VERSION}_amd64.deb"
echo "[Stitchidex .NET] Built $ROOT/artifacts/${PKG_NAME}_${VERSION}_amd64.deb"
