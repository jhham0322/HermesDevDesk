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
        panel.Children.Add(new TextBlock { Text = "도구 호출 횟수 상한은 없습니다. 정상 진척이 있으면 내부 단계를 자동으로 이어갑니다. 사용자 지정 정지·무진전·실제 공급자 한도와 승인 조건은 유지합니다.", TextWrapping = TextWrapping.Wrap });
        var extended = new CheckBox { Content = "긴 내부 단계 (12라운드 / 60분)", IsChecked = current.Extended, Margin = new Thickness(0, 12, 0, 8) }; panel.Children.Add(extended);
        TextBox Field(string label, int value) { panel.Children.Add(new TextBlock { Text = label }); var field = new TextBox { Text = value.ToString(), Margin = new Thickness(0, 2, 0, 8) }; panel.Children.Add(field); return field; }
        var rounds = Field("최대 라운드 (1~100)", current.MaxRounds); var minutes = Field("단계 시간 (분, 1~1440)", current.MaxMinutes); var stagnant = Field("새 근거 없는 연속 라운드 (1~20)", current.MaxStagnantRounds);
        extended.Click += (_, _) => { rounds.Text = extended.IsChecked == true ? "12" : "3"; minutes.Text = extended.IsChecked == true ? "60" : "10"; };
        var confirm = new CheckBox { Content = "시간·회차 도달 시 사용자 확인 후 재개", IsChecked = current.RequiresConfirmation, Margin = new Thickness(0, 8, 0, 8) }; panel.Children.Add(confirm);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap }; panel.Children.Add(error);
        var save = new Button { Content = "다음 단계에 적용" }; panel.Children.Add(save);
        save.Click += (_, _) =>
        {
            if (!int.TryParse(rounds.Text, out var r) || !int.TryParse(minutes.Text, out var m) || !int.TryParse(stagnant.Text, out var s)) { error.Text = "정수를 입력하세요."; return; }
            try { var value = new ExecutionBudget { MaxRounds = r, MaxMinutes = m, MaxStagnantRounds = s, Extended = extended.IsChecked == true, PauseAtStageLimit = confirm.IsChecked == true }; value.Validate(); desk.State.Desktop.ExecutionBudget = value; desk.Save(); window.Close(); } catch (HermesException ex) { error.Text = ex.Message; }
        };
        window.ShowDialog();
    }
}
