; NSIS installer (buildable on Linux with makensis). Usage: makensis -DVERSION=0.5.0 -DSRC=artifacts/win-x64 -DOUTFILE=Setup.exe
Unicode true
SetCompressor /SOLID lzma
Name "Stitchidex (.NET) ${VERSION}"
Icon "${SRC}\stitchidex.ico"
UninstallIcon "${SRC}\stitchidex.ico"
OutFile "${OUTFILE}"
InstallDir "$PROGRAMFILES64\StichidexDotNet"
RequestExecutionLevel admin
LicenseData "${SRC}\LICENSE.txt"
Page license
Page directory
Page instfiles
UninstPage uninstConfirm
UninstPage instfiles
Section "Install"
  SetOutPath "$INSTDIR"
  File "${SRC}\stichidexdotnet.exe"
  File "${SRC}\stitchidexdotnet.exe"
  File /nonfatal "${SRC}\*.dll"
  File "${SRC}\User-Guide.html"
  File "${SRC}\stitchidex.ico"
  File "${SRC}\LICENSE.txt"
  File "${SRC}\THIRD-PARTY-NOTICES.md"
  SetOutPath "$INSTDIR\models"
  File /r "${SRC}\models\*"
  SetOutPath "$INSTDIR\samples"
  File /nonfatal /r "${SRC}\samples\*"
  SetOutPath "$INSTDIR"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\Stitchidex (.NET)"
  CreateShortcut "$SMPROGRAMS\Stitchidex (.NET)\Stitchidex (.NET).lnk" "$INSTDIR\stichidexdotnet.exe" "" "$INSTDIR\stitchidex.ico"
  CreateShortcut "$SMPROGRAMS\Stitchidex (.NET)\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
  CreateShortcut "$DESKTOP\Stitchidex (.NET).lnk" "$INSTDIR\stichidexdotnet.exe" "" "$INSTDIR\stitchidex.ico"
  CreateShortcut "$SMPROGRAMS\Stitchidex (.NET)\Stitchidex User Guide.lnk" "$INSTDIR\User-Guide.html"
  CreateShortcut "$DESKTOP\Stitchidex User Guide.lnk" "$INSTDIR\User-Guide.html"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\StichidexDotNet" "DisplayName" "Stitchidex (.NET)"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\StichidexDotNet" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\StichidexDotNet" "DisplayIcon" "$INSTDIR\stichidexdotnet.exe,0"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\StichidexDotNet" "UninstallString" '"$INSTDIR\Uninstall.exe"'
SectionEnd
Section "Uninstall"
  RMDir /r "$INSTDIR"
  Delete "$DESKTOP\Stitchidex (.NET).lnk"
  Delete "$DESKTOP\Stitchidex User Guide.lnk"
  RMDir /r "$SMPROGRAMS\Stitchidex (.NET)"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\StichidexDotNet"
SectionEnd
