using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hermes.Core;

public static class DailyQuotaRecovery
{
    public const int MaxAttempts = 3;
    internal static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(3);
    public static DateTimeOffset NextReset(DateTimeOffset observed)
    {
        var local = observed.ToOffset(TimeSpan.FromHours(9));
        var reset = new DateTimeOffset(local.Date.AddHours(9).AddMinutes(10), local.Offset);
        return (reset > local ? reset : reset.AddDays(1)).ToUniversalTime();
    }
    internal static DateTimeOffset? MigrateResetTime(DateTimeOffset? time) =>
        time is { } due && due.ToOffset(TimeSpan.FromHours(9)).TimeOfDay == new TimeSpan(9, 30, 0)
            ? due.AddMinutes(-20) : time;

    // Only a provider turn error qualifies. A generic 429, billing quota, a model's
    // prose, and a single-response output limit never grant automatic resumption.
    public static bool IsDailyTokenExhaustion(JsonElement turn)
    {
        if (turn.Text("status") != "failed" || turn.Property("error") is not { ValueKind: JsonValueKind.Object } error) return false;
        var text = error.Text("code") + " " + error.Text("type") + " " + error.Text("message");
        if (error.Property("data") is { ValueKind: JsonValueKind.Object } data)
            text += " " + data.Text("code") + " " + data.Text("type") + " " + data.Text("message");
        bool Match(string pattern) => Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
        if (Match("unauthorized|forbidden|permission|authentication|billing|payment|insufficient_quota|context_length|context.length|finish_reason|content_filter|cancel|interrupt")) return false;
        return Match(@"daily[_ -]?(token|usage)[_ -]?(limit|quota)[_ -]?(exceeded|exhausted)|tokens?[_ -]per[_ -]day[_ -]?(limit)?[_ -]?(exceeded|exhausted)|\bTPD\b.*(exceeded|exhausted)|daily.{0,35}token.{0,35}(exceeded|exhausted)|일일.{0,25}토큰.{0,25}(소진|초과)");
    }
    internal static bool CanContinueAdmission(TaskProcess p, WorkItem task, DateTimeOffset? claim, string threadId, string model, string fingerprint, bool connectionUnchanged, bool hasApproval) =>
        claim != null && p.DailyQuotaClaimedAt == claim && !p.UserStopped && !p.DailyQuotaCancelled && !task.Archived &&
        p.ContinuationEnabled && !p.Active && p.ThreadId == threadId && task.ThreadId == threadId && task.Model == model &&
        task.ProviderFingerprint == fingerprint && p.BlockKind.Length == 0 && !p.Operations.Any(o => o.NeedsReview) &&
        connectionUnchanged && !hasApproval;
    internal static FileStream OpenClaim(string dataDirectory, TaskProcess p)
    {
        var path = Path.Combine(dataDirectory, "daily-quota-" + ExecutionBudget.Digest([p.Id, p.DailyQuotaResumeAt!.Value.ToString("O")]) + ".claim");
        return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
    }
    public static bool CanClaim(TaskProcess p, DateTimeOffset now) =>
        p.DailyQuotaResumeAt is { } due && due <= now && p.DailyQuotaClaimedAt == null &&
        p.DailyQuotaResumeAttempts < MaxAttempts && !p.DailyQuotaCancelled && !p.UserStopped &&
        p.ContinuationEnabled && !p.Active && p.BlockKind == "daily_token_quota" && !p.Operations.Any(o => o.NeedsReview);
}

public sealed partial class DeskController
{
    private int dailyQuotaPollRunning;
    private bool dailyQuotaTimerEnabled;

