using System.Text.Json;

namespace Hermes.Core;

public sealed partial class DeskController
{
    private string HermesChatGptHome => Path.Combine(DataDirectory, "official-chatgpt", "engine");

    public async Task SignInToChatGptAsync(string exe, string cwd, Action<Uri> openBrowser, CancellationToken cancellation)
    {
        EnsureChatGptIdle();
        await using var loginEngine = new AppServerClient();
        await loginEngine.ConnectAsync(exe, cwd, HermesChatGptHome, officialChatGpt: true);
        var completed = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        loginEngine.Notification += evt =>
        {
            if (evt.Method == "account/login/completed") completed.TrySetResult(evt.Params);
        };
        loginEngine.Disconnected += () => completed.TrySetException(new IOException("로그인 중 엔진 연결이 끊겼습니다."));
        cancellation.ThrowIfCancellationRequested();
        var response = await loginEngine.RequestAsync("account/login/start", new { type = "chatgpt" });
        var loginId = response.Text("loginId");
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (loginId.Length == 0 || response.Text("type") != "chatgpt" ||
                !Uri.TryCreate(response.Text("authUrl"), UriKind.Absolute, out var url) ||
                url.Scheme != Uri.UriSchemeHttps || url.Host != "auth.openai.com" || url.UserInfo.Length != 0)
                throw new HermesException("공식 ChatGPT 로그인 주소를 확인하지 못했습니다.");
            openBrowser(url);
            var result = await completed.Task.WaitAsync(TimeSpan.FromMinutes(5), cancellation);
            if (result.Text("loginId") != loginId || result.Property("success") is not { ValueKind: JsonValueKind.True })
                throw new HermesException("ChatGPT 로그인 실패: " + Safety.Redact(result.Text("error")));
        }
        finally
        {
            if (!completed.Task.IsCompleted && loginId.Length != 0 && loginEngine.Connected)
            {
                try { await loginEngine.RequestAsync("account/login/cancel", new { loginId }, timeoutSeconds: 3); }
                catch (Exception ex) { AppLog.Write(ex, "chatgpt-login-cancel"); }
            }
        }

        await loginEngine.DisposeAsync();
        cancellation.ThrowIfCancellationRequested();
        EnsureChatGptIdle();
        Configure(new ProviderSettings
        {
            Mode = ConnectionMode.ExistingChatGptCli,
            UseHermesChatGpt = true,
            OAuthAccountId = Guid.NewGuid().ToString("N")
        });
        await ConnectAsync(exe, cwd, cancellation);
        if (!SubscriptionReady) throw new HermesException("ChatGPT 로그인 상태를 확인하지 못했습니다.");
    }

    public async Task SignOutOfChatGptAsync(string exe, string cwd)
    {
        EnsureChatGptIdle();
        if (!State.Provider.UseHermesChatGpt) return;
        await using var logoutEngine = new AppServerClient();
        await logoutEngine.ConnectAsync(exe, cwd, HermesChatGptHome, officialChatGpt: true);
        await logoutEngine.RequestAsync("account/logout", new { });
        var previous = Engine;
        Engine = null;
        if (previous != null) await previous.DisposeAsync();
        Configure(new ProviderSettings());
        if (State.ModelConnections.FirstOrDefault(c => c.Id == State.ActiveModelConnectionId) is { } saved)
            SelectConnection(saved, false);
    }

    private void EnsureChatGptIdle()
    {
        if (connecting || State.Tasks.Any(IsRunning))
            throw new HermesException("진행 중인 작업이나 연결이 끝난 뒤 ChatGPT 계정을 변경하세요.");
    }
}