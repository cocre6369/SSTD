param([string]$Repository = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$result=Join-Path $Repository 'artifacts/desktop-test-exit.txt'
$log=Join-Path $Repository 'artifacts/desktop-task.log'
Remove-Item $result,$log -ErrorAction SilentlyContinue
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
Add-Type -Namespace CiToken -Name Native -MemberDefinition @'
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct STARTUPINFO { public int cb; public string desktop; public string title; public int x,y,xSize,ySize,xCountChars,yCountChars,fillAttribute,flags; public short showWindow,cbReserved2; public System.IntPtr cbReserved2Ptr,hStdInput,hStdOutput,hStdError; }
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct PROCESS_INFORMATION { public System.IntPtr process,thread; public int processId,threadId; }
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct TOKEN_MANDATORY_LABEL { public System.IntPtr Label; public uint Attributes; }
[System.Runtime.InteropServices.DllImport("advapi32.dll",SetLastError=true)] public static extern bool OpenProcessToken(System.IntPtr p,uint access,out System.IntPtr token);
[System.Runtime.InteropServices.DllImport("advapi32.dll",SetLastError=true)] public static extern bool CreateRestrictedToken(System.IntPtr existing,uint flags,uint disableCount,System.IntPtr disable,uint deleteCount,System.IntPtr delete,uint restrictCount,System.IntPtr restrict,out System.IntPtr restricted);
[System.Runtime.InteropServices.DllImport("advapi32.dll",SetLastError=true)] public static extern bool SetTokenInformation(System.IntPtr token,int cls,ref TOKEN_MANDATORY_LABEL info,uint length);
[System.Runtime.InteropServices.DllImport("advapi32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)] public static extern bool CreateProcessWithTokenW(System.IntPtr token,uint flags,string application,string command,uint creation,System.IntPtr env,string dir,ref STARTUPINFO si,out PROCESS_INFORMATION pi);
[System.Runtime.InteropServices.DllImport("kernel32.dll")] public static extern System.IntPtr GetCurrentProcess();
[System.Runtime.InteropServices.DllImport("kernel32.dll")] public static extern uint WaitForSingleObject(System.IntPtr h,uint ms);
[System.Runtime.InteropServices.DllImport("kernel32.dll")] public static extern bool GetExitCodeProcess(System.IntPtr h,out uint code);
[System.Runtime.InteropServices.DllImport("kernel32.dll")] public static extern bool CloseHandle(System.IntPtr h);
'@
$token=[IntPtr]::Zero;$restricted=[IntPtr]::Zero;$sid=[IntPtr]::Zero
try {
  if(-not [CiToken.Native]::OpenProcessToken([CiToken.Native]::GetCurrentProcess(),0x000F01FF,[ref]$token)){throw 'OpenProcessToken failed.'}
  # LUA_TOKEN removes the admin SID; then explicitly set medium integrity.
  if(-not [CiToken.Native]::CreateRestrictedToken($token,4,0,[IntPtr]::Zero,0,[IntPtr]::Zero,0,[IntPtr]::Zero,[ref]$restricted)){throw 'CreateRestrictedToken failed.'}
  $bytes=New-Object byte[] 12; $bytes[0]=1;$bytes[1]=1;$bytes[7]=16;[BitConverter]::GetBytes([uint32]0x2000).CopyTo($bytes,8)
  $sid=[Runtime.InteropServices.Marshal]::AllocHGlobal(12);[Runtime.InteropServices.Marshal]::Copy($bytes,0,$sid,12)
  $label=[CiToken.Native+TOKEN_MANDATORY_LABEL]::new();$label.Label=$sid;$label.Attributes=0x20
  if(-not [CiToken.Native]::SetTokenInformation($restricted,25,[ref]$label,[uint32][Runtime.InteropServices.Marshal]::SizeOf($label))){throw 'SetTokenInformation integrity failed.'}
  $si=[CiToken.Native+STARTUPINFO]::new();$si.cb=[Runtime.InteropServices.Marshal]::SizeOf($si);$si.desktop='WinSta0\Default'
  $pi=[CiToken.Native+PROCESS_INFORMATION]::new()
  if(-not [CiToken.Native]::CreateProcessWithTokenW($restricted,1,$pwsh,$cmdline,0x400,[IntPtr]::Zero,$Repository,[ref]$si,[ref]$pi)){throw "CreateProcessWithTokenW failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())"}
  try{
    [void][CiToken.Native]::WaitForSingleObject($pi.process,240000)
    [uint32]$code=0;[void][CiToken.Native]::GetExitCodeProcess($pi.process,[ref]$code)
    if(Test-Path $log){Get-Content $log | Write-Host}
    if($code -ne 0){throw "Desktop verification failed with exit code $code."}
  }finally{[void][CiToken.Native]::CloseHandle($pi.thread);[void][CiToken.Native]::CloseHandle($pi.process)}
} catch {
  $_ | Out-String | Set-Content (Join-Path $Repository 'artifacts/windows.log')
  throw
} finally {
  if($sid -ne [IntPtr]::Zero){[Runtime.InteropServices.Marshal]::FreeHGlobal($sid)}
  if($restricted -ne [IntPtr]::Zero){[void][CiToken.Native]::CloseHandle($restricted)}
  if($token -ne [IntPtr]::Zero){[void][CiToken.Native]::CloseHandle($token)}
}
