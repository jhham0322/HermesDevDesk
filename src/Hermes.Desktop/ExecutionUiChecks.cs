using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Hermes.Core;
namespace Hermes.Desktop;
public partial class MainWindow
{
    internal async Task RunExecutionUiChecks(string data)
    {
        await RunProgressVisibilityChecks(data);
        var checks = new List<string>();
        void Check(bool value,string label) { if(!value)throw new IOException(label);checks.Add(label); }
        var project = desk.State.Projects.First(); var task=desk.NewTask(project,"출처와 중간보고 검증");task.Model="local-alias";task.ThreadId="owned-ui-thread";task.TurnId="owned-ui-turn";
        Tasks.ItemsSource=new[]{task};Tasks.SelectedItem=task;
        task.ActiveRequestId="ui-request";task.ResponseModel="local-model-v2";
        ProgressMessages.Delta(task,"ui-summary","0","분석한 내용: 파일 범위를 확인했습니다. 남은 일: 검증. 다음 행동: 테스트 실행.");
        ChatMessages.Delta(task,"ui-answer","실행 결과를 확인하고 있습니다.");ShowProgressSummaries.IsChecked=true;ShowProgressSummaries.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));UpdateLayout();
        Check(MessageStack.Children.Count==2,"model summary and streamed answer rendered");
        var box=(Border)MessageStack.Children[1];var panel=(StackPanel)box.Child;var label=(TextBlock)panel.Children[0];
        Check(label.Text.Contains("[local-model-v2][중간보고]")&&label.Text.Contains("요청 local-alias"),"actual model and request alias shown separately on real control");
        var textControl=panel.Children[1];ChatMessages.Delta(task,"ui-answer"," 검증 전 완료를 주장하지 않습니다.");conversationStamp="";RenderConversation();
        Check(ReferenceEquals(textControl,((StackPanel)((Border)MessageStack.Children[1]).Child).Children[1]),"streaming preserves editable selection control while updating source label");
        desk.State.Desktop.ExecutionBudget.MaxRounds=1;task.Status="실행 중";var process=(TaskProcess)typeof(DeskController).GetMethod("BeginProcess",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(desk,new object[]{task,"출처와 제한된 단계 검증"})!;
        using(var done=JsonDocument.Parse("""{"threadId":"owned-ui-thread","turn":{"id":"owned-ui-turn","status":"completed"}}"""))typeof(DeskController).GetMethod("Handle",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(desk,new object[]{new ServerEvent("turn/completed",done.RootElement)});
        conversationStamp="";RenderConversation();Check(process.PausedForBudget&&task.Status!="완료","actual host turn boundary creates budget checkpoint instead of completion");
        CaptureProgressVisibility(data,"execution-sources");
        ShowProgressSummaries.IsChecked=false;ShowProgressSummaries.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Check(MessageStack.Children.Count==1&&task.Transcript.Count==4,"report toggle hides model and host reports while preserving answer and raw history");
        ShowProgressSummaries.IsChecked=true;ShowProgressSummaries.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Check(MessageStack.Children.Count==4,"reports restore after toggling back on");
        _ = Dispatcher.BeginInvoke(() => { var dialog=Application.Current.Windows.OfType<Window>().Single(w=>w.Title=="단계별 실행 예산");var controls=(StackPanel)dialog.Content;var fields=controls.Children.OfType<TextBox>().ToArray();fields[0].Text="4";fields[1].Text="12";fields[2].Text="2";controls.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); });
        ExecutionBudget_Click(this,new RoutedEventArgs());
        Check(desk.State.Desktop.ExecutionBudget.MaxRounds==4&&new StateStore(data).Load().Desktop.ExecutionBudget.MaxMinutes==12,"budget dialog saves user-selected limits for future stages");
        RefreshProcesses();Check(processDetails.Text.Contains("예산")&&processDetails.Text.Contains("재개 지점"),"process inspector displays budget and resume checkpoint");
        File.WriteAllText(Path.Combine(data,"execution-ui.json"),JsonSerializer.Serialize(new{checks,realCompanyRequests=0},new JsonSerializerOptions{WriteIndented=true}));
    }
}
