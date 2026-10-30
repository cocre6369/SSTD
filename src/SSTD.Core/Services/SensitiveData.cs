using System.IO;
using System.Text.RegularExpressions;

namespace SSTD.Core;

public static class SensitiveData
{
    private static readonly Regex[] Patterns =
    [
        new(@"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----[\s\S]*?(?:-----END [^-]+-----|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\b(?:sk-(?:proj-)?[A-Za-z0-9_-]{16,}|gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[A-Z0-9]{16}|eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,})\b", RegexOptions.Compiled),
        new(@"\b(?:password|passwd|passcode|api[_ -]?key|secret|token|authorization|recovery code|authentication code|otp)\s*[:=]\s*(?:""[^""]*""|'[^']*'|\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bBearer\s+[A-Za-z0-9._~+/-]{12,}=*", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\b(?:\d[ -]?){13,19}\b", RegexOptions.Compiled),
    ];
    public static bool ContainsSecret(string? text) => text is not null && Patterns.Any(p => p.IsMatch(text));
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        foreach (var p in Patterns) text = p.Replace(text, "[REDACTED]");
        return text;
    }
    public static bool IsSensitivePath(string path)
    {
        var p = path.Replace('/', '\\').ToLowerInvariant();
        string[] parts = [@"\.ssh", @"\.aws", @"\.azure", @"\.gnupg", @"\.password", @"\appdata\local\packages\microsoft.windows.credentials",
            @"\windows\system32\config", @"\windows\ntds", @"\login data", @"\web data", @"\cookies", @"\ntuser.dat",
            @"\credentials", @"\passwords", @"\private keys", @"\1password", @"\bitwarden", @"\keepass", @"\lastpass", @"\vault"];
        if (parts.Any(p.Contains)) return true;
        var file = Path.GetFileName(p);
        return file is ".env" or ".netrc" or ".git-credentials" or "id_rsa" or "id_ed25519" || file.StartsWith(".env.") ||
            file.Contains("password") || file.Contains("credential") || Path.GetExtension(p) is ".kdbx" or ".pfx" or ".pem" or ".key" or ".ppk";
    }
    public static string SafeError(Exception ex) => Redact(ex.Message)[..Math.Min(600, Redact(ex.Message).Length)];
}
