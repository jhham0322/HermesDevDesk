using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hermes.Core;

namespace Hermes.Desktop;

internal sealed class OfficialChatGptWindow : Window
{
    public OfficialChatGptWindow(DeskController desk, string cwd)
    {
        Title = "ChatGPT 계정";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(20) };
        Content = panel;
        var status = new TextBlock
        {
            Text = desk.State.Provider.UseHermesChatGpt && desk.SubscriptionReady
                ? desk.AccountStatus : "브라우저에서 ChatGPT에 로그인하면 이 계정의 모델을 사용합니다.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        var login = new Button { Content = "ChatGPT로 로그인" };
        var logout = new Button { Content = "로그아웃하고 회사 모델 사용", Margin = new Thickness(0, 6, 0, 0),
            Visibility = desk.State.Provider.UseHermesChatGpt ? Visibility.Visible : Visibility.Collapsed };
        var cancel = new Button { Content = "닫기", Margin = new Thickness(0, 6, 0, 0) };
        CancellationTokenSource? pending = null;
        panel.Children.Add(status);
        panel.Children.Add(login);
        panel.Children.Add(logout);
        panel.Children.Add(cancel);
        string EnginePath() =>
            AppServerClient.FindInstalledCodex() ?? (File.Exists(desk.State.CodexPath) ? desk.State.CodexPath : null) ?? throw new IOException("동봉된 공식 Codex 엔진을 찾을 수 없습니다.");

        async Task Run(Func<CancellationToken, Task> action)
        {
            using var operation = new CancellationTokenSource();
            pending = operation;
            login.IsEnabled = logout.IsEnabled = false;
            cancel.Content = "취소";
            try { await action(operation.Token); Close(); }
            catch (OperationCanceledException) { status.Text = "로그인을 취소했습니다."; }
            catch (Exception ex) { status.Text = ConnectionErrors.Describe(ex, operation.Token); }
            finally
            {
                pending = null;
                login.IsEnabled = logout.IsEnabled = true;
                cancel.Content = "닫기";
            }
        }

        login.Click += async (_, _) => await Run(token =>
        {
            status.Text = "브라우저에서 ChatGPT 로그인을 완료하세요.";
            return desk.SignInToChatGptAsync(EnginePath(), cwd, url =>
                Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }), token);
        });
        logout.Click += async (_, _) => await Run(_ => desk.SignOutOfChatGptAsync(EnginePath(), cwd));
        cancel.Click += (_, _) => { if (pending != null) pending.Cancel(); else Close(); };
        Closing += (_, _) => pending?.Cancel();
    }
}