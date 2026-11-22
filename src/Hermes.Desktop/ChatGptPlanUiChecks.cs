using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
namespace Hermes.Desktop;
public sealed partial class MainWindow
{
    internal async Task RunChatGptPlanUiChecks(string data)
    {
        int opened = 0; var checks = new List<string>();
        Uri? authorization = null;
        var account = new ChatGptPlanWindow(desk, url => { opened++; authorization = url; }) { Owner = this }; account.Show();
        var panel = (StackPanel)((ScrollViewer)account.Content).Content;
        var buttons = panel.Children.OfType<Button>().ToArray();
        var login = buttons.Single(b => b.Content.ToString()!.StartsWith("Continue with ChatGPT"));
        var cancel = buttons.Single(b => Equals(b.Content,"진행 취소"));
        void Check(bool ok,string label) { if(!ok)throw new IOException(label); checks.Add(label); }
        login.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for(int i=0;i<100 && opened==0;i++)await Task.Delay(10);
        Check(opened==1&&!login.IsEnabled,"sign-in starts once and disables repeated submissions");
        Check(authorization?.Host == "auth.openai.com" && authorization.Query.Contains("client_id=dynamic_agent_client") &&
            authorization.Query.Contains("agent_name_hint=HermesDevDesk"), "Hermes uses the published Sign in with ChatGPT registration");
        login.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(50);
        Check(opened==1,"duplicate login event does not open another authorization attempt");
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for(int i=0;i<100&&!login.IsEnabled;i++)await Task.Delay(10);
        Check(login.IsEnabled&&desk.ChatGptPlan.Accounts().Count==0,"cancel restores controls without creating a registration");
        login.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for(int i=0;i<100&&opened<2;i++)await Task.Delay(10);
        Check(opened==2,"sign-in can be started again after cancellation");
        account.Close();await Task.Delay(100);
        Check(desk.ChatGptPlan.Accounts().Count==0,"closing authentication window cancels pending authorization");
        int attempts=0;
        var launchFailure=new ChatGptPlanWindow(desk,_=>{attempts++;if(attempts==1)throw new IOException("SYNTHETIC_BROWSER_LAUNCH_FAILURE");}){Owner=this};launchFailure.Show();
        var failurePanel=(StackPanel)((ScrollViewer)launchFailure.Content).Content;
        var failureButtons=failurePanel.Children.OfType<Button>().ToArray();
        var retryLogin=failureButtons.Single(b=>b.Content.ToString()!.StartsWith("Continue with ChatGPT"));
        var retryCancel=failureButtons.Single(b=>Equals(b.Content,"진행 취소"));
        retryLogin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for(int i=0;i<100&&(attempts==0||!retryLogin.IsEnabled);i++)await Task.Delay(10);
        Check(attempts==1&&retryLogin.IsEnabled&&failurePanel.Children.OfType<TextBlock>().Any(t=>t.Text.Contains("SYNTHETIC_BROWSER_LAUNCH_FAILURE")),"browser-launch failure is visible and restores authentication controls");
        retryLogin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for(int i=0;i<100&&attempts<2;i++)await Task.Delay(10);
        Check(attempts==2&&!retryLogin.IsEnabled,"sign-in guard is released after browser-launch failure and retry starts once");
        retryCancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for(int i=0;i<100&&!retryLogin.IsEnabled;i++)await Task.Delay(10);
        Check(retryLogin.IsEnabled&&desk.ChatGptPlan.Accounts().Count==0,"cancel after launch-error retry leaves no credential registration");
        launchFailure.Close();
        Check(Equals(ChatGptLoginButton.Content, "ChatGPT 로그인") && ChatGptLoginButton.IsVisible,
            "ChatGPT login is visible on the main window");
        var engine = Hermes.Core.AppServerClient.FindInstalledCodex() ?? throw new IOException("Bundled engine missing");
        using var officialCancel = new CancellationTokenSource();
        var officialBrowserOpened = false;
        try
        {
            await desk.SignInToChatGptAsync(engine, data, url =>
            {
                officialBrowserOpened = url.Host == "auth.openai.com";
                officialCancel.Cancel();
            }, officialCancel.Token);
        }
        catch (OperationCanceledException) when (officialCancel.IsCancellationRequested) { }
        Check(officialBrowserOpened && !desk.State.Provider.UseHermesChatGpt,
            "bundled engine returns its official login URL and cancel does not switch providers");
        desk.SaveModelConnection(new Hermes.Core.ProviderSettings { Endpoint = "https://company.invalid/v1", Deployment = "custom-model" },
            "SYNTHETIC_CHATGPT_COMPANY_KEY", alias: "custom alias");
        desk.Configure(new Hermes.Core.ProviderSettings { Mode = Hermes.Core.ConnectionMode.ExistingChatGptCli,
            UseHermesChatGpt = true, OAuthAccountId = "synthetic" });
        desk.Models.Add("synthetic-official-model");
        Refresh();
        Check(Model.Items.Cast<string>().SequenceEqual(["synthetic-official-model"]) &&
            desk.DeploymentForChoice("synthetic-official-model") == "synthetic-official-model",
            "ChatGPT mode only exposes the official engine catalog");
        File.WriteAllText(Path.Combine(data,"chatgpt-plan-ui-report.json"),JsonSerializer.Serialize(new{passed=true,checks,realLogin=false,realModelCalls=0,nativeMouse=false}));
    }
}
