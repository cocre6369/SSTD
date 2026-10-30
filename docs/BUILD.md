# Build and package

## Requirements

- Windows 10 2004/build 19041 or Windows 11, x64.
- .NET 8 SDK to build; **end users do not need .NET** because the release is self-contained.
- Inno Setup 6 only to build the installer.
- Windows OCR English language support for OCR; Windows UI Automation is built in.

## Exact Windows commands

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
winget install Microsoft.DotNet.SDK.8
winget install JRSoftware.InnoSetup
# Open a new PowerShell window after installing tools.
git clone https://github.com/cocre6369/SSTD.git
cd SSTD
git checkout arena/6dc16062-sstd
./build.ps1 -Package all -Version 1.0.0
```

The script restores, builds, runs portable and real Windows tests, publishes the fixture, publishes the self-contained application folder, runs startup smoke tests, builds the installer, runs install/restart/uninstall smoke tests and writes hashes. Packaging is blocked when tests fail; do not use `-SkipTests` for a release.

Individual commands:

```powershell
dotnet restore SSTD.sln --runtime win-x64 -p:Platform="Any CPU"
dotnet build SSTD.sln -c Release --no-restore -p:Version=1.0.0
dotnet test tests/SSTD.Tests/SSTD.Tests.csproj -c Release --no-build
dotnet publish tools/SSTD.TestApp/SSTD.TestApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o artifacts/test-fixture
dotnet test tests/SSTD.WindowsTests/SSTD.WindowsTests.csproj -c Release --no-build
dotnet publish src/SSTD.UI/SSTD.UI.csproj -c Release -r win-x64 --self-contained true -p:Version=1.0.0 -p:PublishSingleFile=false -p:DebugType=embedded -o artifacts/publish
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" /DAppVersion=1.0.0 installer/SSTD.iss
```

Outputs: `artifacts/publish/` (self-contained app folder), `artifacts/SSTD-Setup.exe`, `artifacts/SHA256SUMS.txt`, test results and smoke reports. The installer creates Start Menu and optional Desktop shortcuts and a standard per-user uninstaller. It does not install Ollama, download models, alter system settings or remove unrelated files.

## Linux development

```bash
./build.sh test
./build.sh cross-publish
```

Linux can compile/test portable components and cross-publish the Windows executable with Windows targeting enabled. It cannot run WPF, UIA, SendInput, Windows OCR or installer smoke tests. The GitHub Windows workflow is the native verification path. Cross-publishing is not a substitute for running the Windows checks.
