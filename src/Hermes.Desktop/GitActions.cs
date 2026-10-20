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
    private async void Commit_Click(object sender, RoutedEventArgs e) => await Guard(async () =>
    {
        var project = CurrentProject ?? throw new IOException("프로젝트를 선택하세요.");
        var window = new GitCommitWindow(project.Root) { Owner = this }; window.ShowDialog();
        if (window.Committed) { Footer.Text = "프로젝트 로컬 Git 커밋 완료"; OpenPanel(1); await RefreshGitChanges(); }
    });
}
