param(
    [Parameter(Mandatory)][string]$PublishDir,
    [Parameter(Mandatory)][string]$OutputMsi,
    [Parameter(Mandatory)][string]$Version
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PublishDir = (Resolve-Path $PublishDir).Path
$root = Split-Path $PSScriptRoot -Parent
$art = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Force $art | Out-Null
$OutputMsi = [System.IO.Path]::GetFullPath($OutputMsi)
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be major.minor.patch.' }
if (-not (Get-Command wix -ErrorAction SilentlyContinue)) {
    throw 'WiX v4 is required. Run: dotnet tool install --global wix --version 4.0.5; wix extension add --global WixToolset.UI.wixext/4.0.5'
}
$stage = Join-Path $art 'msi-staging'
$author = Join-Path $art 'msi-authoring'
Remove-Item $stage,$author -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage,$author | Out-Null
Copy-Item (Join-Path $PublishDir '*') $stage -Recurse -Force
Copy-Item (Join-Path $root 'README.md') $stage -Force
Copy-Item (Join-Path $root 'LICENSE') $stage -Force
New-Item -ItemType Directory -Force (Join-Path $stage 'docs') | Out-Null
Copy-Item (Join-Path $root 'docs/*') (Join-Path $stage 'docs') -Recurse -Force
if (-not (Test-Path (Join-Path $stage 'SSTD.exe'))) { throw 'Publish output is missing SSTD.exe.' }

function Escape-Xml([string]$s) {
    return [System.Security.SecurityElement]::Escape($s)
}
function Id-For([string]$prefix,[string]$s) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($s.ToLowerInvariant())) }
    finally { $sha.Dispose() }
    $hex = ([System.BitConverter]::ToString($hash)).Replace('-','').Substring(0,24)
    return $prefix + $hex
}
$licenseText = Get-Content (Join-Path $root 'LICENSE') -Raw
$rtf = New-Object System.Text.StringBuilder
[void]$rtf.Append('{\rtf1\ansi\deff0{\fonttbl{\f0 Consolas;}}\f0\fs18 ')
foreach ($line in ($licenseText -split "`r?`n")) {
    $esc = $line.Replace('\','\\').Replace('{','\{').Replace('}','\}')
    [void]$rtf.Append($esc + '\par ')
}
[void]$rtf.Append('}')
$rtfPath = Join-Path $author 'LICENSE.rtf'
[System.IO.File]::WriteAllText($rtfPath, $rtf.ToString(), [System.Text.Encoding]::ASCII)

$files = Get-ChildItem $stage -Recurse -File | Sort-Object FullName
$dirChildren = @{}
$dirNames = @{}
$rootId = 'INSTALLFOLDER'
$dirChildren[$rootId] = [System.Collections.Generic.List[string]]::new()
$componentXml = [System.Text.StringBuilder]::new()
$componentIds = [System.Collections.Generic.List[string]]::new()
foreach ($file in $files) {
    $rel = $file.FullName.Substring($stage.Length + 1).Replace('\','/')
    $parent = Split-Path $rel -Parent
    $dirId = $rootId
    if ($parent) {
        $parts = $parent -split '/'
        $walk = ''
        foreach ($part in $parts) {
            $walk = if ($walk) { "$walk/$part" } else { $part }
            $childId = Id-For 'D_' $walk
            if (-not $dirChildren.ContainsKey($dirId)) { $dirChildren[$dirId] = [System.Collections.Generic.List[string]]::new() }
            if (-not $dirChildren[$dirId].Contains($childId)) { [void]$dirChildren[$dirId].Add($childId) }
            $dirNames[$childId] = $part
            if (-not $dirChildren.ContainsKey($childId)) { $dirChildren[$childId] = [System.Collections.Generic.List[string]]::new() }
            $dirId = $childId
        }
    }
    $cid = Id-For 'C_' $rel
    $fid = Id-For 'F_' $rel
    [void]$componentIds.Add($cid)
    [void]$componentXml.AppendLine("      <Component Id=`"$cid`" Directory=`"$dirId`" Guid=`"*`">")
    [void]$componentXml.AppendLine("        <File Id=`"$fid`" Source=`"$(Escape-Xml $file.FullName)`" KeyPath=`"yes`" />")
    [void]$componentXml.AppendLine('      </Component>')
}
function Emit-Dir([string]$id) {
    $sb = [System.Text.StringBuilder]::new()
    foreach ($child in $dirChildren[$id]) {
        $name = Escape-Xml $dirNames[$child]
        [void]$sb.AppendLine("          <Directory Id=`"$child`" Name=`"$name`">")
        [void]$sb.Append((Emit-Dir $child))
        [void]$sb.AppendLine('          </Directory>')
    }
    return $sb.ToString()
}
$iconPath = Join-Path $root 'assets/SSTD.ico'
$upgradeCode = 'd4b7e8f2-6a1c-4e3b-9f0a-2c5d8e7b6a41'
$wxs = [System.Text.StringBuilder]::new()
[void]$wxs.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
[void]$wxs.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:ui="http://wixtoolset.org/schemas/v4/wxs/ui">')
[void]$wxs.AppendLine("  <Package Name=`"SSTD — Super Terminal of Deepthinking`" Language=`"1033`" Version=`"$Version.0`" Manufacturer=`"SSTD contributors`" UpgradeCode=`"$upgradeCode`" Scope=`"perUser`" InstallerVersion=`"500`" Compressed=`"yes`">")
[void]$wxs.AppendLine('    <MajorUpgrade AllowDowngrades="no" AllowSameVersionUpgrades="yes" DowngradeErrorMessage="A newer version of SSTD is already installed." />')
[void]$wxs.AppendLine('    <MediaTemplate EmbedCab="yes" />')
[void]$wxs.AppendLine("    <Icon Id=`"SSTD.ico`" SourceFile=`"$(Escape-Xml $iconPath)`" />")
[void]$wxs.AppendLine('    <Property Id="ARPPRODUCTICON" Value="SSTD.ico" />')
[void]$wxs.AppendLine('    <Property Id="ARPHELPLINK" Value="https://github.com/cocre6369/SSTD" />')
[void]$wxs.AppendLine('    <Property Id="ARPURLINFOABOUT" Value="https://github.com/cocre6369/SSTD" />')
[void]$wxs.AppendLine('    <Property Id="WIXUI_EXITDIALOGOPTIONALTEXT" Value="SSTD has been installed. Launch it from the Start Menu. Your SSTD data folder is not removed by uninstall." />')
[void]$wxs.AppendLine("    <WixVariable Id=`"WixUILicenseRtf`" Value=`"$(Escape-Xml $rtfPath)`" />")
[void]$wxs.AppendLine('    <ui:WixUI Id="WixUI_InstallDir" InstallDirectory="INSTALLFOLDER" />')
[void]$wxs.AppendLine('    <Feature Id="Main" Title="SSTD" Level="1">')
[void]$wxs.AppendLine('      <ComponentGroupRef Id="ProductComponents" />')
[void]$wxs.AppendLine('      <ComponentRef Id="StartMenuShortcut" />')
[void]$wxs.AppendLine('    </Feature>')
[void]$wxs.AppendLine('    <StandardDirectory Id="LocalAppDataFolder">')
[void]$wxs.AppendLine('      <Directory Id="ProgramsFolder" Name="Programs">')
[void]$wxs.AppendLine('        <Directory Id="INSTALLFOLDER" Name="SSTD">')
[void]$wxs.Append((Emit-Dir $rootId))
[void]$wxs.AppendLine('        </Directory>')
[void]$wxs.AppendLine('      </Directory>')
[void]$wxs.AppendLine('    </StandardDirectory>')
[void]$wxs.AppendLine('    <StandardDirectory Id="ProgramMenuFolder">')
[void]$wxs.AppendLine('      <Component Id="StartMenuShortcut" Guid="*">')
[void]$wxs.AppendLine('        <RegistryValue Root="HKCU" Key="Software\SSTD" Name="StartMenuShortcut" Type="integer" Value="1" KeyPath="yes" />')
[void]$wxs.AppendLine('        <Shortcut Id="SSTDStartMenu" Name="SSTD" Description="Super Terminal of Deepthinking" Target="[INSTALLFOLDER]SSTD.exe" WorkingDirectory="INSTALLFOLDER" Icon="SSTD.ico" />')
[void]$wxs.AppendLine('      </Component>')
[void]$wxs.AppendLine('    </StandardDirectory>')
[void]$wxs.AppendLine('    <ComponentGroup Id="ProductComponents">')
[void]$wxs.Append($componentXml.ToString())
[void]$wxs.AppendLine('    </ComponentGroup>')
[void]$wxs.AppendLine('  </Package>')
[void]$wxs.AppendLine('</Wix>')
$wxsPath = Join-Path $author 'SSTD.wxs'
[System.IO.File]::WriteAllText($wxsPath, $wxs.ToString(), [System.Text.UTF8Encoding]::new($false))
& wix build -arch x64 -ext WixToolset.UI.wixext -o $OutputMsi $wxsPath
if ($LASTEXITCODE -ne 0) { throw "WiX build failed with exit code $LASTEXITCODE." }
if (-not (Test-Path $OutputMsi)) { throw 'MSI output missing.' }
Write-Host "MSI created: $OutputMsi"
