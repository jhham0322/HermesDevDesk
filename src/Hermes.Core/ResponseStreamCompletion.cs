using System.Text.Json;
namespace Hermes.Core;
// Terminal protocol evidence, not socket EOF or visible partial text, ends a response.
internal sealed class ResponseStreamCompletion
{
    internal bool Terminated { get; private set; }
    internal static string Data(string frame) => string.Join("\n", frame.Replace("\r\n", "\n").Split('\n').Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l[5..].TrimStart()));
    internal JsonElement? Accept(string frame)
    {
        var data = Data(frame); if (data.Length == 0 || data == "[DONE]") return null;
        using var doc = JsonDocument.Parse(data); var root = doc.RootElement;
        var type = root.Text("type");
        if (type == "error") throw new ModelServerException(ProviderErrors.ReadableMessage(data));
        var status = type switch { "response.completed" => "completed", "response.failed" => "failed", "response.incomplete" => "incomplete", _ => "" };
        if (status.Length == 0) return null;
        if (root.Property("response") is not { ValueKind: JsonValueKind.Object } response || response.Text("status") != status) throw new HermesException("Responses terminal event/status mismatch; partial output is not completion.");
        Terminated = true; return response.Clone();
    }
    internal void RequireTerminal() { if (!Terminated) throw new IncompleteResponseStreamException(); }
}

internal sealed class IncompleteResponseStreamException : IOException { internal IncompleteResponseStreamException() : base("Responses stream ended without a terminal event; partial output retained, no automatic replay.") { } }
