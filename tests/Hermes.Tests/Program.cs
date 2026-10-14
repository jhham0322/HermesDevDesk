using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Hermes.Core;

if (args.Contains("--mcp-local-stdio"))
{
    await new ProjectMcpServer(args[Array.IndexOf(args, "--root") + 1], args.Contains("--allow-write")).RunAsync(); return;
}

if (args.Contains("app-server"))
{
    while (Console.ReadLine() is { } line)
    {
        using var d = JsonDocument.Parse(line); var e = d.RootElement;
        if (!e.TryGetProperty("method", out var m)) { Console.WriteLine(JsonSerializer.Serialize(new { method = "fixture/answered", @params = new { } })); continue; }
        if (!e.TryGetProperty("id", out var id)) continue;
        object response = m.GetString() switch
        {
            "initialize" => new { userAgent = "unit-fixture" },
            "echo" => e.GetProperty("params").Clone(),
            _ => new { ok = true }
        };
        Console.WriteLine(JsonSerializer.Serialize(new { id = id.Clone(), result = response }));
        if (m.GetString() == "emit") Console.WriteLine("{\"method\":\"test/event\",\"params\":{\"value\":123}}");
        if (m.GetString() == "approval") Console.WriteLine("{\"id\":\"server-1\",\"method\":\"item/fileChange/requestApproval\",\"params\":{\"threadId\":\"t\",\"turnId\":\"v\",\"itemId\":\"i\",\"reason\":\"fixture\"}}");
    }
    return;
}
if (args.Contains("--resume-only"))
{
    var reportPath = args[Array.IndexOf(args, "--report") + 1];
    var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(reportPath))!;
    var liveRoot = Path.Combine(node["scratch"]!.GetValue<string>(), "project");
    await using var rpc = new AppServerClient(); await rpc.ConnectAsync(node["officialExecutable"]!.GetValue<string>(), liveRoot);
    var resumed = await rpc.RequestAsync("thread/resume", new { threadId = node["threadId"]!.GetValue<string>(), cwd = liveRoot, modelProvider = "openai", sandbox = "read-only", approvalPolicy = "on-request", approvalsReviewer = "user" });
    node["resumeVerified"] = resumed.GetProperty("thread").Text("id") == node["threadId"]!.GetValue<string>();
    Console.WriteLine("RESUME: " + node["resumeVerified"]); File.WriteAllText(reportPath, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true })); return;
}
var baseDir = Path.Combine(Environment.CurrentDirectory, "artifacts", "test-scratch", "Hermes-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(baseDir);
if (args.Contains("--desktop-engine-smoke"))
{
    await DesktopEngineTests.Run(Path.Combine(baseDir, "desktop-engine"), args[Array.IndexOf(args, "--codex") + 1], args[Array.IndexOf(args, "--report") + 1]); return;
}
if (args.Contains("--mcp-smoke"))
{
    await McpTests.Run(Path.Combine(baseDir, "mcp-project"), args[Array.IndexOf(args, "--codex") + 1], args[Array.IndexOf(args, "--report") + 1]); return;
}
if (args.Contains("--native-features-smoke"))
{
    await NativeFeatureTests.Run(Path.Combine(baseDir, "native-project"), args[Array.IndexOf(args, "--codex") + 1], args[Array.IndexOf(args, "--report") + 1]); return;
}
if (args.Contains("--lifecycle-smoke"))
{
    await LifecycleTests.Run(Path.Combine(baseDir, "lifecycle-project"), args[Array.IndexOf(args, "--codex") + 1], args[Array.IndexOf(args, "--report") + 1]); return;
}
int count = 0;
void Assert(bool truth, string label) { if (!truth) throw new Exception("FAIL: " + label); count++; Console.WriteLine("PASS: " + label); }
void Reject(Action act, string label) { try { act(); } catch (IOException) { Assert(true, label); return; } throw new Exception("FAIL: " + label); }
var root = Path.Combine(baseDir, "project"); Directory.CreateDirectory(root);
File.WriteAllText(Path.Combine(root, "hello.txt"), "hello"); File.WriteAllText(Path.Combine(root, ".env"), "SECRET=value");
Assert(Safety.ProjectRoot(root) == root, "absolute project registration");
Reject(() => Safety.ProjectRoot(Path.GetPathRoot(root)!), "drive root blocked");
Reject(() => Safety.Resolve(root, "..\\other\\file"), "path traversal blocked");
Reject(() => Safety.Resolve(root, ".env"), "secret file blocked");
Reject(() => Safety.Resolve(root, "hello.txt:stream"), "alternate data stream blocked");
Reject(() => Safety.Resolve(root, root + "\\hello.txt"), "absolute file path blocked");
Assert(!Safety.Within(root, root + "-other\\file"), "prefix sibling blocked");
Assert(WorkspaceFiles.List(root).Count == 1, "secret excluded from listing");
Assert(WorkspaceFiles.Read(root, "hello.txt") == "hello", "real text read");
File.WriteAllBytes(Path.Combine(root, "binary.dat"), [1, 0, 2]); Reject(() => WorkspaceFiles.Read(root, "binary.dat"), "binary read blocked");
var token = "sk-" + new string('x', 30);
Assert(!Safety.Redact("Bearer " + token).Contains(token), "token redaction");
Assert(!Safety.Redact("password=unsafevalue").Contains("unsafevalue"), "password redaction");
var store = new StateStore(Path.Combine(baseDir, "state")); var state = new AppState(); state.Tasks.Add(new() { Status = "실행 중", Transcript = [new("log", token + "\napi_key=unsafevalue", DateTime.UtcNow)] }); store.Save(state);
var loaded = store.Load(); Assert(loaded.Tasks[0].Status.Contains("연결 끊김"), "crash recovery status"); Assert(!loaded.Tasks[0].Transcript[0].Text.Contains(token), "history redacted and valid JSON");
store.Save(loaded); Assert(File.Exists(Path.Combine(baseDir, "state", "state.json.backup")), "atomic history backup");
using var fileJson = JsonDocument.Parse("{\"threadId\":\"t\",\"turnId\":\"v\",\"grantRoot\":\"C:\\\\outside\"}");
var request = new ServerRequest(JsonSerializer.SerializeToElement(1), "item/fileChange/requestApproval", fileJson.RootElement.Clone());
Assert(ApprovalPolicy.BlockReason(request, root, "v", true) != null, "external grantRoot blocked");
Assert(ApprovalPolicy.BlockReason(request, root, "stale", true) != null, "stale approval blocked");
Assert(ApprovalPolicy.BlockReason(request, root, "v", false) != null, "read-only patch approval blocked");
using var cmdJson = JsonDocument.Parse(JsonSerializer.Serialize(new { threadId = "t", turnId = "v", cwd = root, command = "dir" }));
var cmdReq = new ServerRequest(JsonSerializer.SerializeToElement(2), "item/commandExecution/requestApproval", cmdJson.RootElement.Clone());
Assert(ApprovalPolicy.BlockReason(cmdReq, root, "v", false) != null, "command escalation requires explicit consent");
Assert(ApprovalPolicy.BlockReason(cmdReq, root, "v", false, true) == null, "one-command user consent supported");
async Task Git(params string[] commands) { var psi = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true }; foreach (var a in commands) psi.ArgumentList.Add(a); using var p = Process.Start(psi)!; await p.StandardOutput.ReadToEndAsync(); var err = await p.StandardError.ReadToEndAsync(); await p.WaitForExitAsync(); if (p.ExitCode != 0) throw new Exception(err); }
await Git("init"); await Git("add", "hello.txt"); await Git("-c", "user.name=Hermes Test", "-c", "user.email=hermes@example.invalid", "commit", "-m", "fixture"); File.WriteAllText(Path.Combine(root, "hello.txt"), "changed\n");
var diff = await WorkspaceFiles.DiffAsync(root); Assert(diff.Contains("+changed") && !diff.Contains("SECRET=value"), "real Git diff and secret exclusion");
var child = Path.Combine(root, "child"); Directory.CreateDirectory(child); Assert((await WorkspaceFiles.DiffAsync(child)).Contains("상위 프로젝트"), "parent Git repository not leaked");
async Task RejectAsync(Func<Task> action, string label) { try { await action(); } catch (IOException) { Assert(true, label); return; } throw new Exception("FAIL: " + label); }
string? liveWorktreeRoot = null;
await using (var manager = new DeskController(Path.Combine(baseDir, "review-history")))
{
    var project = manager.Register(root); var otherRoot = Path.Combine(baseDir, "other-project"); Directory.CreateDirectory(otherRoot); var otherProject = manager.Register(otherRoot);
    var a = manager.NewTask(project, "Alpha search test"); a.Transcript.Add(new("agent", "needle-in-conversation", DateTime.UtcNow));
    manager.NewTask(otherProject, "Alpha other project"); manager.Save();
    Assert(manager.Search("alpha", project.Id).Count == 1, "project-scoped title search");
    Assert(manager.Search("NEEDLE-IN-CONVERSATION", null).Count == 1, "conversation search case-insensitive");
    Assert(manager.Search("alpha", null).Count == 2 && manager.Search("absent", null).Count == 0, "global search and empty results");
    Assert(Review.Detail(manager.State, a).Contains(root), "task detail carries actual execution root");
    var bucket = Path.Combine(baseDir, "worktrees"); Directory.CreateDirectory(bucket);
    await RejectAsync(() => manager.NewIsolatedTaskAsync(project, root, "blocked"), "overlapping worktree storage blocked");
    var occupied = Path.Combine(baseDir, "occupied"); Directory.CreateDirectory(occupied); File.WriteAllText(Path.Combine(occupied, "keep.txt"), "preserve");
    await RejectAsync(() => manager.NewIsolatedTaskAsync(project, occupied, "blocked"), "non-owned nonempty storage blocked");
    Assert(File.ReadAllText(Path.Combine(occupied, "keep.txt")) == "preserve", "existing storage file preserved");
    var before = await GitRunner.Run(root, "rev-parse", "HEAD");
    var isolated = await manager.NewIsolatedTaskAsync(project, bucket, "isolated A");
    Assert(isolated.Project.ParentProjectId == project.Id && isolated.Project.WorktreeBranch!.StartsWith("hermes/"), "managed worktree project registered");
    Assert(File.ReadAllText(Path.Combine(isolated.Project.Root, "hello.txt")) == "hello", "worktree copies committed snapshot only");
    Assert(!File.Exists(Path.Combine(isolated.Project.Root, ".env")), "untracked secrets not copied");
    File.WriteAllText(Path.Combine(isolated.Project.Root, "hello.txt"), "ISOLATED_ONLY\n");
    var isolated2 = await manager.NewIsolatedTaskAsync(project, bucket, "isolated B");
    liveWorktreeRoot = isolated2.Project.Root;
    Assert(File.ReadAllText(Path.Combine(root, "hello.txt")) == "changed\n" && File.ReadAllText(Path.Combine(isolated2.Project.Root, "hello.txt")) == "hello", "source and sibling worktree files isolated");
    Assert((await GitRunner.Run(root, "rev-parse", "HEAD")).Output == before.Output, "source HEAD preserved");
    var changes = await Review.ChangesAsync(isolated.Project.Root);
    Assert(changes.Count == 1 && changes[0].Path == "hello.txt", "worktree change list excludes internal metadata");
    Assert((await Review.FileDiffAsync(isolated.Project.Root, changes[0])).Contains("ISOLATED_ONLY"), "selected file actual diff");
    File.WriteAllText(Path.Combine(isolated.Project.Root, "hello.txt"), "hello");
    Assert((await Review.FileDiffAsync(isolated.Project.Root, changes[0])).Contains("상태가 바뀌"), "stale review selection handled");
    var rename = Review.ParseStatus(root, "R  renamed.txt\0hello.txt\0");
    Assert(rename.Single().PreviousPath == "hello.txt", "NUL rename parser preserves previous path");
    Assert(Review.ParseStatus(root, "R  normal.txt\0.env\0").Count == 0, "rename from secret path excluded");
    await Git("add", "--", ".env"); await Git("-c", "user.name=Hermes Test", "-c", "user.email=hermes@example.invalid", "commit", "-m", "secret rejection fixture");
    var beforeWorktrees = (await GitRunner.Run(root, "worktree", "list", "--porcelain")).Output;
    await RejectAsync(() => manager.NewIsolatedTaskAsync(project, bucket, "blocked-secret"), "committed secret blocks worktree before mutation");
    Assert((await GitRunner.Run(root, "worktree", "list", "--porcelain")).Output == beforeWorktrees, "blocked worktree leaves metadata unchanged");
}
using var additionalJson = JsonDocument.Parse(JsonSerializer.Serialize(new { threadId = "t", turnId = "v", cwd = root, networkApprovalContext = new { host = "example.invalid" } }));
Assert(ApprovalPolicy.BlockReason(new(JsonSerializer.SerializeToElement(3), "item/commandExecution/requestApproval", additionalJson.RootElement.Clone()), root, "v", true, true) != null, "network permission widening still blocked with consent");
await using (var rpc = new AppServerClient())
{
    var eventSeen = new TaskCompletionSource<bool>(); var requestSeen = new TaskCompletionSource<ServerRequest>();
    rpc.Notification += e => { if (e.Method == "test/event") eventSeen.TrySetResult(true); };
    rpc.Approval += r => requestSeen.TrySetResult(r);
    await rpc.ConnectAsync(Environment.ProcessPath!, root);
    var echo = await rpc.RequestAsync("echo", new { value = "stream contract" }); Assert(echo.Text("value") == "stream contract", "JSONL request/response correlation");
    await rpc.RequestAsync("emit", new { }); await eventSeen.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert(true, "notification dispatch");
    await rpc.RequestAsync("approval", new { }); var req = await requestSeen.Task.WaitAsync(TimeSpan.FromSeconds(5)); await rpc.RespondAsync(req, new { decision = "decline" }); Assert(req.Id.GetString() == "server-1", "server request string ID and response");
}
Console.WriteLine($"PASS {count} checks. Fixture transport is unit testing, not engine verification. Scratch: {baseDir}");
await CompanyTests.Run(baseDir, Assert);
await DesktopFeatureTests.Run(baseDir, Assert);
Console.WriteLine($"PASS {count} total checks including synthetic company server/security/usage. No real company endpoint or key used.");

