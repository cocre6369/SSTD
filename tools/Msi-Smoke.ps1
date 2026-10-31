param([Parameter(Mandatory)][string]$Msi)
$ErrorActionPreference = 'Stop'
$Msi = (Resolve-Path $Msi).Path
$target = Join-Path $env:LOCALAPPDATA 'Programs\SSTD'
$art = Join-Path $PSScriptRoot '../artifacts'
New-Item -ItemType Directory -Force $art | Out-Null
$installLog = Join-Path $art 'msi-install.log'
$uninstallLog = Join-Path $art 'msi-uninstall.log'
$dataDir = Join-Path $env:LOCALAPPDATA 'SSTD'
$sentinel = Join-Path $dataDir ('ci-uninstall-sentinel-' + [guid]::NewGuid().ToString('N') + '.txt')
New-Item -ItemType Directory -Force $dataDir | Out-Null
Set-Content $sentinel 'SSTD CI uninstall sentinel'
function Invoke-Msi([string]$msiArgs, [string]$log) {
    $full = "$msiArgs /L*V `"$log`""
    $p = Start-Process msiexec.exe -ArgumentList $full -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "msiexec exit code $($p.ExitCode). See $log" }
}
Invoke-Msi "/i `"$Msi`" /qn /norestart" $installLog
if (-not (Test-Path (Join-Path $target 'SSTD.exe'))) { throw 'Installed executable missing.' }
& "$PSScriptRoot/Smoke.ps1" -Exe (Join-Path $target 'SSTD.exe')
# Reinstall/repair the same package to verify maintenance does not depend on a fresh profile.
Invoke-Msi "/i `"$Msi`" /qn /norestart REINSTALL=ALL REINSTALLMODE=vomus" $installLog
& "$PSScriptRoot/Smoke.ps1" -Exe (Join-Path $target 'SSTD.exe')
$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\SSTD.lnk'
if (-not (Test-Path $shortcut)) { throw 'Start Menu shortcut missing.' }
$installer = New-Object -ComObject WindowsInstaller.Installer
$db = $installer.OpenDatabase($Msi, 0)
$sql = 'SELECT `Value` FROM `Property` WHERE `Property`=''ProductCode'''
$view = $db.OpenView($sql)
$view.Execute()
$record = $view.Fetch()
if (-not $record) { throw 'ProductCode missing from MSI.' }
$productCode = $record.StringData(1)
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($view) | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($db) | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($installer) | Out-Null
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$productCode"
if (-not (Test-Path $uninstallKey)) { throw 'Per-user uninstall registration missing.' }
Invoke-Msi "/x $productCode /qn /norestart" $uninstallLog
$deadline = (Get-Date).AddSeconds(30)
while ((Test-Path (Join-Path $target 'SSTD.exe')) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }
if (Test-Path (Join-Path $target 'SSTD.exe')) { throw 'Uninstaller left the application executable.' }
if (Test-Path $shortcut) { throw 'Uninstaller left the Start Menu shortcut.' }
if (-not (Test-Path $sentinel)) { throw 'Uninstaller removed user data sentinel.' }
Remove-Item $sentinel -Force -ErrorAction SilentlyContinue
$result = "PASS: MSI per-user install exit 0; installed executable startup; reinstall/repair; Start Menu shortcut; registered per-user uninstaller; application and shortcut removal; user data retained."
$result | Set-Content (Join-Path $art 'msi-smoke.txt')
Write-Host $result
