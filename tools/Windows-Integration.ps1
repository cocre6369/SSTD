param([switch]$SkipNotepad,[int]$TimeoutSeconds = 150)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
  dotnet publish tools/SSTD.TestApp/SSTD.TestApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/test-fixture
  if($LASTEXITCODE){throw 'Fixture build failed.'}
  $args=@('test','tests/SSTD.WindowsTests/SSTD.WindowsTests.csproj','-c','Release','--no-build','--logger','trx;LogFileName=windows.trx','--results-directory','artifacts/test-results','--blame-hang','--blame-hang-timeout','90s')
  $psi=[System.Diagnostics.ProcessStartInfo]::new('dotnet')
  foreach($a in $args){$psi.ArgumentList.Add($a)}
  $psi.WorkingDirectory=$root; $psi.UseShellExecute=$false; $psi.RedirectStandardOutput=$true; $psi.RedirectStandardError=$true
  $proc=[System.Diagnostics.Process]::new(); $proc.StartInfo=$psi
  $writer=[System.IO.StreamWriter]::new("$root/artifacts/windows.log",$false)
  $handler={ if($EventArgs.Data){ $Event.MessageData.WriteLine($EventArgs.Data); $Event.MessageData.Flush(); Write-Host $EventArgs.Data } }
  $o=Register-ObjectEvent $proc OutputDataReceived -Action $handler -MessageData $writer
  $e=Register-ObjectEvent $proc ErrorDataReceived -Action $handler -MessageData $writer
  [void]$proc.Start(); $proc.BeginOutputReadLine(); $proc.BeginErrorReadLine()
  if(-not $proc.WaitForExit($TimeoutSeconds*1000)){ try{$proc.Kill($true)}catch{}; $proc.WaitForExit(); $writer.WriteLine("TIMEOUT after $TimeoutSeconds seconds"); throw 'Windows integration test timeout.' }
  $proc.WaitForExit(); Start-Sleep -Milliseconds 300
  Unregister-Event $o.Id; Unregister-Event $e.Id; $writer.Dispose()
  if($proc.ExitCode -ne 0){throw 'Windows integration tests failed.'}
}finally{Pop-Location}
