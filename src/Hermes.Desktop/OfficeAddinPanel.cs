using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Hermes.Desktop;

public partial class MainWindow
{
    private void InitializeOfficeAddins()
    {
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = "Office 추가 기능 자동 로드", FontSize = 20, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "설치 시 사용자 로그인에 자동 시작합니다. Excel·Word·PowerPoint 등 Office가 실행되면 등록된 COM/VSTO 추가 기능을 연결하고, Excel XLL/XLA/XLAM·Word 템플릿·PowerPoint PPA/PPAM을 로드합니다. Office가 차단하거나 파일이 없는 항목은 실패 원인을 표시합니다.", TextWrapping = TextWrapping.Wrap });
        var buttons = new WrapPanel(); var start = new Button { Content = "자동 로드 시작" }; var stop = new Button { Content = "자동 로드 중지" }; var refresh = new Button { Content = "상태 새로고침" };
        buttons.Children.Add(start); buttons.Children.Add(stop); buttons.Children.Add(refresh); panel.Children.Add(buttons);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) }; panel.Children.Add(status);
        var list = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 250 }; panel.Children.Add(list);
        panel.Children.Add(new TextBlock { Text = "Office 오류 RAG 검색 · 프로그램 종료 후에도 보관", Margin = new Thickness(0, 12, 0, 4) });
        var search = new TextBox(); panel.Children.Add(search);
        var history = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 120, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; panel.Children.Add(history);
        void RefreshHistory()
        {
            try
            {
                var directory = Path.Combine(OfficeAddinMonitor.Root, "knowledge"); if (!Directory.Exists(directory)) return;
                var memory = new Hermes.Core.ExecutionMemory(directory);
                history.Text = string.Join("\n\n", memory.Errors("office-addins", search.Text).Select(r => $"{r.Purpose} · {r.Occurrences}회\n{r.Error}"));
            }
            catch (Exception ex) { history.Text = Hermes.Core.ConnectionErrors.Describe(ex); }
        }
        search.TextChanged += (_, _) => RefreshHistory();
        void RefreshOffice()
        {
            RefreshHistory();
            try
            {
                if (!File.Exists(OfficeAddinMonitor.StatePath)) { status.Text = "자동 로드 대기 · 시작 버튼으로 실행할 수 있습니다."; return; }
                var state = JsonSerializer.Deserialize<OfficeAddinState>(File.ReadAllText(OfficeAddinMonitor.StatePath));
                status.Text = state == null ? "상태 없음" : $"{state.Message}\n마지막 확인 {state.Updated.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
                list.Text = string.Join("\n\n", state?.Addins.Select(a => $"{a.Application} (PID {a.ProcessId}) · {a.Kind}\n{a.Name}: {a.Status}" + (a.Detail.Length == 0 ? "" : "\n" + a.Detail)) ?? []);
            }
            catch (Exception ex) { status.Text = Hermes.Core.ConnectionErrors.Describe(ex); }
        }
        start.Click += (_, _) => { try { OfficeAddinMonitor.Start(); status.Text = "시작 요청됨 · 상태를 새로고침하세요."; } catch (Exception ex) { status.Text = ex.Message; } };
        stop.Click += (_, _) => { OfficeAddinMonitor.Stop(); status.Text = "중지 요청됨 · 진행 중인 로드 완료 후 종료합니다 (최대 35초)."; };
        refresh.Click += (_, _) => RefreshOffice();
        var tab = new TabItem { Header = "Office 연동", Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }; tab.GotFocus += (_, _) => RefreshOffice();
        InspectorTabs.Items.Add(tab); RefreshOffice();
    }
}
