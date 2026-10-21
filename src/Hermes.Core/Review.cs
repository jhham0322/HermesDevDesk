using System.Text;

namespace Hermes.Core;
public sealed record ChangedFile(string Path, string Status, string? PreviousPath = null, string? SavedDiff = null)
{
    public override string ToString() => $"{Status}  {Path}" + (PreviousPath == null ? "" : $" ← {PreviousPath}");
}
public static class Review
{
    public static async Task<List<ChangedFile>> ChangesAsync(string root)
    {
        await GitRunner.RepositoryRoot(root);
        var status = await GitRunner.Run(root, "status", "--porcelain=v1", "-z", "--untracked-files=all");
        if (status.Exit != 0) throw new HermesException(status.Error);
        return ParseStatus(root, status.Output);
    }
    public static List<ChangedFile> ParseStatus(string root, string output)
    {
        var records = output.Split('\0', StringSplitOptions.RemoveEmptyEntries); var result = new List<ChangedFile>();
        for (int i = 0; i < records.Length; i++)
        {
            if (records[i].Length < 4) throw new HermesException("Git 변경 목록 형식을 확인할 수 없습니다.");
            var code = records[i][..2]; var path = records[i][3..]; string? previous = null;
            if (code.Contains('R') || code.Contains('C')) { if (++i >= records.Length) throw new HermesException("불완전한 이름 변경 목록입니다."); previous = records[i]; }
            try { Safety.Resolve(root, path); if (previous != null) Safety.Resolve(root, previous); }
            catch (IOException) { continue; }
            result.Add(new(path, code, previous)); if (result.Count >= 1000) break;
        }
        return result;
    }
    public static async Task<string> FileDiffAsync(string root, ChangedFile selected)
    {
        Safety.Resolve(root, selected.Path); if (selected.PreviousPath != null) Safety.Resolve(root, selected.PreviousPath);
        var current = (await ChangesAsync(root)).FirstOrDefault(f => f.Path == selected.Path);
        if (current == null) return "변경 상태가 바뀌었습니다. 목록을 새로고침하세요.";
        if (current.Status == "??") { try { return "+ 새 파일\n" + WorkspaceFiles.Read(root, current.Path); } catch (Exception ex) when (ex is IOException or DecoderFallbackException) { return "미리 보기 제한: " + ex.Message; } }
        var paths = current.PreviousPath == null ? new[] { current.Path } : new[] { current.Path, current.PreviousPath };
        var args = new[] { "diff", "--no-ext-diff", "--no-textconv", "HEAD", "--" }.Concat(paths).ToArray();
        var diff = await GitRunner.Run(root, args);
        if (diff.Exit != 0)
        {
            var staged = await GitRunner.Run(root, new[] { "diff", "--cached", "--no-ext-diff", "--no-textconv", "--" }.Concat(paths).ToArray());
            var working = await GitRunner.Run(root, new[] { "diff", "--no-ext-diff", "--no-textconv", "--" }.Concat(paths).ToArray());
            if (staged.Exit != 0 || working.Exit != 0) throw new HermesException(staged.Error + working.Error);
            return Safety.Redact(staged.Output + working.Output);
        }
        return Safety.Redact(diff.Output);
    }
    public static string Detail(AppState state, WorkItem task)
    {
        var project = state.Projects.Single(p => p.Id == task.ProjectId);
        return $"작업: {task.Title}\n상태: {task.Status}\n프로젝트: {project.Name}\n실행 폴더: {project.Root}\n쓰기: {(task.ThreadWritable == true ? "프로젝트 내부 허용" : "읽기 전용")}\n마지막 변경: {task.Updated.ToLocalTime():g}\n이력 항목: {task.Transcript.Count}\n변경파일: {task.ChangedFiles.Count}\nWorktree branch: {project.WorktreeBranch ?? "일반 프로젝트"}\n기준 commit: {project.BaseCommit ?? "—"}\n작업 ID: {task.Id}\n공식 thread ID: {task.ThreadId ?? "아직 없음"}";
    }
}
