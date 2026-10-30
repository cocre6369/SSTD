param([Parameter(Mandatory)][string]$Exe)
$ErrorActionPreference = 'Stop'
$Exe = (Resolve-Path $Exe).Path
$dir = Split-Path $Exe
Remove-Item "$dir/smoke-result.txt","$dir/smoke-error.txt" -ErrorAction SilentlyContinue
$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = $Exe; $psi.Arguments = '--smoke-test'; $psi.UseShellExecute = $false; $psi.WorkingDirectory = $dir
$p = [System.Diagnostics.Process]::Start($psi)
if (-not $p.WaitForExit(45000)) { $p.Kill(); throw 'SSTD smoke test timed out.' }
$result = Get-Content "$dir/smoke-result.txt" -Raw -ErrorAction SilentlyContinue
Write-Host $result
if ($p.ExitCode -ne 0 -or $result -notmatch '^PASS:') { throw "Executable smoke test failed (exit $($p.ExitCode))." }
New-Item -ItemType Directory -Force "$PSScriptRoot/../artifacts" | Out-Null
Copy-Item "$dir/smoke-result.txt" "$PSScriptRoot/../artifacts/smoke-result.txt" -Force
