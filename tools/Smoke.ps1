param([Parameter(Mandatory)][string]$Exe)
$ErrorActionPreference = 'Stop'
$Exe = (Resolve-Path $Exe).Path
$dir = Split-Path $Exe
$art = Join-Path $PSScriptRoot '../artifacts'
New-Item -ItemType Directory -Force $art | Out-Null
$log = Join-Path $art 'smoke-launch.log'
"start $(Get-Date -Format o) exe=$Exe" | Set-Content $log
Remove-Item "$dir/smoke-result.txt","$dir/smoke-error.txt","$dir/smoke-stage.txt" -ErrorAction SilentlyContinue
$psi = [System.Diagnostics.ProcessStartInfo]::new()
$dll = Join-Path $dir 'SSTD.dll'
if (Test-Path $dll) { $psi.FileName = (Get-Command dotnet).Source; $psi.Arguments = '"' + $dll + '" --smoke-test' } else { $psi.FileName = $Exe; $psi.Arguments = '--smoke-test' }
$psi.UseShellExecute = $false; $psi.WorkingDirectory = $dir; $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
$p = [System.Diagnostics.Process]::Start($psi)
"started pid=$($p.Id)" | Add-Content $log
$stdout=$p.StandardOutput.ReadToEndAsync(); $stderr=$p.StandardError.ReadToEndAsync()
$exited = $p.WaitForExit(60000)
"wait exited=$exited hasExited=$($p.HasExited)" | Add-Content $log
if (-not $exited) { $p.Kill(); $p.WaitForExit(); "killed" | Add-Content $log }
try { $stdout.Result | Set-Content "$dir/smoke-stdout.txt" } catch {}
try { $stderr.Result | Set-Content "$dir/smoke-error.txt" } catch {}
"exitcode=$($p.ExitCode)" | Add-Content $log
$result = Get-Content "$dir/smoke-result.txt" -Raw -ErrorAction SilentlyContinue
if (Test-Path "$dir/smoke-result.txt") { Copy-Item "$dir/smoke-result.txt" "$art/smoke-result.txt" -Force }
if (Test-Path "$dir/smoke-error.txt") { Copy-Item "$dir/smoke-error.txt" "$art/smoke-error.txt" -Force }
if (Test-Path "$dir/smoke-stage.txt") { Copy-Item "$dir/smoke-stage.txt" "$art/smoke-stage.txt" -Force }
if (Test-Path "$dir/smoke-stdout.txt") { Copy-Item "$dir/smoke-stdout.txt" "$art/smoke-stdout.txt" -Force }
if (-not $exited -or $p.ExitCode -ne 0 -or $result -notmatch '^PASS:') { throw "Executable smoke test failed (exited=$exited exit=$($p.ExitCode)). See smoke-launch.log." }
