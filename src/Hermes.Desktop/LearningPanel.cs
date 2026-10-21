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
    private void InitializeLearningPanel()
    {
        var panel = new DockPanel { Margin = new Thickness(8) }; var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        top.Children.Add(new TextBlock { Text = "업무·명령·도구 RAG · 매 채팅에 자동 적용\n프로젝트별 로컬 SQLite 검색 · 컴퓨터 좌표는 재사용하지 않습니다.\nworkflow.report는 모델의 업무 보고이며 목표 달성 검증과 구분합니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) });
        top.Children.Add(learningQuery); var actions = new WrapPanel(); top.Children.Add(actions);
        var search = new Button { Content = "방법 검색 · 새로고침" }; var history = new Button { Content = "최근 실행 기록" }; var save = new Button { Content = "선택 방법 설명 저장" }; var delete = new Button { Content = "선택 방법 삭제" };
        foreach (var button in new[] { search, history, save, delete }) actions.Children.Add(button);
        top.Children.Add(learningMethods); top.Children.Add(new TextBlock { Text = "개선 방법·주의점 (성공 실행 근거와 함께 개정 저장)", Margin = new Thickness(4) }); top.Children.Add(learningNotes); panel.Children.Add(learningDetail);
        var tab = new TabItem { Header = "업무·실행 RAG", Content = panel }; InspectorTabs.Items.Add(tab);
        void Load()
        {
            if (CurrentProject == null) { learningMethods.ItemsSource = null; learningDetail.Text = "프로젝트를 선택하세요."; return; }
            learningMethods.ItemsSource = desk.Memory.Search(ExecutionMemory.Scope(CurrentProject.Root), learningQuery.Text, 30);
            learningDetail.Text = "DB: " + desk.Memory.DatabasePath + "\n기록을 선택하면 실행 근거와 개정 정보를 표시합니다.";
        }
        search.Click += (_, _) => _ = Guard(() => { Load(); return Task.CompletedTask; });
        InspectorTabs.SelectionChanged += (_, e) => { if (ReferenceEquals(e.OriginalSource, InspectorTabs) && InspectorTabs.SelectedItem == tab) _ = Guard(() => { Load(); return Task.CompletedTask; }); };
        history.Click += (_, _) => _ = Guard(() => { if (CurrentProject != null) learningDetail.Text = JsonSerializer.Serialize(desk.Memory.Recent(ExecutionMemory.Scope(CurrentProject.Root)), new JsonSerializerOptions { WriteIndented = true }); return Task.CompletedTask; });
        learningMethods.SelectionChanged += (_, _) => { if (learningMethods.SelectedItem is LearnedMethod method) { learningNotes.Text = method.Notes; learningDetail.Text = JsonSerializer.Serialize(method, new JsonSerializerOptions { WriteIndented = true }); } };
        save.Click += (_, _) => _ = Guard(() => { if (CurrentProject != null && learningMethods.SelectedItem is LearnedMethod method) { desk.Memory.Improve(ExecutionMemory.Scope(CurrentProject.Root), method.Id, method.EvidenceId, learningNotes.Text); Load(); } return Task.CompletedTask; });
        delete.Click += (_, _) => _ = Guard(() => { if (CurrentProject != null && learningMethods.SelectedItem is LearnedMethod method) { desk.Memory.Delete(ExecutionMemory.Scope(CurrentProject.Root), method.Id); Load(); learningNotes.Clear(); } return Task.CompletedTask; });
    }
}
