using System.Diagnostics;
using System.Text;

namespace Hermes.Core;
public sealed record FileEntry(string Relative, bool Directory) { public override string ToString() => (Directory ? "▸ " : "  ") + Relative; }
public static class WorkspaceFiles
{
    public static List<FileEntry> List(string root, string relative = "")
    {
        var directory = Safety.Resolve(root, relative);
        return Directory.EnumerateFileSystemEntries(directory).Where(p => !Safety.IsSecret(Path.GetFileName(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0)
            .Take(500).Select(p => new FileEntry(Path.GetRelativePath(root, p), System.IO.Directory.Exists(p))).OrderByDescending(e => e.Directory).ThenBy(e => e.Relative).ToList();
    }
    public static string Read(string root, string relative)
    {
        var path = Safety.Resolve(root, relative);
        if (new FileInfo(path).Length > 1_000_000) throw new HermesException("1 MB 이하 텍스트만 미리 볼 수 있습니다.");
        var bytes = File.ReadAllBytes(path);
        if (bytes.Contains((byte)0)) throw new HermesException("바이너리 파일은 미리 보기를 지원하지 않습니다.");
        return Safety.Redact(new UTF8Encoding(false, true).GetString(bytes));
    }
    private static async Task<(int Exit, string Output)> Git(string root, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = Safety.ProjectRoot(root), RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "--no-pager", "-c", "core.fsmonitor=false", "-c", "core.hooksPath=NUL" }.Concat(args)) psi.ArgumentList.Add(a);
        psi.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        using var p = Process.Start(psi) ?? throw new HermesException("Git이 필요합니다.");
        var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
        try { await p.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
        catch { p.Kill(true); throw; }
        return (p.ExitCode, await output + await error);
    }
    public static async Task<string> DiffAsync(string root)
    {
        var top = await Git(root, "rev-parse", "--show-toplevel");
        if (top.Exit != 0) return "Git 저장소가 아닙니다. 엔진 변경은 ‘작업 diff’에서 확인할 수 있습니다.";
        if (!string.Equals(Path.GetFullPath(top.Output.Trim()), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) return "상위 프로젝트 Git 정보 노출 방지: 저장소 루트 자체를 등록하세요.";
        var status = await Git(root, "status", "--porcelain=v1", "-z", "--untracked-files=normal");
        if (status.Exit != 0) throw new HermesException(Safety.Redact(status.Output));
        var result = new StringBuilder();
        var records = status.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < records.Length && result.Length < 1_000_000; i++)
        {
            var entry = records[i]; if (entry.Length < 4) continue;
            var code = entry[..2]; var rel = entry[3..];
            if (code.Contains('R') || code.Contains('C')) { i++; result.AppendLine("이름 변경/복사 검토는 Git에서 확인하세요 (내용 읽기 생략)."); continue; }
            try { Safety.Resolve(root, rel); } catch (IOException) { result.AppendLine("[비밀/경계 규칙으로 변경 내용 제외]"); continue; }
            result.AppendLine($"=== {code} {rel} ===");
            if (code == "??")
            {
                if (File.Exists(Path.Combine(root, rel))) { try { result.AppendLine(Read(root, rel)); } catch (Exception ex) when (ex is IOException or DecoderFallbackException) { result.AppendLine(ex.Message); } }
            }
            else
            {
                var diff = await Git(root, "diff", "--no-ext-diff", "--no-textconv", "HEAD", "--", rel);
                if (diff.Exit != 0) // unborn repository: show staged and working changes separately.
                { var staged = await Git(root, "diff", "--cached", "--no-ext-diff", "--no-textconv", "--", rel); var working = await Git(root, "diff", "--no-ext-diff", "--no-textconv", "--", rel); result.AppendLine(staged.Output + working.Output); }
                else result.AppendLine(diff.Output);
            }
        }
        return Safety.Redact(result.Length == 0 ? "변경 없음" : result.ToString()[..Math.Min(result.Length, 1_000_000)]);
    }
}
