using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Hermes.Core;
namespace Hermes.Desktop;
public sealed class SkillsWindow : Window
{
    private readonly string root, cli, home;
    private readonly ListBox list = new() { Height = 230, SelectionMode = SelectionMode.Multiple };
    private readonly TextBox detail = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 240 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly CancellationTokenSource cancellation = new();
    public List<ProjectSkill> Selected { get; private set; } = [];
    public SkillsWindow(string root, string cli, string home)
    {
        this.root = Safety.ProjectRoot(root); this.cli = cli; this.home = home;
        Title = "프로젝트 스킬 선택"; Width = 760; Height = 710; WindowStartupLocation = WindowStartupLocation.CenterOwner; Style = Application.Current.TryFindResource(typeof(Window)) as Style;
        var panel = new StackPanel { Margin = new Thickness(18) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "프로젝트: " + root + "\n프로젝트 .agents/skills 또는 skills의 SKILL.md만 선택합니다. 외부 설치/전역 스킬 수정 없음. 선택 지침은 작업 시작 때 현재 모델로 전송됩니다.", TextWrapping = TextWrapping.Wrap });
        var buttons = new WrapPanel(); panel.Children.Add(buttons);
        var refresh = new Button { Content = "목록 새로고침" }; var official = new Button { Content = "공식 엔진 목록 확인" }; var attach = new Button { Content = "선택 지침 첨부" }; var cancel = new Button { Content = "취소" };
        foreach (var b in new[] { refresh, official, attach, cancel }) buttons.Children.Add(b);
        panel.Children.Add(status); panel.Children.Add(list); panel.Children.Add(detail);
        void Reload() { list.ItemsSource = ProjectSkills.List(root, out var errors); status.Text = errors.Count == 0 ? "프로젝트 스킬 목록. 최대 3개 선택." : string.Join("\n", errors); }
        refresh.Click += (_, _) => Try(Reload);
        official.Click += async (_, _) => { official.IsEnabled = false; try { await using var inspect = new LocalInspectionClient(root); await inspect.ConnectAsync(cli, home, cancellation.Token); var skills = await inspect.SkillsAsync(cancellation.Token); status.Text = $"공식 skills/list에서 프로젝트 스킬 {skills.Count}개 확인 · 모델 호출 없음"; list.ItemsSource = skills; } catch (OperationCanceledException) { status.Text = "확인 취소됨"; } catch (Exception e) { status.Text = Safety.Redact(e.Message); } finally { official.IsEnabled = true; } };
        list.SelectionChanged += (_, _) => Try(() => { if (list.SelectedItem is ProjectSkill skill) detail.Text = skill.Description + "\n\n" + File.ReadAllText(ProjectSkills.Resolve(root, skill.RelativePath)); });
        attach.Click += (_, _) => Try(() => { Selected = ProjectSkills.Validate(root, list.SelectedItems.Cast<ProjectSkill>()); if (Selected.Count == 0) throw new Hermes.Core.HermesException("스킬을 선택하세요."); DialogResult = true; });
        cancel.Click += (_, _) => { cancellation.Cancel(); DialogResult = false; }; Closed += (_, _) => cancellation.Cancel(); Reload();
    }
    private void Try(Action action) { try { action(); } catch (Exception e) { status.Text = Safety.Redact(e.Message); } }
    internal int RunProgrammaticSelectionChecks()
    {
        if (list.Items.Count != 1) throw new Hermes.Core.HermesException("Skill UI fixture list");
        list.SelectedIndex = 0; if (!detail.Text.Contains("UI_SKILL_FIXTURE")) throw new Hermes.Core.HermesException("Skill UI detail");
        Selected = ProjectSkills.Validate(root, list.SelectedItems.Cast<ProjectSkill>()); if (Selected.Count != 1) throw new Hermes.Core.HermesException("Skill UI selection"); return 3;
    }
}
