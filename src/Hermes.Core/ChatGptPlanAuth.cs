using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hermes.Core;

public sealed record ChatGptAccount(string Id, string Label, bool SignedIn, bool PlanAllowed);
public sealed record ChatGptModel(string Slug, string DisplayName) { public override string ToString() => DisplayName; }
public sealed record ChatGptLease(string AccessToken, DateTimeOffset Expires, string AccountId) { public override string ToString() => "ChatGPT lease (credential hidden)"; }
internal sealed class ChatGptRegistration
{
    public string ClientId { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Email { get; set; } = "";
    public string Access { get; set; } = "";
    public string Refresh { get; set; } = "";
    public string IdToken { get; set; } = "";
    public string[] Scopes { get; set; } = [];
    public DateTimeOffset Expires { get; set; }
    public DateTimeOffset EarliestRefresh { get; set; }
    public string Id => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ClientId + "\n" + Subject)));
}

// Own app credentials only. No CLI/ChatGPT browser credential discovery.
public sealed class ChatGptPlanAuth : IDisposable
{
    public const string Issuer = "https://auth.openai.com";
    public const string Resource = "https://api.openai.com/v1";
    public const string Scope = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct";
    private readonly string directory;
    private readonly HttpClient http;
    private int signingIn;
    public ChatGptPlanAuth(string appData, HttpMessageHandler? handler = null)
    {
        directory = Path.Combine(appData, "chatgpt-plan");
        Directory.CreateDirectory(directory); Safety.RejectReparseAncestors(directory);
        http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };
    }
    public IReadOnlyList<ChatGptAccount> Accounts() => Load().Select(r => new ChatGptAccount(r.Id,
        (r.Email.Length > 0 ? r.Email : "ChatGPT account") + " / " + r.Id[..8], r.Access.Length > 0,
        r.Scopes.Contains("chatgpt.tokens.use.direct") && r.Access.Length > 0)).ToArray();
    private string TokenPath => Path.Combine(directory, "accounts.dpapi");
    private List<ChatGptRegistration> Load()
    {
        Safety.RejectReparseAncestors(TokenPath);
        if (!File.Exists(TokenPath)) return [];
        if (new FileInfo(TokenPath).Length > 2_000_000) throw new IOException("OAuth credential store size limit");
        var clear = ProtectedKeyStore.Transform(File.ReadAllBytes(TokenPath), true);
        try { var accounts = JsonSerializer.Deserialize<List<ChatGptRegistration>>(clear) ?? []; foreach (var r in accounts) RegisterSecrets(r); return accounts; }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    private static void RegisterSecrets(ChatGptRegistration r)
    { foreach (var s in new[] { r.Access, r.Refresh, r.IdToken }) if (s.Length > 0) Safety.RegisterSecret(s); }
    private void Save(List<ChatGptRegistration> records)
    {
        var clear = JsonSerializer.SerializeToUtf8Bytes(records);
        try { var bytes = ProtectedKeyStore.Transform(clear, false); Atomic(TokenPath, bytes); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    private static void Atomic(string path, byte[] bytes)
    {
        Safety.RejectReparseAncestors(path); var temp = path + "." + Guid.NewGuid().ToString("N");
        try { using (var s = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { s.Write(bytes); s.Flush(true); } File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private async Task<FileStream> Lock(CancellationToken ct)
    {
        var path = Path.Combine(directory, "session.lock"); Safety.RejectReparseAncestors(path);
        while (true) { ct.ThrowIfCancellationRequested(); try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); } catch (IOException) { await Task.Delay(100, ct); } }
    }
    private string HostId()
    {
        var path = Path.Combine(directory, "host-id.txt"); Safety.RejectReparseAncestors(path);
        if (!File.Exists(path)) Atomic(path, Encoding.UTF8.GetBytes("urn:uuid:" + Guid.NewGuid()));
        var value = File.ReadAllText(path); if (!value.StartsWith("urn:uuid:", StringComparison.Ordinal) || !Guid.TryParse(value[9..], out _)) throw new IOException("Invalid OAuth host identity"); return value;
    }
    internal static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    internal static byte[] Decode(string s) => Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/') + new string('=', (4 - s.Length % 4) % 4));
    private static string Random() => B64(RandomNumberGenerator.GetBytes(32));
    private static string Query(IEnumerable<KeyValuePair<string, string>> values) => string.Join("&", values.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
    internal static Dictionary<string, string> Callback(string path)
    {
        var uri = new Uri("http://127.0.0.1" + path); if (uri.AbsolutePath != "/auth/callback") throw new IOException("Unexpected OAuth callback path");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        { var p = pair.Split('=', 2); if (!result.TryAdd(Uri.UnescapeDataString(p[0]), Uri.UnescapeDataString(p.Length == 2 ? p[1].Replace('+', ' ') : ""))) throw new IOException("Duplicate OAuth callback parameter"); }
        return result;
    }
    internal static string ValidateCallback(Dictionary<string, string> p, string state, string? priorClient)
    {
        if (!p.TryGetValue("state", out var actual) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(actual))) throw new IOException("OAuth state mismatch");
        if (p.ContainsKey("error")) throw new IOException("ChatGPT authorization was declined or failed. No credentials changed.");
        if (!p.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code)) throw new IOException("OAuth callback has no code");
        var client = p.GetValueOrDefault("client_id", priorClient ?? "");
        if (!client.StartsWith("oaiapp_", StringComparison.Ordinal) || (priorClient != null && client != priorClient)) throw new IOException("OAuth registration/client mismatch");
        return client;
    }
    public async Task SignInAsync(string? accountId, Action<Uri> openBrowser, bool enablePlan, CancellationToken ct)
    {
        if (Interlocked.Exchange(ref signingIn, 1) != 0) throw new IOException("ChatGPT sign-in is already in progress");
        try
        {
            ChatGptRegistration? previous; string host;
            using (await Lock(ct)) { var all = Load(); previous = accountId == null ? null : all.Single(r => r.Id == accountId); host = HostId(); }
            var state = Random(); var nonce = Random(); var verifier = Random(); Safety.RegisterSecret(verifier);
            var callback = new TaskCompletionSource<Dictionary<string, string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var listener = new LoopbackHttp(async (req, stream, stop) =>
            {
                try
                {
                    if (req.Method != "GET" || !req.Path.StartsWith("/auth/callback?", StringComparison.Ordinal)) { await LoopbackHttp.Reply(stream, 404, "text/plain", "Not found", stop); return; }
                    var p = Callback(req.Path);
                    // Foreign requests cannot terminate or consume the pending authorization.
                    if (p.GetValueOrDefault("state") != state) { await LoopbackHttp.Reply(stream, 400, "text/plain", "Invalid state", stop); return; }
                    callback.TrySetResult(p); await LoopbackHttp.Reply(stream, 200, "text/plain; charset=utf-8", "Return to Hermes DevDesk. Authorization is being verified.", stop);
                }
                catch (IOException) { await LoopbackHttp.Reply(stream, 400, "text/plain", "Invalid callback", stop); }
            });
            var redirect = new Uri(listener.BaseUri, "auth/callback").AbsoluteUri;
            var query = new Dictionary<string, string> { ["client_id"] = previous?.ClientId ?? "dynamic_agent_client", ["ext_agent_host_id"] = host,
                ["response_type"] = "code", ["redirect_uri"] = redirect, ["scope"] = Scope, ["resource"] = Resource,
                ["state"] = state, ["nonce"] = nonce, ["code_challenge_method"] = "S256", ["code_challenge"] = B64(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) };
            if (previous == null) query["agent_name_hint"] = "HermesDevDesk";
            else { if (previous.IdToken.Length > 0) query["id_token_hint"] = previous.IdToken; if (previous.Email.Length > 0) query["login_hint"] = previous.Email; }
            if (enablePlan) query["prompt"] = "consent";
            LaunchBrowser(new Uri(Issuer + "/api/accounts/authorize?" + Query(query)), openBrowser, ct);
            var returned = await callback.Task.WaitAsync(ct); ct.ThrowIfCancellationRequested();
            var client = ValidateCallback(returned, state, previous?.ClientId);
            using var tokens = await Token(new() { ["grant_type"] = "authorization_code", ["client_id"] = client, ["code"] = returned["code"], ["code_verifier"] = verifier, ["redirect_uri"] = redirect, ["resource"] = Resource }, ct);
            var identity = await Identity(tokens.RootElement.Text("id_token"), client, nonce, ct);
            if (previous != null && identity.Subject != previous.Subject) throw new IOException("Signed-in identity differs from selected ChatGPT account");
            var record = new ChatGptRegistration { ClientId = client, Subject = identity.Subject, Email = identity.Email };
            SetTokens(record, tokens.RootElement, false); ct.ThrowIfCancellationRequested();
            using (await Lock(ct)) { var all = Load(); all.RemoveAll(r => r.Id == record.Id); all.Add(record); Save(all); }
        }
        finally { Interlocked.Exchange(ref signingIn, 0); }
    }
    internal static void LaunchBrowser(Uri authorization, Action<Uri> openBrowser, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); openBrowser(authorization); }
    private async Task<JsonDocument> Json(HttpRequestMessage request, CancellationToken ct)
    {
        using (request) using (var response = await http.SendAsync(request, ct))
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (text.Length > 2_000_000) throw new IOException("OAuth response size limit");
            if (!response.IsSuccessStatusCode)
            {
                string code = ""; try { using var d = JsonDocument.Parse(text); var e = d.RootElement.Property("error"); code = e?.ValueKind == JsonValueKind.String ? e.Value.GetString() ?? "" : e?.Text("code") ?? ""; } catch (JsonException) { }
                var safeCode = code.Length < 120 && code.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? code : "unclassified";
                var requestId = response.Headers.TryGetValues("x-request-id", out var ids) ? ids.FirstOrDefault() ?? "" : "";
                if (requestId.Length > 160 || requestId.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))) requestId = "";
                var shape = text.TrimStart().StartsWith("{") ? "json" : "non-json";
                throw new ChatGptAuthException((int)response.StatusCode, safeCode, requestId, shape);
            }
            return JsonDocument.Parse(text);
        }
    }
    private Task<JsonDocument> Token(Dictionary<string, string> fields, CancellationToken ct) => Json(new(HttpMethod.Post, Issuer + "/api/accounts/oauth/token") { Content = new FormUrlEncodedContent(fields) }, ct);
    private static void SetTokens(ChatGptRegistration record, JsonElement t, bool refresh)
    {
        var access = t.Text("access_token"); var replacement = t.Text("refresh_token");
        if (access.Length == 0 || !string.Equals(t.Text("token_type"), "Bearer", StringComparison.OrdinalIgnoreCase) || t.Property("expires_in") is not { ValueKind: JsonValueKind.Number } lifetime || lifetime.GetInt32() <= 0) throw new IOException("Incomplete OAuth token response");
        if (refresh && replacement.Length == 0) throw new IOException("OAuth refresh did not return a rotating refresh token; reconnect required");
        record.Access = access; record.Refresh = replacement; if (!refresh && t.Text("id_token").Length > 0) record.IdToken = t.Text("id_token");
        if (t.Property("scope") is { ValueKind: JsonValueKind.String }) record.Scopes = t.Text("scope").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        else if (!refresh) record.Scopes = [];
        record.Expires = DateTimeOffset.UtcNow.AddSeconds(lifetime.GetInt32());
        record.EarliestRefresh = t.Property("earliest_refresh_at") is { ValueKind: JsonValueKind.Number } earliest ? DateTimeOffset.FromUnixTimeSeconds(earliest.GetInt64()) : DateTimeOffset.MinValue;
        RegisterSecrets(record);
    }
    public async Task<ChatGptLease> LeaseAsync(string id, CancellationToken ct)
    {
        using var gate = await Lock(ct); var all = Load(); var r = all.Single(x => x.Id == id);
        if (r.Access.Length == 0 || !r.Scopes.Contains("chatgpt.tokens.use.direct")) throw new IOException("Continue with ChatGPT and enable ChatGPT plan usage first. No fallback billing is used.");
        if (r.Expires <= DateTimeOffset.UtcNow.AddMinutes(3))
        {
            if (r.Refresh.Length == 0 || r.EarliestRefresh > DateTimeOffset.UtcNow) throw new IOException("ChatGPT token needs renewal; sign in again or retry after the permitted refresh time");
            try { using var token = await Token(new() { ["grant_type"] = "refresh_token", ["client_id"] = r.ClientId, ["refresh_token"] = r.Refresh, ["resource"] = Resource }, ct); SetTokens(r, token.RootElement, true); Save(all); }
            catch (ChatGptAuthException ex) when (new[] { "invalid_grant", "invalid_refresh_token", "token_expired", "refresh_token_expired", "refresh_token_invalidated", "refresh_token_reused" }.Contains(ex.Code))
            { r.Access = r.Refresh = r.IdToken = ""; r.Scopes = []; Save(all); throw new IOException("ChatGPT session is no longer renewable. Sign in again with the saved registration."); }
        }
        if (!r.Scopes.Contains("chatgpt.tokens.use.direct")) throw new IOException("ChatGPT plan usage permission was not granted");
        return new(r.Access, r.Expires, r.Id);
    }
    public async Task<IReadOnlyList<ChatGptModel>> ModelsAsync(string id, CancellationToken ct)
    {
        var lease = await LeaseAsync(id, ct); var request = new HttpRequestMessage(HttpMethod.Get, Resource + "/models"); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", lease.AccessToken);
        using var result = await Json(request, ct);
        if (result.RootElement.Property("models") is not { ValueKind: JsonValueKind.Array } models) throw new IOException("Account model catalog not returned; no cached entitlement assumed");
        return models.EnumerateArray().Where(m => m.Text("visibility") == "list" && m.Text("slug").Length > 0).Select(m => new ChatGptModel(m.Text("slug"), m.Text("display_name").Length > 0 ? m.Text("display_name") : m.Text("slug"))).ToArray();
    }
    public async Task<bool> SignOutAsync(string id, CancellationToken ct)
    {
        using var gate = await Lock(ct); var all = Load(); var r = all.Single(x => x.Id == id); bool revoked = r.Refresh.Length == 0;
        try
        {
            if (r.Refresh.Length > 0)
            {
                using var discovery = await Json(new(HttpMethod.Get, Issuer + "/.well-known/openid-configuration"), ct);
                var endpoint = Trusted(discovery.RootElement.Text("revocation_endpoint"));
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = r.Refresh, ["token_type_hint"] = "refresh_token", ["client_id"] = r.ClientId }) };
                        using var response = await http.SendAsync(request, ct); revoked = response.StatusCode == HttpStatusCode.OK;
                        if (revoked || (int)response.StatusCode < 500) break;
                    }
                    catch (HttpRequestException) when (attempt < 2) { }
                    if (attempt < 2) await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)), ct);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or OperationCanceledException) { revoked = false; }
        finally { r.Access = r.Refresh = r.IdToken = ""; r.Scopes = []; Save(all); }
        return revoked;
    }
    private static Uri Trusted(string value)
    { if (!Uri.TryCreate(value, UriKind.Absolute, out var u) || u.Scheme != "https" || u.Host != "auth.openai.com" || !u.IsDefaultPort || u.UserInfo.Length > 0 || u.Fragment.Length > 0) throw new IOException("Unexpected OpenAI identity endpoint"); return u; }
    private async Task<(string Subject, string Email)> Identity(string jwt, string client, string nonce, CancellationToken ct)
    {
        Safety.RegisterSecret(jwt);
        using var discovery = await Json(new(HttpMethod.Get, Issuer + "/.well-known/openid-configuration"), ct);
        if (discovery.RootElement.Text("issuer") != Issuer) throw new IOException("OpenAI issuer mismatch");
        using var keys = await Json(new(HttpMethod.Get, Trusted(discovery.RootElement.Text("jwks_uri"))), ct);
        return ValidateIdentity(jwt, client, nonce, keys.RootElement, DateTimeOffset.UtcNow);
    }
    internal static (string Subject, string Email) ValidateIdentity(string jwt, string client, string nonce, JsonElement keys, DateTimeOffset now)
    {
        if (jwt.Length > 65536) throw new IOException("ID token size limit");
        var p = jwt.Split('.'); if (p.Length != 3) throw new IOException("Malformed ID token");
        using var header = JsonDocument.Parse(Decode(p[0])); using var body = JsonDocument.Parse(Decode(p[1]));
        var h = header.RootElement; var b = body.RootElement;
        if (h.Text("alg") != "RS256" || h.Property("crit") != null) throw new IOException("Unsupported ID token signature algorithm");
        var key = keys.GetProperty("keys").EnumerateArray().Single(k => k.Text("kid") == h.Text("kid") && k.Text("kty") == "RSA" && (k.Text("use") is "" or "sig") && (k.Text("alg") is "" or "RS256"));
        using var rsa = RSA.Create(); rsa.ImportParameters(new RSAParameters { Modulus = Decode(key.Text("n")), Exponent = Decode(key.Text("e")) });
        if (rsa.KeySize < 2048 || !rsa.VerifyData(Encoding.ASCII.GetBytes(p[0] + "." + p[1]), Decode(p[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) throw new IOException("ID token signature rejected");
        var aud = b.Property("aud"); var audience = aud?.ValueKind == JsonValueKind.String ? aud.Value.GetString() == client : aud?.ValueKind == JsonValueKind.Array && aud.Value.EnumerateArray().Any(a => a.GetString() == client);
        if (b.Text("iss") != Issuer || !audience || (b.Text("azp").Length > 0 && b.Text("azp") != client) || (aud?.ValueKind == JsonValueKind.Array && aud.Value.GetArrayLength() > 1 && b.Text("azp") != client) || b.Text("nonce") != nonce || b.Text("sub").Length == 0 || b.GetProperty("exp").GetInt64() <= now.ToUnixTimeSeconds() || (b.Property("nbf") is { } nbf && nbf.GetInt64() > now.ToUnixTimeSeconds() + 30)) throw new IOException("ID token identity, audience, nonce or lifetime rejected");
        return (b.Text("sub"), b.Text("email"));
    }
    public void Dispose() => http.Dispose();
}
public sealed class ChatGptAuthException(int status, string code, string requestId = "", string bodyShape = "unknown") : IOException($"ChatGPT HTTP {status}: {code}; request={requestId}; body={bodyShape}. Check account permissions and ChatGPT Settings > Usage. No billing fallback or automatic replay.")
{ public int Status { get; } = status; public string Code { get; } = code; public string RequestId { get; } = requestId; public string BodyShape { get; } = bodyShape; }
