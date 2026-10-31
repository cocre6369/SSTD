param([Parameter(Mandatory)][string]$Installer,[switch]$MarkOfTheWeb)
$ErrorActionPreference = 'Stop'
$Installer = (Resolve-Path $Installer).Path
$art = Join-Path $PSScriptRoot '../artifacts'
New-Item -ItemType Directory -Force $art | Out-Null
$suffix = if ($MarkOfTheWeb) { 'motw' } else { 'interactive' }
$result = Join-Path $art "installer-$suffix.txt"
$log = Join-Path $art "installer-$suffix.log"
Remove-Item $result,$log -ErrorAction SilentlyContinue
$copy = Join-Path $env:TEMP ('SSTD-Setup-Interactive-' + [guid]::NewGuid().ToString('N') + '.exe')
$started = $null
try {
    "start $(Get-Date -Format o) installer=$Installer motw=$MarkOfTheWeb" | Set-Content $log
    Copy-Item $Installer $copy -Force
    if ($MarkOfTheWeb) {
        Set-Content -Path $copy -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=3`r`n" -ErrorAction Stop
        'mark-of-the-web set' | Add-Content $log
    }
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $copy
    $psi.UseShellExecute = $true
    $psi.WorkingDirectory = Split-Path $copy
    $started = [System.Diagnostics.Process]::Start($psi)
    "started pid=$($started.Id)" | Add-Content $log
    $deadline = (Get-Date).AddSeconds(60)
    $found = $null
    while ((Get-Date) -lt $deadline) {
        $candidates = @($started) + @(Get-Process | Where-Object { $_.ProcessName -like 'SSTD-Setup*' -or $_.ProcessName -like 'SSTD*Setup*' })
        foreach ($proc in $candidates) {
            try {
                $proc.Refresh()
                if ($proc.MainWindowHandle -ne 0 -and $proc.MainWindowTitle) {
                    $found = $proc
                    break
                }
            } catch { }
        }
        if ($found) { break }
        try {
            Add-Type -AssemblyName UIAutomationClient -ErrorAction Stop
            $root = [System.Windows.Automation.AutomationElement]::RootElement
            $wins = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
            foreach ($w in $wins) {
                $name = $w.Current.Name
                if ($name -like 'Setup - SSTD*' -or $name -like '*Windows protected your PC*' -or $name -like '*SmartScreen*') {
                    "uia-window=$name class=$($w.Current.ClassName)" | Add-Content $log
                    if ($name -like 'Setup - SSTD*') { $found = $started; break }
                }
            }
        } catch { "uia-unavailable=$($_.Exception.Message)" | Add-Content $log }
        if ($found) { break }
        if ($started.HasExited) { "setup exited early code=$($started.ExitCode)" | Add-Content $log; break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $found) {
        try { Get-Process | Where-Object { $_.ProcessName -like 'SSTD*' -or $_.ProcessName -like '*Setup*' } | Format-List Id,ProcessName,MainWindowTitle,Responding | Out-String | Add-Content $log } catch { }
        throw 'Interactive installer wizard did not appear. See log.'
    }
    try { $found.Refresh(); "window=$($found.MainWindowTitle) pid=$($found.Id)" | Add-Content $log } catch { }
    try { [void]$found.CloseMainWindow() } catch { }
    if (-not $found.WaitForExit(5000)) { try { $found.Kill() } catch { } }
    if ($started -and $started.Id -ne $found.Id) { try { if (-not $started.HasExited) { $started.Kill() } } catch { } }
    "PASS: interactive installer wizard appeared (motw=$MarkOfTheWeb) and was dismissed." | Set-Content $result
    Get-Content $result | Write-Host
}
catch {
    $_ | Out-String | Add-Content $log
    throw
}
finally {
    try { if ($started -and -not $started.HasExited) { $started.Kill() } } catch { }
    Remove-Item $copy -Force -ErrorAction SilentlyContinue
}
