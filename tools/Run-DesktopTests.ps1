param([string]$Repository = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$result=Join-Path $Repository 'artifacts/desktop-test-exit.txt'
$log=Join-Path $Repository 'artifacts/desktop-task.log'
Remove-Item $result,$log -ErrorAction SilentlyContinue
$user='sstdci'
$password='Sstd!'+[guid]::NewGuid().ToString('N')+'Aa1'
$secure=ConvertTo-SecureString $password -AsPlainText -Force
if(Get-LocalUser -Name $user -ErrorAction SilentlyContinue){Remove-LocalUser -Name $user}
New-LocalUser -Name $user -Password $secure -AccountNeverExpires -PasswordNeverExpires:$false -ErrorAction Stop | Out-Null
# The CI runner is elevated; create a normal local standard user so the product's elevation refusal is exercised honestly.
$command="Set-Location '$Repository'; try { ./tools/Windows-Integration.ps1 *> '$log'; `$code=`$LASTEXITCODE; if(-not `$code){`$code=0} } catch { `$_ | Out-String | Add-Content '$log'; `$code=1 }; Set-Content '$result' `$code; exit `$code"
$pwsh=(Get-Command pwsh).Source
$cmdline='"'+$pwsh+'" -NoProfile -ExecutionPolicy Bypass -Command "'+$command.Replace('"','\"')+'"'
Add-Type -Namespace CiNative -Name Process -MemberDefinition @'
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct STARTUPINFO { public int cb; public string desktop; public string title; public int x,y,xSize,ySize,xCountChars,yCountChars,fillAttribute,flags; public short showWindow,cbReserved2; public System.IntPtr cbReserved2Ptr,hStdInput,hStdOutput,hStdError; }
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct PROCESS_INFORMATION { public System.IntPtr process,thread; public int processId,threadId; }
[System.Runtime.InteropServices.DllImport("advapi32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)]
public static extern bool CreateProcessWithLogonW(string user,string domain,string password,int flags,string application,string command,int creation,string env,string dir,ref STARTUPINFO si,out PROCESS_INFORMATION pi);
[System.Runtime.InteropServices.DllImport("kernel32.dll")] public static extern uint WaitForSingleObject(System.IntPtr h,uint ms);
[System.Runtime.InteropServices.DllImport("kernel32.dll")] public static extern bool GetExitCodeProcess(System.IntPtr h,out uint code);
[System.Runtime.InteropServices.DllImport("kernel32.dll")] public static extern bool CloseHandle(System.IntPtr h);
'@
$si=[CiNative.Process+STARTUPINFO]::new();$si.cb=[System.Runtime.InteropServices.Marshal]::SizeOf($si);$si.desktop='WinSta0\Default'
$pi=[CiNative.Process+PROCESS_INFORMATION]::new()
# LOGON_WITH_PROFILE=1, CREATE_UNICODE_ENVIRONMENT=0x400
$ok=[CiNative.Process]::CreateProcessWithLogonW($user,'.',$password,1,$null,$cmdline,0x400,$null,$Repository,[ref]$si,[ref]$pi)
if(-not $ok){throw "CreateProcessWithLogonW failed: $([System.Runtime.InteropServices.Marshal]::GetLastWin32Error())"}
try{
  [void][CiNative.Process]::WaitForSingleObject($pi.process,240000)
  [uint32]$code=0;[void][CiNative.Process]::GetExitCodeProcess($pi.process,[ref]$code)
  if(Test-Path $log){Get-Content $log | Write-Host}
  if($code -ne 0){throw "Desktop verification failed with exit code $code."}
}finally{
  [void][CiNative.Process]::CloseHandle($pi.thread);[void][CiNative.Process]::CloseHandle($pi.process)
  Remove-LocalUser -Name $user -ErrorAction SilentlyContinue
}
