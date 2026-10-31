param([Parameter(Mandatory)][string]$Zip)
$ErrorActionPreference = 'Stop'
$Zip = (Resolve-Path $Zip).Path
$art = Join-Path $PSScriptRoot '../artifacts'
New-Item -ItemType Directory -Force $art | Out-Null
$result = Join-Path $art 'portable-motw.txt'
$log = Join-Path $art 'portable-motw.log'
Remove-Item $result,$log -ErrorAction SilentlyContinue
$extract = Join-Path $env:TEMP ('SSTD-Portable-MOTW-' + [guid]::NewGuid().ToString('N'))
try {
    "start $(Get-Date -Format o) zip=$Zip" | Set-Content $log
    Expand-Archive $Zip -DestinationPath $extract -Force
    $exe = Join-Path $extract 'SSTD.exe'
    if (-not (Test-Path $exe)) { throw 'Portable ZIP missing SSTD.exe.' }
    # Explorer propagates the download zone to extracted files; simulate that on every payload file.
    Get-ChildItem $extract -Recurse -File | ForEach-Object {
        Set-Content -Path $_.FullName -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=3`r`n" -ErrorAction Stop
    }
    'mark-of-the-web set on extracted files' | Add-Content $log
    & "$PSScriptRoot/Smoke.ps1" -Exe $exe
    'PASS: portable ZIP extracted with Mark-of-the-Web and SSTD.exe startup smoke passed.' | Set-Content $result
    Get-Content $result | Write-Host
}
catch {
    $_ | Out-String | Add-Content $log
    throw
}
finally {
    Remove-Item $extract -Recurse -Force -ErrorAction SilentlyContinue
}
