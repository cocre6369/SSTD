param([switch]$SkipNotepad)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
  dotnet publish tools/SSTD.TestApp/SSTD.TestApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/test-fixture
  if($LASTEXITCODE){throw 'Fixture build failed.'}
  & "$root/tools/Invoke-Dotnet.ps1" -Log artifacts/windows.log -Command test,tests/SSTD.WindowsTests/SSTD.WindowsTests.csproj,-c,Release,--no-build,--logger,'trx;LogFileName=windows.trx',--results-directory,artifacts/test-results
  if($LASTEXITCODE){throw 'Windows integration tests failed.'}
}finally{Pop-Location}
