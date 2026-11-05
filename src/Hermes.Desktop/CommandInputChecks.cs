using System.IO;
using System.Text.Json;
using System.Windows;
using Hermes.Core;
namespace Hermes.Desktop;
public partial class MainWindow
{
    internal async Task RunCommandInputChecks(string data, string fixtureExe, bool keepOpen)
    {
        var checks=new List<string>();void Check(bool ok,string label){if(!ok)throw new IOException(label);checks.Add(label);}
        var root=Path.Combine(data,"owned-command-project");Directory.CreateDirectory(root);var project=desk.Register(root);Projects.SelectedItem=project;desk.State.CodexPath=fixtureExe;
        SetInspector(true); InspectorTabs.SelectedItem=InspectorTabs.Items.OfType<System.Windows.Controls.TabItem>().Single(t => Equals(t.Header,"명령"));
        CommandText.Text="FIXTURE_SUCCESS";await RunCommandInputAsync((_,_)=>false);
        Check(CommandText.Text=="FIXTURE_SUCCESS"&&!commandSubmitting&&CommandRunButton.IsEnabled,"confirmation cancellation preserves input and resets submit state");
        CommandText.Text="FIXTURE_SUCCESS";var pending=RunCommandInputAsync((_,_)=>true);
        while(!terminal.Running)await Task.Delay(10);
        await RunCommandInputAsync((_,_)=>throw new IOException("Repeated click opened another confirmation"));
        await pending;await System.Windows.Threading.Dispatcher.Yield();
        Check(CommandText.Text==""&&CommandOutput.Text.Contains("exit=0"),"accepted successful command clears input and displays result");
        Check(CommandOutput.Text.Split("FIXTURE_RESULT").Length==2,"result is displayed once rather than duplicated");
        CommandText.Text="FIXTURE_SUCCESS";var revision=commandInputRevision;
        var edited=ExecuteCommandInputAsync(fixtureExe,root,CommandText.Text,revision);CommandText.Text="NEW_UNSENT_DRAFT";
        await edited;Check(CommandText.Text=="NEW_UNSENT_DRAFT","editing during connection preserves the new draft");
        CommandText.Text="FIXTURE_SUCCESS";revision=commandInputRevision;
        var retyped=ExecuteCommandInputAsync(fixtureExe,root,CommandText.Text,revision);CommandText.Clear();CommandText.Text="FIXTURE_SUCCESS";
        await retyped;Check(CommandText.Text=="FIXTURE_SUCCESS","same text retyped during connection is still an unsent draft");
        CommandText.Text="KEEP_ON_FAILURE";try{await ExecuteCommandInputAsync("missing-owned-fixture.exe",root,CommandText.Text,commandInputRevision);}catch(Exception){}
        Check(CommandText.Text=="KEEP_ON_FAILURE","connection failure preserves unsent input");
        CommandText.Text="FIXTURE_WAIT";var stopped=RunCommandInputAsync((_,_)=>true);
        var deadline=DateTime.UtcNow.AddSeconds(15); while(CommandText.Text.Length>0 && !stopped.IsCompleted && DateTime.UtcNow<deadline)await Task.Delay(10); if(CommandText.Text.Length>0)throw new IOException("Dispatch did not clear the input before deadline");
        CommandText.Text="NEXT_DRAFT_AFTER_STOP";await terminal.StopAsync();try{await stopped;}catch(IOException){}
        Check(CommandText.Text=="NEXT_DRAFT_AFTER_STOP"&&CommandOutput.Text.Contains("FIXTURE_WAIT")&&CommandRunButton.IsEnabled,"Stop preserves new input and the submitted-command record");
        CommandText.Text="FIXTURE_NONZERO";await RunCommandInputAsync((_,_)=>true);await System.Windows.Threading.Dispatcher.Yield();
        Check(CommandText.Text.Length==0&&CommandOutput.Text.Contains("exit=7"),"known nonzero result is shown without restoring a command for accidental replay");
        CommandText.Text="첫째 줄\n둘째 줄";Check(CommandText.AcceptsReturn&&CommandText.Text.Contains('\n'),"multiline Enter remains draft text, not an automatic command submission");
        CommandText.Clear();Footer.Text="명령 입력 회귀 통과 · 합성 엔진만 사용 · 실제 명령 실행 0회";
        File.WriteAllText(Path.Combine(data,"command-input-report.json"),JsonSerializer.Serialize(new{passed=true,checks,syntheticOnly=true,realCommands=0,realModelRequests=0}));
        if(!keepOpen)return;
    }
}
