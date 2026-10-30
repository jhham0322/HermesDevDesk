using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hermes.Core;
internal static class ContinuationEngineTests
{
    internal static async Task Run(string root, string exe)
    {
        Directory.CreateDirectory(root); var dispatch = new ConcurrentQueue<Action>(); var phase = -1; var calls = 0; var extraRequest = "";
        var trace = new ConcurrentQueue<string>();
        Exception? serverFailure = null;
        void Trace(string kind, object metadata)
        {
            var line = JsonSerializer.Serialize(new { at = DateTime.UtcNow, kind, metadata });
            trace.Enqueue(line); Console.WriteLine(line);
        }
        var effects = Path.Combine(root, "effects.txt").Replace("'", "''");
        var extra = Path.Combine(root, "extra.txt").Replace("'", "''"); var done = Path.Combine(root, "done.txt").Replace("'", "''");
        object Item(string id, string file, string marker, string request = "initial", int priority = 0, params string[] deps) => new { id, requestId = request, title = "Work " + id, acceptance = "Actual artifact must contain " + marker, kind = "artifact", priority, dependsOn = deps, artifacts = new[] { new { path = file, contains = marker } } };
        object Command(int i) => new { purpose = "Create exactly one fixture effect " + i, operationId = "effect-" + i, command = "Add-Content -LiteralPath '" + effects + "' -Value 'OP_" + i + "'; Write-Output VERIFIED_" + i };
        await using var server = new LoopbackHttp(async (request, stream, ct) =>
        {
            var n = Interlocked.Increment(ref phase); Interlocked.Increment(ref calls);
            Trace("request-start", new { phase = n });
            try
            {
            using var document = JsonDocument.Parse(request.Body); var body = document.RootElement;
            var names = body.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("function").Text("name")).ToArray();
            Trace("request-shape", new { phase = n, toolCount = names.Length });
            string Name(string suffix) => names.Single(t => t.EndsWith(suffix, StringComparison.Ordinal));
            var lastTool = body.GetProperty("messages").EnumerateArray().LastOrDefault(m => m.Text("role") == "tool"); var tool = lastTool.ValueKind == JsonValueKind.Object ? lastTool.Text("content") : "";
            long Evidence() { var m=Regex.Match(tool,@"evidenceId=(\d+)"); if(!m.Success)throw new IOException("Missing real tool evidence at phase "+n); return long.Parse(m.Groups[1].Value); }
            string? name = null; object? args = null;
            if (n == 0) { name="hermes_task"; args=new{action="plan",items=new[]{Item("build","effects.txt","OP_25"),Item("verify","done.txt","QUEUE_VERIFIED","initial",0,"build")}}; }
            else if(n==1) {name="hermes_task";args=new{action="start_item",itemId="build"};}
            else if(n>=2&&n<=26){name="hermes_command";args=Command(n-1);}
            else if(n==27){name="hermes_command";args=Command(1);}
            else if(n==28){name="hermes_task";args=new{action="verify_item",itemId="build",evidenceId=Evidence(),verification="Observed OP_25 in the real effect artifact after all writes."};}
            else if(n==30){if(extraRequest.Length==0)throw new IOException("Additional request was not retained");name="hermes_task";args=new{action="plan",items=new[]{Item("extra","extra.txt","EXTRA_VERIFIED",extraRequest,-10,"build")}};}
            else if(n==31){name="hermes_task";args=new{action="start_item",itemId="extra"};}
            else if(n==32){name="hermes_command";args=new{purpose="Deliver additional authorized fixture requirement",operationId="extra-once",command="Set-Content -LiteralPath '"+extra+"' -Value EXTRA_VERIFIED; Write-Output EXTRA_VERIFIED"};}
            else if(n==33){name="hermes_task";args=new{action="verify_item",itemId="extra",evidenceId=Evidence(),verification="Read and verified the additional actual artifact."};}
            else if(n==34){name="hermes_task";args=new{action="start_item",itemId="verify"};}
            else if(n==35){name="hermes_command";args=new{purpose="Verify exact effect count without redoing writes",operationId="verify-once",command="$n=(Get-Content -LiteralPath '"+effects+"').Count; if($n -ne 25){throw ('Duplicate or missing effects: '+$n)}; Set-Content -LiteralPath '"+done+"' -Value QUEUE_VERIFIED; Write-Output QUEUE_VERIFIED"};}
            else if(n==36){name="hermes_task";args=new{action="verify_item",itemId="verify",evidenceId=Evidence(),verification="Verified exactly twenty five effects and the final artifact."};}
            else if(n>37)throw new IOException("Host continued after verified final completion");
            object response = name == null
                ? new { id="continuation-"+n, model="synthetic-continuation", choices=new[]{new{message=new{role="assistant",content=n==29?"Partial work finished. The remaining work can be resumed later.":"All artifacts have been delivered and verified."},finish_reason="stop"}} }
                : new { id="continuation-"+n, model="synthetic-continuation", choices=new[]{new{message=new{role="assistant",tool_calls=new[]{new{id="call-"+n,type="function",function=new{name=Name(name),arguments=JsonSerializer.Serialize(args)}}}},finish_reason="tool_calls"}} };
            await LoopbackHttp.Reply(stream,200,"application/json",JsonSerializer.Serialize(response),ct);
            Trace("response-sent", new { phase = n, tool = name ?? "final" });
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref serverFailure, ex, null);
                Trace("fixture-error", new { phase = n, exceptionType = ex.GetType().Name });
                throw;
            }
        });
        void Pump(){while(dispatch.TryDequeue(out var a))a();}
        // A saved checkpoint can be transient: latch it on Changed, not only after draining callbacks.
        Action Observe(DeskController desk, string target, Action reached)
        {
            string previous = "";
            void Sample()
            {
                var p = desk.Processes.SingleOrDefault(); if (p == null) return;
                if (p.State == target) reached();
                if (previous == p.State) return;
                previous = p.State;
                Trace("state", new { state = p.State, phase = Volatile.Read(ref phase), p.Round, p.ToolCalls });
            }
            desk.Changed += Sample;
            // Never print diagnostics verbatim: they can contain request text, paths or tokens.
            desk.Diagnostic += message => Trace("engine-diagnostic", new {
                phase = Volatile.Read(ref phase), contentOmitted = true,
                categories = new[] { "timeout", "panic", "error", "warn", "connection", "stream" }
                    .Where(category => message.Contains(category, StringComparison.OrdinalIgnoreCase)).ToArray()
            });
            return Sample;
        }
        async Task Wait(DeskController desk, string target, Func<bool> reached, Action sample)
        {
            var clock = Stopwatch.StartNew();
            void Fail(string reason)
            {
                var p = desk.Processes.SingleOrDefault();
                Trace("wait-failed", new { reason, target, phase = Volatile.Read(ref phase), elapsedMs = clock.ElapsedMilliseconds,
                    state = p?.State, round = p?.Round, toolCalls = p?.ToolCalls,
                    completedOperations = p?.Operations.Count(o => o.State == "completed"),
                    unknownOperations = p?.Operations.Count(o => o.State == "unknown"),
                    lastOperationState = p?.Operations.LastOrDefault()?.State,
                    exceptionType = Volatile.Read(ref serverFailure)?.GetType().Name });
                File.WriteAllLines(Path.Combine(root, "continuation-diagnostics.jsonl"), trace.ToArray());
                throw new IOException("Continuation engine " + reason + " phase=" + phase);
            }
            while (true)
            {
                sample();
                if (reached()) return;
                // Recheck after EACH callback so an automatic next turn cannot hide the checkpoint.
                if (dispatch.TryDequeue(out var action)) { action(); sample(); }
                if (reached()) return;
                if (Volatile.Read(ref serverFailure) != null) Fail("fixture-error");
                var p = desk.Processes.SingleOrDefault();
                if (p != null && (p.UserStopped || p.BlockKind.Length > 0 || p.PausedForBudget ||
                    p.State is "연결 복구 필요" or "실행 결과 확인 필요" or "승인 대기" or "시작 실패" or "연결 끊김" or "종료됨" or "사용자 확인 필요" or "의존관계 확인 필요" or "오류" or "실패")) Fail("blocked-or-error");
                if (clock.Elapsed >= TimeSpan.FromMinutes(4)) Fail("timeout");
                if (dispatch.IsEmpty) await Task.Delay(20);
            }
        }
        var data=Path.Combine(root,"data");string processId, taskId, threadId;
        await using(var desk=new DeskController(data,a=>dispatch.Enqueue(a),true))
        {
            var checkpointReached = 0; var sample = Observe(desk, "다음 실행 대기", () => Interlocked.Exchange(ref checkpointReached, 1));
            desk.Configure(new(){Endpoint=server.BaseUri.AbsoluteUri+"v1/chat/completions",Deployment="continuation-model",Protocol=ProviderProtocol.GatewayChatCompletions,UseAdvancedSettings=true},"SYNTHETIC_CONTINUATION_TOKEN");
            var project=desk.Register(root);project.BrowserEnabled=project.ComputerEnabled=project.LocalMcpEnabled=false;
            await desk.ConnectAsync(exe,root);Pump();var task=desk.NewTask(project,"Continuation restart fixture");
            await desk.StartAsync(task,"A. Create twenty five unique effects exactly once.\nB. Verify the count and final artifact, continuing until all requirements are complete.","continuation-model",true,continueUntilComplete:true);
            await Wait(desk,"다음 실행 대기",()=>Volatile.Read(ref checkpointReached)==1,sample);var p=desk.Processes.Single();
            if(p.Items.Single(i=>i.Id=="build").State!="verified"||File.ReadAllLines(effects).Length!=25)throw new IOException("Long tool chain or duplicate receipt failed");
            await desk.SteerAsync(task,"Add an urgent extra artifact while retaining the original remaining verification requirement.");extraRequest=p.Requirements.Last().Id;
            processId=p.Id;taskId=task.Id;threadId=task.ThreadId!;desk.Save();
        }
        Pump();
        await using(var desk=new DeskController(data,a=>dispatch.Enqueue(a),true))
        {
            var completionReached = 0; var sample = Observe(desk, "완료", () => Interlocked.Exchange(ref completionReached, 1));
            await desk.ConnectAsync(exe,root);Pump();
            await Wait(desk,"완료",()=>Volatile.Read(ref completionReached)==1,sample);var p=desk.Processes.Single();var task=desk.State.Tasks.Single(t=>t.Id==taskId);
            if(p.Id!=processId||task.ThreadId!=threadId||p.Items.Count!=3||p.Items.Any(i=>i.State!="verified")||File.ReadAllLines(effects).Length!=25||!File.ReadAllText(extra).Contains("EXTRA_VERIFIED")||!File.ReadAllText(done).Contains("QUEUE_VERIFIED"))throw new IOException("Automatic restore lost requirements, replayed effects, or missed verification");
            var report=new{passed=true,calls,uniqueEffects=25,replayedEffects=0,completedItems=p.Items.Count,operations=p.Operations.Count,toolCalls=p.ToolCalls,sameThread=true,automaticRestart=true,additionalRequirementPreserved=true,realCompanyRequests=0};
            File.WriteAllLines(Path.Combine(root,"continuation-diagnostics.jsonl"),trace.ToArray());
            File.WriteAllText(Path.Combine(root,"continuation-engine.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(report));
        }
    }
}
