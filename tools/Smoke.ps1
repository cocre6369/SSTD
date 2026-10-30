param([Parameter(Mandatory)][string]$Exe)
$ErrorActionPreference = 'Stop'
$Exe = (Resolve-Path $Exe).Path
$dir = Split-Path $Exe
$art = Join-Path $PSScriptRoot '../artifacts'
New-Item -ItemType Directory -Force $art | Out-Null
$log = Join-Path $art 'smoke-launch.log'
"start $(Get-Date -Format o) exe=$Exe" | Set-Content $log
Remove-Item "$art/smoke-result.txt","$art/smoke-error.txt","$art/smoke-stage.txt","$art/smoke-boot.txt","$art/smoke-unhandled.txt" -ErrorAction SilentlyContinue
$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = $Exe; $psi.Arguments = '--smoke-test'
$env:SSTD_SMOKE_DIR = $art
$psi.UseShellExecute = $false; $psi.WorkingDirectory = $dir; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
$p = [System.Diagnostics.Process]::Start($psi)
"started pid=$($p.Id)" | Add-Content $log
$stdout=$p.StandardOutput.ReadToEndAsync(); $stderr=$p.StandardError.ReadToEndAsync()
$exited = $p.WaitForExit(60000)
"wait exited=$exited hasExited=$($p.HasExited)" | Add-Content $log
if (-not $exited) {
  try { "mainwindow=$($p.MainWindowTitle)" | Add-Content $log } catch {}
  try {
    Add-Type -AssemblyName UIAutomationClient
    $root=[System.Windows.Automation.AutomationElement]::RootElement
    $cond=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$p.Id)
    $wins=$root.FindAll([System.Windows.Automation.TreeScope]::Children,$cond)
    foreach($w in $wins){ "window=$($w.Current.Name) class=$($w.Current.ClassName)" | Add-Content $log }
  } catch { "uia-error=$($_.Exception.Message)" | Add-Content $log }
  $p.Kill(); $p.WaitForExit(); "killed" | Add-Content $log
}
try { $stdout.Result | Set-Content "$art/smoke-stdout.txt" } catch {}
try { $stderr.Result | Set-Content "$art/smoke-stderr.txt" } catch {}
"exitcode=$($p.ExitCode)" | Add-Content $log
$result = Get-Content "$art/smoke-result.txt" -Raw -ErrorAction SilentlyContinue
if (Test-Path "$art/smoke-result.txt") { }

if (-not $exited -or $p.ExitCode -ne 0 -or $result -notmatch '^PASS:') { throw "Executable smoke test failed (exited=$exited exit=$($p.ExitCode)). See smoke-launch.log." }
