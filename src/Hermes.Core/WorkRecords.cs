namespace Hermes.Core;

public sealed partial class DeskController
{
    public string WorkRecordDirectory(TaskProcess process) =>
        Path.Combine(DataDirectory, "work", Guid.ParseExact(process.Id, "N").ToString("N"));

    private void SaveCheckpoint(TaskProcess process)
    {
        Save();
        if (!process.RequirePlan) return;

        var directory = WorkRecordDirectory(process);
        try
        {
            Directory.CreateDirectory(directory);
            var plan = process.Requirements.Select(r => $"[{(r.Planned ? "계획됨" : "계획 대기")}] {r.Id}\n{r.Text}")
                .Concat(process.Items.Select(i => $"{i.Id} · {i.Title}\n완료 조건: {i.Acceptance}" +
                    (i.DependsOn.Count == 0 ? "" : "\n선행 항목: " + string.Join(", ", i.DependsOn))));
            var history = new[] { $"상태: {process.State}", $"다음 항목: {WorkQueue.Next(process)?.Title ?? "없음"}" }
                .Concat(process.Items.Select(i =>
                    $"[{(i.State == "verified" ? "x" : " ")}] {i.Id} · {i.Title} ({i.State})" +
                    (i.Verification.Length == 0 ? "" : "\n확인: " + i.Verification)));
            WriteRecord(directory, "작업계획.txt", plan);
            WriteRecord(directory, "작업내역.txt", history);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write(ex, "work-checkpoint-export");
        }
    }

    private static void WriteRecord(string directory, string name, IEnumerable<string> lines)
    {
        var path = Path.Combine(directory, name);
        var pending = path + "." + Guid.NewGuid().ToString("N") + ".pending";
        File.WriteAllText(pending, Safety.Redact(string.Join("\n\n", lines) + "\n"));
        File.Move(pending, path, overwrite: true);
    }
}
