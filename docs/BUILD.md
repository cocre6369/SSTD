# Build and packaging

## Native Windows — recommended

Prerequisites: Windows 10 2004+/Windows 11 x64, .NET 8 SDK, and Inno Setup 6 for an installer. **Visual Studio is not required.** End users need neither development tool.

Install build tools yourself (these are developer setup commands, not actions SSTD can automate):

```powershell
winget install Microsoft.DotNet.SDK.8
winget install JRSoftware.InnoSetup
```

From the repository root in PowerShell:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
./build.ps1 -Package all -Version 1.0.0
```

Outputs:

```text
artifacts/publish/SSTD.exe      self-contained, single-file Windows x64 application
artifacts/SSTD-Setup.exe       per-user Windows installer
artifacts/SHA256SUMS.txt       SHA-256 checksums
artifacts/test-results/        actual test result files
artifacts/smoke-result.txt     executable startup/self-check result
artifacts/installer-smoke.txt  install/start/uninstall smoke result
```

The script restores, builds, runs independent tests and real Windows UIA tests, publishes, smoke-starts the executable, builds the installer, and tests installation/uninstallation. It fails on failed tests; it does not manufacture passing results.

### Exact underlying commands

```powershell
dotnet restore SSTD.sln --runtime win-x64 -p:Platform="Any CPU"
dotnet build SSTD.sln -c Release --no-restore
dotnet test tests/SSTD.Tests/SSTD.Tests.csproj -c Release --no-build
./tools/Windows-Integration.ps1
dotnet publish src/SSTD.UI/SSTD.UI.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=embedded -o artifacts/publish
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" /DAppVersion=1.0.0 installer\SSTD.iss
```

WPF trimming is intentionally disabled: trimming WPF/XAML is unsupported and can break runtime resource loading. Native libraries are included for self-extraction. The .NET runtime is bundled; Ollama and Windows OCR language packs are not.

### Individual packages

```powershell
./build.ps1 -Package portable -SkipTests   # SSTD.exe only; not recommended for release
./build.ps1 -Package installer             # publishes first, then builds setup
./build.ps1 -Package test                  # build and tests only
```

## Linux sandbox / cross-build

The portable security, AI protocol, planner and SQLite tests can run on Linux:

```bash
./build.sh test
```

Cross-publish a Windows executable (cannot run WPF here):

```bash
./build.sh cross-publish
```

The SDK enables Windows targeting so reference assemblies can be restored on Linux. Cross-publishing is **not** a Windows UI test. Use a native Windows machine or the included `Windows build and verification` GitHub Actions workflow for runtime verification.

## GitHub Actions

`.github/workflows/windows.yml` runs on `windows-latest`: restore, compile, portable tests, real Notepad/UIA fixture tests, single-file publish, executable smoke test, Inno Setup build, silent install/start/uninstall test, checksums and artifact upload.

Download `SSTD-windows-x64` from a successful run. CI artifacts expire after 14 days; create a GitHub Release for durable distribution. The repository ignores build outputs and secrets. Never commit code-signing certificates.

## Signing

The included build is unsigned unless you supply your own signing process. Windows SmartScreen may warn about an unknown publisher. Verify the source and SHA-256 checksum; do not disable Windows security to run it. For public distribution, sign both executables with your organization's Authenticode certificate:

```powershell
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a artifacts/publish/SSTD.exe
# Rebuild the installer after signing the inner executable, then:
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a artifacts/SSTD-Setup.exe
```

`signtool` comes from the Windows SDK and is optional for local development. No certificate is embedded in this repository.

## Installer behavior

Inno Setup installs to `%LOCALAPPDATA%\Programs\SSTD`, creates a Start Menu shortcut and an optional desktop shortcut, and registers a normal per-user uninstaller. It does not request elevation, install Ollama, pull models, change security settings, or recursively delete user data. Uninstall removes only the application's installed files and shortcuts. Clear SSTD history in Settings before uninstall if desired.
