using Hermes.Core;
internal static class ChatGitTests
{
    internal static async Task Run(string directory, Action<bool, string> check)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        var root = Path.Combine(directory, "repo"); Directory.CreateDirectory(root);
        await GitWorkbench.Initialize(root); await GitWorkbench.SetAuthor(root, "Hermes Fixture", "fixture@example.invalid");
        File.WriteAllText(Path.Combine(root, "first.txt"), "first"); File.WriteAllText(Path.Combine(root, "keep.txt"), "keep");
        await GitWorkbench.Stage(root, "keep.txt", true);
        await GitWorkbench.CommitSelected(root, "first selected commit", ["first.txt"]);
        var committed = await GitRunner.Run(root, "show", "--format=", "--name-only", "HEAD");
        check(committed.Output.Trim() == "first.txt", "first local commit includes only selected file");
        var staged = await GitRunner.Run(root, "diff", "--cached", "--name-only");
        check(staged.Output.Trim() == "keep.txt", "other staged file is preserved outside selected commit");
        File.WriteAllText(Path.Combine(root, "first.txt"), "second"); File.WriteAllText(Path.Combine(root, "new file 한글.txt"), "new");
        await GitWorkbench.CommitSelected(root, "update with new file", ["first.txt", "new file 한글.txt"]);
        check((await GitRunner.Run(root, "log", "-1", "--format=%s")).Output.Trim() == "update with new file", "commit message and UTF8 path accepted");
        File.Delete(Path.Combine(root, "first.txt")); await GitWorkbench.CommitSelected(root, "delete selected", ["first.txt"]);
        check((await GitRunner.Run(root, "ls-tree", "--name-only", "HEAD")).Output.Contains("first.txt") == false, "selected deleted file committed");
        await GitRunner.Run(root, "mv", "new file 한글.txt", "renamed.txt");
        await GitWorkbench.CommitSelected(root, "rename selected", ["renamed.txt"]);
        check((await GitRunner.Run(root, "ls-tree", "--name-only", "HEAD")).Output.Contains("renamed.txt"), "selected staged rename commits both source and destination");
        File.WriteAllText(Path.Combine(root, ".env"), "SYNTHETIC_TEST=1");
        bool rejected = false; try { await GitWorkbench.CommitSelected(root, "blocked", [".env"]); } catch (IOException) { rejected = true; }
        check(rejected && File.Exists(Path.Combine(root, ".env")), "excluded project file rejected without deletion");
        rejected = false; try { await GitWorkbench.CommitSelected(root, "", ["keep.txt"]); } catch (IOException) { rejected = true; }
        check(rejected, "empty commit message rejected");
        var badImage = Path.Combine(root, "truncated.png"); File.WriteAllBytes(badImage, new byte[20]);
        rejected = false; try { ImageAttachments.Read(root, "truncated.png"); } catch (IOException) { rejected = true; }
        check(rejected, "truncated image fails as a recoverable image error");
        AppLog.Configure(Path.Combine(directory, "diagnostics")); const string syntheticSecret = "SYNTHETIC_LOG_SECRET_2026"; Safety.RegisterSecret(syntheticSecret);
        var logPath = AppLog.Write(new IOException("image import failed " + syntheticSecret, new InvalidOperationException("decoder fixture")), "image-import");
        var log = File.ReadAllText(logPath);
        check(log.Contains("image-import") && log.Contains("decoder fixture") && !log.Contains(syntheticSecret), "persistent exception log includes context and inner error with secret redaction");
        var geometry = new SavedWindowBounds(-1400, 50, 1000, 700);
        check(WindowGeometry.Fit(geometry, new(-1920, 0, 0, 1080)) == geometry, "window placement preserves negative coordinates on left monitor");
        var fitted = WindowGeometry.Fit(geometry, new(0, 0, 1920, 1040));
        check(fitted.Left == 0 && fitted.Top == 50, "disconnected monitor placement moves onto available work area");
        check(WindowGeometry.Fit(new(8000, 9000, 3000, 2000), new(0, 0, 1280, 720)) == new SavedWindowBounds(0, 0, 1280, 720), "oversized saved window fits new monitor");
        check(!WindowGeometry.Valid(new(int.MinValue, 0, 0, 0)), "invalid saved geometry rejected");
        var geometryState = new AppState(); geometryState.Desktop.WindowBounds = geometry; geometryState.Desktop.WindowMaximized = true;
        var geometryStore = new StateStore(Path.Combine(directory, "geometry")); geometryStore.Save(geometryState);
        check(geometryStore.Load().Desktop.WindowBounds == geometry && geometryStore.Load().Desktop.WindowMaximized, "window dimensions location and maximize state persist together");
        var data = Path.Combine(directory, "history");
        await using var desk = new DeskController(data); var project = desk.Register(root);
        var victim = desk.NewTask(project, "DELETE_FIXTURE"); victim.Transcript.Add(new("사용자", "PRIVATE_FIXTURE_TEXT", DateTime.UtcNow));
        var running = desk.NewTask(project, "CONTINUE_FIXTURE"); running.Status = "실행 중";
        desk.State.Desktop.Drafts[victim.Id] = "DELETE_DRAFT"; desk.State.Usage.Add(new() { TaskId = victim.Id, Model = "fixture", Status = "completed" }); desk.Save();
        desk.DeleteTask(victim);
        check(!desk.State.Tasks.Contains(victim) && desk.State.Tasks.Contains(running) && running.Status == "실행 중", "deleting one chat leaves another running chat untouched");
        check(!desk.State.Desktop.Drafts.ContainsKey(victim.Id) && desk.State.Usage.Any(u => u.TaskId == victim.Id), "delete removes draft and preserves usage ledger");
        check(!new StateStore(data).Load().Tasks.Any(t => t.Id == victim.Id) && !File.ReadAllText(Path.Combine(data, "state.json.backup")).Contains("PRIVATE_FIXTURE_TEXT"), "chat deletion persists and recovery backup no longer contains transcript");
        check(File.Exists(Path.Combine(root, "keep.txt")) && (await GitRunner.Run(root, "rev-parse", "HEAD")).Exit == 0, "chat deletion preserves project files and Git history");
        rejected = false; try { desk.DeleteTask(running); } catch (IOException) { rejected = true; }
        check(rejected && desk.State.Tasks.Contains(running), "active chat must stop before deletion");
    }
}
