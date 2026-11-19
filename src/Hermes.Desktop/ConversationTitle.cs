using System.Globalization;
using System.Windows;
namespace Hermes.Desktop;
public partial class MainWindow
{
    private static void SetInitialConversationTitle(Hermes.Core.WorkItem task, string prompt)
    { if (task.Transcript.Count == 0) task.Title = Hermes.Core.Safety.Redact(prompt); }
    private static string ShortTitle(string title)
    {
        var line = title.AsSpan().Trim();
        var end = line.IndexOfAny('\r', '\n');
        if (end >= 0) line = line[..end].TrimEnd();
        var length = 0;
        for (var count = 0; count < 36 && length < line.Length; count++)
            length += StringInfo.GetNextTextElementLength(line[length..]);
        return length == title.AsSpan().Trim().Length ? line.ToString() : line[..length].ToString() + "…";
    }
    private string? titleTaskId;
    private bool titleExpanded;
    private void UpdateConversationTitle()
    {
        if (titleTaskId != CurrentTask?.Id) { titleTaskId = CurrentTask?.Id; titleExpanded = false; TitleScroll.ScrollToTop(); }
        var full = CurrentTask?.Title ?? "프로젝트를 등록하고 작업을 시작하세요";
        var shortTitle = ShortTitle(full);
        TaskTitle.Text = titleExpanded ? full : shortTitle;
        TaskTitle.MaxHeight = titleExpanded ? double.PositiveInfinity : 78;
        TitleExpand.Content = titleExpanded ? "제목 접기" : "제목 펼치기";
        TitleExpand.IsEnabled = CurrentTask != null && shortTitle != full;
        TitleCopy.IsEnabled = CurrentTask != null;
        TaskTitle.TextTrimming = titleExpanded ? TextTrimming.None : TextTrimming.CharacterEllipsis;
        TaskTitle.TextWrapping = titleExpanded ? TextWrapping.Wrap : TextWrapping.NoWrap;
    }
    private void TitleExpand_Click(object sender, RoutedEventArgs e)
    { titleExpanded = !titleExpanded; TitleScroll.ScrollToTop(); UpdateConversationTitle(); }
    private async void TitleCopy_Click(object sender, RoutedEventArgs e) => await Guard(() =>
    {
        if (CurrentTask is { } task) { Clipboard.SetText(task.Title); Footer.Text = "전체 제목을 복사했습니다."; }
        return Task.CompletedTask;
    }, "conversation-title-copy");
}
