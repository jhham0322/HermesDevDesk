using System.Windows;
namespace Hermes.Desktop;
public partial class MainWindow
{
    private static void SetInitialConversationTitle(Hermes.Core.WorkItem task, string prompt)
    { if (task.Transcript.Count == 0) task.Title = Hermes.Core.Safety.Redact(prompt); }
    private string? titleTaskId;
    private bool titleExpanded;
    private void UpdateConversationTitle()
    {
        if (titleTaskId != CurrentTask?.Id) { titleTaskId = CurrentTask?.Id; titleExpanded = false; TitleScroll.ScrollToTop(); }
        TaskTitle.Text = CurrentTask?.Title ?? "프로젝트를 등록하고 작업을 시작하세요";
        TaskTitle.MaxHeight = titleExpanded ? double.PositiveInfinity : 78;
        TitleExpand.Content = titleExpanded ? "제목 접기" : "제목 펼치기";
        TitleExpand.IsEnabled = TitleCopy.IsEnabled = CurrentTask != null;
    }
    private void TitleExpand_Click(object sender, RoutedEventArgs e)
    { titleExpanded = !titleExpanded; UpdateConversationTitle(); }
    private async void TitleCopy_Click(object sender, RoutedEventArgs e) => await Guard(() =>
    {
        if (CurrentTask is { } task) { Clipboard.SetText(task.Title); Footer.Text = "전체 제목을 복사했습니다."; }
        return Task.CompletedTask;
    }, "conversation-title-copy");
}
