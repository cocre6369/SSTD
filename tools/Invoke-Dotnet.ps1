param([Parameter(Mandatory)][string]$Log,[Parameter(Mandatory)][string[]]$Command)
$ErrorActionPreference='Continue'
New-Item -ItemType Directory -Force (Split-Path $Log) | Out-Null
& dotnet @Command 2>&1 | Tee-Object $Log
$code=$LASTEXITCODE
if($code -ne 0){
  Get-Content $Log | Select-Object -Last 100 | ForEach-Object { $line=$_.ToString(); $escaped=$line.Replace("%","%25").Replace("`r","%0D").Replace("`n","%0A"); Write-Host "::error title=dotnet::$escaped" }
}
exit $code
