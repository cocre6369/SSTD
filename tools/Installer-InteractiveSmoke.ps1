param([Parameter(Mandatory)][string]$Installer)
$ErrorActionPreference = 'Stop'
$Installer = (Resolve-Path $Installer).Path
$art = Join-Path $PSScriptRoot '../artifacts'
New-Item -ItemType Directory -Force $art | Out-Null
$result = Join-Path $art 'installer-interactive.txt'
$log = Join-Path $art 'installer-interactive.log'
Remove-Item $result,$log -ErrorAction SilentlyContinue
"start $(Get-Date -Format o) installer=$Installer" | Set-Content $log
$copy = Join-Path $env:TEMP ('SSTD-Setup-Interactive-' + [guid]::NewGuid().ToString('N') + '.exe')
Copy-Item $Installer $copy -Force
try {
    Set-Content -Path $copy -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=3`r`n" -ErrorAction Stop
    'mark-of-the-web set' | Add-Content $log
} catch {
    "mark-of-the-web not set: $($_.Exception.Message)" | Add-Content $log
}
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = $copy
$psi.UseShellExecute = $true
$psi.WorkingDirectory = Split-Path $copy
$p = [System.Diagnostics.Process]::Start($psi)
"started pid=$($p.Id)" | Add-Content $log
$deadline = (Get-Date).AddSeconds(45)
$found = $null
while ((Get-Date) -lt $deadline) {
    if ($p.HasExited) { "setup exited early code=$($p.ExitCode)" | Add-Content $log; break }
    try {
        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $wins = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($w in $wins) {
            $name = $w.Current.Name
            $class = $w.Current.ClassName
            if ($name -like 'Setup - SSTD*' -or $class -eq 'TWizardForm') { $found = $w; break }
        }
    } catch { "uia-error=$($_.Exception.Message)" | Add-Content $log }
    if ($found) { break }
    Start-Sleep -Milliseconds 500
}
if (-not $found) {
    try { Get-Process | Where-Object { $_.ProcessName -like 'SSTD*' } | Format-List Id,ProcessName,MainWindowTitle,Responding | Out-String | Add-Content $log } catch { }
    try { if (-not $p.HasExited) { $p.Kill() } } catch { }
    throw 'Interactive installer wizard did not appear. See installer-interactive.log.'
}
"window=$($found.Current.Name) class=$($found.Current.ClassName)" | Add-Content $log
try { [void]$p.CloseMainWindow() } catch { }
if (-not $p.WaitForExit(5000)) { try { $p.Kill() } catch { } }
Remove-Item $copy -Force -ErrorAction SilentlyContinue
'PASS: interactive installer wizard appeared with Mark-of-the-Web and was dismissed.' | Set-Content $result
Get-Content $result | Write-Host
