using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hermes.Core;

namespace Hermes.Desktop;
public partial class MainWindow
{
    private async void Stage_Click(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var project = CurrentProject ?? throw new IOException("프로젝트를 선택하세요.");
        var file = ChangedFiles.SelectedItem as ChangedFile ?? throw new IOException("현재 Git 변경 파일을 선택하세요.");
        if (savedChanges) throw new IOException("Git 새로고침 후 현재 파일을 선택하세요.");
        await GitWorkbench.Stage(project.Root, file.Path, true); await RefreshGitChanges();
    });
    private async void Unstage_Click(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var project = CurrentProject ?? throw new IOException("프로젝트를 선택하세요.");
        var file = ChangedFiles.SelectedItem as ChangedFile ?? throw new IOException("파일을 선택하세요.");
        if (savedChanges) throw new IOException("Git 새로고침 후 현재 파일을 선택하세요.");
        await GitWorkbench.Stage(project.Root, file.Path, false); await RefreshGitChanges();
    });
    private void Commit_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentProject is not { } project) return;
        var window = new Window { Owner = this, Title = "스테이징한 변경 커밋", Width = 520, Height = 260, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(20) }; panel.Children.Add(new TextBlock { Text = project.Root, TextWrapping = TextWrapping.Wrap });
        var message = new TextBox { MinHeight = 75, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 8) }; panel.Children.Add(message);
        var commit = new Button { Content = "로컬 커밋 생성" }; panel.Children.Add(commit); window.Content = panel;
        commit.Click += async (_, _) =>
        {
            try { commit.IsEnabled = false; var output = await GitWorkbench.Commit(project.Root, message.Text); Footer.Text = output.Split('\n')[0]; window.Close(); await RefreshGitChanges(); }
            catch (Exception ex) { MessageBox.Show(window, Safety.Redact(ex.Message), "커밋 실패"); }
            finally { commit.IsEnabled = true; }
        };
        window.ShowDialog();
    }
}
