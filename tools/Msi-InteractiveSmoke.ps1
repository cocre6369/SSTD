param([Parameter(Mandatory)][string]$Msi,[switch]$MarkOfTheWeb)
$ErrorActionPreference = 'Stop'
$Msi = (Resolve-Path $Msi).Path
$art = Join-Path $PSScriptRoot '../artifacts'
New-Item -ItemType Directory -Force $art | Out-Null
$suffix = if ($MarkOfTheWeb) { 'motw' } else { 'interactive' }
$result = Join-Path $art "msi-$suffix.txt"
$log = Join-Path $art "msi-$suffix.log"
$msiLog = Join-Path $art "msi-$suffix-windows.log"
Remove-Item $result,$log,$msiLog -ErrorAction SilentlyContinue
$copy = Join-Path $env:TEMP ('SSTD-Setup-' + [guid]::NewGuid().ToString('N') + '.msi')
$started = $null
try {
    "start $(Get-Date -Format o) msi=$Msi motw=$MarkOfTheWeb" | Set-Content $log
    Copy-Item $Msi $copy -Force
    if ($MarkOfTheWeb) {
        Set-Content -Path $copy -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=3`r`n" -ErrorAction Stop
        'mark-of-the-web set' | Add-Content $log
    }
    # Launch through msiexec, the same engine Explorer uses after the user approves SmartScreen.
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = 'msiexec.exe'
    $psi.Arguments = "/i `"$copy`" /L*V `"$msiLog`""
    $psi.UseShellExecute = $false
    $started = [System.Diagnostics.Process]::Start($psi)
    "started pid=$($started.Id)" | Add-Content $log
    Add-Type -AssemblyName UIAutomationClient -ErrorAction SilentlyContinue
    $deadline = (Get-Date).AddSeconds(75)
    $found = $null
    while ((Get-Date) -lt $deadline) {
        try {
            $root = [System.Windows.Automation.AutomationElement]::RootElement
            $wins = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
            foreach ($w in $wins) {
                $name = $w.Current.Name
                if ($name -like '*SSTD*Setup*') {
                    "uia-window=$name class=$($w.Current.ClassName)" | Add-Content $log
                    $found = $w
                    break
                }
                if ($name -like '*SmartScreen*' -or $name -like '*Windows protected your PC*') {
                    "security-window=$name" | Add-Content $log
                }
            }
        } catch { "uia-error=$($_.Exception.Message)" | Add-Content $log }
        if ($found) { break }
        if ($started.HasExited) { "msiexec exited early code=$($started.ExitCode)" | Add-Content $log; break }
        Start-Sleep -Milliseconds 500
    }
    if (-not $found) {
        try { Get-Process msiexec -ErrorAction SilentlyContinue | Format-List Id,MainWindowTitle,Responding | Out-String | Add-Content $log } catch { }
        throw 'MSI wizard did not appear. See log.'
    }
    # Cancel the wizard without installing.
    try {
        $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Cancel')
        $cancel = $found.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
        if ($cancel) {
            $invoke = $cancel.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
            $invoke.Invoke()
            Start-Sleep -Milliseconds 800
        }
    } catch { "cancel-error=$($_.Exception.Message)" | Add-Content $log }
    try {
        $proc = Get-Process -Id $started.Id -ErrorAction SilentlyContinue
        if ($proc) { [void]$proc.CloseMainWindow() }
    } catch { }
    if (-not $started.WaitForExit(5000)) { try { $started.Kill() } catch { } }
    "PASS: MSI wizard appeared (motw=$MarkOfTheWeb) and was dismissed." | Set-Content $result
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
