namespace Hermes.Core;

public sealed class TaskProcess
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TaskId { get; set; } = "";
    public string Goal { get; set; } = "";
    public string State { get; set; } = "실행 중";
    public int Round { get; set; } = 1;
    public ExecutionBudget Budget { get; set; } = new();
    public int ToolCalls { get; set; }
    public int ObservedToolResults { get; set; }
    public int StagnantRounds { get; set; }
    public string LastProgressDigest { get; set; } = "";
    public string Checkpoint { get; set; } = "";
    public string BudgetReason { get; set; } = "";
    public long ToolQueueMs { get; set; }
    public long ToolExecutionMs { get; set; }
    public long AutomaticWaitMs { get; set; }
    public bool PausedForBudget { get; set; }
    public int ConsecutiveFailures { get; set; }
    public string Detail { get; set; } = "완료 근거를 확인할 때까지 자동으로 이어갑니다.";
    public string? Decision { get; set; }
    public string LastCompletedTurnId { get; set; } = "";
    public string DecisionTurnId { get; set; } = "";
    public DateTime Started { get; set; } = DateTime.UtcNow;
    public DateTime Updated { get; set; } = DateTime.UtcNow;
    public bool Active => State is "실행 중" or "다음 실행 대기" or "종료 중";
    public override string ToString() => $"{Goal[..Math.Min(55, Goal.Length)]} · {State} · {Round}회";
}
