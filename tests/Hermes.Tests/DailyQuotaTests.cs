using System.Text.Json;
using Hermes.Core;
internal static class DailyQuotaTests
{
    internal static async Task Run(string root, Action<bool,string> check)
    {
        Directory.CreateDirectory(root);
        bool Classify(string message, string status = "failed")
        { using var d=JsonDocument.Parse(JsonSerializer.Serialize(new { status, error=new {message} }));return DailyQuotaRecovery.IsDailyTokenExhaustion(d.RootElement); }
        check(Classify("daily_token_limit_exceeded"),"explicit daily token code qualifies");
        check(Classify("Daily token limit exceeded"),"daily token provider description qualifies");
        check(Classify("Tokens per day exhausted"),"tokens per day exhaustion qualifies");
        check(Classify("일일 토큰 한도 소진"),"Korean daily token exhaustion qualifies");
        foreach(var message in new[]{"finish_reason=length","HTTP 429 Too many requests","insufficient_quota","context_length_exceeded","daily token limit exceeded unauthorized","daily token limit exceeded billing","weekly usage limit exceeded","rate_limit_exceeded"})
            check(!Classify(message),"not daily quota: "+message);
        check(!Classify("daily token limit exceeded","completed"),"model successful answer does not schedule quota recovery");
        var now=DateTimeOffset.Parse("2026-11-18T00:10:00Z");
        check(DailyQuotaRecovery.NextReset(now)==DateTimeOffset.Parse("2026-11-19T00:30:00Z"),"next calendar day 09:30 KST even before today's 09:30");
        check(DailyQuotaRecovery.NextReset(DateTimeOffset.Parse("2026-11-18T23:50:00Z"))==DateTimeOffset.Parse("2026-11-20T00:30:00Z"),"KST calendar rather than machine UTC date");
        await using var desk=new DeskController(Path.Combine(root,"data"));var project=desk.Register(root);var task=desk.NewTask(project,"daily quota owned fixture");
        task.ThreadId="quota-thread";task.TurnId="first";task.Status="실행 중";
        var p=desk.BeginProcess(task,"Preserve the original requirement and finish it after daily reset");
        p.Items.Add(new QueuedWork {Id="done",State="verified",Title="Already done",Kind="answer"});
        p.Operations.Add(new OperationReceipt { Key="already-done", State="completed", Result="evidence preserved" });
        using(var d=JsonDocument.Parse(JsonSerializer.Serialize(new{threadId=task.ThreadId,turn=new{id="first",status="failed",error=new{message="daily_token_limit_exceeded"}}}))) desk.Handle(new("turn/completed",d.RootElement));
        check(p.BlockKind=="daily_token_quota"&&p.DailyQuotaResumeAt!=null&&!p.Active,"actual turn handler schedules a persistent inactive checkpoint");
        check(p.Items.Single().State=="verified"&&p.Operations.Single().State=="completed"&&p.Requirements.Single().Text.Contains("original"),"completed work, operation receipts and original request preserved");
        var due=p.DailyQuotaResumeAt!.Value;
        check(!DailyQuotaRecovery.CanClaim(p,due.AddSeconds(-1))&&DailyQuotaRecovery.CanClaim(p,due),"due boundary exact and not early");
        await desk.RunDueDailyQuotaResumesAsync(due);
        check(p.DailyQuotaResumeAttempts==0&&p.DailyQuotaResumeAt==due,"no connected engine cannot consume or execute reservation");
        desk.Save();var saved=new StateStore(Path.Combine(root,"data")).Load().Processes.Single();
        check(saved.DailyQuotaResumeAt==due&&saved.ThreadId==task.ThreadId&&saved.Checkpoint.Length>0,"reservation, session and checkpoint survive state reload");
        saved.DailyQuotaClaimedAt=due;
        check(!DailyQuotaRecovery.CanClaim(saved,due),"admitted or uncertain request never claimed twice");
        saved.DailyQuotaClaimedAt=null;saved.DailyQuotaResumeAttempts=3;
        check(!DailyQuotaRecovery.CanClaim(saved,due),"three daily attempts maximum");
        saved.DailyQuotaResumeAttempts=0;saved.BlockKind="permission_denied";
        check(!DailyQuotaRecovery.CanClaim(saved,due),"permission blocker never auto-cleared");
        saved.BlockKind="daily_token_quota";saved.Operations.Add(new OperationReceipt {Key="uncertain"});
        check(!DailyQuotaRecovery.CanClaim(saved,due),"uncertain operation prevents duplicate side effects");
        var claimRoot=Path.Combine(root,"claims");Directory.CreateDirectory(claimRoot);
        int winners=0;await Task.WhenAll(Enumerable.Range(0,12).Select(_=>Task.Run(()=>
        { try { using var claim=DailyQuotaRecovery.OpenClaim(claimRoot,p);claim.WriteByte(1);claim.Flush(true);Interlocked.Increment(ref winners); } catch(IOException) { } })));
        check(winners==1,"simultaneous exclusive durable admission has one winner");
        var replayRejected=false;try{using var replay=DailyQuotaRecovery.OpenClaim(claimRoot,p);}catch(IOException){replayRejected=true;}
        check(replayRejected,"claim survives closed process handle and prevents restart replay");
        var retryTask=desk.NewTask(project,"repeated daily quota");retryTask.ThreadId="retry-thread";retryTask.TurnId="retry-one";retryTask.Status="실행 중";
        var retry=desk.BeginProcess(retryTask,"Keep pending work without claiming success");retry.DailyQuotaResumeAttempts=1;retry.DailyQuotaClaimedAt=DateTimeOffset.UtcNow;
        void Fail(string id) { using var d=JsonDocument.Parse(JsonSerializer.Serialize(new{threadId=retryTask.ThreadId,turn=new{id,status="failed",error=new{message="daily_token_limit_exceeded"}}}));desk.Handle(new("turn/completed",d.RootElement)); }
        Fail("retry-one");
        check(retry.DailyQuotaResumeAt!=null&&retry.DailyQuotaClaimedAt==null&&retry.DailyQuotaResumeAttempts==1,"confirmed repeated daily exhaustion schedules another day, not immediate replay");
        var reservation=retry.DailyQuotaResumeAt;Fail("retry-one");
        check(retry.DailyQuotaResumeAt==reservation&&retry.DailyQuotaResumeAttempts==1,"duplicate completion does not create extra reservation or attempts");
        retry.State="실행 중";retryTask.Status="실행 중";retryTask.TurnId="retry-limit";retry.DailyQuotaResumeAt=null;retry.DailyQuotaResumeAttempts=3;
        Fail("retry-limit");
        check(retry.BlockKind=="daily_quota_retry_limit"&&retry.DailyQuotaResumeAt==null&&!retry.Active,"three confirmed failures stop automatic retries in real completion handler");
        var admission=new TaskProcess {ContinuationEnabled=true,State="일일 토큰 초기화 대기",ThreadId="admission-thread",DailyQuotaClaimedAt=due};
        var admissionTask=new WorkItem {ThreadId="admission-thread",Model="same-model",ProviderFingerprint="same-provider"};
        bool Admission(bool connected=true,bool approval=false)=>DailyQuotaRecovery.CanContinueAdmission(admission,admissionTask,due,"admission-thread","same-model","same-provider",connected,approval);
        check(Admission(),"unchanged unloaded-thread resume may proceed to turn start");
        admission.UserStopped=true;check(!Admission(),"Stop during awaited thread resume cannot be undone");admission.UserStopped=false;
        admission.DailyQuotaCancelled=true;check(!Admission(),"cancelled reservation after await remains cancelled");admission.DailyQuotaCancelled=false;
        admissionTask.ThreadId="new-request";check(!Admission(),"new request thread during await is not overwritten");admissionTask.ThreadId="admission-thread";
        admissionTask.Model="other-model";check(!Admission(),"model switch during await stops admission");admissionTask.Model="same-model";
        admissionTask.ProviderFingerprint="changed";check(!Admission(),"task provider drift during await stops admission");admissionTask.ProviderFingerprint="same-provider";
        check(!Admission(false),"engine replacement or disconnected provider prevents turn start");
        check(!Admission(true,true),"new pending approval prevents automatic turn start");
        admission.Operations.Add(new OperationReceipt{Key="new-uncertain"});check(!Admission(),"new uncertain operation after await stops admission");
        var rpcTask=desk.NewTask(project,"explicit RPC quota rejection");rpcTask.ThreadId="rpc-quota-thread";rpcTask.Status="시작 중";
        var rpcProcess=desk.BeginProcess(rpcTask,"Preserve this text objective after rejected turn admission");
        check(!desk.TryScheduleDailyQuotaRpc(rpcTask,rpcProcess,new IOException("daily_token_limit_exceeded"),now),"ordinary exception text never authorizes quota retry");
        check(!desk.TryScheduleDailyQuotaRpc(rpcTask,rpcProcess,new ModelServerException("daily_token_limit_exceeded"),now),"unstructured model exception never authorizes quota retry");
        using(var denied=JsonDocument.Parse("""{"code":-32000,"message":"daily token limit exceeded; authentication failed"}"""))
            check(!desk.TryScheduleDailyQuotaRpc(rpcTask,rpcProcess,EngineRpcException.FromResponse("turn/start",denied.RootElement),now),"typed RPC authentication rejection remains blocked");
        using(var quota=JsonDocument.Parse("""{"code":-32001,"message":"Daily token limit exceeded","data":{"code":"daily_token_limit_exceeded","type":"provider_quota","privatePayload":"not retained"}}"""))
        {
            var refused=EngineRpcException.FromResponse("thread/resume",quota.RootElement);
            check(!desk.TryScheduleDailyQuotaRpc(rpcTask,rpcProcess,refused,now),"thread restoration rejection cannot impersonate turn admission rejection");
            check(refused.Error.GetProperty("code").GetInt32()==-32001&&!refused.Error.GetRawText().Contains("not retained"),"RPC projection keeps codes and removes unrelated payload fields");
        }
        await using(var rpc=new AppServerClient())
        {
            await rpc.ConnectAsync(Environment.ProcessPath!,root);
            EngineRpcException? failure=null;
            try { await rpc.RequestAsync("turn/start",new { dailyQuotaFixture=true }); }
            catch(EngineRpcException ex){failure=ex;}
            check(failure!=null&&failure.RequestMethod=="turn/start"&&failure.Error.GetProperty("code").GetInt32()==-32001,"actual RPC reader retains correlated method and structured error");
            check(failure!=null&&ConnectionErrors.Describe(failure)=="Daily token limit exceeded","legacy visible provider message remains unchanged");
            check(failure!=null&&desk.TryScheduleDailyQuotaRpc(rpcTask,rpcProcess,failure,now)&&rpcProcess.DailyQuotaResumeAt==DailyQuotaRecovery.NextReset(now),"actual typed RPC turn rejection schedules original process");
            check(rpcTask.ThreadId=="rpc-quota-thread"&&rpcTask.TurnId==null&&rpcProcess.Requirements.Single().Text.Contains("text objective"),"RPC scheduling preserves objective and thread without inventing an admitted turn");
            check(!desk.TryScheduleDailyQuotaRpc(rpcTask,rpcProcess,failure!,now),"same RPC rejection cannot schedule an already paused process twice");
        }
        await desk.StopProcessAsync(p.Id);
        check(p.DailyQuotaCancelled&&p.UserStopped&&p.DailyQuotaResumeAt==null,"existing Stop button cancels inactive scheduled work");
        await desk.RunDueDailyQuotaResumesAsync(due.AddDays(1));
        check(p.DailyQuotaResumeAttempts==0,"cancelled work remains stopped on later polls");
    }
}
