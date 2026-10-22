using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Hermes.Core;
namespace Hermes.Desktop;
public partial class MainWindow
{
    private readonly TextBox learningQuery = new() { ToolTip = "업무 목적·오류·도구 검색", Margin = new Thickness(4) };
    private readonly ListBox learningMethods = new() { Margin = new Thickness(4), MaxHeight = 230 };
    private readonly TextBox learningNotes = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 80 };
    private readonly TextBox learningDetail = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private void LoadLearningErrors()
    {
        if (CurrentProject == null) return;
        learningMethods.ItemsSource = desk.Memory.Errors(ExecutionMemory.Scope(CurrentProject.Root), learningQuery.Text);
        learningDetail.Text = "프로젝트별 오류 목록 · 동일 오류 정규화/발생 횟수/실행된 개선 방법 연결. 검색어로 오류와 목적을 조회합니다.";
    }
    private void LoadLearningRecoveries()
    {
        if (CurrentProject == null) return;
        learningMethods.ItemsSource = desk.Memory.Recoveries(ExecutionMemory.Scope(CurrentProject.Root), learningQuery.Text);
        learningDetail.Text = "오류 이후 바꾼 방법의 성공 실행 근거입니다. 업무 목표 달성은 별도로 검증해야 합니다. 선택 후 설명을 수정하고 저장하면 개정 이력을 보관합니다.";
    }
    private void InitializeLearningPanel()
    {
        var panel = new DockPanel { Margin = new Thickness(8) }; var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "업무·명령·도구 RAG · 매 채팅에 자동 적용\n프로젝트별 로컬 SQLite 검색 · 컴퓨터 좌표는 재사용하지 않습니다.\nworkflow.report는 모델의 업무 보고이며 목표 달성 검증과 구분합니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) });
        top.Children.Add(learningQuery); var actions = new WrapPanel(); top.Children.Add(actions);
        var search = new Button { Content = "방법 검색 · 새로고침" }; var history = new Button { Content = "최근 실행 기록" }; var save = new Button { Content = "선택 기록 설명 저장" }; var delete = new Button { Content = "선택 방법 삭제" }; var errors = new Button { Content = "오류 목록" }; var recoveries = new Button { Content = "개선 방법 목록" }; var resolve = new Button { Content = "선택 오류 해결 / 다시 열기" };
        foreach (var button in new[] { search, history, errors, recoveries, save, resolve, delete }) actions.Children.Add(button);
        top.Children.Add(learningMethods); top.Children.Add(new TextBlock { Text = "개선 방법·주의점 (성공 실행 근거와 함께 개정 저장)", Margin = new Thickness(4) }); top.Children.Add(learningNotes); panel.Children.Add(learningDetail);
        var tab = new TabItem { Header = "업무·실행 RAG", Content = panel }; InspectorTabs.Items.Add(tab);
        void Load()
        {
            if (CurrentProject == null) { learningMethods.ItemsSource = null; learningDetail.Text = "프로젝트를 선택하세요."; return; }
            learningMethods.ItemsSource = desk.Memory.Search(ExecutionMemory.Scope(CurrentProject.Root), learningQuery.Text, 30);
            learningDetail.Text = "DB: " + desk.Memory.DatabasePath + "\n기록을 선택하면 실행 근거와 개정 정보를 표시합니다.";
        }
        errors.Click += (_, _) => _ = Guard(() => { LoadLearningErrors(); return Task.CompletedTask; });
        recoveries.Click += (_, _) => _ = Guard(() => { LoadLearningRecoveries(); return Task.CompletedTask; });
        search.Click += (_, _) => _ = Guard(() => { Load(); return Task.CompletedTask; });
        InspectorTabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.OriginalSource, InspectorTabs) && InspectorTabs.SelectedItem == tab) _ = Guard(() => { Load(); return Task.CompletedTask; }); };
        history.Click += (_, _) => _ = Guard(() => { if (CurrentProject != null) learningDetail.Text = JsonSerializer.Serialize(desk.Memory.Recent(ExecutionMemory.Scope(CurrentProject.Root)), new JsonSerializerOptions { WriteIndented = true }); return Task.CompletedTask; });
        learningMethods.SelectionChanged += (_, _) => { if (learningMethods.SelectedItem is ErrorRecord error) { learningNotes.Text = error.Notes; learningDetail.Text = JsonSerializer.Serialize(error, new JsonSerializerOptions { WriteIndented = true }); } else if (learningMethods.SelectedItem is RecoveryRecord recovery) { learningNotes.Text = recovery.Notes; learningDetail.Text = JsonSerializer.Serialize(recovery, new JsonSerializerOptions { WriteIndented = true }); } else if (learningMethods.SelectedItem is LearnedMethod method) { learningNotes.Text = method.Notes; learningDetail.Text = JsonSerializer.Serialize(method, new JsonSerializerOptions { WriteIndented = true }); } };
        save.Click += (_, _) => _ = Guard(() =>
        {
            if (CurrentProject == null) return Task.CompletedTask;
            var scope = ExecutionMemory.Scope(CurrentProject.Root);
            if (learningMethods.SelectedItem is LearnedMethod method) { desk.Memory.Improve(scope, method.Id, method.EvidenceId, learningNotes.Text); Load(); }
            else if (learningMethods.SelectedItem is ErrorRecord error) { desk.Memory.UpdateError(scope, error.Key, error.Tool, learningNotes.Text, error.Status); LoadLearningErrors(); }
            else if (learningMethods.SelectedItem is RecoveryRecord recovery) { desk.Memory.UpdateRecovery(scope, recovery.Id, learningNotes.Text); LoadLearningRecoveries(); }
            return Task.CompletedTask;
        });
        resolve.Click += (_, _) => _ = Guard(() => { if (CurrentProject != null && learningMethods.SelectedItem is ErrorRecord error) { desk.Memory.UpdateError(ExecutionMemory.Scope(CurrentProject.Root), error.Key, error.Tool, learningNotes.Text, error.Status == "미해결" ? "해결 확인" : "미해결"); LoadLearningErrors(); } return Task.CompletedTask; });
        delete.Click += (_, _) => _ = Guard(() => { if (CurrentProject != null && learningMethods.SelectedItem is LearnedMethod method) { desk.Memory.Delete(ExecutionMemory.Scope(CurrentProject.Root), method.Id); Load(); learningNotes.Clear(); } return Task.CompletedTask; });
    }
}
