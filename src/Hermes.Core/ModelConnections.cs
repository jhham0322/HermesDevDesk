using System.Text.Json;

namespace Hermes.Core;
public sealed class ModelConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ProviderSettings Settings { get; set; } = new();
    public override string ToString() => Settings.Deployment + " · " + Settings.Endpoint;
}
public sealed record ModelConnectionExport(string ModelName, string ModelUrl, string Token,
    ProviderProtocol Protocol = ProviderProtocol.AnthropicMessages, ProviderAuth Authentication = ProviderAuth.BearerKey,
    int MaxOutputTokens = 8192);
public sealed partial class DeskController
{
    private void MigrateModelConnections()
    {
        if (State.ModelConnections.Count > 0 || State.Provider.Endpoint.Length == 0 || State.Provider.Deployment.Length == 0) return;
        var names = SavedModels.Append(State.Provider.Deployment).Distinct().ToList();
        foreach (var name in names)
        {
            var settings = CopySettings(State.Provider); settings.Deployment = name;
            var connection = new ModelConnection { Settings = settings };
            if (KeyStore.HasKey) ConnectionKey(connection).Save(KeyStore.ReadOwnKey());
            State.ModelConnections.Add(connection);
        }
        Store.Save(State);
    }
    private ProtectedKeyStore ConnectionKey(ModelConnection connection)
    {
        if (!Guid.TryParseExact(connection.Id, "N", out _)) throw new IOException("모델 설정 ID가 올바르지 않습니다.");
        return new(Path.Combine(DataDirectory, "secrets", "models", connection.Id));
    }
    private static ProviderSettings CopySettings(ProviderSettings settings) => JsonSerializer.Deserialize<ProviderSettings>(JsonSerializer.Serialize(settings))!;
    public void SaveModelConnection(ProviderSettings settings, string? token, string? replaceId = null)
    {
        if (connecting || State.Tasks.Any(IsRunning)) throw new IOException("실행 중인 작업을 마친 뒤 모델을 수정하세요.");
        settings.NormalizeInferenceEndpoint(); settings.Validate();
        var existing = State.ModelConnections.FirstOrDefault(c => c.Id == replaceId);
        if (State.ModelConnections.Any(c => c.Id != existing?.Id && c.Settings.Deployment == settings.Deployment)) throw new IOException("같은 모델명이 이미 있습니다. 해당 모델을 선택해 수정하세요.");
        var connection = existing ?? new ModelConnection();
        var keys = ConnectionKey(connection);
        token = string.IsNullOrWhiteSpace(token) ? keys.HasKey ? keys.ReadOwnKey() : null : token.Trim();
        if (settings.Authentication != ProviderAuth.None && token == null) throw new IOException("새 모델의 토큰을 입력하세요.");
        if (token != null && (settings.Endpoint.Contains(token, StringComparison.Ordinal) || settings.Deployment.Contains(token, StringComparison.Ordinal))) throw new IOException("토큰은 전용 입력란에 입력하세요.");
        var oldName = existing?.Settings.Deployment;
        Configure(settings, token, settings.Authentication == ProviderAuth.None);
        if (token != null) keys.Save(token);
        connection.Settings = CopySettings(State.Provider);
        if (existing == null) State.ModelConnections.Add(connection);
        if (oldName != null && oldName != settings.Deployment)
        { foreach (var names in State.SavedModels.Values) names.Remove(oldName); Models.Remove(oldName); }
        Save();
    }
    public bool SelectModelConnection(string model)
    {
        var connection = State.ModelConnections.FirstOrDefault(c => c.Settings.Deployment == model);
        if (connection == null) return false;
        var settings = CopySettings(connection.Settings);
        var keys = ConnectionKey(connection);
        var token = keys.HasKey ? keys.ReadOwnKey() : null;
        if (settings.Authentication != ProviderAuth.None && token == null) throw new IOException("선택 모델의 토큰이 없습니다. 연결 설정에서 다시 입력하세요.");
        // Configure invalidates the old engine connection. The send flow reconnects before inference.
        if (State.Provider.Fingerprint == settings.Fingerprint &&
            (settings.Authentication == ProviderAuth.None || (KeyStore.HasKey && KeyStore.ReadOwnKey() == token))) return true;
        Configure(settings, token, settings.Authentication == ProviderAuth.None);
        connection.Settings = CopySettings(State.Provider); Save(); return true;
    }
    public void DeleteModelConnection(string id)
    {
        if (connecting || State.Tasks.Any(IsRunning)) throw new IOException("실행 중인 작업을 마친 뒤 모델을 삭제하세요.");
        var connection = State.ModelConnections.Single(c => c.Id == id);
        var name = connection.Settings.Deployment;
        ConnectionKey(connection).DeleteOwnKey(); State.ModelConnections.Remove(connection);
        foreach (var names in State.SavedModels.Values) names.Remove(name);
        Models.Remove(name);
        if (State.Provider.Deployment == name)
        {
            KeyStore.DeleteOwnKey(); State.Provider = new(); State.PendingModel = "";
            SubscriptionReady = false; Models.Clear();
        }
        Save();
    }
    public void DeleteModelToken(string? id)
    {
        if (connecting || State.Tasks.Any(IsRunning)) throw new IOException("실행 중인 작업을 마친 뒤 토큰을 삭제하세요.");
        var connection = State.ModelConnections.FirstOrDefault(c => c.Id == id);
        if (connection != null) ConnectionKey(connection).DeleteOwnKey();
        if (connection == null || connection.Settings.Deployment == State.Provider.Deployment) Configure(State.Provider, deleteKey: true);
        Save();
    }
    public string ExportModelConnections()
    {
        var rows = State.ModelConnections.Select(c => new ModelConnectionExport(c.Settings.Deployment, c.Settings.Endpoint,
            ConnectionKey(c).HasKey ? ConnectionKey(c).ReadOwnKey() : "", c.Settings.Protocol, c.Settings.Authentication, c.Settings.MaxOutputTokens)).ToList();
        if (rows.Count == 0 && State.Provider.Endpoint.Length > 0)
            rows.Add(new(State.Provider.Deployment, State.Provider.Endpoint, KeyStore.HasKey ? KeyStore.ReadOwnKey() : "", State.Provider.Protocol, State.Provider.Authentication, State.Provider.MaxOutputTokens));
        return JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
    }
    public void ImportModelConnections(string json)
    {
        if (json.Length > 2_000_000) throw new IOException("설정 파일 크기 제한은 2MB입니다.");
        var rows = JsonSerializer.Deserialize<List<ModelConnectionExport>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new IOException("모델 배열이 필요합니다.");
        if (rows.Count is < 1 or > 200 || rows.Select(r => r.ModelName).Distinct().Count() != rows.Count) throw new IOException("모델은 중복 없이 1~200개 입력하세요.");
        // Validate the whole document before changing any settings or keys.
        var candidates = rows.Select(r => (Row: r, Settings: new ProviderSettings { Deployment = r.ModelName?.Trim() ?? "", Endpoint = r.ModelUrl?.Trim() ?? "", Protocol = r.Protocol, Authentication = r.Authentication, UseAdvancedSettings = true, MaxOutputTokens = r.MaxOutputTokens })).ToList();
        if (candidates.Select(c => c.Settings.Deployment).Distinct().Count() != candidates.Count) throw new IOException("모델명이 중복되었습니다.");
        foreach (var (row, settings) in candidates)
        {
            settings.Validate();
            if (!Enum.IsDefined(settings.Protocol) || !Enum.IsDefined(settings.Authentication)) throw new IOException("명시적인 연결 프로토콜을 입력하세요.");
            if (row.Token == null || (settings.Authentication != ProviderAuth.None && (row.Token.Trim().Length is < 8 or > 4096 || row.Token.Any(char.IsControl)))) throw new IOException("토큰 형식이 올바르지 않습니다.");
            if (row.Token.Length > 0 && (settings.Endpoint.Contains(row.Token, StringComparison.Ordinal) || settings.Deployment.Contains(row.Token, StringComparison.Ordinal))) throw new IOException("URL 또는 모델명에 토큰을 넣을 수 없습니다.");
        }
        foreach (var (row, settings) in candidates)
            SaveModelConnection(settings, row.Token, State.ModelConnections.FirstOrDefault(c => c.Settings.Deployment == settings.Deployment)?.Id);
    }
}