    private void UpdateDailyQuotaTimer()
    {
        if (disposed) return;
        var needed = State.Processes.Any(p => p.DailyQuotaResumeAt != null && !p.DailyQuotaCancelled && !p.UserStopped) ||
            (State.ModelSelectedByQuota && State.ModelConnections.FirstOrDefault() is { QuotaExhaustedUntil: not null } first &&
             ActiveModelConnection?.Id != first.Id);
        if (needed == dailyQuotaTimerEnabled) return;
        dailyQuotaTimerEnabled = needed;
        var interval = needed ? DailyQuotaRecovery.CheckInterval : Timeout.InfiniteTimeSpan;
        dailyQuotaTimer.Change(interval, interval);
    }
    internal bool TryScheduleDailyQuotaRpc(WorkItem task, TaskProcess process, Exception exception, DateTimeOffset now)
    {
        if (exception is not EngineRpcException { RequestMethod: "turn/start" } rpc || ActiveProcess(task) != process ||
            task.ThreadId == null || task.ThreadId != process.ThreadId) return false;
        using var turn = JsonDocument.Parse(JsonSerializer.Serialize(new { status = "failed", error = rpc.Error }));
        if (!TryScheduleDailyQuotaResume(task, process, turn.RootElement, now)) return false;
        task.TurnId = null;
        task.Transcript.Add(TranscriptPresentation.Host("공급자 시작 거절", process.DailyQuotaResumeAt != null ? "turn/start 요청이 일일 토큰 소진 오류로 거절되었습니다. 새 실행은 접수되지 않았으며 기존 체크포인트를 예약했습니다." : "turn/start 요청이 일일 토큰 소진 오류로 거절되었습니다. 재시도 상한에 도달하여 기존 체크포인트를 보존하고 자동 재개를 중단했습니다."));
        Save(); return true;
    }
    private bool TryScheduleDailyQuotaResume(WorkItem task, TaskProcess process, JsonElement turn, DateTimeOffset now)
    {
        if (!DailyQuotaRecovery.IsDailyTokenExhaustion(turn) || process.DailyQuotaCancelled || process.UserStopped ||
            process.BlockKind is "permission_denied" or "approval" or "uncertain_operation" ||
            process.Operations.Any(o => o.NeedsReview) || Approvals.Any(a => a.Params.Text("threadId") == task.ThreadId)) return false;
        if (process.DailyQuotaResumeAttempts >= DailyQuotaRecovery.MaxAttempts)
        {
            process.DailyQuotaResumeAt = null; process.BlockKind = "daily_quota_retry_limit";
            EndProcess(task, "일일 한도 재시도 중단", "일일 토큰 한도 재개를 3회 시도했습니다. 공급자 한도·초기화 시각을 확인해 주세요.");
            task.Status = process.State; Save(); return true;
        }
        process.Checkpoint = WorkQueue.Summary(process);
        var reset = DailyQuotaRecovery.NextReset(now);
        var connection = State.ModelConnections.FirstOrDefault(c => c.Id == task.ModelConnectionId);
        if (connection != null) connection.QuotaExhaustedUntil = reset;
        var alternate = connection != null && FirstAvailableConnection(now) != null;
        process.DailyQuotaResumeAt = alternate ? now + DailyQuotaRecovery.CheckInterval : reset; process.DailyQuotaClaimedAt = null;
        process.BlockKind = "daily_token_quota";
        var detail = alternate ? "일일 토큰을 모두 사용했습니다. 3분 후 저장 순서에 따라 사용 가능한 모델에서 작업을 이어갑니다." : $"일일 토큰 소진으로 중단했습니다. {process.DailyQuotaResumeAt.Value.ToOffset(TimeSpan.FromHours(9)):yyyy-MM-dd HH:mm} 한국시간에 기존 작업을 한 번 재개합니다. 실제 공급자 초기화 성공은 아직 확인되지 않았습니다. 앱과 같은 모델 연결이 필요하며 꺼져 있으면 다음 연결 후 확인합니다. 프로세스 종료 버튼으로 예약을 취소할 수 있습니다.";
        EndProcess(task, alternate ? "다음 모델 연결 대기" : "일일 토큰 초기화 대기", detail); task.Status = process.State;
        task.Transcript.Add(TranscriptPresentation.Host("자동 재개 예약", detail));
        Save();
        UpdateDailyQuotaTimer();
        return true;
    }
    public void CancelDailyQuotaResume(string processId)
    {
        var p = State.Processes.Single(p => p.Id == processId);
        p.DailyQuotaCancelled = true; p.DailyQuotaResumeAt = null; p.UserStopped = true;
        p.State = "자동 재개 취소"; p.Detail = "사용자가 일일 토큰 자동 재개 예약을 취소했습니다.";
        if (State.Tasks.SingleOrDefault(t => t.Id == p.TaskId) is { } task)
        { task.Status = p.State; task.Transcript.Add(TranscriptPresentation.Host("자동 재개 취소", p.Detail)); }
        Save();
        UpdateDailyQuotaTimer();
    }
    public async Task RunDueDailyQuotaResumesAsync(DateTimeOffset now)
    {
        if (disposed || Interlocked.Exchange(ref dailyQuotaPollRunning, 1) != 0) return;
        try
        {
            // Exclusive process claim prevents two application instances dispatching
            // the same persisted checkpoint. The marker survives a crash and is
            // never automatically replayed when turn admission is uncertain.
            foreach (var p in State.Processes.Where(p => DailyQuotaRecovery.CanClaim(p, now)).ToList())
            {
                var task = State.Tasks.SingleOrDefault(t => t.Id == p.TaskId);
                if (task == null || task.Archived || IsRunning(task) || Approvals.Any(a => a.Params.Text("threadId") == task.ThreadId)) continue;
                var rotates = task.ModelConnectionId != null && State.ModelConnections.Any(c => c.Id == task.ModelConnectionId);
                var target = rotates ? FirstAvailableConnection(now) : null;
                if (rotates)
                {
                    if (target == null || connecting || State.Tasks.Any(IsRunning) ||
                        State.Provider.Mode != ConnectionMode.CompanyApi || string.IsNullOrWhiteSpace(State.CodexPath)) continue;
                }
                else if (Engine == null || !SubscriptionReady || connectedFingerprint != State.Provider.Fingerprint || task.ProviderFingerprint != connectedFingerprint) continue;
                if (task.ThreadId == null || task.ThreadId != p.ThreadId || p.ThreadId.Length == 0) continue;
                if (State.Provider.IsChatGptPlanOAuth && oauthEngineExpires <= now.AddMinutes(1)) continue;
                FileStream claim;
                try { claim = DailyQuotaRecovery.OpenClaim(DataDirectory, p); }
                catch (IOException)
                {
                    p.DailyQuotaResumeAt = null; p.BlockKind = "daily_quota_admission_review";
                    p.State = "자동 재개 접수 확인 필요"; p.Detail = "이 예약은 이미 접수 시도되었습니다. 중복 실행하지 않고 실제 실행 결과 확인을 기다립니다."; Save(); continue;
                }
                await using (claim)
                {
                    p.DailyQuotaClaimedAt = now; p.DailyQuotaResumeAt = null; if (!rotates || target?.Id == task.ModelConnectionId) p.DailyQuotaResumeAttempts++;
                    p.BlockKind = "daily_quota_admission_review";
                    await claim.WriteAsync(System.Text.Encoding.UTF8.GetBytes(p.Id + "\n" + now.ToString("O"))); claim.Flush(true);
                    // Persist the claim before any provider request. Never reset this
                    // marker on an ambiguous transport error.
                    Save();
                    if (p.UserStopped || p.DailyQuotaCancelled || task.Archived || p.Operations.Any(o => o.NeedsReview) || Approvals.Any(a => a.Params.Text("threadId") == task.ThreadId)) continue;
                    try
                    {
                        if (target != null)
                        {
                            var previous = task.ModelConnectionId;
                            SelectConnection(target, true);
                            if (Engine == null || !SubscriptionReady || connectedFingerprint != State.Provider.Fingerprint)
                                await ConnectAsync(State.CodexPath!, Project(task).Root);
                            if (p.UserStopped || p.DailyQuotaCancelled || task.Archived || task.ThreadId != p.ThreadId) continue;
                            task.Model = target.Settings.Deployment;
                            task.ModelConnectionId = target.Id;
                            task.ProviderFingerprint = connectedFingerprint;
                            if (previous != target.Id) task.ReasoningEffort = "";
                            task.Transcript.Add(TranscriptPresentation.Host("자동 재개 시작", $"{ModelChoice(target.Id, task.Model)} 연결에서 저장된 작업을 이어갑니다."));
                        }
                        else task.Transcript.Add(TranscriptPresentation.Host("자동 재개 시작", "한국시간 09:10 이후 일일 토큰 재개를 시도합니다. 기존 작업·완료 항목·실행 증거를 보존합니다."));
                        p.BlockKind = "";
                        await ResumeSavedProcessAsync(p.Id);
                    }
                    catch (Exception ex)
                    {
                        if (p.UserStopped || p.DailyQuotaCancelled) { Save(); continue; }
                        if (target != null && DailyQuotaRecovery.IsDailyTokenExhaustion(JsonSerializer.SerializeToElement(
                            new { status = "failed", error = new { message = ex.Message } })))
                        {
                            target.QuotaExhaustedUntil = DailyQuotaRecovery.NextReset(DateTimeOffset.UtcNow);
                            p.DailyQuotaClaimedAt = null;
                            p.DailyQuotaResumeAt = FirstAvailableConnection(DateTimeOffset.UtcNow) != null
                                ? DateTimeOffset.UtcNow + DailyQuotaRecovery.CheckInterval : target.QuotaExhaustedUntil;
                            p.BlockKind = "daily_token_quota";
                            p.State = "다음 모델 연결 대기"; task.Status = p.State; Save(); continue;
                        }
                        p.BlockKind = "daily_quota_admission_review"; p.State = "자동 재개 확인 필요";
                        p.Detail = ConnectionErrors.Describe(ex); if (task.ThreadId == p.ThreadId) task.Status = p.State;
                        task.Transcript.Add(TranscriptPresentation.Host("자동 재개 보류", p.Detail)); Save();
                    }
                }
            }
            if (State.ModelSelectedByQuota && State.Provider.Mode == ConnectionMode.CompanyApi && !connecting &&
                !State.Tasks.Any(IsRunning) && State.ModelConnections.FirstOrDefault() is { QuotaExhaustedUntil: { } reset } &&
                reset <= now && FirstAvailableConnection(now) is { } first &&
                ActiveModelConnection?.Id != first.Id)
                SelectConnection(first, true);
        }
        catch (Exception ex) { AppLog.Write(ex, "daily-quota-scheduler"); }
        finally
        {
            Volatile.Write(ref dailyQuotaPollRunning, 0);
            UpdateDailyQuotaTimer();
        }
    }
}
