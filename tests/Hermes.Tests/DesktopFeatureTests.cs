using System.Text.Json;
using Hermes.Core;

internal static class DesktopFeatureTests
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var task = new WorkItem(); ChatMessages.Delta(task, "one", "Hello "); ChatMessages.Delta(task, "two", "Second"); ChatMessages.Delta(task, "one", "world");
        check(task.Transcript.Count == 2 && task.Transcript[0].Text == "Hello world" && task.Transcript[1].Text == "Second", "interleaved message items stay separate");
        ChatMessages.Complete(task, "one", "Hello world"); ChatMessages.Complete(task, "one", "Hello world");
        check(task.Transcript.Count == 2 && task.Transcript[0].Kind == "에이전트", "stream completion and duplicate completion replace one message");
        var state = new AppState(); state.Tasks.Add(task); ChatMessages.Rename(state, task, "Renamed"); task.Pinned = true; state.Desktop.DarkTheme = true; state.Desktop.Drafts[task.Id] = "draft text";
        var store = new StateStore(Path.Combine(root, "desktop-preferences")); store.Save(state); var loaded = store.Load();
        check(loaded.Tasks.Single().Pinned && loaded.Tasks.Single().Title == "Renamed" && loaded.Desktop.DarkTheme && loaded.Desktop.Drafts[task.Id] == "draft text", "pinned chats, rename, theme and drafts persist together");
        try { ChatMessages.Rename(state, task, " "); check(false, "blank rename rejected"); } catch (IOException) { check(task.Title == "Renamed", "blank rename preserves original"); }
        try { BrowserTools.ValidateUrl("file:///C:/Windows/win.ini"); check(false, "file browser URL blocked"); } catch (IOException) { check(true, "file browser URL blocked"); }
        try { BrowserTools.ValidateUrl("https://user:secret@example.invalid"); check(false, "credential browser URL blocked"); } catch (IOException) { check(true, "credential browser URL blocked"); }
        using var selector = JsonDocument.Parse("{\"selector\":\"input[name=\\\"q\\\"]\"}");
        check(JsonSerializer.Deserialize<string>(BrowserTools.Selector(selector.RootElement)) == "input[name=\"q\"]", "browser selector encoded as JavaScript data");
        var git = Path.Combine(root, "desktop-git"); Directory.CreateDirectory(git); File.WriteAllText(Path.Combine(git, "sample.txt"), "initial\n");
        await GitRunner.Run(git, "init"); await GitRunner.Run(git, "config", "user.name", "Hermes Fixture"); await GitRunner.Run(git, "config", "user.email", "fixture@example.invalid");
        await GitWorkbench.Stage(git, "sample.txt", true); await GitWorkbench.Commit(git, "initial fixture");
        File.WriteAllText(Path.Combine(git, "sample.txt"), "changed\n"); await GitWorkbench.Stage(git, "sample.txt", true); await GitWorkbench.Stage(git, "sample.txt", false);
        var staged = await GitRunner.Run(git, "diff", "--cached", "--name-only");
        check(staged.Output.Trim().Length == 0 && File.ReadAllText(Path.Combine(git, "sample.txt")) == "changed\n", "unstage preserves working file content");
        await GitWorkbench.Stage(git, "sample.txt", true); await GitWorkbench.Commit(git, "changed fixture");
        var status = await GitRunner.Run(git, "status", "--porcelain"); check(status.Output.Trim().Length == 0, "selected staged file committed in isolated repository");
        File.WriteAllText(Path.Combine(git, ".env"), "SECRET_FIXTURE=1");
        try { await GitWorkbench.Stage(git, ".env", true); check(false, "secret staging blocked"); } catch (IOException) { check(true, "secret staging blocked"); }
        var bare = Path.Combine(root, "remote-fixture.git"); Directory.CreateDirectory(bare); await GitRunner.Run(bare, "init", "--bare");
        await GitWorkbench.ConfigureRemote(git, "fixture", bare); await GitWorkbench.Sync(git, "fixture", true);
        var remoteHead = await GitRunner.Run(bare, "rev-parse", "HEAD"); var localHead = await GitRunner.Run(git, "rev-parse", "HEAD");
        check(remoteHead.Output.Trim() == localHead.Output.Trim(), "local git pushes to selected bare repository server path");
        await GitWorkbench.Sync(git, "fixture", false); check(true, "local git fetches configured remote");
        try { GitWorkbench.RemoteUrl("https://user:password@example.invalid/repo.git"); check(false, "remote secret URL blocked"); } catch (IOException) { check(true, "remote credentials must use Git authentication"); }
        var queue = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        await using var desk = new DeskController(Path.Combine(root, "stream-benchmark"), a => queue.Enqueue(a));
        var project = desk.Register(git); var streaming = desk.NewTask(project, "stream benchmark"); streaming.ThreadId = "benchmark-thread"; streaming.Status = "실행 중";
        var writes = desk.Store.SaveCount; var timer = System.Diagnostics.Stopwatch.StartNew();
        using var parameters = JsonDocument.Parse("{\"threadId\":\"benchmark-thread\",\"itemId\":\"benchmark-item\",\"delta\":\"benchmark \"}");
        for (var i = 0; i < 2000; i++) desk.Handle(new("item/agentMessage/delta", parameters.RootElement));
        await Task.Delay(300); while (queue.TryDequeue(out var action)) action(); timer.Stop();
        check(desk.Store.SaveCount - writes <= 2 && streaming.Transcript.Single().Text.Length == 20000, "2000 streaming events persist in at most two snapshots without text loss");
        Console.WriteLine($"PERF: 2000 deltas; snapshot writes={desk.Store.SaveCount - writes}; elapsedMs={timer.ElapsedMilliseconds}");
    }
}
