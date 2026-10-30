using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Hermes.Core;
namespace Hermes.Desktop;
public partial class MainWindow
{
    internal async Task RunContinuationUiChecks(string data)
    {
        var checks = new List<string>(); void Check(bool ok,string label){if(!ok)throw new IOException(label);checks.Add(label);}
        var root=Path.Combine(data,"owned-project");Directory.CreateDirectory(root);var project=desk.Register(root);Projects.SelectedItem=project;
        var task=desk.NewTask(project,"작업 큐와 무제한 도구 호출 검증");task.ThreadId="owned-queue-thread";task.TurnId="owned-turn";task.Status="실행 중";SelectTask(task);
        var p=(TaskProcess)typeof(DeskController).GetMethod("BeginProcess",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(desk,new object[]{task,"A. 실제 산출물 생성\nB. 남은 산출물 검증"})!;
        string Decide(object args){using var json=JsonDocument.Parse(JsonSerializer.Serialize(args));return (string)typeof(DeskController).GetMethod("DecideProcess",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(desk,new object[]{task,json.RootElement,"owned-turn"})!;}
        Decide(new{action="plan",items=new[]{new{id="A",title="첫 산출물",acceptance="파일의 실제 문자열 FIRST_VERIFIED를 확인",kind="artifact",artifacts=new[]{new{path="first.txt",contains="FIRST_VERIFIED"}},dependsOn=Array.Empty<string>()},new{id="B",title="남은 검증",acceptance="파일의 실제 문자열 SECOND_VERIFIED를 확인",kind="artifact",artifacts=new[]{new{path="second.txt",contains="SECOND_VERIFIED"}},dependsOn=new[]{"A"}}}});
        RenderTask();RefreshProcesses();processList.SelectedItem=p;ShowProcess();
        Check(RequirementMode.Items.Count==3&&RequirementMode.SelectedIndex==0&&RequirementMode.IsEnabled,"running UI defaults to additive requirements with explicit cancel and replace choices");
        Check(processDetails.Text.Contains("도구 횟수 상한 없음")&&processDetails.Text.Contains("첫 산출물")&&processDetails.Text.Contains("완료 조건")&&processDetails.Text.Contains("선행"),"process panel shows uncapped tool telemetry and explicit queue acceptance dependencies");
        Decide(new{action="start_item",itemId="A"});File.WriteAllText(Path.Combine(root,"first.txt"),"FIRST_VERIFIED");var evidence=desk.Memory.Record(ExecutionMemory.Scope(root),task.Id,"owned-proof","verify first artifact","command","read first.txt",true,"FIRST_VERIFIED");Decide(new{action="verify_item",itemId="A",evidenceId=evidence.Id,verification="실제 파일 내용을 읽고 FIRST_VERIFIED 문자열을 확인했습니다."});
        typeof(DeskController).GetMethod("QueueRequirement",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(desk,new object[]{task,"기존 미완료 요구를 유지하면서 새 산출물을 추가합니다.",RequirementChange.Append});ShowProcess();
        Check(processDetails.Text.Contains("[verified] A")&&processDetails.Text.Contains("[pending] B")&&processDetails.Text.Contains("미계획 요구 1개"),"verified pending and newly added requirements are visible together");
        _=Dispatcher.BeginInvoke(()=>{var dialog=Application.Current.Windows.OfType<Window>().Single(w=>w.Title=="단계별 실행 예산");var panel=(StackPanel)dialog.Content;var fields=panel.Children.OfType<TextBox>().ToArray();Check(fields.Length==3&&!panel.Children.OfType<TextBlock>().Any(t=>t.Text.Contains("도구 시작 수")),"obsolete tool count cap input is removed from actual budget dialog");fields[0].Text="7";fields[1].Text="20";fields[2].Text="3";panel.Children.OfType<CheckBox>().Single(c=>c.Content.ToString()!.Contains("사용자 확인")).IsChecked=true;panel.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));});
        ExecutionBudget_Click(this,new RoutedEventArgs());
        var saved=new StateStore(data).Load();Check(saved.Desktop.ExecutionBudget.RequiresConfirmation&&saved.Desktop.ExecutionBudget.MaxRounds==7&&saved.Desktop.ExecutionBudget.MaxMinutes==20,"explicit user time and round confirmation setting persists");
        Check(!p.Budget.RequiresConfirmation,"changing future-stage settings does not silently rewrite a running request policy");
        Processes_Click(this,new RoutedEventArgs()); processList.SelectedItem=p; ShowProcess(); processDetails.ScrollToHome();
        CaptureProgressVisibility(data,"continuation-queue");await desk.StopProcessAsync(p.Id);RenderTask();
        Check(p.UserStopped&&!RequirementMode.IsEnabled&&task.Status=="중단됨","user stop disables active requirement mode and retains completed items");
        File.WriteAllText(Path.Combine(data,"continuation-ui.json"),JsonSerializer.Serialize(new{checks,realModelCalls=0,commandsExecuted=0},new JsonSerializerOptions{WriteIndented=true}));
    }
}
