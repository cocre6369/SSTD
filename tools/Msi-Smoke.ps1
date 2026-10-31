param([Parameter(Mandatory)][string]$Msi)
$ErrorActionPreference = 'Stop'
$Msi = (Resolve-Path $Msi).Path
$target = Join-Path $env:LOCALAPPDATA 'Programs\SSTD'
$art = Join-Path $PSScriptRoot '../artifacts'
New-Item -ItemType Directory -Force $art | Out-Null
$installLog = Join-Path $art 'msi-install.log'
$uninstallLog = Join-Path $art 'msi-uninstall.log'
$resultPath = Join-Path $art 'msi-smoke.txt'
$dataDir = Join-Path $env:LOCALAPPDATA 'SSTD'
$sentinel = Join-Path $dataDir ('ci-uninstall-sentinel-' + [guid]::NewGuid().ToString('N') + '.txt')
$stage = 'start'
function Write-Stage([string]$message) {
    $script:stage = $message
    "$(Get-Date -Format o) $message" | Add-Content (Join-Path $art 'msi-smoke-stage.txt')
}
function Invoke-Msi([string]$msiArgs, [string]$log) {
    $full = "$msiArgs /L*V `"$log`""
    $p = Start-Process msiexec.exe -ArgumentList $full -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "msiexec exit code $($p.ExitCode). See $log" }
}
try {
    Remove-Item $resultPath, (Join-Path $art 'msi-smoke-stage.txt') -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $dataDir | Out-Null
    Set-Content $sentinel 'SSTD CI uninstall sentinel'

    Write-Stage 'install'
    Invoke-Msi "/i `"$Msi`" /qn /norestart" $installLog
    if (-not (Test-Path (Join-Path $target 'SSTD.exe'))) { throw 'Installed executable missing.' }

    Write-Stage 'installed-startup'
    & "$PSScriptRoot/Smoke.ps1" -Exe (Join-Path $target 'SSTD.exe')

    Write-Stage 'repair'
    Invoke-Msi "/i `"$Msi`" /qn /norestart REINSTALL=ALL REINSTALLMODE=vomus" $installLog
    Write-Stage 'repaired-startup'
    & "$PSScriptRoot/Smoke.ps1" -Exe (Join-Path $target 'SSTD.exe')

    Write-Stage 'shortcut'
    $shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\SSTD.lnk'
    if (-not (Test-Path $shortcut)) { throw "Start Menu shortcut missing: $shortcut" }

    Write-Stage 'uninstall-registration'
    # Windows Installer may publish a per-user managed product's ARP entry under
    # HKLM even when the product assignment is per-user. Prove the user context
    # from the MSI log, install path, and HKCU component key instead of assuming
    # the ARP registry root alone determines scope.
    $installText = [System.IO.File]::ReadAllText($installLog, [System.Text.Encoding]::Unicode)
    if ($installText -notmatch 'Assignment=0') { throw 'MSI log did not record a per-user product assignment (Assignment=0).' }
    if (-not $target.StartsWith([System.IO.Path]::GetFullPath($env:LOCALAPPDATA), [System.StringComparison]::OrdinalIgnoreCase)) { throw "Install path is not user-local: $target" }
    $componentKey = Get-ItemProperty 'HKCU:\Software\SSTD' -ErrorAction SilentlyContinue
    if (-not $componentKey -or $componentKey.StartMenuShortcut -ne 1) { throw 'HKCU SSTD component key missing.' }
    $uninstallRoots = @(
        'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
    )
    $entry = $null
    foreach ($root in $uninstallRoots) {
        if (-not (Test-Path $root)) { continue }
        $entry = Get-ChildItem $root -ErrorAction SilentlyContinue | ForEach-Object {
            $props = Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue
            if ($props.DisplayName -like 'SSTD*' -and $props.UninstallString -match 'MsiExec') {
                [pscustomobject]@{ Root = $root; Key = $_.PSChildName; DisplayName = $props.DisplayName; UninstallString = $props.UninstallString }
            }
        } | Select-Object -First 1
        if ($entry) { break }
    }
    if (-not $entry) { throw 'SSTD Windows Installer uninstall registration missing.' }
    $productCode = $entry.Key

    Write-Stage "uninstall $productCode"
    Invoke-Msi "/x $productCode /qn /norestart" $uninstallLog
    $deadline = (Get-Date).AddSeconds(30)
    while ((Test-Path (Join-Path $target 'SSTD.exe')) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }
    if (Test-Path (Join-Path $target 'SSTD.exe')) { throw 'Uninstaller left the application executable.' }
    if (Test-Path $shortcut) { throw 'Uninstaller left the Start Menu shortcut.' }
    if (-not (Test-Path $sentinel)) { throw 'Uninstaller removed user data sentinel.' }
    Remove-Item $sentinel -Force -ErrorAction SilentlyContinue

    $result = 'PASS: MSI per-user product assignment; install exit 0; installed executable startup; reinstall/repair; Start Menu shortcut; Windows Installer uninstall registration; application and shortcut removal; user data retained.'
    $result | Set-Content $resultPath
    Write-Stage 'pass'
    Write-Host $result
}
catch {
    $message = "FAIL at stage '$stage': $($_.Exception.Message)"
    $message | Set-Content $resultPath
    Write-Stage $message
    throw
}
