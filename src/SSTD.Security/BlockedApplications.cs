using System.IO;
using System.Text.RegularExpressions;

namespace SSTD.Security;

public static class ApplicationPolicy
{
    private static readonly string[] Names = """
    cmd.exe powershell.exe pwsh.exe powershell_ise.exe windowsterminal.exe wt.exe openconsole.exe conhost.exe
    bash.exe wsl.exe wslhost.exe git-bash.exe mintty.exe putty.exe telnet.exe
    regedit.exe regedt32.exe reg.exe mmc.exe devmgmt.msc diskmgmt.msc compmgmt.msc services.msc
    gpedit.msc secpol.msc taskschd.msc fsmgmt.msc lusrmgr.msc certlm.msc certmgr.msc
    taskmgr.exe msconfig.exe msinfo32.exe control.exe systemsettings.exe computerdefaults.exe
    netplwiz.exe useraccountcontrolsettings.exe wf.msc firewall.cpl inetcpl.cpl sysdm.cpl appwiz.cpl
    diskpart.exe format.com diskmgmt.exe vssadmin.exe bcdedit.exe bootrec.exe recdisc.exe rstrui.exe
    sethc.exe utilman.exe runas.exe runlegacycplelevated.exe sdclt.exe recoverydrive.exe resetengine.exe
    credentialui.exe credentialenrollmentmanager.exe vaultcmd.exe cmdkey.exe keymgr.dll
    keepass.exe keepassxc.exe 1password.exe bitwarden.exe lastpass.exe dashlane.exe nordpass.exe
    procexp.exe procexp64.exe processhacker.exe autoruns.exe autorunsc.exe psexec.exe psexec64.exe
    wscript.exe cscript.exe mshta.exe wmic.exe schtasks.exe sc.exe net.exe net1.exe
    python.exe pythonw.exe py.exe node.exe deno.exe bun.exe ruby.exe perl.exe lua.exe
    """.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    private static readonly Regex Description = new(@"terminal|command.?prompt|powershell|registry editor|management console|device manager|disk management|group policy|security policy|task scheduler|credential manager|password manager|process explorer|process hacker|system configuration|remote desktop|virtual machine|powershell ise", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Title = new(@"^(administrator\s*[:：]|administrator\s*[-–]|command prompt|windows powershell|powershell\s*[-–]|windows terminal|registry editor|task manager|services|computer management|device manager|disk management|group policy|local security policy|task scheduler|credential manager|system configuration|user account control)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    public static bool IsBlockedPackage(string appId, string name) =>
        System.Text.RegularExpressions.Regex.IsMatch(appId + " " + name, @"WindowsTerminal|PowerShell|Windows.Console|DevHome|Sysinternals|AdminCenter|Registry|Terminal|RemoteDesktop|Windows.Settings|immersivecontrolpanel|SecHealthUI|Credential|Password|Bitwarden|KeePass|1Password|LastPass", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    public static bool IsBlockedName(string name)
    {
        var file = Normalize(name);
        if (!Path.HasExtension(file)) file += ".exe";
        var compact = new string(file.Where(char.IsLetterOrDigit).ToArray());
        string[] aliases = ["cmd.exe", "commandprompt.exe", "windowsterminal.exe", "powershellise.exe", "registryeditor.exe", "devicemanager.exe", "diskmanagement.exe", "taskmanager.exe", "groupolicyeditor.exe", "localsecuritypolicy.exe", "computermanagement.exe", "services.exe", "credentialmanager.exe", "systemsettings.exe", "windowssecurity.exe"];
        return Names.Contains(file) || aliases.Contains(compact);
    }
    public static bool IsBlocked(string path, string processName = "", string title = "", string className = "")
    {
        path = path.Replace('/', '\\');
        var file = Path.GetFileName(path);
        if (IsBlockedName(file) || IsBlockedName(processName) || Description.IsMatch(processName) || Title.IsMatch(title)) return true;
        if (file.Equals("wt.exe", StringComparison.OrdinalIgnoreCase) || file.Equals("windowsterminal.exe", StringComparison.OrdinalIgnoreCase) || file.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase)) return true;
        if (className is "ConsoleWindowClass" or "CASCADIA_HOSTING_WINDOW_CLASS" or "PseudoConsoleWindow") return true;
        if (path.Contains(@"WindowsApps\Microsoft.WindowsTerminal", StringComparison.OrdinalIgnoreCase) ||
            path.Contains(@"WindowsApps\Microsoft.PowerShell", StringComparison.OrdinalIgnoreCase) ||
            path.Contains(@"WindowsApps\Microsoft.Windows.Console", StringComparison.OrdinalIgnoreCase)) return true;
        // An allow-list for the few genuinely ordinary inbox apps; other Windows tools fail closed.
        string[] inboxExceptions = [@"\windows\explorer.exe", @"\windows\notepad.exe", @"\windows\system32\notepad.exe", @"\windows\system32\mspaint.exe", @"\windows\winhlp32.exe"];
        if (ApplicationPolicy.IsScriptOrInstaller(file) && path.Contains(@"\windows\", StringComparison.OrdinalIgnoreCase)) return true;
        if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            (path.Contains(@"\windows\system32\", StringComparison.OrdinalIgnoreCase) || path.Contains(@"\windows\syswow64\", StringComparison.OrdinalIgnoreCase)) &&
            !inboxExceptions.Any(x => path.EndsWith(x, StringComparison.OrdinalIgnoreCase))) return true;
        if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && path.Contains(@"\windows\", StringComparison.OrdinalIgnoreCase) &&
            !inboxExceptions.Any(x => path.EndsWith(x, StringComparison.OrdinalIgnoreCase))) return true;
        return false;
    }
    public static string Normalize(string value) => Path.GetFileName(value.Replace('/', '\\').Trim().Trim('"')).ToLowerInvariant();
    public static bool IsScriptOrInstaller(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".bat" or ".cmd" or ".ps1" or ".psm1" or ".vbs" or ".vbe" or ".js" or ".jse" or ".wsf" or ".wsh" or
        ".scr" or ".com" or ".msi" or ".msp" or ".msc" or ".cpl" or ".reg" or ".lnk" or ".url" or ".hta" or ".jar" or ".appref-ms";
    public static bool IsDangerousControl(string text) => Regex.IsMatch(text,
        @"run as administrator|run elevated|open (?:a )?(?:terminal|command prompt|powershell)|new terminal|terminal\s*[:>]|developer tools|devtools|javascript console|visual basic|macros?|empty recycle|format (?:drive|disk)|delete (?:file|folder|volume|partition)|disable (?:antivirus|firewall|defender|security)|reset (?:this pc|computer)|modify (?:registry|permissions)|manage passwords|saved passwords|payment methods|saved cards",
        RegexOptions.IgnoreCase);
    public static bool IsDestructiveControl(string text) => Regex.IsMatch(text,
        @"^(?:delete|erase|format|uninstall|remove account|empty recycle bin|wipe|reset this pc|factory reset|rename|move to)(?:\b|$)|shift\s*\+\s*delete|permanently delete|overwrite|replace existing|delete all|delete folder|delete file",
        RegexOptions.IgnoreCase);
    public static bool IsCommitControl(string text) => Regex.IsMatch(text,
        @"^(?:send|reply all|reply|post|publish|submit|upload|share|buy|purchase|pay|checkout|place order|confirm order|confirm purchase|transfer|subscribe|install|download|save as|save a copy|save|create account|delete account)(?:\b|\s|$)|(?:send message|send email|post comment|post reply|buy now|add to cart|pay now|place your order|complete purchase|confirm payment|upload file|share file)", RegexOptions.IgnoreCase);
}
