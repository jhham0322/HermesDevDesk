using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hermes.Core;

namespace Hermes.Desktop;
public partial class MainWindow
{
    internal async Task RunConversationPerformanceChecks(string data)
    {
        var checks = new List<string>();
        void Check(bool condition, string label) { if (!condition) throw new IOException(label); checks.Add(label); }
        var root = Path.Combine(data, "owned-project"); Directory.CreateDirectory(root);
        var project = desk.Register(root);
        Projects.SelectedItem = project;
        var task = desk.NewTask(project, "긴 대화 성능 검증");
        for (var i = 0; i < 5000; i++) task.Transcript.Add(new(i % 2 == 0 ? "사용자" : "에이전트", $"메시지 {i + 1}\n**긴 대화**에서도 최근 메시지 화면을 유지합니다.\n코드와 링크를 확인하세요.", DateTime.UtcNow));
        Tasks.ItemsSource = new[] { task }; Tasks.SelectedItem = task;
        conversationStamp = "";
        var timer = Stopwatch.StartNew(); RenderConversation(); UpdateLayout(); timer.Stop();
        var initialMs = timer.ElapsedMilliseconds;
        Check(MessageStack.Children.Count == 40, "5000 messages produce only 40 message views");
        var retained = MessageStack.Children[1];
        task.Transcript.Add(new("에이전트 스트림", "실시간 응답", DateTime.UtcNow));
        timer.Restart(); RenderConversation(); UpdateLayout(); timer.Stop(); var appendMs = timer.ElapsedMilliseconds;
        Check(ReferenceEquals(retained, MessageStack.Children[0]), "append retains existing message views");
        var live = MessageStack.Children[^1];
        task.Transcript[^1] = task.Transcript[^1] with { Text = "실시간 응답 업데이트" };
        conversationStamp = ""; RenderConversation();
        Check(ReferenceEquals(live, MessageStack.Children[^1]), "stream delta updates existing control");
        ConversationOlder_Click(this, new RoutedEventArgs());
        Check(historyEnd == 4961 && MessageStack.Children.Count == 40, "older page remains bounded");
        for (var i = 0; i < 125; i++) ConversationOlder_Click(this, new RoutedEventArgs());
        Check(renderedOffset == 0 && !OlderMessages.IsEnabled, "oldest history remains accessible");
        ConversationLatest_Click(this, new RoutedEventArgs());
        Check(historyEnd == null && NewerMessages.IsEnabled == false, "latest navigation restores following");
        var markdown = Markdown(string.Join('\n', Enumerable.Repeat("연속된 문단 본문입니다.", 1000)));
        Check(markdown.Children.Count == 1, "1000 prose lines share one text control");
        await Dispatcher.InvokeAsync(() => { }); UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(data, "conversation.png"))) encoder.Save(file);
        File.WriteAllText(Path.Combine(data, "conversation-performance.json"), JsonSerializer.Serialize(new { checks, messages = task.Transcript.Count, realizedViews = MessageStack.Children.Count, initialMs, appendMs, realModelCalls = 0 }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
