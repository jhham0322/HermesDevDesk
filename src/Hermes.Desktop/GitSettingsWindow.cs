using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hermes.Core;
using Microsoft.Win32;

namespace Hermes.Desktop;
public sealed class GitSettingsWindow : Window
{
    public GitSettingsWindow(DeskController desk, ProjectEntry? project)
    {
        Title = "로컬 Git 및 원격 서버"; Width = 700; Height = 570; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) }; Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "로컬 Git 및 원격 서버", FontSize = 24, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "모든 Git 작업은 PC의 git.exe로 실행합니다. 원격 연결은 지정한 저장소에서 사용자의 Git/SSH 인증을 사용합니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
        var executable = new TextBox { Text = desk.State.Desktop.GitExecutable }; panel.Children.Add(executable);
        var pick = new Button { Content = "git.exe 선택" }; var save = new Button { Content = "Git 경로 저장" }; var paths = new WrapPanel(); paths.Children.Add(pick); paths.Children.Add(save); panel.Children.Add(paths);
        panel.Children.Add(new TextBlock { Text = project?.Root ?? "원격 서버를 설정하려면 프로젝트를 선택하세요.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
        var name = new TextBox { Text = "origin" }; var url = new TextBox(); panel.Children.Add(new TextBlock { Text = "원격 이름" }); panel.Children.Add(name); panel.Children.Add(new TextBlock { Text = "원격 주소 · HTTPS / SSH / 로컬 bare 저장소" }); panel.Children.Add(url);
        var actions = new WrapPanel(); var set = new Button { Content = "원격 주소 저장" }; var fetch = new Button { Content = "Fetch" }; var push = new Button { Content = "현재 branch Push" }; var refresh = new Button { Content = "조회" }; foreach (var button in new[] { set, fetch, push, refresh }) actions.Children.Add(button); panel.Children.Add(actions);
        foreach (var button in new[] { set, fetch, push, refresh }) button.IsEnabled = project != null;
        var status = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100 }; panel.Children.Add(status);
        async Task Run(Func<Task<string>> action) { try { actions.IsEnabled = false; status.Text = "로컬 Git 실행 중…"; status.Text = await action(); } catch (Exception ex) { status.Text = Safety.Redact(ex.Message); } finally { actions.IsEnabled = true; } }
        string Root() => project?.Root ?? throw new Hermes.Core.HermesException("프로젝트를 선택하세요.");
        pick.Click += (_, _) => { var dialog = new OpenFileDialog { Filter = "Git executable|git.exe" }; if (dialog.ShowDialog(this) == true) executable.Text = dialog.FileName; };
        save.Click += (_, _) =>
        {
            try { var path = executable.Text.Trim(); if (path != "git" && (!Path.IsPathFullyQualified(path) || !File.Exists(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))) throw new Hermes.Core.HermesException("git 또는 설치된 git.exe의 절대 경로를 입력하세요."); GitRunner.Executable = path; desk.State.Desktop.GitExecutable = path; desk.Save(); status.Text = "로컬 Git 실행 경로 저장됨"; }
            catch (Exception ex) { status.Text = Safety.Redact(ex.Message); }
        };
        set.Click += async (_, _) => await Run(async () => { await GitWorkbench.ConfigureRemote(Root(), name.Text, url.Text); return "저장소 원격 설정 저장됨"; });
        fetch.Click += async (_, _) => await Run(() => GitWorkbench.Sync(Root(), name.Text, false));
        push.Click += async (_, _) => { if (MessageBox.Show(this, $"{Root()}\n\n{ name.Text } 서버로 현재 branch를 Push할까요?", "Git Push", MessageBoxButton.YesNo) == MessageBoxResult.Yes) await Run(() => GitWorkbench.Sync(Root(), name.Text, true)); };
        refresh.Click += async (_, _) => await Run(async () => { var result = await GitRunner.Run(Root(), "remote", "-v"); return Safety.Redact(result.Output + result.Error); });
    }
}
