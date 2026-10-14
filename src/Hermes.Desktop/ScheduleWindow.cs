using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hermes.Core;
namespace Hermes.Desktop;
public sealed class ScheduleWindow : Window
{
    public ScheduleWindow(DeskController desk, ProjectEntry project)
    {
        Title = "프로젝트 작업 초안 예약 · 모델 자동 실행 없음"; Width = 740; Height = 650; WindowStartupLocation = WindowStartupLocation.CenterOwner; Style = Application.Current.TryFindResource(typeof(Window)) as Style;
        var panel = new StackPanel { Margin = new Thickness(18) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "앱이 켜져 있을 때 지정한 시각에 작업 초안을 만들고 알립니다. 모델·명령은 자동 실행하지 않습니다. 앱을 다시 열면 놓친 예약을 한 번 처리합니다.", TextWrapping = TextWrapping.Wrap });
        var title = new TextBox { Text = "예약 작업" }; var prompt = new TextBox { AcceptsReturn = true, MinHeight = 90, TextWrapping = TextWrapping.Wrap }; var at = new TextBox { Text = DateTimeOffset.UtcNow.AddHours(1).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) };
        panel.Children.Add(new TextBlock { Text = "제목 / 지시 / UTC 시각 (yyyy-MM-dd HH:mm)" }); panel.Children.Add(title); panel.Children.Add(prompt); panel.Children.Add(at);
        var create = new Button { Content = "작업 초안 예약" }; var cancel = new Button { Content = "선택 예약 취소" }; var list = new ListBox { Height = 200 }; var status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(new WrapPanel { Children = { create, cancel } }); panel.Children.Add(list); panel.Children.Add(status);
        void Refresh() => list.ItemsSource = desk.State.ScheduledDrafts.Where(d => d.ProjectId == project.Id).ToList();
        create.Click += (_, _) => { try { var due = DateTimeOffset.ParseExact(at.Text, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal); desk.ScheduleDraft(project, title.Text, prompt.Text, due); Refresh(); status.Text = "예약 저장됨. 초안 생성만 수행하며 모델을 자동 실행하지 않습니다."; } catch (Exception e) { status.Text = Safety.Redact(e.Message); } };
        cancel.Click += (_, _) => { if (list.SelectedItem is ScheduledDraft entry) { desk.CancelScheduledDraft(entry); Refresh(); status.Text = "예약 취소됨. 기록 보존."; } }; Refresh();
    }
}
