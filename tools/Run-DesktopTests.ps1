param([string]$Repository = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$result=Join-Path $Repository 'artifacts/desktop-test-exit.txt'
$log=Join-Path $Repository 'artifacts/desktop-task.log'
Remove-Item $result,$log -ErrorAction SilentlyContinue
$command="Set-Location '$Repository'; try { ./tools/Windows-Integration.ps1 *> '$log'; `$code=`$LASTEXITCODE; if(-not `$code){`$code=0} } catch { `$_ | Out-String | Add-Content '$log'; `$code=1 }; Set-Content '$result' `$code; exit `$code"
$pwsh=(Get-Command pwsh).Source
$cmd=Join-Path $Repository 'artifacts/run-desktop.cmd'
Set-Content $cmd "@echo off`r`n`"$pwsh`" -NoProfile -ExecutionPolicy Bypass -Command `"$command`""
# SAFER Basic User (0x20000) gives the CI test a limited token without changing the product's policy.
$p=Start-Process runas.exe -ArgumentList '/trustlevel:0x20000',"`"$cmd`"" -PassThru -Wait
if(-not(Test-Path $result)){throw 'Limited desktop verification did not produce a result.'}
if(Test-Path $log){Get-Content $log | Write-Host}
$code=[int](Get-Content $result | Select-Object -Last 1)
if($p.ExitCode -ne 0 -or $code -ne 0){throw "Desktop verification failed (launcher $($p.ExitCode), tests $code)."}
