using System.Text.Json;
namespace Hermes.Core;
public sealed class LocalInspectionClient : IAsyncDisposable
{
    private readonly AppServerClient client = new();
    private readonly LoopbackHttp offline;
    private readonly string root;
    public int RejectedModelRequests { get; private set; }
    public LocalInspectionClient(string root)
    {
        this.root = Safety.ProjectRoot(root);
        offline = new(async (_, stream, ct) => { RejectedModelRequests++; await LoopbackHttp.Reply(stream, 403, "application/json", "{\"error\":{\"message\":\"Inspection model access disabled\"}}", ct); });
    }
    public async Task ConnectAsync(string cli, string home, CancellationToken cancellation = default)
    { cancellation.ThrowIfCancellationRequested(); await client.ConnectAsync(cli, root, home, new Uri(offline.BaseUri, "v1"), Guid.NewGuid().ToString("N"), "hermes-offline-inspection"); cancellation.ThrowIfCancellationRequested(); }
    public async Task<List<ProjectSkill>> SkillsAsync(CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var result = await client.RequestAsync("skills/list", new { cwds = new[] { root }, forceReload = true });
        cancellation.ThrowIfCancellationRequested(); return ProjectSkills.FilterOfficial(root, result);
    }
    public async ValueTask DisposeAsync() { await client.DisposeAsync(); await offline.DisposeAsync(); }
}
