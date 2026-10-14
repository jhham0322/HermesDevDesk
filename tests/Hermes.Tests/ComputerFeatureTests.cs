using System.Text.Json;
using Hermes.Core;

internal static class ComputerFeatureTests
{
    internal static void Run(Action<bool, string> check)
    {
        void Reject(Action action, string label) { try { action(); } catch (IOException) { check(true, label); return; } throw new IOException("Expected rejection: " + label); }
        var window = Guid.NewGuid().ToString("N"); var other = Guid.NewGuid().ToString("N"); var now = DateTimeOffset.UtcNow;
        var gate = new ComputerSnapshotGate(); var snapshot = gate.Record(window, now);
        Reject(() => gate.Consume(other, snapshot, now), "computer snapshot cannot target another window");
        gate.Consume(window, snapshot, now.AddSeconds(1)); check(true, "fresh selected-window computer snapshot accepted");
        Reject(() => gate.Consume(window, snapshot, now.AddSeconds(2)), "computer snapshot consumed once per input");
        snapshot = gate.Record(window, now); Reject(() => gate.Consume(window, snapshot, now.AddSeconds(46)), "expired computer snapshot rejected");
        snapshot = gate.Record(window, now); gate.Clear(); Reject(() => gate.Consume(window, snapshot, now), "emergency reset invalidates computer snapshots");
        ComputerAction Parse(string json) { using var data = JsonDocument.Parse(json); return ComputerTools.Parse(data.RootElement); }
        check(Parse("{\"action\":\"windows\"}").Action == "windows", "computer tool lists only host-allowed windows");
        Reject(() => Parse("{\"action\":\"click\",\"windowId\":\"123\"}"), "computer action rejects raw native window handles");
        Reject(() => Parse(JsonSerializer.Serialize(new { action = "click", windowId = window, x = 10, y = 10 })), "computer input requires observed snapshot");
        Reject(() => Parse(JsonSerializer.Serialize(new { action = "click", windowId = window, snapshotId = Guid.NewGuid().ToString("N"), x = -1, y = 10 })), "computer negative coordinates rejected");
        Reject(() => Parse(JsonSerializer.Serialize(new { action = "click", windowId = window, snapshotId = Guid.NewGuid().ToString("N"), x = "bad", y = 10 })), "computer nonnumeric coordinates rejected");
        check(ComputerTools.KeyCodes("CTRL+SHIFT+S").SequenceEqual(new ushort[] { 0x11, 0x10, 0x53 }), "computer key chord maps ordered modifiers");
        foreach (var key in new[] { "WIN+R", "ALT+TAB", "ALT+F4", "CTRL+ESC", "CTRL+SHIFT+ESC", "CTRL+V", "CTRL+C", "CTRL+X", "A+CTRL", "CTRL+CTRL+A" }) Reject(() => ComputerTools.KeyCodes(key), "computer restricted key rejected: " + key);
        using var response = JsonDocument.Parse(JsonSerializer.Serialize(new ComputerToolResult("fixture", "data:image/png;base64,fixture").ContentItems));
        check(response.RootElement[0].Text("type") == "inputText" && response.RootElement[1].Text("type") == "inputImage", "computer result uses official text and image content kinds");
    }
}
