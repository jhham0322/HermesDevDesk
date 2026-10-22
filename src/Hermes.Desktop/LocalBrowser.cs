using System.IO;
using System.Windows.Controls;
using Hermes.Core;
using Microsoft.Web.WebView2.Core;
using WebView2 = Microsoft.Web.WebView2.Wpf.WebView2CompositionControl;
namespace Hermes.Desktop;
public partial class MainWindow
{
    private readonly CheckBox browserLocalDevelopment = new() { Content = "로컬 개발 모드 · HTTP / 개발 인증서", ToolTip = "localhost·127.0.0.1·::1만 적용. 자체 서명 개발 인증서를 허용하고 HTTPS 연결 실패 시 같은 포트의 HTTP로 한 번 재시도합니다." };
    private readonly Dictionary<WebView2, Uri> browserNavigationTargets = [];
    private readonly Dictionary<WebView2, HashSet<string>> localHttpAttempts = [];
    private readonly HashSet<WebView2> localBrowserRecoveries = [];
    private int localCertificateBypasses;
    private void InitializeLocalBrowser(Panel toolbar)
    {
        browserLocalDevelopment.IsChecked = desk.State.Browser.LocalDevelopmentMode; toolbar.Children.Add(browserLocalDevelopment);
        browserLocalDevelopment.Click += async (_, _) => await Guard(() => SetLocalDevelopmentMode(browserLocalDevelopment.IsChecked == true), "browser-local-development");
        var http = new Button { Content = "로컬 HTTP로 열기", ToolTip = "현재 localhost HTTPS 주소를 같은 포트의 HTTP로 다시 엽니다." };
        http.Click += async (_, _) => await Guard(async () => {
            var current = activeBrowserView?.Source;
            var alternative = current == null ? null : BrowserTools.LocalHttpAlternative(current);
            if (alternative == null) throw new Hermes.Core.HermesException("HTTPS localhost 주소에서 사용할 수 있습니다. 개발 서버 예: http://localhost:3020");
            await NavigateBrowser(alternative.AbsoluteUri);
        });
        toolbar.Children.Add(http);
    }
    private async Task SetLocalDevelopmentMode(bool enabled)
    {
        desk.State.Browser.LocalDevelopmentMode = enabled; browserLocalDevelopment.IsChecked = enabled; desk.Save();
        if (!enabled) localBrowserRecoveries.Clear();
        foreach (var view in browserTabOwners.Keys) if (view.CoreWebView2 != null) await view.CoreWebView2.ClearServerCertificateErrorActionsAsync();
    }
    private void ConfigureLocalBrowser(WebView2 view)
    {
        view.CoreWebView2.NavigationStarting += (_, args) => {
            localBrowserRecoveries.Remove(view);
            if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri)) browserNavigationTargets[view] = uri;
        };
        view.CoreWebView2.ServerCertificateErrorDetected += (_, args) => {
            if (desk.State.Browser.LocalDevelopmentMode && Uri.TryCreate(args.RequestUri, UriKind.Absolute, out var uri) && BrowserTools.IsLocalDevelopmentUrl(uri) && args.ErrorStatus != CoreWebView2WebErrorStatus.CertificateRevoked)
            {
                args.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow; localCertificateBypasses++;
                if (activeBrowserView == view) browserStatus.Text = "로컬 개발 인증서 허용 · " + uri.Authority;
            }
        };
        view.CoreWebView2.NavigationCompleted += (_, args) => {
            if (args.IsSuccess || args.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled || args.WebErrorStatus.ToString().Contains("Certificate", StringComparison.Ordinal) || args.HttpStatusCode != 0 || !desk.State.Browser.LocalDevelopmentMode) return;
            if (!browserNavigationTargets.TryGetValue(view, out var target) || target.IsDefaultPort || BrowserTools.LocalHttpAlternative(target) is not { } alternative) return;
            if (!localHttpAttempts.TryGetValue(view, out var attempts)) localHttpAttempts[view] = attempts = [];
            if (!attempts.Add(target.AbsoluteUri)) return;
            localBrowserRecoveries.Add(view);
            Dispatcher.BeginInvoke(() => {
                if (closing || !desk.State.Browser.LocalDevelopmentMode || !browserNavigationTargets.TryGetValue(view, out var current) || current != target) return;
                if (activeBrowserView == view) browserStatus.Text = "로컬 HTTPS 연결 실패 · 같은 포트의 HTTP로 다시 연결합니다: " + alternative.AbsoluteUri;
                view.CoreWebView2.Navigate(alternative.AbsoluteUri);
            });
        };
    }
    private void ResetLocalBrowserAttempts(WebView2 view) { localHttpAttempts.Remove(view); localBrowserRecoveries.Remove(view); }
    private bool LocalBrowserRecoveryInProgress(WebView2 view, CoreWebView2NavigationCompletedEventArgs args)
        => localBrowserRecoveries.Contains(view) || (desk.State.Browser.LocalDevelopmentMode && browserNavigationTargets.TryGetValue(view, out var uri) && BrowserTools.IsLocalDevelopmentUrl(uri) && args.WebErrorStatus.ToString().Contains("Certificate", StringComparison.Ordinal) && args.WebErrorStatus != CoreWebView2WebErrorStatus.CertificateRevoked);
}
