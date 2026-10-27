# StichidexDotNet

Cross-platform C# / .NET 8 Avalonia UI embroidery pattern catalog & studio (standalone port of [rtb144/stichidex](https://github.com/rtb144/stichidex)).

## Layout
- `src/Stitchidex.Core` — parsers (PES, DST, EXP, JEF, XXX…), catalog, thumbnails, USB export
- `src/Stitchidex.Desktop` — Avalonia app (`stichidexdotnet` executable)
- `tests/Stitchidex.Tests` — xUnit tests, incl. real files in `samples/`
- `packaging/` — `build_linux_dotnet.sh`, `build_windows_dotnet.ps1`, `cross_build_windows_from_linux.sh`

## Build
```
dotnet build -c Release
dotnet test
./packaging/cross_build_windows_from_linux.sh   # -> artifacts/win-x64 + ~/stichidex/packaging/windows_vm/shared
```

## Windows VM notes
Run `install_and_run_dotnet.bat` from the Shared folder: it `pushd`s the UNC share, copies to `C:\StichidexDotNet`, sets `DOTNET_BUNDLE_EXTRACT_BASE_DIR`, unblocks files and launches. Startup errors go to `stichidexdotnet-crash.log`.
