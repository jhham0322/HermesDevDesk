using System.Text.Json;

namespace Hermes.Core;
public sealed class ModelConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ProviderSettings Settings { get; set; } = new();
    public string Alias { get; set; } = "";
    public DateTimeOffset? QuotaExhaustedUntil { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string Label => string.IsNullOrWhiteSpace(Alias) ? Settings.Deployment : Alias;
    public override string ToString() => Label + " · " + Settings.Deployment + " · " + Settings.Endpoint;
}
public sealed record ModelConnectionExport(string ModelName, string ModelUrl, string Token,
    ProviderProtocol Protocol = ProviderProtocol.AnthropicMessages, ProviderAuth Authentication = ProviderAuth.BearerKey,
    int MaxOutputTokens = 8192, string Alias = "");
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
        if (!Guid.TryParseExact(connection.Id, "N", out _)) throw new HermesException("모델 설정 ID가 올바르지 않습니다.");
        return new(Path.Combine(DataDirectory, "secrets", "models", connection.Id));
    }
    private static ProviderSettings CopySettings(ProviderSettings settings) => JsonSerializer.Deserialize<ProviderSettings>(JsonSerializer.Serialize(settings))!;
    public ModelConnection? ActiveModelConnection
    {
        get
        {
            if (State.Provider.Mode != ConnectionMode.CompanyApi) return null;
            return State.ModelConnections.FirstOrDefault(c => c.Id == State.ActiveModelConnectionId &&
                c.Settings.Deployment == State.Provider.Deployment && c.Settings.Endpoint == State.Provider.Endpoint)
                ?? State.ModelConnections.FirstOrDefault(c => c.Settings.Fingerprint == State.Provider.Fingerprint);
        }
    }

    public string ModelChoice(string? connectionId, string model)
    {
        var connection = State.ModelConnections.FirstOrDefault(c => c.Id == connectionId);
        if (connection == null) return model;
        var index = State.ModelConnections.IndexOf(connection) + 1;
        var label = connection.Label;
        while (State.ModelConnections.Any(c => c != connection && string.Equals(c.Label, label, StringComparison.OrdinalIgnoreCase)))
            label += $" · #{index}";
        return label;
    }

    public ModelConnection? FindModelChoice(string choice) =>
        State.ModelConnections.FirstOrDefault(c => ModelChoice(c.Id, c.Settings.Deployment) == choice)
        ?? State.ModelConnections.FirstOrDefault(c => c.Id == choice)
        ?? State.ModelConnections.FirstOrDefault(c => c.Settings.Deployment == choice);

    public string DeploymentForChoice(string choice) => State.Provider.Mode != ConnectionMode.CompanyApi
        ? choice : FindModelChoice(choice)?.Settings.Deployment ?? choice;

    public ModelConnection SaveModelConnection(ProviderSettings settings, string? token, string? replaceId = null, string? alias = null)
    {
        if (connecting || State.Tasks.Any(IsRunning)) throw new HermesException("실행 중인 작업을 마친 뒤 모델을 수정하세요.");
        settings.NormalizeInferenceEndpoint(); settings.Validate();
        var existing = State.ModelConnections.FirstOrDefault(c => c.Id == replaceId);
        alias = (alias ?? existing?.Alias ?? "").Trim();
        if (alias.Length > 100 || alias.Any(char.IsControl) || Safety.Redact(alias) != alias)
            throw new HermesException("별칭은 제어 문자나 토큰을 제외한 100자 이하로 입력하세요.");
        var connection = existing ?? new ModelConnection();
        var keys = ConnectionKey(connection);
        token = string.IsNullOrWhiteSpace(token) ? keys.HasKey ? keys.ReadOwnKey() : null : token.Trim();
        if (settings.Authentication != ProviderAuth.None && token == null) throw new HermesException("새 모델의 토큰을 입력하세요.");
        if (token != null && (settings.Endpoint.Contains(token, StringComparison.Ordinal) || settings.Deployment.Contains(token, StringComparison.Ordinal) || alias.Contains(token, StringComparison.Ordinal))) throw new HermesException("토큰은 전용 입력란에 입력하세요.");
        var oldName = existing?.Settings.Deployment;
        var clearQuota = existing == null || existing.Settings.Endpoint != settings.Endpoint ||
            existing.Settings.Deployment != settings.Deployment || (keys.HasKey ? keys.ReadOwnKey() : null) != token;
        Configure(settings, token, settings.Authentication == ProviderAuth.None);
        connection.Alias = alias;
        if (clearQuota) connection.QuotaExhaustedUntil = null;
        State.ActiveModelConnectionId = connection.Id;
        State.ModelSelectedByQuota = false;
        if (token != null) keys.Save(token);
        connection.Settings = CopySettings(State.Provider);
        if (existing == null) State.ModelConnections.Add(connection);
        if (oldName != null && oldName != settings.Deployment && !State.ModelConnections.Any(c => c.Settings.Deployment == oldName))
        { foreach (var names in State.SavedModels.Values) names.Remove(oldName); Models.Remove(oldName); }
        Save(); return connection;
    }
    internal ModelConnection? FirstAvailableConnection(DateTimeOffset now) =>
        State.ModelConnections.FirstOrDefault(c => c.Settings.Mode == ConnectionMode.CompanyApi &&
            (c.QuotaExhaustedUntil == null || c.QuotaExhaustedUntil <= now) &&
            (c.Settings.Authentication == ProviderAuth.None || ConnectionKey(c).HasKey));

    public bool SelectModelForNewRequest(string choice)
    {
        var selected = FindModelChoice(choice);
        if (selected == null) return false;
        if (ActiveModelConnection?.Id == selected.Id && !HasPendingModel) return true;
        SelectConnection(selected, false);
        return true;
    }

    public bool SelectModelConnection(string model)
    {
        var connection = FindModelChoice(model);
        if (connection == null) return false;
        SelectConnection(connection, false);
        return true;
    }

    private void SelectConnection(ModelConnection connection, bool byQuota)
    {
        var settings = CopySettings(connection.Settings);
        var keys = ConnectionKey(connection);
        var token = keys.HasKey ? keys.ReadOwnKey() : null;
        if (settings.Authentication != ProviderAuth.None && token == null) throw new HermesException("선택 모델의 토큰이 없습니다. 연결 설정에서 다시 입력하세요.");
        // Configure invalidates the old engine connection. The send flow reconnects before inference.
        if (State.Provider.Fingerprint != settings.Fingerprint ||
            (settings.Authentication != ProviderAuth.None && (!KeyStore.HasKey || KeyStore.ReadOwnKey() != token)))
        {
            Configure(settings, token, settings.Authentication == ProviderAuth.None);
            connection.Settings = CopySettings(State.Provider);
        }
        if (State.ActiveModelConnectionId == connection.Id && State.ModelSelectedByQuota == byQuota) return;
        State.ActiveModelConnectionId = connection.Id;
        State.ModelSelectedByQuota = byQuota;
        Save();
    }
    public void DeleteModelConnection(string id)
    {
        if (connecting || State.Tasks.Any(IsRunning)) throw new HermesException("실행 중인 작업을 마친 뒤 모델을 삭제하세요.");
        var connection = State.ModelConnections.Single(c => c.Id == id);
        var name = connection.Settings.Deployment;
        var active = ActiveModelConnection?.Id == id;
        ConnectionKey(connection).DeleteOwnKey(); State.ModelConnections.Remove(connection);
        if (!State.ModelConnections.Any(c => c.Settings.Deployment == name))
        { foreach (var names in State.SavedModels.Values) names.Remove(name); Models.Remove(name); }
        if (active)
        {
            State.ActiveModelConnectionId = null; State.ModelSelectedByQuota = false;
            KeyStore.DeleteOwnKey(); State.Provider = new(); State.PendingModel = "";
            SubscriptionReady = false; Models.Clear();
        }
        Save();
    }
    public void DeleteModelToken(string? id)
    {
        if (connecting || State.Tasks.Any(IsRunning)) throw new HermesException("실행 중인 작업을 마친 뒤 토큰을 삭제하세요.");
        var connection = State.ModelConnections.FirstOrDefault(c => c.Id == id);
        if (connection != null) ConnectionKey(connection).DeleteOwnKey();
        if (connection == null || connection.Id == ActiveModelConnection?.Id) Configure(State.Provider, deleteKey: true);
        Save();
    }
    public string ExportModelConnections()
    {
        var rows = State.ModelConnections.Select(c => new ModelConnectionExport(c.Settings.Deployment, c.Settings.Endpoint,
            ConnectionKey(c).HasKey ? ConnectionKey(c).ReadOwnKey() : "", c.Settings.Protocol, c.Settings.Authentication, c.Settings.MaxOutputTokens, c.Alias)).ToList();
        if (rows.Count == 0 && State.Provider.Endpoint.Length > 0)
            rows.Add(new(State.Provider.Deployment, State.Provider.Endpoint, KeyStore.HasKey ? KeyStore.ReadOwnKey() : "", State.Provider.Protocol, State.Provider.Authentication, State.Provider.MaxOutputTokens));
        return JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
    }
    public void ImportModelConnections(string json)
    {
        if (json.Length > 2_000_000) throw new HermesException("설정 파일 크기 제한은 2MB입니다.");
        var rows = JsonSerializer.Deserialize<List<ModelConnectionExport>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new HermesException("모델 배열이 필요합니다.");
        if (rows.Count is < 1 or > 200) throw new HermesException("모델은 중복 없이 1~200개 입력하세요.");
        // Validate the whole document before changing any settings or keys.
        var candidates = rows.Select(r => (Row: r, Settings: new ProviderSettings { Deployment = r.ModelName?.Trim() ?? "", Endpoint = r.ModelUrl?.Trim() ?? "", Protocol = r.Protocol, Authentication = r.Authentication, UseAdvancedSettings = true, MaxOutputTokens = r.MaxOutputTokens })).ToList();

        foreach (var (row, settings) in candidates)
        {
            settings.Validate();
            var alias = (row.Alias ?? "").Trim();
            if (alias.Length > 100 || alias.Any(char.IsControl) || Safety.Redact(alias) != alias)
                throw new HermesException("별칭은 제어 문자나 토큰을 제외한 100자 이하로 입력하세요.");
            if (!Enum.IsDefined(settings.Protocol) || !Enum.IsDefined(settings.Authentication)) throw new HermesException("명시적인 연결 프로토콜을 입력하세요.");
            if (row.Token == null || (settings.Authentication != ProviderAuth.None && (row.Token.Trim().Length is < 8 or > 4096 || row.Token.Any(char.IsControl)))) throw new HermesException("토큰 형식이 올바르지 않습니다.");
            if (row.Token.Length > 0 && (settings.Endpoint.Contains(row.Token, StringComparison.Ordinal) || settings.Deployment.Contains(row.Token, StringComparison.Ordinal) || alias.Contains(row.Token, StringComparison.Ordinal))) throw new HermesException("URL 또는 모델명에 토큰을 넣을 수 없습니다.");
        }
        foreach (var (row, settings) in candidates)
        {
            var alias = (row.Alias ?? "").Trim();
            var existing = State.ModelConnections.FirstOrDefault(c => c.Settings.Deployment == settings.Deployment &&
                c.Settings.Endpoint == settings.Endpoint && c.Alias == alias &&
                (settings.Authentication == ProviderAuth.None || (ConnectionKey(c).HasKey && ConnectionKey(c).ReadOwnKey() == row.Token.Trim())));
            SaveModelConnection(settings, row.Token, existing?.Id, alias);
        }
    }
}
