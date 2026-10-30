param([Parameter(Mandatory)][string]$Installer)
$ErrorActionPreference = 'Stop'
$Installer = (Resolve-Path $Installer).Path
$target = Join-Path $env:LOCALAPPDATA 'Programs\SSTD'
$stamp = Get-Date
$proc = Start-Process $Installer -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-' -Wait -PassThru
if ($proc.ExitCode -ne 0) { throw "Installer exit code $($proc.ExitCode)" }
if (-not (Test-Path "$target/SSTD.exe")) { throw 'Installed executable missing.' }
& "$PSScriptRoot/Smoke.ps1" -Exe "$target/SSTD.exe"
$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\SSTD.lnk'
if (-not (Test-Path $shortcut)) { throw 'Start Menu shortcut missing.' }
$uninstall = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{8A77F9D2-5E36-4D01-A4C3-9F5051D6C8B2}_is1' -ErrorAction Stop
$command = $uninstall.UninstallString
if ($command -notmatch '^"([^"]+)"') { throw 'Unexpected uninstall command.' }
$uninstaller = $Matches[1]
if (-not $uninstaller.StartsWith($target, [StringComparison]::OrdinalIgnoreCase)) { throw 'Uninstaller is not scoped to SSTD installation.' }
$p = Start-Process $uninstaller -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait -PassThru
# Inno's uninstaller copies itself to temp and may return before completion.
$deadline = (Get-Date).AddSeconds(25)
while ((Test-Path "$target/SSTD.exe") -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }
if (Test-Path "$target/SSTD.exe") { throw 'Uninstaller left the application executable.' }
if (Test-Path $shortcut) { throw 'Uninstaller left the Start Menu shortcut.' }
$result = "PASS: installer exit 0; installed executable startup; Start Menu shortcut; registered uninstaller; application and shortcut removal.`nUser data is deliberately retained; remove it from Settings before uninstall if desired."
New-Item -ItemType Directory -Force "$PSScriptRoot/../artifacts" | Out-Null
$result | Set-Content "$PSScriptRoot/../artifacts/installer-smoke.txt"
Write-Host $result
