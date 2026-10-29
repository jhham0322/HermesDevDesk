using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Hermes.Core;
namespace Hermes.Desktop;
public partial class MainWindow
{
    internal async Task RunCommandDetailChecks(string data)
    {
        var checks=new List<string>();
        void Check(bool value,string label){if(!value)throw new IOException(label);checks.Add(label);}
        void Event(string method,string json){using var d=JsonDocument.Parse(json);typeof(DeskController).GetMethod("Handle",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(desk,new object[]{new ServerEvent(method,d.RootElement)});}
        Expander View(string id){var index=renderedEntries.Single(kv=>kv.Value.ItemId==id).Key;return (Expander)MessageStack.Children[index];}
        string Header(Expander view)=>string.Join(" ",((DockPanel)view.Header).Children.OfType<TextBlock>().Select(t=>t.Text));
        var root=Path.Combine(data,"owned-project");Directory.CreateDirectory(root);var project=desk.Register(root);Projects.SelectedItem=project;
        var task=desk.NewTask(project,"명령 상세 기본 접힘 검증");task.ThreadId="owned-command-thread";task.TurnId="owned-turn";
        Tasks.ItemsSource=new[]{task};Tasks.SelectedItem=task;
        Event("item/started","""{"threadId":"owned-command-thread","item":{"type":"dynamicToolCall","id":"owned-command","tool":"hermes_command"}}""");
        task.Transcript.Add(new("작업 진행","명령 실행 중 · 기존 저장 형식\nGet-ChildItem",DateTime.UtcNow,"legacy-command"));
        task.Transcript.Add(new TranscriptEntry("명령 결과","Get-ChildItem\nevidenceId=1; exit=0\nOUTPUT_PRESERVED",DateTime.UtcNow,"command-result"){Origin="도구: hermes_command",Stage="완료"});
        task.Transcript.Add(new TranscriptEntry("명령 출력",string.Join('\n',Enumerable.Repeat("LONG_OUTPUT",5000)),DateTime.UtcNow,"stream-output"){Origin="도구: commandExecution"});
        ProgressMessages.Complete(task,"public-summary","0","완료한 일: 범위 확인. 남은 일: 검증. 다음 행동: 테스트.");
        conversationStamp="";RenderConversation();UpdateLayout();
        Check(new[]{"tool-progress:owned-command","legacy-command","command-result","stream-output"}.All(id=>!View(id).IsExpanded&&View(id).Content==null),"new, legacy, result and long streamed command details default collapsed and lazy");
        Check(Header(View("tool-progress:owned-command")).Contains("명령 실행")&&Header(View("tool-progress:owned-command")).Contains("중"),"running command summary remains in folded header");
        Check(Header(View("command-result")).Contains("[완료]"),"observed completed command status remains in folded header");
        Check(View("progress:public-summary:0").IsExpanded,"public progress summary retains existing default expansion");
        CaptureProgressVisibility(data,"command-default-collapsed");
        View("tool-progress:owned-command").IsExpanded=true;
        Check(View("tool-progress:owned-command").Content!=null,"user can explicitly expand command detail");
        Event("item/completed","""{"threadId":"owned-command-thread","item":{"type":"dynamicToolCall","id":"owned-command","tool":"hermes_command","success":false,"status":"completed","contentItems":[{"type":"inputText","text":"OWNED_ERROR"}]}}""");
        conversationStamp="";RenderConversation();
        Check(View("tool-progress:owned-command").IsExpanded&&Header(View("tool-progress:owned-command")).Contains("실패"),"explicit expansion survives tool status and kind change while failure summary stays visible");
        View("tool-progress:owned-command").IsExpanded=false;
        Event("item/completed","""{"threadId":"owned-command-thread","item":{"type":"dynamicToolCall","id":"owned-command","tool":"hermes_command","success":false,"status":"completed","contentItems":[{"type":"inputText","text":"OWNED_ERROR_UPDATE"}]}}""");
        conversationStamp="";RenderConversation();Check(!View("tool-progress:owned-command").IsExpanded,"explicit collapse also survives repeated error update");
        View("stream-output").IsExpanded=true;
        var output=task.Transcript.FindIndex(t=>t.ItemId=="stream-output");task.Transcript[output]=task.Transcript[output] with{Text=task.Transcript[output].Text+"\nLATEST_OUTPUT"};
        conversationStamp="";RenderConversation();Check(View("stream-output").IsExpanded&&View("stream-output").Content!=null,"user-expanded long output stays expanded on stream update");
        Check(task.Transcript[output].Text.EndsWith("LATEST_OUTPUT"),"stream update preserves full original text");
        ShowProgressSummaries.IsChecked=false;ShowProgressSummaries_Click(this,new RoutedEventArgs());Check(View("stream-output").IsExpanded,"global report toggle does not reset command expansion");
        ShowProgressSummaries.IsChecked=true;ShowProgressSummaries_Click(this,new RoutedEventArgs());
        for(var i=0;i<50;i++)task.Transcript.Add(new("사용자","page filler "+i,DateTime.UtcNow));
        conversationStamp="";RenderConversation();ConversationOlder_Click(this,new RoutedEventArgs());Check(View("stream-output").IsExpanded,"explicit command expansion survives paging away and back");
        var sibling=desk.NewTask(project,"sibling");sibling.Transcript.Add(new TranscriptEntry("명령 출력","other command",DateTime.UtcNow,"stream-output"){Origin="도구: commandExecution"});
        Tasks.ItemsSource=new[]{task,sibling};Tasks.SelectedItem=sibling;conversationStamp="";RenderConversation();Check(!View("stream-output").IsExpanded,"same item identifier in another conversation starts collapsed");
        Tasks.SelectedItem=task;conversationStamp="";RenderConversation();ConversationOlder_Click(this,new RoutedEventArgs());Check(View("stream-output").IsExpanded,"returning to original chat preserves its explicit expansion");
        task.Status="대기";desk.Save();var savedCount=task.Transcript.Count;
        var reopened=new MainWindow(data);reopened.Projects.SelectedItem=reopened.desk.State.Projects.Single(p=>p.Id==project.Id);var saved=reopened.desk.State.Tasks.Single(t=>t.Id==task.Id);reopened.Tasks.ItemsSource=new[]{saved};reopened.Tasks.SelectedItem=saved;reopened.conversationStamp="";reopened.RenderConversation();reopened.ConversationOlder_Click(this,new RoutedEventArgs());
        var entry=reopened.renderedEntries.Single(kv=>kv.Value.ItemId=="stream-output").Key;Check(!((Expander)reopened.MessageStack.Children[entry]).IsExpanded,"saved conversation in a fresh window starts with command details collapsed");
        Check(saved.Transcript.Count==savedCount&&saved.Transcript.Any(t=>t.Text.Contains("LATEST_OUTPUT"))&&saved.Transcript.Any(t=>t.Text.Contains("OUTPUT_PRESERVED")),"reopen preserves raw command, result and long output records");
        await reopened.ShutdownController();reopened.closing=true;reopened.Close();
        CaptureProgressVisibility(data,"command-errors-and-reopen");
        File.WriteAllText(Path.Combine(data,"command-details.json"),JsonSerializer.Serialize(new{checks,realCompanyRequests=0,commandsExecuted=0},new JsonSerializerOptions{WriteIndented=true}));
    }
}
