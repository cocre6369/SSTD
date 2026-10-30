param([Parameter(Mandatory)][string]$Exe)
$ErrorActionPreference = 'Stop'
$Exe = (Resolve-Path $Exe).Path
$dir = Split-Path $Exe
Remove-Item "$dir/smoke-result.txt","$dir/smoke-error.txt" -ErrorAction SilentlyContinue
$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = $Exe; $psi.Arguments = '--smoke-test'; $psi.UseShellExecute = $false; $psi.WorkingDirectory = $dir; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
$p = [System.Diagnostics.Process]::Start($psi)
$stdout=$p.StandardOutput.ReadToEndAsync(); $stderr=$p.StandardError.ReadToEndAsync()
if (-not $p.WaitForExit(90000)) { $p.Kill(); $p.WaitForExit(); $stdout.Result | Set-Content "$dir/smoke-stdout.txt"; $stderr.Result | Set-Content "$dir/smoke-error.txt"; Copy-Item "$dir/smoke-error.txt" "$PSScriptRoot/../artifacts/smoke-error.txt" -Force -ErrorAction SilentlyContinue; throw 'SSTD smoke test timed out.' }
$stdout.Result | Set-Content "$dir/smoke-stdout.txt"; $stderr.Result | Set-Content "$dir/smoke-error.txt"
Write-Host "Smoke exit code: $($p.ExitCode)"
$result = Get-Content "$dir/smoke-result.txt" -Raw -ErrorAction SilentlyContinue
Write-Host $result
New-Item -ItemType Directory -Force "$PSScriptRoot/../artifacts" | Out-Null
if (Test-Path "$dir/smoke-result.txt") { Copy-Item "$dir/smoke-result.txt" "$PSScriptRoot/../artifacts/smoke-result.txt" -Force }
if (Test-Path "$dir/smoke-error.txt") { Copy-Item "$dir/smoke-error.txt" "$PSScriptRoot/../artifacts/smoke-error.txt" -Force }
if (Test-Path "$dir/smoke-stdout.txt") { Copy-Item "$dir/smoke-stdout.txt" "$PSScriptRoot/../artifacts/smoke-stdout.txt" -Force }
if ($p.ExitCode -ne 0 -or $result -notmatch '^PASS:') { throw "Executable smoke test failed (exit $($p.ExitCode))." }
