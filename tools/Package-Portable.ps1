param([Parameter(Mandatory)][string]$PublishDir,[Parameter(Mandatory)][string]$OutputZip)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = Split-Path $PSScriptRoot -Parent
$PublishDir = (Resolve-Path $PublishDir).Path
$OutputZip = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputZip)
$art = Split-Path $OutputZip -Parent
if (-not $art) { $art = '.' }
New-Item -ItemType Directory -Force $art | Out-Null
$staging = Join-Path $art 'portable-staging'
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force $staging | Out-Null
Copy-Item (Join-Path $PublishDir '*') $staging -Recurse -Force
foreach ($file in 'README.md','LICENSE') {
    $source = Join-Path $root $file
    if (Test-Path $source) { Copy-Item $source $staging -Force }
}
$docs = Join-Path $root 'docs'
if (Test-Path $docs) { Copy-Item $docs (Join-Path $staging 'docs') -Recurse -Force }
@'
SSTD portable build

1. Extract this ZIP to a folder you own, for example Documents\SSTD.
2. Run SSTD.exe from that folder.
3. Install and start Ollama separately, then connect SSTD to http://localhost:11434.

This portable build does not run the setup wizard and does not create shortcuts or an uninstaller.
It uses the same per-user SSTD data folder as the installed build.
'@ | Set-Content (Join-Path $staging 'PORTABLE.txt')
if (Test-Path $OutputZip) { Remove-Item $OutputZip -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $OutputZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host "Portable package: $OutputZip"
