using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Hermes.Core;
namespace Hermes.Desktop;
public partial class MainWindow
{
    internal async Task RunResponseRecoveryUiChecks(string data,string fixture,bool restart)
    {
        var checks=new List<string>();void Check(bool ok,string label){if(!ok)throw new IOException(label);checks.Add(label);}
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var root=Path.Combine(data,"owned-project");Directory.CreateDirectory(root);
        string Text(DependencyObject node)
        {
            var own=node is TextBlock b?b.Text:node is TextBox t?t.Text:node is RichTextBox r?new TextRange(r.Document.ContentStart,r.Document.ContentEnd).Text:"";
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)own+="\n"+Text(VisualTreeHelper.GetChild(node,i));return own;
        }
        if(!restart)
        {
            Check(!desk.State.Tasks.Any(),"fixture starts with empty owned profile");var project=desk.Register(root);Projects.SelectedItem=project;
            foreach(var kind in new[]{"response_output_limit","response_early_eof","response_timeout"})
            {
                var task=desk.NewTask(project,kind);task.ThreadId=kind=="response_output_limit"?"STAGE3_RESUME_FIXTURE":"UI-"+kind;task.TurnId="turn-"+kind;task.Status="실행 중";task.ThreadLearningVersion=2;
                var p=(TaskProcess)typeof(DeskController).GetMethod("BeginProcess",flags)!.Invoke(desk,[task,"Preserve completed artifact and verify remaining work", false] )!;
                if (kind == "response_output_limit") p.OutputLimitContinuations = 2;
                p.Items.Add(new(){Id="written",Title="Completed file",State="verified"});p.Items.Add(new(){Id="remaining",Title="Verify remaining",State="running"});
                File.WriteAllText(Path.Combine(root,kind+".txt"),"WRITE_ONCE");
                p.Operations.Add(new(){Key="write-once",ItemId="written",State="completed",Result="WRITE_ONCE",ResultReceived=true,ExitCode=0,MethodDigest=ExecutionBudget.Digest(["Write-Output WRITE_ONCE","already written"])});
                ChatMessages.Delta(task,"partial","PARTIAL_KEPT · 받은 부분 답변을 보존합니다.");
                var note=(string)typeof(DeskController).Assembly.GetType("Hermes.Core.ResponseInterruption")!.GetMethod("Message",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[kind])!;
                typeof(DeskController).GetMethod("ObserveCompanyUsage",flags)!.Invoke(desk,[new UsageEntry{Id=kind,TaskId=task.Id,TurnId=task.TurnId,At=DateTimeOffset.UtcNow,Source="company-api-response",Status=kind=="response_output_limit"?"incomplete":kind=="response_early_eof"?"unknown":"failed",FailureKind=kind,IncompleteReason=kind=="response_output_limit"?"max_output_tokens":"",Note=note}]);
                ChatMessages.Complete(task,"partial","");
                using(var end=JsonDocument.Parse(JsonSerializer.Serialize(new{threadId=task.ThreadId,turn=new{id=task.TurnId,status="completed"}})))typeof(DeskController).GetMethod("Handle",flags)!.Invoke(desk,[new ServerEvent("turn/completed",end.RootElement)]);
                SelectTask(task);RenderTask();RefreshProcesses();processList.SelectedItem=p;ShowProcess();await Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);UpdateLayout();
                if (kind == "response_output_limit")
                {
                    task.Transcript.Add(new("오류", "stream disconnected before completion: Incomplete response returned, reason: max_output_tokens", DateTime.UtcNow));
                    RenderTask();
                    Check(!Text(MessageStack).Contains("stream disconnected before completion"), "legacy engine error is hidden in the actual chat UI");
                }
                if (kind != "response_output_limit")
                {
                    var errorCard = MessageStack.Children.OfType<Expander>().Last(card =>
                        Text(card.Header as DependencyObject ?? throw new IOException("Error header missing")).Contains("오류 상세 보기"));
                    Check(!errorCard.IsExpanded && errorCard.Content == null, "error is collapsed and its content is not rendered before expansion");
                    errorCard.IsExpanded = true;
                    UpdateLayout();
                }
                var expectedNotice = kind == "response_output_limit" ? note : note.Split(']')[0] + "]";
                Check(Text(MessageStack).Contains("PARTIAL_KEPT")&&Text(MessageStack).Contains(expectedNotice),kind+" actual WPF displays distinct reason and retained partial answer");
                Check(processDetails.Text.Contains(kind)&&processDetails.Text.Contains("remaining")&&!p.Active,kind+" process panel shows interruption category and resume checkpoint");
                Processes_Click(this,new RoutedEventArgs());CaptureProgressVisibility(data,"recovery-"+kind);desk.Save();
            }
        }
        else
        {
            foreach(var task in desk.State.Tasks)
            {
                var p=desk.State.Processes.Single(p=>p.TaskId==task.Id);SelectTask(task);RenderTask();RefreshProcesses();processList.SelectedItem=p;ShowProcess();UpdateLayout();
                Check(Text(MessageStack).Contains("PARTIAL_KEPT")&&p.BlockKind==task.Title&&!p.Active,task.Title+" survives actual application restart without automatic execution");
                Check(File.ReadAllText(Path.Combine(root,task.Title+".txt"))=="WRITE_ONCE"&&p.Items[0].State=="verified",task.Title+" completed artifact and item survive restart");
            }
            var selected=desk.State.Tasks.Single(t=>t.Title=="response_output_limit");var process=desk.State.Processes.Single(p=>p.TaskId==selected.Id);SelectTask(selected);
            bool blocked=false;try{await desk.ResumeSavedProcessAsync(process.Id);}catch(HermesException){blocked=true;}Check(blocked,"restart does not bypass explicit resume requirement");
            if(!Path.GetFileName(fixture).Equals("Hermes.Tests.exe",StringComparison.OrdinalIgnoreCase))throw new IOException("Owned test RPC fixture required");
            await using var rpc=new AppServerClient();await rpc.ConnectAsync(fixture,root,Path.Combine(data,"empty-mock-engine"));
            typeof(DeskController).GetProperty("Engine")!.SetValue(desk,rpc);typeof(DeskController).GetProperty("SubscriptionReady")!.SetValue(desk,true);
            typeof(DeskController).GetField("connectedFingerprint",flags)!.SetValue(desk,desk.State.Provider.Fingerprint);selected.ProviderFingerprint=desk.State.Provider.Fingerprint;
            ((HashSet<string>)typeof(DeskController).GetField("loadedThreads",flags)!.GetValue(desk)!).Add(selected.ThreadId!);
            await desk.ResumeSavedProcessAsync(process.Id,true);
            var args=JsonSerializer.SerializeToElement(new{command="Write-Output WRITE_ONCE",purpose="already written",operationId="write-once"});
            var cached=await (Task<string>)typeof(DeskController).GetMethod("RunLearnedCommand",flags)!.Invoke(desk,[selected,rpc,args,"ui-resume",selected.TurnId])!;
            var stats=await rpc.RequestAsync("fixture/stats",new{});
            Check(process.Active&&WorkQueue.Next(process)?.Id=="remaining"&&stats.GetProperty("commands").GetInt32()==0&&cached.Contains("WRITE_ONCE"),"actual post-restart explicit resume selects remaining work and reuses receipt with zero command executions");
            RenderTask();RefreshProcesses();processList.SelectedItem=process;ShowProcess();Processes_Click(this,new RoutedEventArgs());UpdateLayout();
            Check(Text(MessageStack).Contains("PARTIAL_KEPT")&&File.ReadAllText(Path.Combine(root,selected.Title+".txt"))=="WRITE_ONCE","WPF retains partial answer and completed file after explicit resume");
            CaptureProgressVisibility(data,"recovery-after-restart-resume");var stoppedTurn=selected.TurnId;await desk.StopProcessAsync(process.Id);
            // A manually attached mock engine has no controller notification subscription.
            // Deliver its interruption acknowledgement, just as a connected engine does.
            using(var stopped=JsonDocument.Parse(JsonSerializer.Serialize(new{threadId=selected.ThreadId,turn=new{id=stoppedTurn,status="interrupted"}})))typeof(DeskController).GetMethod("Handle",flags)!.Invoke(desk,[new ServerEvent("turn/completed",stopped.RootElement)]);
        }
        File.WriteAllText(Path.Combine(data,restart?"response-recovery-restart.json":"response-recovery-ui.json"),JsonSerializer.Serialize(new{checks,actualWpf=true,actualApplicationRestart=restart,modelCalls=0,commandExecutions=0,fixtureScope="synthetic host state; real relay protocol separately tested; explicit resume uses local mock RPC"},new JsonSerializerOptions{WriteIndented=true}));
    }
}
