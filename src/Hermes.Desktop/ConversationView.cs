using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Hermes.Core;

namespace Hermes.Desktop;

public partial class MainWindow
{
    private readonly Dictionary<int, TranscriptEntry> renderedEntries = [];
    private string? renderedConversation;
    private int renderedOffset;
    private int? historyEnd;
    private const int ConversationPageSize = 40;
    private void ConversationOlder_Click(object sender, RoutedEventArgs e)
    {
        historyEnd = Math.Max(ConversationPageSize, (historyEnd ?? CurrentTask?.Transcript.Count ?? 0) - ConversationPageSize);
        conversationStamp = ""; RenderConversation(); ConversationScroll.ScrollToTop();
    }
    private void ConversationLatest_Click(object sender, RoutedEventArgs e)
    { historyEnd = null; conversationStamp = ""; RenderConversation(); ConversationScroll.ScrollToEnd(); }
    private void ConversationNewer_Click(object sender, RoutedEventArgs e)
    {
        var count = CurrentTask?.Transcript.Count ?? 0;
        var next = (historyEnd ?? count) + ConversationPageSize;
        historyEnd = next >= count ? null : next;
        conversationStamp = ""; RenderConversation(); ConversationScroll.ScrollToTop();
    }
    private void RenderConversation()
    {
        var entries = CurrentTask?.Transcript ?? [];
        if (renderedConversation != CurrentTask?.Id) historyEnd = null;
        var end = Math.Min(historyEnd ?? entries.Count, entries.Count);
        var offset = Math.Max(0, end - ConversationPageSize);
        var stamp = offset + ":" + end + ":" + (CurrentTask?.Id ?? "empty") + ":" + entries.Count + ":" + string.Join('|', entries.TakeLast(8).Select(e => e.Kind + ":" + e.Text.GetHashCode()));
        if (stamp == conversationStamp) return;
        conversationStamp = stamp;
        var follow = ConversationScroll.ScrollableHeight - ConversationScroll.VerticalOffset < 60;
        OlderMessages.IsEnabled = offset > 0;
        NewerMessages.IsEnabled = end < entries.Count;
        ConversationRange.Text = entries.Count == 0 ? "" : $"{offset + 1}–{end} / {entries.Count}";
        // Slide the bounded window without rebuilding retained messages.
        if (renderedConversation == CurrentTask?.Id && offset > renderedOffset && offset - renderedOffset <= MessageStack.Children.Count)
        {
            var remove = offset - renderedOffset;
            for (var i = 0; i < remove; i++) MessageStack.Children.RemoveAt(0);
            var retained = renderedEntries.Where(kv => kv.Key >= remove).ToList();
            renderedEntries.Clear(); foreach (var kv in retained) renderedEntries[kv.Key - remove] = kv.Value;
            renderedOffset = offset;
        }
        if (renderedConversation != CurrentTask?.Id || renderedOffset != offset || MessageStack.Children.Count > entries.Count)
        { MessageStack.Children.Clear(); renderedEntries.Clear(); renderedConversation = CurrentTask?.Id; renderedOffset = offset; }
        Welcome.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var index = 0;
        foreach (var entry in entries.Skip(offset).Take(end - offset))
        {
            var position = index++;
            if (renderedEntries.TryGetValue(position, out var previous) && previous == entry && position < MessageStack.Children.Count) continue;
            if (entry.Kind == "에이전트 스트림" && previous?.Kind == "에이전트 스트림" && position < MessageStack.Children.Count && MessageStack.Children[position] is Border { Child: TextBox live })
            { live.Text = entry.Text; renderedEntries[position] = entry; continue; }
            UIElement view;
            if (entry.Kind is "사용자" or "에이전트" or "에이전트 스트림")
            {
                UIElement content = entry.Kind == "에이전트 스트림" ? new TextBox { Text = entry.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontSize = 18, BorderThickness = new Thickness(0), Padding = new Thickness(0), Background = Brushes.Transparent } : Markdown(entry.Text);
                var box = new Border { Child = content, Padding = new Thickness(entry.Kind == "사용자" ? 16 : 0), Margin = new Thickness(0, 10, 0, 22), CornerRadius = new CornerRadius(16), Background = entry.Kind == "사용자" ? Brush("Hover") : Brushes.Transparent, HorizontalAlignment = entry.Kind == "사용자" ? HorizontalAlignment.Right : HorizontalAlignment.Stretch, MaxWidth = entry.Kind == "사용자" ? 650 : double.PositiveInfinity };
                view = box;
            }
            else
            {
                var shortText = entry.Text.Split('\n')[0]; if (shortText.Length > 95) shortText = shortText[..95] + "…";
                var header = new DockPanel(); header.Children.Add(MakeIcon(entry.Kind.Contains("명령") ? "code" : entry.Kind.Contains("파일") ? "document" : "chat", 16));
                header.Children.Add(new TextBlock { Text = entry.Kind + "  " + shortText, Foreground = Brush("Muted"), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
                var expander = new Expander { Header = header, Margin = new Thickness(0, 2, 0, 12), IsExpanded = entry.Kind is "오류" or "진행 요약" or "작업 진행" or "계획" or "명령 출력" or "브라우저 소스" };
                if (expander.IsExpanded) expander.Content = Markdown(entry.Text);
                expander.Expanded += (_, _) => { expander.Content ??= Markdown(entry.Text); };
                view = expander;
            }
            if (position < MessageStack.Children.Count) { MessageStack.Children.RemoveAt(position); MessageStack.Children.Insert(position, view); }
            else MessageStack.Children.Add(view);
            renderedEntries[position] = entry;
        }
        while (MessageStack.Children.Count > end - offset) MessageStack.Children.RemoveAt(MessageStack.Children.Count - 1);
        if (follow && historyEnd == null) Dispatcher.BeginInvoke(() => ConversationScroll.ScrollToEnd());
    }
    private StackPanel Markdown(string text)
    {
        var panel = new StackPanel(); var lines = text.Replace("\r\n", "\n").Split('\n'); var code = new List<string>(); string? language = null; var fenceLength = 3;
        void CodeBlock()
        {
            var source = string.Join('\n', code); var body = new StackPanel(); var top = new DockPanel();
            var copy = new Button { Content = "복사", FontSize = 11, Padding = new Thickness(8, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(copy, Dock.Right); top.Children.Add(copy);
            top.Children.Add(new TextBlock { Text = language ?? "", Foreground = Brush("Muted"), Margin = new Thickness(8, 6, 0, 0), FontSize = 11 }); body.Children.Add(top);
            copy.Click += (_, _) => Clipboard.SetText(source);
            body.Children.Add(new TextBox { Text = source, IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 12, BorderThickness = new Thickness(0), Background = Brush("Code"), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(12) });
            panel.Children.Add(new Border { Child = body, Background = Brush("Code"), CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 6, 0, 12) }); code.Clear();
        }
        RichTextBox? prose = null;
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart(); var ticks = trimmed.TakeWhile(c => c == '`').Count();
            if (ticks >= 3 && language == null) { fenceLength = ticks; language = trimmed[ticks..].Trim(); prose = null; continue; }
            if (language != null && ticks >= fenceLength) { CodeBlock(); language = null; continue; }
            if (language != null) { code.Add(line); continue; }
            if (line.Length == 0) { if (prose != null) prose.Document.Blocks.Add(new Paragraph { Margin = new Thickness(0), FontSize = 4 }); continue; }
            var value = line; var size = 20.0; var bold = false;
            if (value.StartsWith("### ", StringComparison.Ordinal)) { value = value[4..]; size = 17; bold = true; }
            else if (value.StartsWith("## ", StringComparison.Ordinal)) { value = value[3..]; size = 20; bold = true; }
            else if (value.StartsWith("# ", StringComparison.Ordinal)) { value = value[2..]; size = 23; bold = true; }
            var document = new FlowDocument { FontFamily = FontFamily, FontSize = size, Foreground = Brush("Ink"), PagePadding = new Thickness(0), LineHeight = size * 1.65 };
            var paragraph = new Paragraph { FontSize = size, LineHeight = size * 1.65, Margin = new Thickness(0, 0, 0, 3), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
            var pattern = @"(\*\*[^*]+\*\*|`[^`]+`|\[[^\]]+\]\([^)]+\))";
            foreach (var part in Regex.Split(value, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100)))
            {
                if (part.StartsWith("**", StringComparison.Ordinal) && part.EndsWith("**", StringComparison.Ordinal)) paragraph.Inlines.Add(new Bold(new Run(part[2..^2])));
                else if (part.StartsWith('`') && part.EndsWith('`') && part.Length >= 2) paragraph.Inlines.Add(new Run(part[1..^1]) { FontFamily = new FontFamily("Consolas"), Background = Brush("Code") });
                else if (part.StartsWith('[') && part.Contains("](", StringComparison.Ordinal) && part.EndsWith(')'))
                {
                    var index = part.IndexOf("](", StringComparison.Ordinal); var link = new Hyperlink(new Run(part[1..index])); var target = part[(index + 2)..^1];
                    link.Click += (_, _) => _ = Guard(() => { OpenLink(target); return Task.CompletedTask; }); paragraph.Inlines.Add(link);
                }
                else paragraph.Inlines.Add(new Run(part));
            }
            if (prose != null) { prose.Document.Blocks.Add(paragraph); continue; }
            document.Blocks.Add(paragraph);
            prose = new RichTextBox { Document = document, IsReadOnly = true, BorderThickness = new Thickness(0), Padding = new Thickness(0), Background = Brushes.Transparent, Foreground = Brush("Ink"), VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, IsDocumentEnabled = true };
            panel.Children.Add(prose);
        }
        if (language != null) CodeBlock();
        return panel;
    }
    private void OpenLink(string target)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http") { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); return; }
        var root = CurrentProject?.Root ?? throw new Hermes.Core.HermesException("파일 링크를 열 프로젝트가 없습니다.");
        var path = target; var colon = path.LastIndexOf(':'); if (colon > 2 && int.TryParse(path[(colon + 1)..], out _)) path = path[..colon];
        path = Safety.Resolve(root, System.IO.Path.IsPathFullyQualified(path) ? System.IO.Path.GetRelativePath(root, path) : path);
        FilePreview.Text = WorkspaceFiles.Read(root, System.IO.Path.GetRelativePath(root, path)); OpenPanel(0);
    }
}

public sealed class CapabilitiesWindow : Window
{
    public CapabilitiesWindow(DeskController desk)
    {
        Title = "Hermes 0.8.11 · 기능 및 연결 상태"; Width = 740; Height = 640; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) }; Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "기능 및 연결 상태", FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        panel.Children.Add(new TextBlock { Text = desk.AccountStatus, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        var rows = new (string Name, string Status)[] {
            ("프로젝트 · 채팅 · 고정 · 검색 · 이름 변경", "로컬 구현 · 앱 이력에 저장"),
            ("실시간 대화 · 실행 중 추가 지시 · 중단 · 재개", "공식 App Server 연결 필요"),
            ("모델 및 추론 수준", "연결된 model/list가 제공한 옵션 사용"),
            ("회사 API · 보호 키 · 사용량", "회사 주소 / 배포 / 인증 설정 필요"),
            ("파일 · Git diff · worktree · AI 검토", "로컬 구현 · AI 검토는 모델 연결 필요"),
            ("이미지 입력 · 스킬 · 대화 분기 · 보관", "공식 엔진 연결 지원"),
            ("터미널 명령", "공식 Windows sandbox · 일회성 명령 · 완료 후 출력"),
            ("MCP", "현재 프로젝트 로컬 서버 · 외부 OAuth 통합은 추가 개발 필요"),
            ("예약", "앱 실행 중 초안 생성 / 알림 · 자동 AI 실행은 추가 개발 필요"),
            ("브라우저", "WebView2 앱 전용 프로필 · 새 채팅에서 연결 · 자동 허용 · 채팅별 페이지 · 탐색/읽기/클릭/입력/JavaScript/업로드"),
            ("데스크톱 컴퓨터 제어", "선택한 Windows 창 · 화면/클릭/입력/스크롤/드래그 · 요청별 승인"),
            ("음성 · 이미지 생성", "지원 provider / 장치 연결 및 추가 개발 필요"),
            ("클라우드 작업 · 원격 모바일 · 공식 프로젝트 동기화", "독립 앱용 공식 연결 계약 및 서비스 접근 미확보"),
            ("플러그인 스토어 · Space / 문서 아티팩트 · Pets", "현재 앱에 서비스 구현 / 연결 없음")
        };
        foreach (var row in rows) { panel.Children.Add(new TextBlock { Text = row.Name, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) }); panel.Children.Add(new TextBlock { Text = row.Status, TextWrapping = TextWrapping.Wrap }); }
        panel.Children.Add(new TextBlock { Text = "독립 Windows 클라이언트입니다. 위 연결되지 않은 기능은 구현 완료로 표시하지 않습니다.", Margin = new Thickness(0, 22, 0, 0), TextWrapping = TextWrapping.Wrap });
    }
}
