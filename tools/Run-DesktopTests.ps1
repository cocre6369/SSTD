param([string]$Repository = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$result=Join-Path $Repository 'artifacts/desktop-test-exit.txt'
$log=Join-Path $Repository 'artifacts/desktop-task.log'
Remove-Item $result,$log -ErrorAction SilentlyContinue
$user='sstdci'
$password='Sstd!'+[guid]::NewGuid().ToString('N')+'Aa1'
$secure=ConvertTo-SecureString $password -AsPlainText -Force
if(Get-LocalUser -Name $user -ErrorAction SilentlyContinue){Remove-LocalUser -Name $user}
New-LocalUser -Name $user -Password $secure -AccountNeverExpires -PasswordNeverExpires:$false | Out-Null
$rule=New-Object System.Security.AccessControl.FileSystemAccessRule($user,'Modify','ContainerInherit,ObjectInherit','None','Allow')
$acl=Get-Acl (Join-Path $Repository 'artifacts');$acl.AddAccessRule($rule);Set-Acl (Join-Path $Repository 'artifacts') $acl
$repoAcl=Get-Acl $Repository;$repoAcl.AddAccessRule($rule);Set-Acl $Repository $repoAcl
$inner=Join-Path $Repository 'artifacts/run-desktop-inner.ps1'
@"
Set-Location '$Repository'
'STARTED' | Set-Content '$log'
try { ./tools/Windows-Integration.ps1 *>> '$log'; `$code=`$LASTEXITCODE; if(-not `$code){`$code=0} } catch { `$_ | Out-String | Add-Content '$log'; `$code=1 }
Set-Content '$result' `$code
exit `$code
"@ | Set-Content $inner
$pwsh=(Get-Command pwsh).Source
$cmdline='-NoProfile -ExecutionPolicy Bypass -File "'+$inner+'"'
Add-Type -Namespace CiLogon -Name Native -MemberDefinition @'
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct STARTUPINFO { public int cb; public string desktop; public string title; public int x,y,xSize,ySize,xCountChars,yCountChars,fillAttribute,flags; public short showWindow,cbReserved2; public System.IntPtr cbReserved2Ptr,hStdInput,hStdOutput,hStdError; }
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct PROCESS_INFORMATION { public System.IntPtr process,thread; public int processId,threadId; }
[System.Runtime.InteropServices.DllImport("advapi32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)]
public static extern bool CreateProcessWithLogonW(string user,string domain,string password,uint flags,string application,string command,uint creation,System.IntPtr env,string dir,ref STARTUPINFO si,out PROCESS_INFORMATION pi);
[System.Runtime.InteropServices.DllImport("kernel32.dll")] public static extern uint WaitForSingleObject(System.IntPtr h,uint ms);
[System.Runtime.InteropServices.DllImport("kernel32.dll")] public static extern bool GetExitCodeProcess(System.IntPtr h,out uint code);
[System.Runtime.InteropServices.DllImport("kernel32.dll")] public static extern bool CloseHandle(System.IntPtr h);
[System.Runtime.InteropServices.DllImport("kernel32.dll")] public static extern bool TerminateProcess(System.IntPtr h,uint code);
'@
$si=[CiLogon.Native+STARTUPINFO]::new();$si.cb=[Runtime.InteropServices.Marshal]::SizeOf($si);$si.desktop='WinSta0\Default'
$pi=[CiLogon.Native+PROCESS_INFORMATION]::new()
try{
  if(-not [CiLogon.Native]::CreateProcessWithLogonW($user,'.',$password,1,$pwsh,$cmdline,0x400,[IntPtr]::Zero,$Repository,[ref]$si,[ref]$pi)){throw "CreateProcessWithLogonW failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"}
  try{
    [void][CiLogon.Native]::WaitForSingleObject($pi.process,150000)
    [uint32]$code=0;[void][CiLogon.Native]::GetExitCodeProcess($pi.process,[ref]$code)
    if(Test-Path $log){Get-Content $log | Write-Host}
    if($code -eq 259){[void][CiLogon.Native]::TerminateProcess($pi.process,259);throw 'Desktop verification timed out.'}
    if($code -ne 0){throw "Desktop verification failed with exit code $code."}
  }finally{[void][CiLogon.Native]::CloseHandle($pi.thread);[void][CiLogon.Native]::CloseHandle($pi.process)}
}catch{$_ | Out-String | Set-Content (Join-Path $Repository 'artifacts/launcher.log');throw}
finally{Remove-LocalUser -Name $user -ErrorAction SilentlyContinue}
