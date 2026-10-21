using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Hermes.Core;
using Microsoft.Web.WebView2.Core;
namespace Hermes.Desktop;
public partial class MainWindow
{
    internal async Task RunLocalBrowserChecks(string data)
    {
        var checks = new List<string>(); void Check(bool ok, string label) { if (!ok) throw new IOException(label); checks.Add(label); }
        await using var plain = new DevelopmentServer(false); await using var tls = new DevelopmentServer(true);
        var root = Path.Combine(data, "owned-local-project"); Directory.CreateDirectory(root); var project = desk.Register(root); Projects.SelectedItem = project;
        var chat = desk.NewTask(project, "Local development fixture"); Tasks.ItemsSource = new[] { chat }; Tasks.SelectedItem = chat; OpenPanel(8);
        var view = await EnsureChatBrowser(chat); SetBrowserView(view);
        async Task Navigate(string url, string scheme)
        {
            var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var path = BrowserTools.ValidateUrl(url).AbsolutePath;
            void Loaded(object? sender, CoreWebView2NavigationCompletedEventArgs args) { if (args.IsSuccess && view.Source?.Scheme == scheme && view.Source.AbsolutePath == path) complete.TrySetResult(); }
            view.CoreWebView2.NavigationCompleted += Loaded;
            try { await NavigateBrowser(url); await complete.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
            finally { view.CoreWebView2.NavigationCompleted -= Loaded; }
        }
        await SetLocalDevelopmentMode(true);
        await Navigate($"127.0.0.1:{plain.Port}/plain", "http");
        Check(view.CoreWebView2.DocumentTitle == "LOCAL_DEV_HTTP", "bare loopback address opens actual HTTP development server");
        await Navigate($"https://127.0.0.1:{plain.Port}/wrong-protocol", "http");
        Check(view.Source?.Port == plain.Port && view.CoreWebView2.DocumentTitle == "LOCAL_DEV_HTTP", "TLS-to-HTTP protocol error retries same loopback port using HTTP");
        Check(desk.State.Browser.LastUrl.StartsWith($"http://127.0.0.1:{plain.Port}/"), "working HTTP address is remembered");
        await Navigate($"https://127.0.0.1:{tls.Port}/certificate", "https");
        Check(localCertificateBypasses > 0 && view.CoreWebView2.DocumentTitle == "LOCAL_DEV_TLS", "self-signed local development certificate is accepted without installing a root certificate");
        chat.ThreadBrowserEnabled = true; chat.Status = "실행 중";
        var result = await BrowserAction(chat, JsonSerializer.SerializeToElement(new { action = "navigate", url = $"https://127.0.0.1:{plain.Port}/agent-http" }));
        Check(result.Text.Contains($"http://127.0.0.1:{plain.Port}/agent-http"), "agent navigation waits for HTTP recovery instead of reporting the initial TLS error");
        await SetLocalDevelopmentMode(true);
        result = await BrowserAction(chat, JsonSerializer.SerializeToElement(new { action = "navigate", url = $"https://127.0.0.1:{tls.Port}/agent-tls" }));
        Check(result.Text.Contains($"https://127.0.0.1:{tls.Port}/agent-tls"), "agent navigation waits for accepted local certificate and reports HTTPS success");
        chat.Status = "대기";
        var bypasses = localCertificateBypasses; await SetLocalDevelopmentMode(false);
        var denied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Failed(object? sender, CoreWebView2NavigationCompletedEventArgs args) { if (!args.IsSuccess && args.WebErrorStatus.ToString().Contains("Certificate", StringComparison.Ordinal)) denied.TrySetResult(); }
        view.CoreWebView2.NavigationCompleted += Failed;
        try { await NavigateBrowser($"https://127.0.0.1:{tls.Port}/not-allowed"); await denied.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
        finally { view.CoreWebView2.NavigationCompleted -= Failed; }
        Check(localCertificateBypasses == bypasses, "turning development mode off clears cached certificate exceptions");
        Check(!new StateStore(data).Load().Browser.LocalDevelopmentMode, "development-mode preference survives state reload");
        await SetLocalDevelopmentMode(true);
        File.WriteAllText(Path.Combine(data, "local-browser-report.json"), JsonSerializer.Serialize(new { passed = true, version = AppVersion.Current, checks, publicNetworkCalls = 0, rootCertificatesInstalled = 0 }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private sealed class DevelopmentServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly X509Certificate2? certificate;
        private readonly Task accepting;
        private readonly System.Collections.Concurrent.ConcurrentBag<Task> clients = [];
        internal int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
        internal DevelopmentServer(bool tls)
        {
            if (tls)
            {
                using var key = RSA.Create(2048); var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback); request.CertificateExtensions.Add(names.Build());
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
                using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
                certificate = X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.UserKeySet);
            }
            listener.Start(); accepting = Accept();
        }
        private async Task Accept()
        {
            try { while (!stop.IsCancellationRequested) { var client = await listener.AcceptTcpClientAsync(stop.Token); clients.Add(Serve(client)); } }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException) { }
        }
        private async Task Serve(TcpClient client)
        {
            using (client)
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                try
                {
                    using Stream stream = certificate == null ? client.GetStream() : new SslStream(client.GetStream());
                    if (stream is SslStream ssl) await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, timeout.Token);
                    var bytes = new byte[16000]; var read = await stream.ReadAsync(bytes, timeout.Token); if (read == 0) return;
                    var html = "<html><head><title>" + (certificate == null ? "LOCAL_DEV_HTTP" : "LOCAL_DEV_TLS") + "</title></head><body>Owned local development fixture</body></html>";
                    var response = "HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Type: text/html\r\nContent-Length: " + Encoding.UTF8.GetByteCount(html) + "\r\n\r\n" + html;
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(response), timeout.Token); await stream.FlushAsync(timeout.Token);
                }
                catch (Exception ex) when (ex is IOException or AuthenticationException or OperationCanceledException) { }
            }
        }
        public async ValueTask DisposeAsync() { stop.Cancel(); listener.Stop(); await accepting; await Task.WhenAll(clients); certificate?.Dispose(); stop.Dispose(); }
    }
}
