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
        var account = new ChatGptPlanWindow(desk, _ => opened++) { Owner = this }; account.Show();
        var panel = (StackPanel)((ScrollViewer)account.Content).Content;
        var buttons = panel.Children.OfType<Button>().ToArray();
        var login = buttons.Single(b => b.Content.ToString()!.StartsWith("Continue with ChatGPT"));
        var cancel = buttons.Single(b => Equals(b.Content,"진행 취소"));
        void Check(bool ok,string label) { if(!ok)throw new IOException(label); checks.Add(label); }
        login.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        for(int i=0;i<100 && opened==0;i++)await Task.Delay(10);
        Check(opened==1&&!login.IsEnabled,"sign-in starts once and disables repeated submissions");
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
        File.WriteAllText(Path.Combine(data,"chatgpt-plan-ui-report.json"),JsonSerializer.Serialize(new{passed=true,checks,realLogin=false,realModelCalls=0,nativeMouse=false}));
    }
}
