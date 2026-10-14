using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hermes.Core;
internal static class LifecycleTests
{
    internal static async Task Run(string root, string cli, string reportPath)
    {
        Directory.CreateDirectory(root); var directory = Path.Combine(root, ".agents", "skills", "project-note"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "SKILL.md");
        const string text = "---\nname: project-note\ndescription: Test-only local project note instructions\n---\n\nHERMES_SKILL_INSTRUCTIONS_05. Reply HERMES_SKILL_OK without tools.\n";
        File.WriteAllText(path, text);
        var records = new List<string>();
        void Check(bool truth, string label) { if (!truth) throw new Exception(label); records.Add("PASS " + label); Console.WriteLine(records[^1]); }
        void Reject(Action action, string label) { try { action(); } catch (IOException) { Check(true, label); return; } throw new Exception(label); }
        var skills = ProjectSkills.List(root, out var errors); Check(skills.Count == 1 && errors.Count == 0, "project-local skill listing");
        var skill = skills.Single(); Reject(() => ProjectSkills.Resolve(root, "../global/SKILL.md"), "outside skill blocked"); Reject(() => ProjectSkills.Validate(root, [skill, skill]), "duplicate skill name blocked");
        File.AppendAllText(path, "changed"); Reject(() => ProjectSkills.Validate(root, [skill]), "changed skill blocked"); File.WriteAllText(path, text);
        var broken = Path.Combine(root, "skills", "broken"); Directory.CreateDirectory(broken); File.WriteAllText(Path.Combine(broken, "SKILL.md"), "not a frontmatter file");
        ProjectSkills.List(root, out errors); Check(errors.Count == 1, "invalid skill surfaced without overwriting source");
        File.WriteAllText(Path.Combine(broken, "SKILL.md"), "---\nname: leaked\ndescription: unsafe fixture\n---\npassword=SYNTHETIC_ONLY_DO_NOT_SEND\n");
        Reject(() => ProjectSkills.Read(root, "skills/broken/SKILL.md"), "sensitive fixture skill not sent");
        await using (var inspect = new LocalInspectionClient(root))
        {
            await inspect.ConnectAsync(cli, Path.Combine(Path.GetDirectoryName(root)!, "skill-inspection"));
            var listed = await inspect.SkillsAsync(); Check(listed.Any(s => s.Name == "project-note") && listed.All(s => Safety.Within(root, ProjectSkills.Resolve(root, s.RelativePath))), "official skills/list filtered to project only");
            var repeated = await inspect.SkillsAsync(); Check(repeated.Count == listed.Count && inspect.RejectedModelRequests == 0, "repeat skill discovery has no model call");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { await inspect.SkillsAsync(cancelled.Token); throw new Exception("Discovery cancel failed"); } catch (OperationCanceledException) { Check(true, "skill discovery cancellation"); }
        }
        var queue = new ConcurrentQueue<Action>(); int requests = 0; bool skillSeen = false, delay = false;
        await using var mock = new LoopbackHttp(async (request, stream, ct) =>
        {
            using var document = JsonDocument.Parse(request.Body); requests++; skillSeen |= document.RootElement.GetRawText().Contains("HERMES_SKILL_INSTRUCTIONS_05");
            if (delay) await Task.Delay(20000, ct);
            var response = JsonNode.Parse("""{"id":"resp_skill","object":"response","status":"completed","created_at":1,"model":"company-skill-test","output":[{"id":"msg_skill","type":"message","role":"assistant","status":"completed","content":[{"type":"output_text","text":"HERMES_SKILL_OK","annotations":[]}]}],"usage":{"input_tokens":10,"output_tokens":5,"total_tokens":15}}""")!.AsObject();
            await LoopbackHttp.Reply(stream, 200, "text/event-stream", ResponseWire.Sse(response), ct);
        });
        var data = Path.Combine(Path.GetDirectoryName(root)!, "lifecycle-history");
        await using var desk = new DeskController(data, a => queue.Enqueue(a), true); desk.Diagnostic += line => Console.WriteLine("ENGINE: " + line);
        desk.Configure(new() { Endpoint = mock.BaseUri.AbsoluteUri, Deployment = "company-skill-test", Authentication = ProviderAuth.None });
        var project = desk.Register(root); var task = desk.NewTask(project, "Skill archive lifecycle"); await desk.ConnectAsync(cli, root);
        void Pump() { while (queue.TryDequeue(out var a)) a(); }
        async Task Finish()
        {
            var until = DateTime.UtcNow.AddSeconds(35);
            while (DeskController.IsRunning(task) && DateTime.UtcNow < until) { Pump(); foreach (var approval in desk.Approvals.ToList()) await desk.AnswerAsync(approval, false); await Task.Delay(50); }
            Pump(); if (DeskController.IsRunning(task)) { await desk.StopAsync(task); throw new Exception("Lifecycle turn timeout"); }
        }
        await desk.StartAsync(task, "Use selected project-note skill.", desk.Models.Single(), false, skills: [skill]); await Finish();
        Check(skillSeen && task.Transcript.Any(e => e.Text.Contains("HERMES_SKILL_OK")), "official skill input reaches configured provider with actual instructions");
        Check(task.Skills.Count == 1 && desk.Store.Load().Tasks.Single().Skills.Count == 1, "skill metadata persists without importing private sessions");
        var beforeArchive = requests; await desk.SetArchivedAsync(task, true); Pump();
        Check(task.Archived && desk.Search("", project.Id).Count == 0 && desk.Search("", project.Id, true).Count == 1, "official archive and archive view preserve task");
        var historyCount = task.Transcript.Count; await desk.SetArchivedAsync(task, true); Check(task.Transcript.Count == historyCount && requests == beforeArchive, "repeat archive idempotent and no model call");
        try { await desk.StartAsync(task, "Blocked while archived", desk.Models.Single(), false); throw new Exception("Archived start allowed"); } catch (IOException) { Check(true, "archived task execution blocked"); }
        using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); try { await desk.SetArchivedAsync(task, false, cancel.Token); throw new Exception("Restore cancellation failed"); } catch (OperationCanceledException) { Check(task.Archived, "cancel restore before RPC preserves archive"); } }
        await desk.SetArchivedAsync(task, false); Pump(); await desk.SetArchivedAsync(task, false);
        Check(!task.Archived && desk.Store.Load().Tasks.Single().Archived == false && requests == beforeArchive, "official restore repeat/restart state and no model call");
        await desk.StartAsync(task, "Repeat selected skill after restore.", desk.Models.Single(), false, skills: [skill]); await Finish();
        Check(requests == beforeArchive + 1 && task.Skills.Count == 2, "restored thread/resume repeats skill call");
        delay = true; await desk.StartAsync(task, "Skill call cancel test.", desk.Models.Single(), false, skills: [skill]); Pump();
        try { await desk.SetArchivedAsync(task, true); throw new Exception("Running archive allowed"); } catch (IOException) { Check(!task.Archived, "archive while running rejected"); }
        await desk.StopAsync(task); await Finish(); Check(task.Status != "완료" && !task.Archived, "skill turn interruption preserves task/history");
        Check(File.ReadAllText(path) == text, "skill source unchanged after invoke/archive/restore/cancel");
        var failedTask = desk.NewTask(project, "Archive error fixture"); failedTask.ThreadId = "missing-" + Guid.NewGuid().ToString("N"); failedTask.ProviderFingerprint = desk.State.Provider.Fingerprint;
        try { await desk.SetArchivedAsync(failedTask, true); throw new Exception("Missing thread archived"); } catch (IOException) { Check(!failedTask.Archived && failedTask.Transcript.Count == 0, "official archive RPC failure preserves app state"); }
        failedTask.ProviderFingerprint = "wrong-provider";
        try { await desk.SetArchivedAsync(failedTask, true); throw new Exception("Wrong provider archived"); } catch (IOException) { Check(!failedTask.Archived, "archive provider mismatch blocked"); }
        var due = desk.ScheduleDraft(project, "Scheduled draft fixture", "Manual-start-only fixture prompt", DateTimeOffset.UtcNow.AddHours(1));
        var countBeforeDraft = requests; var drafts = desk.CreateDueDrafts(due.DueAtUtc);
        Check(drafts.Count == 1 && drafts.Single().Status == "예약 초안" && requests == countBeforeDraft, "due reminder creates draft without model/command call");
        Check(desk.CreateDueDrafts(due.DueAtUtc.AddMinutes(1)).Count == 0 && desk.Store.Load().ScheduledDrafts.Single().CreatedTaskId != null, "repeated/restarted timer does not duplicate draft");
        var cancelledDraft = desk.ScheduleDraft(project, "Cancelled fixture", "Do not execute", DateTimeOffset.UtcNow.AddHours(2)); desk.CancelScheduledDraft(cancelledDraft);
        Check(desk.CreateDueDrafts(cancelledDraft.DueAtUtc).Count == 0, "scheduled draft cancellation");
        Reject(() => desk.ScheduleDraft(project, "", "invalid", DateTimeOffset.UtcNow.AddHours(1)), "invalid schedule rejected");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { checks = records, providerRequests = requests, actualSkillInstructionsSeen = skillSeen, realCompanyModelCalls = 0, root }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
