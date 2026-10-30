using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
namespace Hermes.Desktop;
public partial class MainWindow
{
    internal async Task RunConversationTitleChecks(string data)
    {
        var checks = new List<string>();
        void Check(bool ok, string label) { if (!ok) throw new IOException(label); checks.Add(label); }
        var root = Path.Combine(data, "owned-project"); Directory.CreateDirectory(root);
        var project = desk.Register(root); Projects.SelectedItem = project;
        SetSidebar(false); SetInspector(false); Width = 900; Height = 620;
        var titles = new[] { string.Concat(Enumerable.Repeat("긴 한국어 제목의 전체 내용을 확인합니다. ", 35)), new string('W', 650), "첫 번째 줄\n두 번째 줄\r\nthird line " + new string('x', 400) };
        var previousClipboard = Clipboard.GetDataObject();
        try
        {
            foreach (var title in titles)
            {
                var task = desk.NewTask(project, "initial"); SetInitialConversationTitle(task, title); desk.Save(); SelectTask(task);
                await Dispatcher.InvokeAsync(() => { }); UpdateLayout();
                Check(TaskTitle.Text == title && task.Title == title, "full initial title stored and rendered: " + checks.Count);
                Check(TaskTitle.TextWrapping == TextWrapping.Wrap && TaskTitle.ActualWidth <= TitleScroll.ActualWidth + 1, "narrow window wraps title within available width: " + checks.Count);
                for (var i = 0; i < 3; i++)
                {
                    TitleExpand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); UpdateLayout();
                    Check(double.IsPositiveInfinity(TaskTitle.MaxHeight), "expand exposes complete text: " + checks.Count);
                    RenderTask(); Check(double.IsPositiveInfinity(TaskTitle.MaxHeight), "refresh preserves expansion: " + checks.Count);
                    TitleExpand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); UpdateLayout();
                    Check(TaskTitle.MaxHeight == 78, "collapse restores bounded preview: " + checks.Count);
                }
                TitleExpand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); UpdateLayout();
                CaptureProgressVisibility(data, "title-expanded-top-" + checks.Count);
                TitleScroll.ScrollToEnd(); await Dispatcher.InvokeAsync(() => { }); UpdateLayout();
                Check(TitleScroll.VerticalOffset >= TitleScroll.ScrollableHeight - 1, "expanded title can scroll to its final line: " + checks.Count);
                TitleCopy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(Clipboard.GetText() == title, "clipboard equals full original including line breaks: " + checks.Count);
                Check(desk.Store.Load().Tasks.Single(t => t.Id == task.Id).Title == title, "saved full title survives reload: " + checks.Count);
                CaptureProgressVisibility(data, "conversation-title-" + checks.Count);
            }
            var other = desk.NewTask(project, "another conversation"); SelectTask(other); UpdateLayout();
            Check(TaskTitle.Text == other.Title && TaskTitle.MaxHeight == 78, "conversation switch updates title and resets expansion");
            other.Title = "renamed title"; RenderTask(); TitleCopy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Clipboard.GetText() == other.Title && TaskTitle.Text == other.Title, "rename updates displayed and copied title");
            Check(desk.State.Tasks.Any(t => t.Title.Length > 600), "long titles remain intact after switching conversations");
        }
        finally { if (previousClipboard != null) Clipboard.SetDataObject(previousClipboard, true); else Clipboard.Clear(); }
        File.WriteAllText(Path.Combine(data, "conversation-title-report.json"), JsonSerializer.Serialize(new { checks, realModelCalls = 0, isolatedProfile = true }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
