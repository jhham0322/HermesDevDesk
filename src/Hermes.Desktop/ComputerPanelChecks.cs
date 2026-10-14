using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Hermes.Core;

namespace Hermes.Desktop;

public partial class MainWindow
{
    public async Task RunComputerPanelChecks(string data)
    {
        var results = new List<string>(); Process? fixture = null; WorkItem? task = null;
        void Check(bool ok, string label) { if (!ok) throw new IOException(label); results.Add("PASS " + label); }
        void Click(string caption)
        {
            var tab = InspectorTabs.Items.OfType<TabItem>().Last();
            var panel = (StackPanel)((ScrollViewer)tab.Content).Content;
            var buttons = panel.Children.OfType<Button>().Concat(panel.Children.OfType<WrapPanel>().SelectMany(p => p.Children.OfType<Button>()));
            buttons.Single(b => Equals(b.Content, caption)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        JsonElement Args(object args) => JsonSerializer.SerializeToElement(args);
        try
        {
            var root = Path.Combine(data,"panel-owned-project"); Directory.CreateDirectory(root);
            var project = desk.Register(root); task = desk.NewTask(project,"Owned panel test"); SelectTask(task);
            Check(!computerArmed&&!computerAutomatic,"scope and auto mode initially revoked");
            computerEnable.IsChecked=true;computerEnable.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var fixtureRoot=Path.Combine(root,"fixture");Directory.CreateDirectory(fixtureRoot);
            var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in new[]{"--computer-fixture",fixtureRoot,Environment.ProcessId.ToString()})info.ArgumentList.Add(arg);
            fixture=Process.Start(info)??throw new IOException("Panel fixture start failed");
            var line=await fixture.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var target=JsonSerializer.Deserialize<ComputerWindow>(line??"")??throw new IOException("Panel handshake failed");
            computerCandidates.ItemsSource=new[]{target};computerCandidates.SelectedItem=target;Click("선택 창을 대상에 추가");
            Check(computerWindows.Count==1&&!computerArmed,"panel allowlist adds only owned fixture and stays disarmed");
            computerAuto.IsChecked=true;computerAuto.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Click("선택한 창 제어 허용");
            Check(computerArmed&&computerAutomatic&&computerProjectId==project.Id,"programmatic panel events explicitly arm session-scoped auto mode");
            // Synthetic running task/delivery acknowledgements only. No model connection or credentials.
            task.ThreadComputerEnabled=true;task.Status="실행 중";task.TurnId="owned-panel-local-turn";
            var allowed=computerWindows.Single();
            var inspected=await ComputerActionAsync(task,Args(new{action="inspect",windowId=allowed.Id}));
            Check(inspected.ImageUrl!=null&&inspected.ContentItems.Length==2,"actual panel tool inspect returns official text/image content");
            var pending=pendingComputerDelivery??throw new IOException("Missing pending delivery");
            deliveredComputerFrame=pending.Frame;deliveredComputerWindow=pending.WindowId;deliveredComputerAt=DateTimeOffset.UtcNow;pendingComputerDelivery=null;
            var waited=await ComputerActionAsync(task,Args(new{action="wait_for_change",windowId=allowed.Id,timeoutMs=2000}));
            File.WriteAllText(Path.Combine(data,"panel-wait-metadata.json"),JsonSerializer.Serialize(new { imagePresent=waited.ImageUrl!=null,observation=waited.Observation, reason=JsonDocument.Parse(waited.Text).RootElement.Text("reason") }));
            Check(waited.ImageUrl==null&&waited.Observation?.Captures>=3,"actual panel wait tool returns timeout metadata with no unchanged image");
            try {await ComputerActionAsync(task,Args(new{action="inspect",windowId=Guid.NewGuid().ToString("N")}));Check(false,"unapproved window accepted");}
            catch(IOException){Check(true,"panel rejects unknown window before worker launch");}
            computerLifecycle.Add("new-command");
            var lifecycle=await ComputerActionAsync(task,Args(new{action="wait_for_change",windowId=allowed.Id,timeoutMs=2000}));
            Check(lifecycle.ImageUrl==null&&lifecycle.Text.Contains("new-command"),"new command independently wakes panel tool without waiting for pixels");
            var operation=ComputerActionAsync(task,Args(new{action="wait_for_change",windowId=allowed.Id,timeoutMs=30000}));
            await Task.Delay(700);computerAuto.IsChecked=false;computerAuto.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            try{await operation;Check(false,"revoked wait returned success");}catch(Exception ex)when(ex is IOException or OperationCanceledException){Check(!computerArmed&&!computerAutomatic&&deliveredComputerFrame==null,"auto-mode withdrawal stops pending capture and clears delivery baseline");}
            computerAuto.IsChecked=true;computerAuto.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Click("선택한 창 제어 허용");
            File.WriteAllText(Path.Combine(fixtureRoot,"blank-capture.signal"),"owned blank fixture only");await Task.Delay(400);
            try{await ComputerActionAsync(task,Args(new{action="wait_for_change",windowId=allowed.Id,timeoutMs=1200}));Check(false,"blank frame returned");}catch(IOException ex)when(ex.Message.StartsWith("CaptureBlockedOrBlank")){Check(!computerArmed&&!computerAutomatic&&deliveredComputerFrame==null,"blocked/blank capture revokes session and prevents automatic retries");}
            Report(true,"");
        }
        catch(Exception ex){Report(false,ex.Message);throw;}
        finally
        {
            StopComputer();if(task!=null)task.Status="완료";
            if(fixture!=null){try{if(!fixture.HasExited){fixture.CloseMainWindow();if(!fixture.WaitForExit(2000))fixture.Kill(true);}}finally{fixture.Dispose();}}
        }
        void Report(bool passed,string error)=>File.WriteAllText(Path.Combine(data,"computer-panel-report.json"),JsonSerializer.Serialize(new{passed,error,results,programmaticUiEvents=true,nativeCaptureUsed=true,syntheticTaskAndDeliveryAck=true,realModelCalls=0,nativeInputEvents=0,realProviderLoopVerified=false,framesPersisted=0},new JsonSerializerOptions{WriteIndented=true}));
    }
}
