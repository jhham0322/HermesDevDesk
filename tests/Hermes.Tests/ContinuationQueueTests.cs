using System.Reflection;
using System.Text.Json;
using Hermes.Core;
internal static class ContinuationQueueTests
{
    internal static async Task Run(string root, Action<bool,string> check)
    {
        Directory.CreateDirectory(root);
        await using var desk = new DeskController(Path.Combine(root, "data")); var project = desk.Register(root);
        WorkItem Task(string name) { var t = desk.NewTask(project, name); t.ThreadId = Guid.NewGuid().ToString("N"); t.TurnId = "turn-" + name; t.Status = "실행 중"; return t; }
        string Decide(WorkItem t, object args) { using var d = JsonDocument.Parse(JsonSerializer.Serialize(args)); return desk.DecideProcess(t, d.RootElement, t.TurnId ?? ""); }
        bool Reject(Action action) { try { action(); return false; } catch (Exception ex) when (ex is IOException or TargetInvocationException) { return true; } }
        void Reserve(WorkItem t) => typeof(DeskController).GetMethod("ReserveManagedTool", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(desk, new object[]{t});
        void End(WorkItem t, string id, string status = "completed", object? error = null) { using var d = JsonDocument.Parse(JsonSerializer.Serialize(new { threadId = t.ThreadId, turn = new { id, status, error } })); desk.Handle(new("turn/completed", d.RootElement)); }
        QueuedWork Item(string id, string request = "initial", params string[] depends) => new() { Id = id, RequestId = request, Title = "Work " + id, Acceptance = "Artifact must contain VERIFIED_" + id, Kind = "artifact", DependsOn = depends.ToList(), Artifacts = [new(){Path = id + ".txt", Contains = "VERIFIED_" + id}] };
        long Proof(WorkItem t, string id, bool success = true) => desk.Memory.Record(ExecutionMemory.Scope(root), t.Id, Guid.NewGuid().ToString("N"), "verify " + id, "command", "check " + id, success, "VERIFIED_" + id).Id;
        var task = Task("AZ"); var process = desk.BeginProcess(task, "A. first requirement\nB. second requirement through Z");
        check(Reject(() => Decide(task, new { action = "blocked", blockerType = "external_dependency", verification = "The internal stage tool budget is exhausted; ask the user to resume." })), "internal stage count cannot be disguised as an external blocker");
        var items = Enumerable.Range(0,26).Select(n => Item(((char)('A'+n)).ToString(), "initial", n == 0 ? [] : [((char)('A'+n-1)).ToString()])).ToArray();
        check(Reject(() => Decide(task, new { action = "complete", verification = "A model claim without planned requirements is insufficient." })), "multiple requirements cannot finish without a host work plan");
        Decide(task, new { action = "plan", items });
        check(process.Items.Count == 26 && process.Items.All(i => i.State == "pending"), "A to Z plan records explicit acceptance dependencies and pending status");
        check(Reject(() => Decide(task,new {action="start_item",itemId="B"})), "dependency prevents starting B before verified A");
        for (var n=0;n<26;n++)
        {
            var id=((char)('A'+n)).ToString(); Decide(task,new{action="start_item",itemId=id});
            if(n==0)
            {
                process.Budget=JsonSerializer.Deserialize<ExecutionBudget>("""{"MaxTools":1}""")!;
                for(var i=0;i<250;i++)Reserve(task);
                check(process.ToolCalls==250 && process.BudgetReason.Length==0 && process.Active, "250 useful tool reservations cross the removed legacy count cap without rejection");
                var early=Proof(task,id);check(Reject(()=>Decide(task,new{action="verify_item",itemId=id,evidenceId=early,verification="Execution success alone does not create the requested artifact."})),"successful tool claim cannot complete a missing artifact");
            }
            File.WriteAllText(Path.Combine(root,id+".txt"),"VERIFIED_"+id);var evidence=Proof(task,id);
            Decide(task,new{action="verify_item",itemId=id,evidenceId=evidence,verification="Read the actual artifact and verified its declared content."});
            check(process.Items.Single(i=>i.Id==id).State=="verified", "host verifies artifact and records completed item "+id);
            if(n==0)
            {
                Decide(task,new{action="start_item",itemId="A"});
                check(Reject(()=>Reserve(task)),"completed item cannot reopen tool execution when no running item exists");
                check(File.ReadAllText(Path.Combine(root,"A.txt"))=="VERIFIED_A","already completed side effect is preserved");
            }
            if(n==12)
            {
                var original=process.Items.Count;var request=desk.QueueRequirement(task,"Add one urgent requirement while preserving every original unfinished requirement.");
                check(process.Items.Count==original && process.Items.Count(i=>i.State=="verified")==13 && process.Decision==null,"additional request preserves verified and original unfinished items");
                check(Reject(()=>Decide(task,new{action="complete",verification="All done, ignoring the added unplanned requirement."})),"pending additional requirement blocks premature completion");
                var extra=Item("urgent",request,"M");extra.Priority=-10;Decide(task,new{action="plan",items=new[]{extra}});
                check(WorkQueue.Next(process)?.Id=="urgent","new eligible urgent item moves ahead without interrupting completed dependencies");
                Decide(task,new{action="start_item",itemId="urgent"});File.WriteAllText(Path.Combine(root,"urgent.txt"),"VERIFIED_urgent");var e=Proof(task,"urgent");Decide(task,new{action="verify_item",itemId="urgent",evidenceId=e,verification="Verified the additional requirement artifact."});
            }
        }
        check(process.Items.Count==27 && process.Items.All(i=>i.State=="verified"),"all original A to Z and added requirement are verified without omission");
        Decide(task,new{action="complete",verification="All twenty seven requested artifacts satisfy the saved acceptance checks."}); End(task,"turn-AZ");
        check(process.State=="완료" && task.Status=="완료","whole objective completes only after all host checks pass");
        var count=task.Transcript.Count;End(task,"turn-AZ");check(task.Transcript.Count==count,"duplicate completed turn cannot repeat final side effects");
        var cycle=Task("cycle");var cp=desk.BeginProcess(cycle,"Cycle test");
        check(Reject(()=>Decide(cycle,new{action="plan",items=new[]{Item("cycle-A","initial","cycle-B"),Item("cycle-B","initial","cycle-A")}}))&&cp.Items.Count==0,"cyclic plan is rejected atomically");
        var partial=Task("partial");var pp=desk.BeginProcess(partial,"Persist partial objective");Decide(partial,new{action="plan",items=new[]{Item("saved-A"),Item("saved-B","initial","saved-A")}});Decide(partial,new{action="start_item",itemId="saved-A"});File.WriteAllText(Path.Combine(root,"saved-A.txt"),"VERIFIED_saved-A");var saved=Proof(partial,"saved-A");Decide(partial,new{action="verify_item",itemId="saved-A",evidenceId=saved,verification="Actual saved artifact content was verified."});
        End(partial,"turn-partial");check(pp.Active&&pp.State=="다음 실행 대기"&&WorkQueue.Next(pp)?.Id=="saved-B","partial final message queues next unfinished item instead of stopping");
        desk.Save();var restored=new StateStore(desk.DataDirectory).Load().Processes.Single(p=>p.Id==pp.Id);
        check(restored.Items.Single(i=>i.Id=="saved-A").State=="verified"&&restored.Items.Single(i=>i.Id=="saved-B").State=="pending","restart restores completed and pending item states without replay");
        var replacement=desk.QueueRequirement(partial,"Replace only unfinished requirements with a new explicit request.",RequirementChange.ReplacePending);
        check(pp.Items.Single(i=>i.Id=="saved-A").State=="verified"&&pp.Items.Single(i=>i.Id=="saved-B").State=="superseded","explicit replace preserves completed effects and supersedes only unfinished items");
        Decide(partial,new{action="plan",items=new[]{Item("replacement",replacement,"saved-A")}});check(WorkQueue.Next(pp)?.Id=="replacement","replacement item links to retained completed dependency");
        desk.QueueRequirement(partial,"Cancel remaining work explicitly.",RequirementChange.CancelPending);await desk.StopProcessAsync(pp.Id);
        check(pp.UserStopped&&!pp.Active&&pp.Items.Single(i=>i.Id=="saved-A").State=="verified","explicit cancellation stops future work and preserves completed artifacts");
        End(partial,"late-completion-after-user-stop");check(partial.Status=="중단됨"&&!pp.Active,"late completion cannot turn a user-cancelled request into completed");
        partial.ThreadId=Guid.NewGuid().ToString("N");partial.TurnId="new-manual-turn";partial.Status="실행 중";End(partial,"new-manual-turn");check(partial.Status=="완료","a valid newer thread remains independent of stopped history");

        var stage=Task("stage");var sp=desk.BeginProcess(stage,"Long normal sequence");sp.Round=3;sp.ToolCalls=500;Proof(stage,"stage-progress");End(stage,"turn-stage");
        check(sp.StageNumber==2&&sp.Active&&!sp.PausedForBudget,"useful progress automatically advances internal stage instead of manual handoff");
        sp.Budget.PauseAtStageLimit=true;sp.Round=sp.StageRoundOffset+sp.Budget.MaxRounds;Proof(stage,"more-progress");End(stage,"stage-next");
        check(sp.PausedForBudget&&!sp.Active,"explicit user time/round confirmation limit is respected");
        desk.Save();check(new StateStore(desk.DataDirectory).Load().Processes.Single(p=>p.Id==sp.Id).CompletedTurnIds.SetEquals(new[]{"turn-stage","stage-next"}),"processed completion identities survive restart and preserve out-of-order deduplication");

        var pausedStatus=stage.Status;End(stage,"turn-stage");check(stage.Status==pausedStatus&&!sp.Active,"old completed turn cannot overwrite a later paused checkpoint");
        var stagnant=Task("stagnant");var np=desk.BeginProcess(stagnant,"Do not spin on prose");End(stagnant,"turn-stagnant");End(stagnant,"stagnant-next");
        check(np.PausedForBudget&&np.StagnantRounds==2&&!np.Active,"unchanged results and prose cannot bypass no-progress stop");
        var quota=Task("quota");var qp=desk.BeginProcess(quota,"Provider quota is external");End(quota,"turn-quota","failed",new{code="insufficient_quota",message="Provider quota exhausted"});
        check(qp.BlockKind=="provider_limit_or_denial"&&!qp.Active&&quota.Status=="외부 제한 확인 필요","actual provider quota stops with preserved checkpoint instead of retrying or billing");
        var quotaCode=Task("quota-code");var qcp=desk.BeginProcess(quotaCode,"Respect structured provider usage limit");End(quotaCode,"turn-quota-code","failed",new{codexErrorInfo="usageLimitExceeded",message="Temporary restriction"});check(qcp.BlockKind=="provider_limit_or_denial"&&!qcp.Active,"structured camel-case provider usage limit stops without automatic retry");
        var denied=Task("denied");var dp=desk.BeginProcess(denied,"Respect rejected authorization");dp.BlockKind="permission_denied";End(denied,"turn-denied");check(!dp.Active&&denied.Status=="승인 거절","rejected authorization is never automatically retried");
        var approval=Task("approval");var ap=desk.BeginProcess(approval,"Wait for approval");ap.BlockKind="approval";End(approval,"turn-approval");check(ap.State=="승인 대기"&&approval.Status=="승인 대기","actual approval wait remains a distinct blocked state");await desk.StopProcessAsync(ap.Id);
        var uncertain=Task("uncertain");var up=desk.BeginProcess(uncertain,"Never repeat an uncertain side effect");up.Operations.Add(new(){Key="write-once",State="started"});up.State="앱 종료로 중단됨";uncertain.Status="연결 끊김 · 재개 가능";
        var blocked=false;try{await desk.ResumeSavedProcessAsync(up.Id,true);}catch(HermesException){blocked=true;}check(blocked&&up.Operations.Single().State=="started","crash during side effect requires reconciliation and never auto-replays");
        var native=Task("native");var nativeProcess=desk.BeginProcess(native,"Preserve an in-flight native operation");
        using(var e=JsonDocument.Parse(JsonSerializer.Serialize(new{threadId=native.ThreadId,item=new{type="mcpToolCall",id="native-write",server="owned",tool="write_file"}})))desk.Handle(new("item/started",e.RootElement));
        check(nativeProcess.Operations.Single().State=="started","native MCP in-flight operation is persisted without storing sensitive arguments");
        End(native,"turn-native");check(!nativeProcess.Active&&nativeProcess.BlockKind=="uncertain_operation","unacknowledged native side effect also blocks automatic replay");
        var legacy=Task("legacy-contract");legacy.ThreadLearningVersion=1;var lp=desk.BeginProcess(legacy,"A. old first requirement\nB. old second requirement");desk.QueueRequirement(legacy,"An additional old-contract requirement must remain available.");
        check(!lp.NeedsPlan&&lp.Requirements.Count==2&&lp.Requirements.Last().Planned,"old thread contract preserves added requirements without requiring unsupported new tool actions");
        var receipts=Task("receipts");var rp=desk.BeginProcess(receipts,"Retain execution receipt");
        using var args=JsonDocument.Parse("""{"command":"Write-Output verified","purpose":"read fixture","operationId":"stable-one"}""");
        var method=typeof(DeskController).GetMethod("BeginOperation",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var first=(OperationReceipt)method.Invoke(desk,new object[]{receipts,args.RootElement,"event-one"})!;first.State="completed";first.Result="ONE_EXECUTION";
        var again=(OperationReceipt)method.Invoke(desk,new object[]{receipts,args.RootElement,"event-two"})!;
        check(ReferenceEquals(first,again)&&rp.Operations.Count==1&&again.Result=="ONE_EXECUTION","stable operation identity returns saved result instead of executing twice");
        desk.Save();check(new StateStore(desk.DataDirectory).Load().Processes.Single(p=>p.Id==rp.Id).Operations.Single().State=="completed","completed execution receipt survives restart");
    }
}
