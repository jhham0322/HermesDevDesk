using System.Windows;
using System.Windows.Controls;
using Hermes.Core;
namespace Hermes.Desktop;
public partial class MainWindow
{
    private void ExecutionBudget_Click(object sender, RoutedEventArgs e)
    {
        var current = desk.State.Desktop.ExecutionBudget;
        var panel = new StackPanel { Margin = new Thickness(18) };
        var window = new Window { Title = "단계별 실행 예산", Owner = this, Width = 440, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        panel.Children.Add(new TextBlock { Text = "다음 실행/명시적 재개 단계부터 적용합니다. 시간 예산은 새 도구·자동 턴 시작을 제한하며, 진행 중인 작업을 강제 종료하지 않습니다. 예산 도달은 완료가 아닙니다.", TextWrapping = TextWrapping.Wrap });
        var extended = new CheckBox { Content = "명시적 긴 작업 (12라운드 / 60분 / 100도구)", IsChecked = current.Extended, Margin = new Thickness(0, 12, 0, 8) }; panel.Children.Add(extended);
        TextBox Field(string label, int value) { panel.Children.Add(new TextBlock { Text = label }); var field = new TextBox { Text = value.ToString(), Margin = new Thickness(0, 2, 0, 8) }; panel.Children.Add(field); return field; }
        var rounds = Field("최대 라운드 (1~100)", current.MaxRounds); var minutes = Field("단계 시간 (분, 1~1440)", current.MaxMinutes); var tools = Field("도구 시작 수 (1~1000)", current.MaxTools); var stagnant = Field("새 근거 없는 연속 라운드 (1~20)", current.MaxStagnantRounds);
        extended.Click += (_, _) => { rounds.Text = extended.IsChecked == true ? "12" : "3"; minutes.Text = extended.IsChecked == true ? "60" : "10"; tools.Text = extended.IsChecked == true ? "100" : "20"; };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap }; panel.Children.Add(error);
        var save = new Button { Content = "다음 단계에 적용" }; panel.Children.Add(save);
        save.Click += (_, _) =>
        {
            if (!int.TryParse(rounds.Text, out var r) || !int.TryParse(minutes.Text, out var m) || !int.TryParse(tools.Text, out var t) || !int.TryParse(stagnant.Text, out var s)) { error.Text = "정수를 입력하세요."; return; }
            try { var value = new ExecutionBudget { MaxRounds = r, MaxMinutes = m, MaxTools = t, MaxStagnantRounds = s, Extended = extended.IsChecked == true }; value.Validate(); desk.State.Desktop.ExecutionBudget = value; desk.Save(); window.Close(); } catch (HermesException ex) { error.Text = ex.Message; }
        };
        window.ShowDialog();
    }
}
