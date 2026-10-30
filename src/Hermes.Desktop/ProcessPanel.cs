using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Hermes.Core;

namespace Hermes.Desktop;
public partial class MainWindow
{
    private readonly ListBox processList = new() { MinHeight = 100, MaxHeight = 300, Margin = new Thickness(4) };
    private readonly TextBox processDetails = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(4) };
    private TabItem? processTab;
    private void InitializeProcessPanel()
    {
        var panel = new DockPanel { Margin = new Thickness(8) }; var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "완료까지 계속 실행 · 전체 프로젝트 프로세스\n완료 근거가 보고될 때까지 같은 요청을 이어갑니다. 승인·외부 차단·연결 복구가 필요하면 상태에 표시합니다. 재시작 후 같은 연결에서 체크포인트를 복원합니다. 결과 미확인 실행·정지·승인·외부 제한은 자동 재시도하지 않습니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) });
        var actions = new WrapPanel(); var refresh = new Button { Content = "새로고침" }; var stop = new Button { Content = "선택 프로세스 종료" }; var resume = new Button { Content = "조건 확인 후 재개" }; actions.Children.Add(refresh); actions.Children.Add(stop); actions.Children.Add(resume);
        resume.Click += async (_, _) => await Guard(async () => { if (processList.SelectedItem is TaskProcess selected) { if (selected.PausedForBudget && selected.Items.Count == 0) await desk.ResumeBudgetAsync(selected.Id); else if (MessageBox.Show(this, "취소·승인·외부 제한 조건이 해결됐는지 확인하세요. 완료 항목은 보존하며 결과 미확인 실행은 자동 반복하지 않습니다. 재개할까요?", "체크포인트 재개", MessageBoxButton.YesNo) == MessageBoxResult.Yes) await desk.ResumeSavedProcessAsync(selected.Id, true); } RefreshProcesses(); }); top.Children.Add(actions); top.Children.Add(processList); panel.Children.Add(processDetails);
        processTab = new TabItem { Header = "프로세스", Content = panel }; InspectorTabs.Items.Add(processTab);
        refresh.Click += (_, _) => RefreshProcesses();
        stop.Click += async (_, _) => await Guard(async () => { if (processList.SelectedItem is TaskProcess selected) await desk.StopProcessAsync(selected.Id); RefreshProcesses(); });
        processList.SelectionChanged += (_, _) => ShowProcess();
        InspectorTabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.OriginalSource, InspectorTabs) && InspectorTabs.SelectedItem == processTab) RefreshProcesses(); };
    }
    private void RefreshProcesses()
    {
        var selected = (processList.SelectedItem as TaskProcess)?.Id;
        processList.ItemsSource = desk.Processes;
        processList.SelectedItem = desk.Processes.FirstOrDefault(p => p.Id == selected) ?? desk.Processes.FirstOrDefault();
        ShowProcess();
    }
    private void ShowProcess()
    {
        if (processList.SelectedItem is not TaskProcess process) { processDetails.Text = "프로세스 없음. 입력창의 ‘완료까지 계속 실행’을 켜고 요청을 보내세요."; return; }
        var task = desk.State.Tasks.FirstOrDefault(t => t.Id == process.TaskId);
        var project = desk.State.Projects.FirstOrDefault(p => p.Id == task?.ProjectId);
        var usage = desk.State.Usage.LastOrDefault(u => u.TaskId == task?.Id && u.Source == "company-api-response");
        processDetails.Text = $"최근 요청 계측(ms): 헤더 {usage?.HeadersMs} / 첫 유효 출력 {usage?.FirstUsefulOutputMs} / 전체 {usage?.TotalMs} · 버퍼 응답 {usage?.Buffered}\n예산: {process.Budget.MaxRounds}라운드 / {process.Budget.MaxMinutes}분 · 도구 횟수 상한 없음 / 사용 {process.ToolCalls} · 무진전 {process.StagnantRounds}\n계측(ms): 도구 대기 {process.ToolQueueMs} / 실행 {process.ToolExecutionMs} / 자동 대기 {process.AutomaticWaitMs}\n작업 큐: {process.Items.Count(i => i.State == "verified")}개 검증 / {process.Items.Count(i => !i.Terminal)}개 미완료 · 내부 단계 {process.StageNumber}\n다음 항목: {WorkQueue.Next(process)?.Title}\n차단 구분: {process.BlockKind}\n재개 지점: {process.Checkpoint}\n프로세스: {process.Id}\n프로젝트: {project?.Name}\n채팅: {task?.Title}\n상태: {process.State} / {task?.Status}\n실행 회차: {process.Round}\n시작: {process.Started.ToLocalTime():g}\n최근 변경: {process.Updated.ToLocalTime():g}\n\n목표\n{process.Goal}\n\n최근 결과·완료 검증 보고\n{process.Detail}\n\n완료 근거는 모델의 검증 보고이며 호스트는 인용된 실행 근거의 출처를 확인합니다.";
        processDetails.AppendText("\n\n항목별 실행·검증 기록\n" + string.Join("\n\n", process.Items.Select(i => $"[{i.State}] {i.Id} · {i.Title}\n완료 조건: {i.Acceptance}\n선행: {string.Join(", ", i.DependsOn)}\n검증: {i.Verification}")));
        processDetails.AppendText($"\n\n추가 요구 {process.Requirements.Count(r => r.Id != "initial")}개 / 미계획 요구 {process.Requirements.Count(r => !r.Planned)}개");

    }
    private void Processes_Click(object sender, RoutedEventArgs e) { SetInspector(true); InspectorTabs.SelectedItem = processTab; RefreshProcesses(); }
    private void LoopPreference_Click(object sender, RoutedEventArgs e)
    { if (CurrentTask is { } task) { task.LoopPreference = ContinueUntilComplete.IsChecked == true; desk.Save(); } }

    internal async Task RunProcessPanelChecks(string data)
    {
        var root = Path.Combine(data, "process-project"); Directory.CreateDirectory(root); var project = desk.Register(root); var task = desk.NewTask(project, "프로세스 종료 검증");
        var process = new TaskProcess { TaskId = task.Id, Goal = "완료되지 않은 테스트 작업을 목록에서 종료한다.", State = "다음 실행 대기", Round = 2 };
        desk.State.Processes.Add(process); task.Status = "계속 실행 대기"; Projects.SelectedItem = project; Tasks.SelectedItem = task;
        InspectorTabs.SelectedItem = processTab; RefreshProcesses();
        if (!processList.Items.Contains(process) || !processDetails.Text.Contains(process.Goal)) throw new IOException("Process panel does not display run details");
        await desk.StopProcessAsync(process.Id); RefreshProcesses();
        if (process.Active || process.State != "종료됨" || task.Status != "중단됨") throw new IOException("Process panel stop left the loop active");
        if (ContinueUntilComplete.IsChecked != true) throw new IOException("Completion loop must default on in composer");
        var scope = ExecutionMemory.Scope(root);
        desk.Memory.Record(scope, task.Id, "bad1", "의존성 복구", "command", "missing-one", false, "Error: dependency missing");
        desk.Memory.Record(scope, task.Id, "bad2", "의존성 복구", "command", "missing-one", false, "Error: dependency missing");
        desk.Memory.Record(scope, task.Id, "fixed", "의존성 복구", "command", "fixed-two", true, "verified");
        LoadLearningErrors(); if (learningMethods.Items.Count != 1 || learningMethods.Items[0] is not ErrorRecord) throw new IOException("Error registry UI missing");
        LoadLearningRecoveries(); if (learningMethods.Items.Count != 1 || learningMethods.Items[0] is not RecoveryRecord) throw new IOException("Improvement registry UI missing");
        File.WriteAllText(Path.Combine(data, "process-panel-report.json"), JsonSerializer.Serialize(new { passed = true, version = AppVersion.Current, checks = 5 }));
    }
}
