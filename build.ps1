<#
.SYNOPSIS
Builds SSTD on Windows x64. No Visual Studio required.
.EXAMPLE
./build.ps1 -Package all -Version 1.0.0
#>
[CmdletBinding()]
param(
    [ValidateSet('test','portable','installer','all')][string]$Package = 'all',
    [string]$Version = '1.0.0',
    [switch]$SkipTests,
    [switch]$NoRestore
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = $PSScriptRoot
Push-Location $root
try {
    if (-not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core') { throw 'Use build.sh for Linux portable tests. WPF and the installer must be built/tested on Windows.' }
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be major.minor.patch.' }
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET 8 SDK first: winget install Microsoft.DotNet.SDK.8' }
    $sdks = & dotnet --list-sdks
    if (-not ($sdks | Where-Object { $_ -match '^8\.0\.' })) { throw '.NET 8 SDK is required. Install with: winget install Microsoft.DotNet.SDK.8' }
    $artifacts = Join-Path $root 'artifacts'
    New-Item -ItemType Directory -Force $artifacts | Out-Null
    if (-not $NoRestore) { & dotnet restore SSTD.sln --runtime win-x64 -p:Platform="Any CPU"; if ($LASTEXITCODE) { throw 'Restore failed.' } }
    & dotnet build SSTD.sln -c Release --no-restore -p:Version=$Version
    if ($LASTEXITCODE) { throw 'Build failed.' }
    if (-not $SkipTests) {
        & dotnet test tests/SSTD.Tests/SSTD.Tests.csproj -c Release --no-build --logger 'trx;LogFileName=tests.trx' --results-directory "$artifacts/test-results"
        if ($LASTEXITCODE) { throw 'Tests failed.' }
        & dotnet publish tools/SSTD.TestApp/SSTD.TestApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o "$artifacts/test-fixture"
        if ($LASTEXITCODE) { throw 'Windows fixture publish failed.' }
        & dotnet test tests/SSTD.WindowsTests/SSTD.WindowsTests.csproj -c Release --no-build --logger 'trx;LogFileName=windows.trx' --results-directory "$artifacts/test-results"
        if ($LASTEXITCODE) { throw 'Windows integration tests failed.' }
    }
    if ($Package -in 'portable','installer','all') {
        $publish = Join-Path $artifacts 'publish'
        & dotnet publish src/SSTD.UI/SSTD.UI.csproj -c Release -r win-x64 --self-contained true -p:Version=$Version -p:PublishSingleFile=false -p:DebugType=embedded -o $publish
        if ($LASTEXITCODE) { throw 'Publish failed.' }
        & "$root/tools/Smoke.ps1" -Exe "$publish/SSTD.exe"
    }
    if ($Package -in 'portable','all') {
        & "$root/tools/Package-Portable.ps1" -PublishDir "$artifacts/publish" -OutputZip "$artifacts/SSTD-Portable.zip"
    }
    if ($Package -in 'installer','all') {
        $iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
        if (-not (Test-Path $iscc)) { throw 'Install Inno Setup 6 first: winget install JRSoftware.InnoSetup' }
        & $iscc "/DAppVersion=$Version" "$root/installer/SSTD.iss"
        if ($LASTEXITCODE) { throw 'Installer build failed.' }
        & "$root/tools/Installer-Smoke.ps1" -Installer "$artifacts/SSTD-Setup.exe"
    }
    if (Test-Path "$artifacts/publish/SSTD.exe") {
        $hashes = Get-ChildItem $artifacts -File | Where-Object { $_.Extension -in '.exe','.zip' }
        $hashes += Get-Item "$artifacts/publish/SSTD.exe"
        $hashes | Get-FileHash -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($_.Path))" } | Set-Content "$artifacts/SHA256SUMS.txt"
    }
    Write-Host "SSTD build complete. Outputs: $artifacts" -ForegroundColor Green
}
finally { Pop-Location }
