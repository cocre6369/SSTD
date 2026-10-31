# Build and package

## Requirements

- Windows 10 2004/build 19041 or Windows 11, x64.
- .NET 8 SDK to build; **end users do not need .NET** because the release is self-contained.
- WiX Toolset v4.0.5 and `WixToolset.UI.wixext` 4.0.5 to build the MSI installer.
- Windows OCR English language support for OCR; Windows UI Automation is built in.

## Exact Windows commands

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
winget install Microsoft.DotNet.SDK.8
# Open a new PowerShell window after installing the SDK.
dotnet tool install --global wix --version 4.0.5
wix extension add --global WixToolset.UI.wixext/4.0.5
git clone https://github.com/cocre6369/SSTD.git
cd SSTD
git checkout arena/6dc16062-sstd
./build.ps1 -Package all -Version 1.0.4
```

The script restores, builds, runs portable and real Windows tests, publishes the fixture, publishes the self-contained application folder, runs startup smoke tests, builds the native MSI, runs MSI install/repair/uninstall smoke tests, packages the portable ZIP, and writes hashes. Packaging is blocked when tests fail; do not use `-SkipTests` for a release.

Individual commands:

```powershell
dotnet restore SSTD.sln --runtime win-x64 -p:Platform="Any CPU"
dotnet build SSTD.sln -c Release --no-restore -p:Version=1.0.4
dotnet test tests/SSTD.Tests/SSTD.Tests.csproj -c Release --no-build
dotnet publish tools/SSTD.TestApp/SSTD.TestApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o artifacts/test-fixture
dotnet test tests/SSTD.WindowsTests/SSTD.WindowsTests.csproj -c Release --no-build
dotnet publish src/SSTD.UI/SSTD.UI.csproj -c Release -r win-x64 --self-contained true -p:Version=1.0.4 -p:PublishSingleFile=false -p:DebugType=embedded -o artifacts/publish
./tools/Package-Portable.ps1 -PublishDir artifacts/publish -OutputZip artifacts/SSTD-Portable.zip
./tools/Package-Msi.ps1 -PublishDir artifacts/publish -OutputMsi artifacts/SSTD-Setup.msi -Version 1.0.4
./tools/Msi-Smoke.ps1 -Msi artifacts/SSTD-Setup.msi
```

Outputs: `artifacts/publish/` (self-contained app folder), `artifacts/SSTD-Setup.msi`, `artifacts/SSTD-Portable.zip`, `artifacts/SHA256SUMS.txt`, test results and smoke reports. The MSI creates a Start Menu shortcut and a standard per-user Windows Installer uninstall entry. It does not install Ollama, download models, alter system settings or remove unrelated files.

The GitHub Windows workflow additionally runs downloaded-file checks: the MSI wizard is launched through `msiexec` with Mark-of-the-Web, and the portable ZIP is extracted, marked with Mark-of-the-Web, and startup-smoked. These checks are release gates.

## Legacy Inno Setup script

`installer/SSTD.iss` and `tools/Installer-Smoke.ps1` remain in the repository for historical comparison, but they are not the release packaging path. The supported installer is the native MSI produced by `tools/Package-Msi.ps1`.

## Linux development

```bash
./build.sh test
./build.sh cross-publish
```

Linux can compile/test portable components and cross-publish the Windows executable with Windows targeting enabled. It cannot run WPF, UIA, SendInput, Windows OCR, MSI, or installer smoke tests. The GitHub Windows workflow is the native verification path. Cross-publishing is not a substitute for running the Windows checks.
