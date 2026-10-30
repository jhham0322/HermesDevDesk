using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Hermes.Core;
namespace Hermes.Desktop;
public partial class MainWindow
{
    internal async Task RunToolCardVisibilityChecks(string data, bool restart)
    {
        var checks = new List<string>();
        void Check(bool ok, string label) { if (!ok) throw new IOException(label); checks.Add(label); }
        void Render() { conversationStamp = ""; RenderConversation(); UpdateLayout(); }
        bool Visible(string id) => renderedEntries.Values.Any(e => e.ItemId == id);
        void Toggle(bool on) { ShowProgressSummaries.IsChecked = on; ShowProgressSummaries_Click(this, new RoutedEventArgs()); UpdateLayout(); }
        if (restart)
        {
            var saved = desk.State.Tasks.Single(t => t.Title == "Tool cards fixture"); SelectTask(saved); Render();
            Check(!desk.State.Desktop.ShowProgressSummaries && ShowProgressSummaries.IsChecked == false, "hidden setting survives actual process restart");
            Check(MessageStack.Children.Count == 5 && !Visible("stream-live"), "restart hides entire tool cards but preserves attention and answers");
            Check(saved.Transcript.Any(t => t.ItemId == "stream-live" && t.Text == "LATEST_HIDDEN_OUTPUT"), "restart retains streamed tool output");
            Toggle(true); ConversationOlder_Click(this, new RoutedEventArgs());
            Check(!saved.Transcript.Where(ProgressMessages.IsHiddenWhenProgressOff).Any(e => e.Text.Length == 0), "reopening does not delete raw tool entries");
            Toggle(false);
        }
        else
        {
            Check(!ProgressMessages.IsHiddenWhenProgressOff(TranscriptPresentation.Host("입력 필요", "NEEDS_INPUT")), "host input-needed report remains visible");
            Check(!ProgressMessages.IsHiddenWhenProgressOff(new("명령 결과", "legacy command\nexit: 1", DateTime.UtcNow)), "legacy nonzero command result remains visible");
            Check(!ProgressMessages.IsHiddenWhenProgressOff(new("MCP 도구", """{"status":"failed","error":{"message":"observed"}}""", DateTime.UtcNow)), "native MCP failed status remains visible");
            Check(!ProgressMessages.IsHiddenWhenProgressOff(new("MCP 도구", """{"result":{"isError":true}}""", DateTime.UtcNow)), "native MCP error result remains visible");
            var root = Path.Combine(data, "owned-project"); Directory.CreateDirectory(root);
            var project = desk.Register(root); Projects.SelectedItem = project;
            var task = desk.NewTask(project, "Tool cards fixture"); task.ThreadId = "tool-view-thread"; task.Status = "실행 중"; SelectTask(task);
            void Event(string method, string json) { using var d = JsonDocument.Parse(json); typeof(DeskController).GetMethod("Handle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(desk, new object[] { new ServerEvent(method, d.RootElement) }); Render(); }
            task.Transcript.AddRange(new[] {
                new TranscriptEntry("사용자", "USER_REMAINS", DateTime.UtcNow, "user"),
                new TranscriptEntry("에이전트", "FINAL_REMAINS", DateTime.UtcNow, "answer"),
                new TranscriptEntry("오류", "ERROR_REMAINS", DateTime.UtcNow, "error"),
                new TranscriptEntry("승인 요청", "APPROVAL_REMAINS", DateTime.UtcNow, "approval") });
            ProgressMessages.Complete(task, "summary", "0", "PUBLIC_PROGRESS");
            Event("item/started", """{"threadId":"tool-view-thread","item":{"type":"dynamicToolCall","id":"command","tool":"hermes_command"}}""");
            Check(Visible("tool-progress:command"), "tool card renders while reports enabled");
            var index = renderedEntries.Single(e => e.Value.ItemId == "tool-progress:command").Key;
            Check(!((Expander)MessageStack.Children[index]).IsExpanded, "command card retains collapsed default");
            Toggle(false);
            Check(MessageStack.Children.Count == 4 && !Visible("tool-progress:command"), "uncheck removes tool card header and body while preserving answer error approval user");
            Event("item/started", """{"threadId":"tool-view-thread","item":{"type":"dynamicToolCall","id":"late","tool":"hermes_memory"}}""");
            task.Transcript.Add(new TranscriptEntry("명령 출력", "LATEST_HIDDEN_OUTPUT", DateTime.UtcNow, "stream-live") { Origin = "도구: commandExecution" });
            task.Transcript.Add(new TranscriptEntry("MCP 도구", "MCP_RESULT", DateTime.UtcNow, "mcp") { Origin = "도구: MCP/fixture/read", Stage = "검증" });
            task.Transcript.Add(new TranscriptEntry("명령 결과", "LEGACY_COMMAND_RESULT", DateTime.UtcNow, "legacy")); Render();
            Check(MessageStack.Children.Count == 4 && !Visible("stream-live") && !Visible("mcp") && !Visible("legacy"), "new streaming MCP native and legacy tool entries remain hidden");
            Event("item/completed", """{"threadId":"tool-view-thread","item":{"type":"dynamicToolCall","id":"command","tool":"hermes_command","success":true,"status":"completed"}}""");
            Check(!Visible("tool-progress:command") && task.Status == "실행 중", "hiding does not cancel execution or reveal successful completion card");
            Event("item/completed", """{"threadId":"tool-view-thread","item":{"type":"dynamicToolCall","id":"late","tool":"hermes_memory","success":false,"status":"completed","contentItems":[{"type":"inputText","text":"OBSERVED_FAILURE"}]}}""");
            Check(Visible("tool-progress:late") && MessageStack.Children.Count == 5, "tool failure arriving while hidden remains visible");
            var count = task.Transcript.Count;
            for (var i = 0; i < 3; i++) { Toggle(true); Check(Visible("tool-progress:command") && Visible("stream-live"), "toggle restores complete cards " + i); Toggle(false); Check(MessageStack.Children.Count == 5 && task.Transcript.Count == count, "repeated toggle preserves raw records " + i); }
            Toggle(true); index = renderedEntries.Single(e => e.Value.ItemId == "tool-progress:command").Key;
            ((Expander)MessageStack.Children[index]).IsExpanded = true; Toggle(false); Toggle(true);
            index = renderedEntries.Single(e => e.Value.ItemId == "tool-progress:command").Key;
            Check(((Expander)MessageStack.Children[index]).IsExpanded, "explicit command expansion survives hide and restore");
            CaptureProgressVisibility(data, "tools-visible"); Toggle(false); CaptureProgressVisibility(data, "tools-hidden");
            for (var i = 0; i < 85; i++) task.Transcript.Add(new TranscriptEntry("명령 출력", "BULK_OUTPUT_" + i, DateTime.UtcNow, "bulk-" + i)); Render();
            Check(MessageStack.Children.Count == 5 && !OlderMessages.IsEnabled, "hidden cards never crowd out answers or create empty pages");
            Toggle(true); ConversationOlder_Click(this, new RoutedEventArgs()); Check(NewerMessages.IsEnabled, "restored tool cards paginate normally");
            Toggle(false); Check(MessageStack.Children.Count == 5 && !NewerMessages.IsEnabled, "hide resets old-page position using same tool filter");
            var other = desk.NewTask(project, "Other tool fixture"); other.Transcript.Add(new("에이전트", "OTHER_ANSWER", DateTime.UtcNow)); other.Transcript.Add(new("명령 출력", "OTHER_TOOL", DateTime.UtcNow)); SelectTask(other); Render();
            Check(MessageStack.Children.Count == 1 && ShowProgressSummaries.IsChecked == false, "new chat inherits hidden preference");
            SelectTask(task); Render(); Check(MessageStack.Children.Count == 5, "returning to saved conversation preserves filter");
            task.Status = "대기"; desk.Save(); var reopened = new MainWindow(data); var saved = reopened.desk.State.Tasks.Single(t => t.Id == task.Id); reopened.SelectTask(saved); reopened.UpdateLayout();
            Check(reopened.MessageStack.Children.Count == 5 && saved.Transcript.Count == task.Transcript.Count, "fresh window reopens hidden cards without losing transcript");
            await reopened.ShutdownController(); reopened.closing = true; reopened.Close();
            Check(new StateStore(data).Load().Desktop.ShowProgressSummaries == false, "hidden preference persisted for actual restart check");
        }
        File.WriteAllText(Path.Combine(data, restart ? "tool-card-restart.json" : "tool-card-visibility.json"), JsonSerializer.Serialize(new { checks, realModelCalls = 0, commandsExecuted = 0 }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
