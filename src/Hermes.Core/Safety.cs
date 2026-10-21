using System.Text.RegularExpressions;

namespace Hermes.Core;

public static partial class Safety
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> KnownSecrets = new(StringComparer.Ordinal);
    public static void RegisterSecret(string secret) { if (secret.Length >= 8) KnownSecrets.TryAdd(secret, 0); }
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    { ".git", ".codex", ".agents", ".aws", ".ssh", ".azure", "node_modules", "bin", "obj", "AppData", "credentials", "secrets", ".env", "auth.json", "credentials.json", "id_rsa", "id_ed25519", ".hermes-worktree.json", ".hermes-worktree-storage" };
    public static bool IsSecret(string name) => Excluded.Contains(name) || name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)
        || new[] { ".pem", ".key", ".pfx", ".p12", ".keystore", ".dpapi" }.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
    public static string ProjectRoot(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) throw new HermesException("로컬 절대 프로젝트 경로가 필요합니다.");
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (!Directory.Exists(full) || string.Equals(full, Path.GetPathRoot(full)?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new HermesException("드라이브 루트는 프로젝트로 등록할 수 없습니다.");
        RejectReparseAncestors(full);
        if (full.Split(Path.DirectorySeparatorChar).Any(IsSecret)) throw new HermesException("민감한 시스템/인증 폴더는 등록할 수 없습니다.");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.Equals(full, home, StringComparison.OrdinalIgnoreCase) || full.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\", StringComparison.OrdinalIgnoreCase)) throw new HermesException("홈/Windows 폴더는 등록할 수 없습니다.");
        return full;
    }
    public static bool Within(string root, string path) => string.Equals(Path.GetFullPath(root), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)
        || Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static string Resolve(string root, string relative)
    {
        root = ProjectRoot(root);
        if (Path.IsPathRooted(relative) || relative.Contains(':')) throw new HermesException("상대 파일 경로만 사용할 수 있습니다.");
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!Within(root, path) || relative.Split('\\', '/').Any(IsSecret)) throw new HermesException("프로젝트 경계 또는 비밀 제외 규칙 위반입니다.");
        RejectReparseAncestors(path);
        return path;
    }
    public static void RejectReparseAncestors(string path)
    {
        for (var p = path; !string.IsNullOrEmpty(p); p = Path.GetDirectoryName(p))
            if ((Directory.Exists(p) || File.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0) throw new HermesException("링크/접합점 경로는 허용하지 않습니다.");
    }
    public static string Redact(string value)
    {
        foreach (var key in KnownSecrets.Keys) value = value.Replace(key, "[REDACTED]", StringComparison.Ordinal);
        value = TokenPattern().Replace(value, "[REDACTED]");
        value = SecretAssignment().Replace(value, "$1[REDACTED]");
        return PemPattern().Replace(value, "[REDACTED PRIVATE KEY]");
    }
    [GeneratedRegex(@"(?i)\b(?:sk-[a-z0-9_-]{12,}|gh[pousr]_[a-z0-9]{15,}|eyJ[a-z0-9_-]+\.[a-z0-9_-]+\.[a-z0-9_-]+)\b")] private static partial Regex TokenPattern();
    [GeneratedRegex(@"(?im)((?:api[_-]?key|access[_-]?token|refresh[_-]?token|password|authorization)\s*[:=]\s*|Bearer\s+)[^\s,;""}]+")] private static partial Regex SecretAssignment();
    [GeneratedRegex(@"-----BEGIN [^-]*PRIVATE KEY-----[\s\S]*?-----END [^-]*PRIVATE KEY-----")] private static partial Regex PemPattern();
}
