using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Hermes.Core;
namespace Hermes.Desktop;
public partial class MainWindow
{
    private void CaptureProgressVisibility(string data, string state)
    {
        UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(data, "progress-" + state + ".png")); encoder.Save(file);
    }
    internal Task RunProgressRestartChecks(string data)
    {
        if (ShowProgressSummaries.IsChecked != false || desk.State.Desktop.ShowProgressSummaries) throw new IOException("Hidden checkbox preference did not survive process restart");
        var task = desk.State.Tasks.Single(t => t.Title == "New conversation");
        Projects.SelectedItem = desk.State.Projects.Single(p => p.Id == task.ProjectId);
        Tasks.ItemsSource = new[] { task }; Tasks.SelectedItem = task;
        conversationStamp = ""; RenderConversation();
        if (MessageStack.Children.Count != 1 || !task.Transcript.Any(t => t.Text == "NEW_ANSWER") || task.Transcript.Count != 87) throw new IOException("Restart did not restore filtered conversation with intact original transcript");
        File.WriteAllText(Path.Combine(data, "progress-restart.json"), JsonSerializer.Serialize(new { checkboxHidden = true, visibleMessages = MessageStack.Children.Count, preservedEntries = task.Transcript.Count, realModelCalls = 0 }));
        return Task.CompletedTask;
    }
    internal async Task RunProgressVisibilityChecks(string data)
    {
        var checks = new List<string>();
        void Check(bool condition, string label) { if (!condition) throw new IOException(label); checks.Add(label); }
        void Toggle(bool value) { ShowProgressSummaries.IsChecked = value; ShowProgressSummaries.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }
        Check(new DesktopPreferences().ShowProgressSummaries && JsonSerializer.Deserialize<AppState>("{}")!.Desktop.ShowProgressSummaries, "new and legacy state default to visible");
        Check(ShowProgressSummaries.IsChecked == true, "actual checkbox starts enabled");
        var root = Path.Combine(data, "owned-project"); Directory.CreateDirectory(root);
        var project = desk.Register(root); Projects.SelectedItem = project;
        var task = desk.NewTask(project, "Progress visibility fixture");
        Tasks.ItemsSource = new[] { task }; Tasks.SelectedItem = task;
        ProgressMessages.Delta(task, "owned", "summary", "Public progress");
        task.Transcript.Add(new TranscriptEntry("에이전트", "ANSWER_REMAINS", DateTime.UtcNow));
        conversationStamp = ""; RenderConversation();
        Check(MessageStack.Children.Count == 2, "existing summary and answer render when enabled");
        Toggle(false);
        Check(MessageStack.Children.Count == 1 && task.Transcript.Count == 2, "click hides summary without deleting transcript or answer");
        ProgressMessages.Delta(task, "owned", "summary", " continued");
        conversationStamp = ""; RenderConversation();
        Check(MessageStack.Children.Count == 1, "streaming summary remains hidden");
        Toggle(true);
        Check(MessageStack.Children.Count == 2 && task.Transcript[0].Text.Contains("continued"), "toggle during stream restores latest public summary");
        Toggle(false); ProgressMessages.Complete(task, "owned", "summary", "Completed public progress");
        conversationStamp = ""; RenderConversation();
        Check(MessageStack.Children.Count == 1 && task.Transcript[1].Text == "ANSWER_REMAINS", "completion while hidden preserves answer");
        desk.Save();
        var restored = new StateStore(data).Load();
        Check(!restored.Desktop.ShowProgressSummaries && restored.Tasks.Single(t => t.Id == task.Id).Transcript.Count == 2, "restart state retains hidden preference and complete transcript");
        var newer = desk.NewTask(project, "New conversation");
        Tasks.ItemsSource = new[] { task, newer }; Tasks.SelectedItem = newer;
        ProgressMessages.Complete(newer, "new", "summary", "New public progress");
        newer.Transcript.Add(new TranscriptEntry("에이전트", "NEW_ANSWER", DateTime.UtcNow));
        conversationStamp = ""; RenderConversation();
        Check(MessageStack.Children.Count == 1 && ShowProgressSummaries.IsChecked == false, "new conversation inherits hidden preference");
        for (var i = 0; i < 85; i++) ProgressMessages.Complete(newer, "bulk" + i, "summary", "Summary " + i);
        conversationStamp = ""; RenderConversation();
        Check(MessageStack.Children.Count == 1 && !OlderMessages.IsEnabled, "hidden summaries do not crowd out answers or create empty pages");
        Toggle(true); ConversationOlder_Click(this, new RoutedEventArgs());
        Check(MessageStack.Children.Count == 40 && NewerMessages.IsEnabled, "visible summaries paginate normally");
        Toggle(false);
        Check(MessageStack.Children.Count == 1 && !NewerMessages.IsEnabled, "toggle from older page resets filtered pagination");
        Toggle(true); desk.Save();
        Check(new StateStore(data).Load().Desktop.ShowProgressSummaries, "enabled choice also persists");
        CaptureProgressVisibility(data, "visible");
        Toggle(false); CaptureProgressVisibility(data, "hidden");
        await Dispatcher.InvokeAsync(() => { }); UpdateLayout();
        File.WriteAllText(Path.Combine(data, "progress-visibility.json"), JsonSerializer.Serialize(new { checks, realModelCalls = 0, dataDeleted = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
