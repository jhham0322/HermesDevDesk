using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Hermes.Core;
namespace Hermes.Desktop;
public partial class MainWindow
{
 internal async Task RunUiResponseBenchmark(string data)
 {
  draftTimer.Stop();draftSaveTimer.Stop();refreshTimer.Stop();
  var root=Path.Combine(data,"owned-project");Directory.CreateDirectory(root);var project=desk.Register(root);Projects.SelectedItem=project;
  for(int i=0;i<64;i++)Safety.RegisterSecret("SYNTHETIC_UI_PERF_CREDENTIAL_"+i.ToString("0000"));
  var measurements=new List<object>();
  async Task<object> Measure(string name,Action action)
  {
   var times=new List<double>();var input=new List<double>();
   for(int i=0;i<5;i++)
   {
    var clock=Stopwatch.StartNew();double work=0;
    var op=Dispatcher.InvokeAsync(()=>{var sw=Stopwatch.StartNew();action();UpdateLayout();work=sw.Elapsed.TotalMilliseconds;},DispatcherPriority.Normal);
    var probe=Dispatcher.InvokeAsync(()=>clock.Elapsed.TotalMilliseconds,DispatcherPriority.Input);
    await op;input.Add(await probe);times.Add(work);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
   }
   return new{name,medianWorkMs=times.Order().ElementAt(2),maxWorkMs=times.Max(),medianInputDispatchMs=input.Order().ElementAt(2),maxInputDispatchMs=input.Max(),samples=times};
  }
  foreach(var count in new[]{20,2000,10000})
  {
   desk.State.Tasks.Clear();openChats.Clear();closedChats.Clear();renderedConversation=null;conversationStamp="";
   var task=new WorkItem{ProjectId=project.Id,Title="Synthetic UI "+count};var other=new WorkItem{ProjectId=project.Id,Title="Other synthetic chat"};desk.State.Tasks.Add(task);desk.State.Tasks.Add(other);
   for(int i=0;i<count;i++)task.Transcript.Add(new("에이전트",i+": "+new string('x',256),DateTime.UtcNow));
   Refresh();Tasks.SelectedItem=task;RenderTask();desk.Store.Save(desk.State);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
   var phases=new List<object>();
   phases.Add(await Measure("open",()=>{Tasks.SelectedItem=other;Tasks.SelectedItem=task;}));
   phases.Add(await Measure("input-scroll",()=>{Prompt.AppendText("x");ConversationScroll.ScrollToVerticalOffset(Math.Max(0,ConversationScroll.VerticalOffset-20));}));
   task.Transcript[^1]=task.Transcript[^1] with{Kind="에이전트 스트림"};Refresh();
   phases.Add(await Measure("stream-refresh",()=>{task.Transcript[^1]=task.Transcript[^1] with{Text=task.Transcript[^1].Text+" delta"};Refresh();}));
   phases.Add(await Measure("save-only",()=>desk.Store.Save(desk.State)));
   phases.Add(await Measure("save-ui-refresh",()=>desk.Save()));
   if(MessageStack.Children.Count>40||Prompt.Text.Length==0||desk.Store.Load().Tasks.Single(t=>t.Id==task.Id).Transcript.Count!=count)throw new IOException("UI responsiveness fixture lost state or exceeded realized view bound");
   measurements.Add(new{messages=count,realizedViews=MessageStack.Children.Count,phases});
  }
  File.WriteAllText(Path.Combine(data,"ui-response-benchmark.json"),JsonSerializer.Serialize(new{syntheticOnly=true,realWpfWindow=true,nativeMouse=false,syntheticRegisteredSecrets=64,realModelCalls=0,measurements},new JsonSerializerOptions{WriteIndented=true}));
 }
}
