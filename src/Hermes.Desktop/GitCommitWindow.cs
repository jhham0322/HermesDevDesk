using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hermes.Core;

namespace Hermes.Desktop;
public sealed class GitCommitWindow : Window
{
    private readonly string root;
    private readonly ListBox files = new() { SelectionMode = SelectionMode.Extended, MinHeight = 120 };
    private readonly TextBox message = new() { MinHeight = 72, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 5000 };
    private readonly TextBox author = new(), email = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 8, 4, 8) };
    private readonly Button initialize = new() { Content = "이 프로젝트에 로컬 Git 저장소 만들기", Visibility = Visibility.Collapsed };
    private readonly Button commit = new() { Content = "선택한 파일 로컬 커밋" };
    private readonly StackPanel controls = new();
    public bool Committed { get; private set; }
    public GitCommitWindow(string root)
    {
        this.root = root; Title = "프로젝트 Git 커밋"; Width = 720; Height = 700; MinWidth = 560; MinHeight = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(22) }; Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "로컬 Git에 변경 저장", FontSize = 24, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = root, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) });
        panel.Children.Add(initialize); panel.Children.Add(controls);
        controls.Children.Add(new TextBlock { Text = "커밋할 파일 선택 · Ctrl/Shift로 여러 파일 선택 · 선택한 파일의 현재 내용을 저장합니다.", TextWrapping = TextWrapping.Wrap });
        controls.Children.Add(files);
        var buttons = new WrapPanel(); var refresh = new Button { Content = "새로고침" }; var all = new Button { Content = "모두 선택" }; var none = new Button { Content = "선택 해제" };
        foreach (var button in new[] { refresh, all, none }) buttons.Children.Add(button); controls.Children.Add(buttons);
        controls.Children.Add(new TextBlock { Text = "커밋 메시지", Margin = new Thickness(0, 10, 0, 5) }); controls.Children.Add(message);
        var identity = new StackPanel(); identity.Children.Add(new TextBlock { Text = "작성자 이름" }); identity.Children.Add(author); identity.Children.Add(new TextBlock { Text = "작성자 이메일" }); identity.Children.Add(email);
        var saveAuthor = new Button { Content = "이 프로젝트의 작성자 저장" }; identity.Children.Add(saveAuthor);
        controls.Children.Add(new Expander { Header = "Git 작성자 설정 · 이 프로젝트에만 적용", Content = identity, Margin = new Thickness(0, 8, 0, 8) });
        controls.Children.Add(commit); panel.Children.Add(status);
        panel.Children.Add(new TextBlock { Text = "선택하지 않은 파일과 기존 스테이징은 유지합니다. 원격 서버로 Push하지 않습니다.", TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        files.SelectionChanged += (_, _) => commit.IsEnabled = files.SelectedItems.Count > 0;
        all.Click += (_, _) => files.SelectAll(); none.Click += (_, _) => files.UnselectAll();
        refresh.Click += async (_, _) => await Run(RefreshAsync);
        initialize.Click += async (_, _) => await Run(async () => { await GitWorkbench.Initialize(root); await RefreshAsync(); });
        saveAuthor.Click += async (_, _) => await Run(async () => { await GitWorkbench.SetAuthor(root, author.Text, email.Text); status.Text = "프로젝트 작성자 저장됨"; });
        commit.Click += async (_, _) => await Run(async () =>
        {
            var output = await GitWorkbench.CommitSelected(root, message.Text, files.SelectedItems.Cast<ChangedFile>().Select(f => f.Path).ToList());
            Committed = true; message.Clear(); await RefreshAsync(); status.Text = "커밋 완료\n" + output;
        });
        Loaded += async (_, _) => await Run(RefreshAsync);
    }
    private async Task Run(Func<Task> action)
    {
        controls.IsEnabled = initialize.IsEnabled = false;
        try { await action(); } catch (Exception ex) { status.Text = Safety.Redact(ex.Message); }
        finally { controls.IsEnabled = initialize.IsEnabled = true; }
    }
    internal async Task RefreshAsync()
    {
        files.ItemsSource = null; commit.IsEnabled = false;
        var repository = await GitRunner.Run(root, "rev-parse", "--show-toplevel");
        initialize.Visibility = repository.Exit == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (repository.Exit != 0) { status.Text = "로컬 Git 저장소가 없습니다. 위 버튼으로 프로젝트 폴더에 만들 수 있습니다."; return; }
        await GitRunner.RepositoryRoot(root);
        var changes = await Review.ChangesAsync(root); files.ItemsSource = changes;
        var branch = await GitRunner.Run(root, "branch", "--show-current");
        author.Text = (await GitRunner.Run(root, "config", "user.name")).Output.Trim(); email.Text = (await GitRunner.Run(root, "config", "user.email")).Output.Trim();
        status.Text = $"브랜치: {branch.Output.Trim()} · 변경 {changes.Count}개 · 커밋할 파일을 선택하세요.";
    }
    internal async Task<int> RunProgrammaticChecks()
    {
        await RefreshAsync();
        var name = "ui-commit-selected.txt"; File.WriteAllText(Path.Combine(root, name), "UI_COMMIT_FIXTURE"); await RefreshAsync();
        var selected = files.Items.Cast<ChangedFile>().Single(f => f.Path == name); files.SelectedItem = selected; message.Text = "UI selected local commit";
        if (!commit.IsEnabled) throw new IOException("Commit button unavailable after file selection");
        commit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var until = DateTime.UtcNow.AddSeconds(15); while (!Committed && DateTime.UtcNow < until) await Task.Delay(25);
        if (!Committed) throw new IOException("UI local commit failed: " + status.Text);
        var output = await GitRunner.Run(root, "show", "--format=", "--name-only", "HEAD");
        if (output.Output.Trim() != name) throw new IOException("UI commit included unexpected files");
        return 2;
    }

}
