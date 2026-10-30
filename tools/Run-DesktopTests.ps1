param([string]$Repository = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$name='SSTD-Desktop-Verification'
$result=Join-Path $Repository 'artifacts/desktop-test-exit.txt'
$log=Join-Path $Repository 'artifacts/desktop-task.log'
Remove-Item $result,$log -ErrorAction SilentlyContinue
$command="Set-Location '$Repository'; try { ./tools/Windows-Integration.ps1 *> '$log'; `$code=`$LASTEXITCODE; if(-not `$code){`$code=0} } catch { `$_ | Out-String | Add-Content '$log'; `$code=1 }; Set-Content '$result' `$code; exit `$code"
$pwsh=(Get-Command pwsh).Source
$cmd=Join-Path $Repository 'artifacts/run-desktop.cmd'
Set-Content $cmd "@echo off`r`n`"$pwsh`" -NoProfile -ExecutionPolicy Bypass -Command `"$command`""
# Explorer's shell broker launches the test command with the normal medium-integrity desktop token.
Start-Process explorer.exe -ArgumentList "`"$cmd`""
$deadline=(Get-Date).AddMinutes(4)
while((Get-Date) -lt $deadline -and -not(Test-Path $result)){Start-Sleep -Seconds 2}
if(Test-Path $log){Get-Content $log | Write-Host}
if(-not(Test-Path $result)){throw 'Desktop verification task timed out.'}
$code=[int](Get-Content $result | Select-Object -Last 1)
if($code -ne 0){throw "Desktop verification failed with exit code $code."}

