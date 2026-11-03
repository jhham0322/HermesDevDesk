using System.Windows;
using System.Windows.Controls;
using Hermes.Core;
namespace Hermes.Desktop;
public partial class MainWindow
{
    private void ShowOperationReview(string processId)
    {
        BuildOperationReview(processId).ShowDialog();
    }
    private Window BuildOperationReview(string processId)
    {
        var process = desk.State.Processes.Single(p => p.Id == processId);
        var dialog = new Window { Title = "실행 기록 확인 · 자동 재실행 없음", Owner = this, Width = 800, Height = 650, MinWidth = 480, MinHeight = 380, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new Thickness(12) };
        var header = new TextBlock { Text = DeskController.OperationReviewSummary(process), TextWrapping = TextWrapping.Wrap, MaxHeight = 150, Margin = new Thickness(0,0,0,8) };
        DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        var list = new ListBox { Name = "OperationReviewList", MaxHeight = 150, MinHeight = 60 }; DockPanel.SetDock(list, Dock.Top); panel.Children.Add(list);
        var actions = new WrapPanel { Margin = new Thickness(0,8,0,8) };
        var reconcile = new Button { Name = "ReconcileOperation", Content = "수신 응답과 대조 · 실행하지 않음", IsEnabled = false, Padding = new Thickness(8) };
        var close = new Button { Content = "닫기", Padding = new Thickness(8) }; close.Click += (_,_) => dialog.Close();
        actions.Children.Add(reconcile); actions.Children.Add(close); DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions);
        var details = new TextBox { Name = "OperationReviewDetails", IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(details); dialog.Content = panel;
        void Show()
        {
            reconcile.IsEnabled = list.SelectedItem is OperationReceipt o && o.NeedsReview && !process.Active;
            try { details.Text = list.SelectedItem is OperationReceipt selected ? desk.DescribeOperation(process.Id, selected.Key) : "실행 항목을 선택하세요."; }
            catch (Exception ex) { details.Text = "기록 확인 실패: " + Safety.Redact(ex.Message); reconcile.IsEnabled = false; }
        }
        void Refresh(string? key)
        {
            list.ItemsSource = null; list.ItemsSource = process.Operations;
            list.SelectedItem = process.Operations.FirstOrDefault(o => o.Key == key) ?? process.Operations.FirstOrDefault(o => o.NeedsReview) ?? process.Operations.FirstOrDefault();
            header.Text = process.Operations.Any(o => o.NeedsReview) ? DeskController.OperationReviewSummary(process) : "실행 응답 기록을 확인했습니다. 업무 목표 완료를 판정하지 않았으며 작업은 재개하지 않았습니다. 다른 정지·승인 조건을 확인한 뒤 별도로 재개하세요.";
            Show();
        }
        list.SelectionChanged += (_,_) => Show();
        reconcile.Click += (_,_) =>
        {
            if (list.SelectedItem is not OperationReceipt selected) return;
            try { desk.RecoverKnownOperation(process.Id, selected.Key); Refresh(selected.Key); RefreshProcesses(); }
            catch (Exception ex) { details.Text = "기록을 확정하지 못했습니다. 실행하지 않았습니다.\n" + Safety.Redact(ex.Message); }
        };
        Refresh(null); return dialog;
    }
}
