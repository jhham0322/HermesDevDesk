using System.Text.Json;
using Hermes.Core;
internal static class OperationRecoveryTests
{
    internal static async Task Run(string root, Action<bool,string> check)
    {
        Directory.CreateDirectory(root);
        var data=Path.Combine(root,"data");
        await using var desk=new DeskController(data,a=>a(),true);
        var project=desk.Register(root); var task=desk.NewTask(project,"Owned command receipt fixture");
        var p=desk.BeginProcess(task,"No real command or model requests"); p.State="중단됨"; task.Status="중단됨"; p.UserStopped=true;
        var receipt=new OperationReceipt { Key="known",ItemId="A",MethodDigest="fixed-method" }; p.Operations.Add(receipt); desk.Save();
        using var success=JsonDocument.Parse("""{"exitCode":0,"stdout":"OWNED_RESULT","stderr":""}""");
        var statePath=Path.Combine(data,"state.json"); var attributes=File.GetAttributes(statePath);var original=File.ReadAllBytes(statePath);
        File.SetAttributes(statePath,attributes|FileAttributes.ReadOnly);
        try
        {
            try { desk.RecordCommandResponse(task,receipt,success.RootElement);throw new Exception("Save fault not detected"); }
            catch(OperationReviewRequiredException ex){check(ex.Message.Contains("수신"),"received result save failure is a review notice, not an unknown command");}
            check(receipt.State=="completed"&&receipt.ExitCode==0&&receipt.ResultReceived&&receipt.RecordingPending,"known result survives state-store failure without becoming unknown");
            check(File.ReadAllBytes(statePath).SequenceEqual(original),"save failure preserves original checkpoint bytes");
            check(Directory.GetFiles(Path.Combine(data,"command-results"),"*.json").Length==1,"host response is durably recorded before checkpoint replacement");
        }
        finally{File.SetAttributes(statePath,attributes);}
        // Simulate restart from the old checkpoint, not from the modified in-memory receipt.
        await using var restarted=new DeskController(data,a=>a(),true);
        var restored=restarted.State.Processes.Single(x=>x.Id==p.Id);var pending=restored.Operations.Single();
        check(pending.State=="started"&&!pending.ResultReceived,"restart fixture restores pre-response checkpoint");
        check(restarted.DescribeOperation(p.Id,pending.Key).Contains("동일 작업"),"review locates the exact preserved host response after restart");
        File.SetAttributes(statePath,attributes|FileAttributes.ReadOnly);
        try
        {
            try{restarted.RecoverKnownOperation(p.Id,pending.Key);throw new Exception("Reconcile save fault missing");}catch(HermesException){}
            check(pending.State=="started"&&pending.Resolution==null,"failed reconciliation rolls back in-memory resolution and keeps replay blocked");
        }
        finally{File.SetAttributes(statePath,attributes);}
        restarted.RecoverKnownOperation(p.Id,pending.Key);
        check(pending.State=="completed"&&pending.ResultReceived&&!pending.NeedsReview&&pending.ExitCode==0,"exact host response resolves only the command receipt");
        check(pending.Resolution?.PreviousState=="started"&&pending.Result.Contains("OWNED_RESULT"),"resolution retains previous state and original response evidence");
        check(restored.UserStopped&&restored.State=="중단됨"&&restarted.State.Tasks.Single(t=>t.Id==task.Id).Status=="중단됨","reconciliation neither resumes nor completes the request and preserves user Stop");
        var loaded=new StateStore(data).Load().Processes.Single(x=>x.Id==p.Id).Operations.Single();
        check(loaded.Resolution!=null&&loaded.State=="completed","resolution and audit survive another restart");
        try{restarted.RecoverKnownOperation(p.Id,pending.Key);throw new Exception("Duplicate resolution allowed");}catch(HermesException){check(true,"already resolved receipt cannot be reconciled or replayed again");}
        var unknown=new OperationReceipt{Key="legacy-unknown",State="unknown",MethodDigest="no-response"};restored.Operations.Add(unknown);restored.BlockKind="approval";restarted.Save();
        try{restarted.RecoverKnownOperation(p.Id,unknown.Key);throw new Exception("Unknown assertion accepted");}catch(HermesException){check(unknown.State=="unknown"&&unknown.Resolution==null,"missing host response cannot be resolved by user confirmation or another receipt");}
        try{await restarted.ResumeSavedProcessAsync(p.Id,true);throw new Exception("Unknown replay allowed");}catch(OperationReviewRequiredException ex){check(ex.Message.Contains(unknown.Key),"resume identifies the exact unknown receipt without bypassing it");}
        var failed=new OperationReceipt{Key="failed",MethodDigest="failed-method"};p.Operations.Add(failed);
        using var failure=JsonDocument.Parse("""{"exitCode":7,"stdout":"PARTIAL_EFFECT_POSSIBLE","stderr":"owned failure"}""");
        desk.RecordCommandResponse(task,failed,failure.RootElement);
        check(failed.State=="failed"&&failed.ExitCode==7&&failed.ResultReceived,"nonzero received exit is a known failure, never proof of no side effect");
        var missing=new OperationReceipt{Key="missing-exit"};p.Operations.Add(missing);
        using var malformed=JsonDocument.Parse("""{"stdout":"not a terminal result"}""");
        try{desk.RecordCommandResponse(task,missing,malformed.RootElement);throw new Exception("Missing exit accepted");}catch(OperationReviewRequiredException){check(missing.State=="unknown"&&!missing.ResultReceived,"missing terminal exit code remains unknown");}
        // New app-owned record with deliberately mismatched embedded identity must not be trusted.
        var bad=new OperationReceipt{Key="bad-record",State="unknown",MethodDigest="bad-method"};restored.Operations.Add(bad);
        var path=Path.Combine(data,"command-results",ExecutionBudget.Digest([restored.TaskId,restored.Id,bad.Key,bad.MethodDigest])+".json");
        File.WriteAllText(path,JsonSerializer.Serialize(new{TaskId="other-task",ProcessId=restored.Id,Key=bad.Key,MethodDigest=bad.MethodDigest,ExitCode=0,Result="unrelated"}));
        try{restarted.RecoverKnownOperation(restored.Id,bad.Key);throw new Exception("Wrong identity trusted");}catch(HermesException){check(bad.State=="unknown"&&restored.BlockKind=="approval","mismatched evidence cannot resolve a receipt or clear approval");}
        var legacy=new OperationReceipt{Key="event:legacy-result",State="unknown",MethodDigest=ExecutionBudget.Digest(["Write-Output owned", "owned purpose"])}; restored.Operations.Add(legacy);
        restarted.Memory.Record(ExecutionMemory.Scope(root),task.Id,"legacy-result","owned purpose","command","Write-Output owned",true,"OWNED_LEGACY_OUTPUT");
        restarted.RecoverKnownOperation(restored.Id,legacy.Key);
        check(legacy.State=="completed"&&legacy.Result.Contains("OWNED_LEGACY_OUTPUT")&&restored.BlockKind=="approval","legacy receipt requires exact event/task/command/purpose success and preserves unrelated approval");
        var mismatch=new OperationReceipt{Key="event:legacy-result",State="unknown",MethodDigest="other-command"};
        var other= new TaskProcess{TaskId=task.Id,State="중단됨"};other.Operations.Add(mismatch);restarted.State.Processes.Add(other);
        try{restarted.RecoverKnownOperation(other.Id,mismatch.Key);throw new Exception("Wrong command trusted");}catch(HermesException){check(mismatch.State=="unknown","legacy event with different command digest stays unknown");}
        var blockedData=Path.Combine(root,"blocked-result-store");
        await using(var blockedDesk=new DeskController(blockedData,a=>a(),true))
        {
            var bp=blockedDesk.Register(root);var bt=blockedDesk.NewTask(bp,"Owned blocked journal");var run=blockedDesk.BeginProcess(bt,"Do not repeat known effects");run.State="중단됨";bt.Status="중단됨";run.UserStopped=true;
            var br=new OperationReceipt{Key="blocked-journal",MethodDigest="owned"};run.Operations.Add(br);blockedDesk.Save();
            var blockedPath=Path.Combine(blockedData,"command-results");File.WriteAllText(blockedPath,"OWNED_DIRECTORY_CONFLICT");
            try{blockedDesk.RecordCommandResponse(bt,br,success.RootElement);throw new Exception("Blocked journal accepted");}catch(OperationReviewRequiredException){}
            check(br.ResultReceived&&br.ExitCode==0&&br.State=="completed"&&br.RecordingPending,"blocked result storage preserves known response and blocks replay");
            try{blockedDesk.RecoverKnownOperation(run.Id,br.Key);throw new Exception("Blocked journal recovered");}catch(IOException){}
            check(br.NeedsReview&&br.Resolution==null,"unavailable result storage cannot be falsely resolved");
            File.Delete(blockedPath); // Owned synthetic file only; no user data or permissions changed.
            blockedDesk.RecoverKnownOperation(run.Id,br.Key);
            check(!br.NeedsReview&&run.UserStopped&&bt.Status=="중단됨","storage repair reconciles retained response without command replay or automatic resume");
        }
        check(!restored.Items.Any(i=>i.State=="verified"),"receipt reconciliation does not invent task completion evidence");
    }
}
