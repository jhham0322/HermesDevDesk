using System.Diagnostics;
using System.Text;

namespace Hermes.Core;
public sealed record GitResult(int Exit, string Output, string Error);
public static class GitRunner
{
    public static string Executable { get; set; } = "git";
    private static ProcessStartInfo Info(string root, IEnumerable<string> args)
    {
        var psi = new ProcessStartInfo(Executable) { WorkingDirectory = Safety.ProjectRoot(root), RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var a in new[] { "--no-pager", "-c", "core.fsmonitor=false", "-c", "core.hooksPath=NUL", "-c", "gc.auto=0", "-c", "maintenance.auto=false" }.Concat(args)) psi.ArgumentList.Add(a);
        psi.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        foreach (var key in psi.Environment.Keys.Where(k => k.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase) && k != "GIT_OPTIONAL_LOCKS").ToArray()) psi.Environment.Remove(key);
        return psi;
    }
    public static async Task<GitResult> Run(string root, params string[] args)
    {
        using var p = Process.Start(Info(root, args)) ?? throw new HermesException("Git을 실행할 수 없습니다.");
        var output = ReadBounded(p.StandardOutput.BaseStream, 8_000_000); var error = p.StandardError.ReadToEndAsync();
        try { await Task.WhenAll(p.WaitForExitAsync(), output).WaitAsync(TimeSpan.FromSeconds(30)); }
        catch { if (!p.HasExited) p.Kill(true); throw; }
        return new(p.ExitCode, Encoding.UTF8.GetString(await output), Safety.Redact(await error));
    }
    public static async Task<byte[]> Archive(string root, string commit)
    {
        using var p = Process.Start(Info(root, ["archive", "--format=zip", commit])) ?? throw new HermesException("Git archive 실행 실패");
        var output = ReadBounded(p.StandardOutput.BaseStream, 120_000_000); var error = p.StandardError.ReadToEndAsync();
        try { await Task.WhenAll(p.WaitForExitAsync(), output).WaitAsync(TimeSpan.FromSeconds(60)); }
        catch { if (!p.HasExited) p.Kill(true); throw; }
        if (p.ExitCode != 0) throw new HermesException(Safety.Redact(await error));
        return await output;
    }
    private static async Task<byte[]> ReadBounded(Stream source, int limit)
    {
        using var buffer = new MemoryStream(); var block = new byte[65536]; int count;
        while ((count = await source.ReadAsync(block)) > 0) { if (buffer.Length + count > limit) throw new HermesException("Git 결과가 안전한 크기 제한을 넘었습니다."); await buffer.WriteAsync(block.AsMemory(0, count)); }
        return buffer.ToArray();
    }
    public static async Task<string> RepositoryRoot(string root)
    {
        var top = await Run(root, "rev-parse", "--show-toplevel");
        if (top.Exit != 0) throw new HermesException("Git 저장소가 아닙니다.");
        if (!string.Equals(Path.GetFullPath(top.Output.Trim()), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) throw new HermesException("상위 프로젝트 Git 정보 노출 방지: 저장소 루트를 등록하세요.");
        Safety.RejectReparseAncestors(Path.Combine(root, ".git")); return root;
    }
}