if (args.Contains("--company-engine-smoke"))
{
    await CompanyEngineTests.Run(baseDir, args[Array.IndexOf(args, "--codex") + 1], args[Array.IndexOf(args, "--report") + 1]); return;
}

if (args.Contains("--probe") || args.Contains("--live-smoke"))
{
    var queue = new ConcurrentQueue<Action>(); var data = Path.Combine(baseDir, "live-history");
    await using var desk = new DeskController(data, a => queue.Enqueue(a));
    desk.State.Provider.Mode = ConnectionMode.ExistingChatGptCli;
    desk.Diagnostic += line => Console.WriteLine("ENGINE_DIAGNOSTIC: " + line);
    var exeIndex = Array.IndexOf(args, "--codex");
    var exe = exeIndex >= 0 && args.Length > exeIndex + 1 ? args[exeIndex + 1] : AppServerClient.FindInstalledCodex() ?? throw new Exception("Official codex.exe missing");
    void Pump() { while (queue.TryDequeue(out var a)) a(); }
    var project = desk.Register(liveWorktreeRoot ?? root);
    try { await desk.ConnectAsync(exe, project.Root); } catch { Pump(); throw; }
    Pump(); Console.WriteLine("OFFICIAL: " + desk.AccountStatus + "; model count=" + desk.Models.Count);
    var report = new Dictionary<string, object> { ["officialExecutable"] = exe, ["accountStatus"] = desk.AccountStatus, ["modelCount"] = desk.Models.Count, ["unitChecks"] = count, ["atUtc"] = DateTime.UtcNow, ["scratch"] = baseDir, ["engineRoot"] = project.Root, ["targetRuntime"] = "10.0.12" };
    if (args.Contains("--live-smoke"))
    {
        if (!desk.SubscriptionReady) throw new Exception("Additional billing/auth path is not approved.");
        var task = desk.NewTask(project, "Official live smoke test");
        await desk.StartAsync(task, "Reply with exactly HERMES_ENGINE_OK. Do not call any tools, read files, or modify files.", desk.Models.First(), false);
        var until = DateTime.UtcNow.AddSeconds(90);
        while (DeskController.IsRunning(task) && DateTime.UtcNow < until) { Pump(); foreach (var a in desk.Approvals.ToList()) await desk.AnswerAsync(a, false); await Task.Delay(100); }
        Pump(); report["liveStatus"] = task.Status; report["threadId"] = task.ThreadId ?? ""; report["responseObserved"] = task.Transcript.Any(t => t.Kind == "에이전트" && t.Text.Contains("HERMES_ENGINE_OK"));
        Console.WriteLine("LIVE: " + task.Status + "; expected response=" + report["responseObserved"]);
        if (DeskController.IsRunning(task)) { await desk.StopAsync(task); Pump(); report["liveStatus"] = "timeout; interrupt requested"; }
        if (task.Status == "완료")
        {
            await desk.StartAsync(task, "Do not use tools. Write a long 1500-word explanation of how sorting works.", desk.Models.First(), false);
            Pump(); if (DeskController.IsRunning(task)) await desk.StopAsync(task);
            until = DateTime.UtcNow.AddSeconds(20); while (DeskController.IsRunning(task) && DateTime.UtcNow < until) { Pump(); await Task.Delay(100); }
            Pump(); report["interruptStatus"] = task.Status; Console.WriteLine("INTERRUPT: " + task.Status);
        }
        if (args.Contains("--tool-smoke"))
        {
            task = desk.NewTask(project, "Official isolated command/file test");
            await desk.StartAsync(task, "Only in the current project: use a command to read hello.txt, then use your file editing tool to create engine-output.txt with exactly HERMES_FILE_OK followed by a newline. Run a command to read engine-output.txt. Never request escalation, network, other projects, credentials, deletion or security changes. Finish with HERMES_TOOLS_OK.", desk.Models.First(), true);
            until = DateTime.UtcNow.AddSeconds(120);
            while (DeskController.IsRunning(task) && DateTime.UtcNow < until) { Pump(); foreach (var a in desk.Approvals.ToList()) await desk.AnswerAsync(a, false); await Task.Delay(100); }
            Pump(); report["toolStatus"] = task.Status;
            report["commandResultsObserved"] = task.Transcript.Count(t => t.Kind == "명령 결과");
            report["fileChangesObserved"] = task.Transcript.Count(t => t.Kind == "파일 변경");
            report["fileVerified"] = File.Exists(Path.Combine(project.Root, "engine-output.txt")) && File.ReadAllText(Path.Combine(project.Root, "engine-output.txt")).Trim() == "HERMES_FILE_OK";
            report["sourcePreserved"] = File.ReadAllText(Path.Combine(root, "hello.txt")) == "changed\n" && !File.Exists(Path.Combine(root, "engine-output.txt"));
            report["engineDiffObserved"] = !string.IsNullOrEmpty(task.Diff);
            Console.WriteLine("TOOLS: " + task.Status + "; file=" + report["fileVerified"] + "; commands=" + report["commandResultsObserved"]);
            if (DeskController.IsRunning(task)) await desk.StopAsync(task);
        }
    }
    if (args.Contains("--live-smoke"))
    {
        var persisted = desk.State.Tasks.FirstOrDefault(t => t.ThreadId != null);
        if (persisted != null)
        {
            await desk.DisposeAsync();
            await using var rpc = new AppServerClient(); await rpc.ConnectAsync(exe, project.Root);
            var resumed = await rpc.RequestAsync("thread/resume", new { threadId = persisted.ThreadId, cwd = project.Root, modelProvider = "openai", approvalPolicy = "on-request", approvalsReviewer = "user", sandbox = persisted.ThreadWritable == true ? "workspace-write" : "read-only" });
            report["resumeVerified"] = resumed.GetProperty("thread").Text("id") == persisted.ThreadId;
            Console.WriteLine("RESUME: " + report["resumeVerified"]);
        }
    }
    var outputIndex = Array.IndexOf(args, "--report");
    if (outputIndex >= 0 && args.Length > outputIndex + 1) File.WriteAllText(args[outputIndex + 1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
}
