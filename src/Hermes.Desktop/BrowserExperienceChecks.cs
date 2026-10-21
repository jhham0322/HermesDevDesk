using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hermes.Core;
using Microsoft.Web.WebView2.Core;
namespace Hermes.Desktop;
public partial class MainWindow
{
    internal async Task RunBrowserExperienceChecks(string data)
    {
        var checks = new List<string>();
        void Check(bool condition, string label) { if (!condition) throw new IOException(label); checks.Add(label); }
        await using var pages = new LoopbackHttp((request, stream, ct) => LoopbackHttp.Reply(stream, 200, "text/html", "<html><head><title>Browser fixture " + request.Path + "</title></head><body><div id='picked'><h1>SELECTED_HTML_OK</h1><input type='password' value='SYNTHETIC_PASSWORD_NOT_TO_COPY'><textarea>PRIVATE_TYPED_VALUE</textarea></div><a href='/second'>Second page</a></body></html>", ct));
        var root = Path.Combine(data, "owned-browser-project"); Directory.CreateDirectory(root);
        var project = desk.Register(root); Projects.SelectedItem = project;
        var chat = desk.NewTask(project, "브라우저 기능 검사"); Tasks.ItemsSource = new[] { chat }; Tasks.SelectedItem = chat; OpenPanel(8);
        async Task Navigate(string path)
        {
            await NavigateBrowser(pages.BaseUri.AbsoluteUri.TrimEnd('/') + path);
            var view = activeBrowserView!;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while ((!view.CoreWebView2.DocumentTitle.Contains(path) || view.CoreWebView2.DocumentTitle.Length == 0) && watch.Elapsed < TimeSpan.FromSeconds(20)) await Task.Delay(40);
            RememberBrowserPage(view); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        }
        await Navigate("/first"); var view = activeBrowserView!;
        Check(view.CoreWebView2.Profile.IsPasswordAutosaveEnabled && view.CoreWebView2.Profile.IsGeneralAutofillEnabled, "native password-save/autofill enabled without reading credentials");
        ToggleCurrentBookmark();
        Check(desk.State.Browser.Bookmarks.Any(p => p.Url.EndsWith("/first")), "bookmark stores current URL/title");
        await Navigate("/second"); desk.Save();
        var saved = new StateStore(data).Load();
        Check(saved.Browser.LastUrl.EndsWith("/second") && saved.Tasks.Single(t => t.Id == chat.Id).BrowserLastUrl.EndsWith("/second"), "global and per-chat last URLs persist");
        Check(saved.Browser.History.Any(p => p.Url.EndsWith("/first")) && saved.Browser.History.Any(p => p.Url.EndsWith("/second")), "visited pages survive state reload");
        Check(saved.Browser.Bookmarks.Single().Title.Contains("/first"), "bookmark title survives state reload");
        await PickBrowserSource();
        await view.ExecuteScriptAsync("document.querySelector('#picked').dispatchEvent(new MouseEvent('click',{bubbles:true,cancelable:true}));");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!chat.Transcript.Any(t => t.Kind == "브라우저 소스") && timer.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(30);
        var source = chat.Transcript.Single(t => t.Kind == "브라우저 소스").Text;
        Check(source.Contains("SELECTED_HTML_OK") && source.Contains("<div") && source.Contains("렌더링된 DOM HTML"), "clicked DOM element appears as HTML source in chat");
        Check(!source.Contains("SYNTHETIC_PASSWORD_NOT_TO_COPY") && !source.Contains("PRIVATE_TYPED_VALUE"), "selected source excludes form password and typed textarea values");
        Prompt.Clear(); browserAttachSource.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(Prompt.Text.Contains("SELECTED_HTML_OK"), "source attachment fills composer without sending an AI request");
        browserBack.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var navigationTimer = System.Diagnostics.Stopwatch.StartNew();
        while ((activeBrowserView?.Source?.AbsolutePath != "/first" || activeBrowserView.CoreWebView2.CanGoForward != true) && navigationTimer.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(30);
        Check(activeBrowserView?.Source?.AbsolutePath == "/first", "previous-page button follows real browser history");
        browserForward.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); navigationTimer.Restart();
        while (activeBrowserView?.Source?.AbsolutePath != "/second" && navigationTimer.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(30);
        Check(activeBrowserView?.Source?.AbsolutePath == "/second", "forward-page button restores real next page");
        Check(activeBrowserView!.CoreWebView2.Settings.AreDevToolsEnabled && browserDevTools.IsEnabled, "developer tools enabled and accessible from navigation toolbar");
        chatBrowsers.Remove(chat.Id); browserSurface.Children.Remove(view); view.Dispose();
        browserAddress.Clear(); await OpenSavedBrowser();
        var restored = System.Diagnostics.Stopwatch.StartNew();
        while (activeBrowserView?.Source?.AbsolutePath != "/second" && restored.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(30);
        Check(activeBrowserView?.Source?.AbsolutePath == "/second", "new browser instance restores persisted last page");
        var captionLabels = desk.State.Browser.History.Count;
        File.WriteAllText(Path.Combine(data, "browser-experience-report.json"), JsonSerializer.Serialize(new { passed = true, version = AppVersion.Current, checks, historyEntries = captionLabels, nativePasswordStore = true, actualPasswordLoginTested = false, realModelCalls = 0 }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
