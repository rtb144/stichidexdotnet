; NSIS installer (buildable on Linux with makensis). Usage: makensis -DVERSION=0.5.0 -DSRC=artifacts/win-x64 -DOUTFILE=Setup.exe
Unicode true
SetCompressor /SOLID lzma
Name "Stitchidex (.NET) ${VERSION}"
OutFile "${OUTFILE}"
InstallDir "$PROGRAMFILES64\StichidexDotNet"
RequestExecutionLevel admin
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
  SetOutPath "$INSTDIR\models"
  File /r "${SRC}\models\*"
  SetOutPath "$INSTDIR\samples"
  File /nonfatal /r "${SRC}\samples\*"
  SetOutPath "$INSTDIR"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\Stitchidex (.NET)"
  CreateShortcut "$SMPROGRAMS\Stitchidex (.NET)\Stitchidex (.NET).lnk" "$INSTDIR\stichidexdotnet.exe"
  CreateShortcut "$SMPROGRAMS\Stitchidex (.NET)\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
  CreateShortcut "$DESKTOP\Stitchidex (.NET).lnk" "$INSTDIR\stichidexdotnet.exe"
  CreateShortcut "$SMPROGRAMS\Stitchidex (.NET)\Stitchidex User Guide.lnk" "$INSTDIR\User-Guide.html"
  CreateShortcut "$DESKTOP\Stitchidex User Guide.lnk" "$INSTDIR\User-Guide.html"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\StichidexDotNet" "DisplayName" "Stitchidex (.NET)"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\StichidexDotNet" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\StichidexDotNet" "UninstallString" '"$INSTDIR\Uninstall.exe"'
SectionEnd
Section "Uninstall"
  RMDir /r "$INSTDIR"
  Delete "$DESKTOP\Stitchidex (.NET).lnk"
  Delete "$DESKTOP\Stitchidex User Guide.lnk"
  RMDir /r "$SMPROGRAMS\Stitchidex (.NET)"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\StichidexDotNet"
SectionEnd
