using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;
using Hermes.Core;

namespace Hermes.Desktop;
public partial class MainWindow
{
    internal async Task RunRelaySendChecks(string data)
    {
        int probes = 0, generations = 0, models = 0; bool imageSent = false, fileSent = false, drmSkillSent = false;
        await using var server = new LoopbackHttp(async (request, stream, ct) =>
        {
            if (request.Path.EndsWith("/models")) models++;
            if (request.Path != "/v1/responses") { await LoopbackHttp.Reply(stream, 404, "application/json", "{}", ct); return; }
            using var body = JsonDocument.Parse(request.Body);
            if (body.RootElement.Text("model") != "gpt-6-luna") throw new IOException("Wrong relay model");
            bool probe = body.RootElement.Property("max_output_tokens")?.GetInt32() == 1;
            if (probe) probes++; else { generations++; imageSent |= body.RootElement.GetRawText().Contains("data:image/png;base64,"); fileSent |= body.RootElement.GetRawText().Contains("UI_SEND_FILE_087"); drmSkillSent |= body.RootElement.GetRawText().Contains("HERMES_TEST_AUTHORIZED_DRM"); }
            JsonObject Message(string text) => new() { ["id"] = "msg_owned_" + (probes + generations), ["type"] = "message", ["role"] = "assistant", ["status"] = "completed", ["phase"] = "final_answer", ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = text, ["annotations"] = new JsonArray() }) };
            JsonObject Call(object args)
            {
                var tools = body.RootElement.Property("tools") is { ValueKind: JsonValueKind.Array } values
                    ? values.EnumerateArray().ToArray()
                    : [];
                var ns = tools.FirstOrDefault(t => t.Text("type") == "namespace" &&
                    t.GetProperty("tools").EnumerateArray().Any(member => member.Text("name") == "hermes_task"));
                if (ns.ValueKind == JsonValueKind.Undefined && !tools.Any(t => t.Text("name") == "hermes_task"))
                    throw new IOException("The fixture request did not include the work plan tool.");
                var call = new JsonObject { ["id"] = "fc_fixture_" + generations, ["type"] = "function_call", ["status"] = "completed", ["call_id"] = "call_fixture_" + generations, ["name"] = "hermes_task", ["arguments"] = JsonSerializer.Serialize(args) };
                if (ns.ValueKind != JsonValueKind.Undefined) call["namespace"] = ns.Text("name");
                return call;
            }
            var output = probe ? Message("OK") : generations switch
            {
                1 => Call(new { action = "plan", items = new[] { new { id = "describe", title = "Describe the attachments", acceptance = "Deliver a description of the image and file.", kind = "answer" } } }),
                _ => Message("OWNED_RELAY_SEND_OK")
            };
            var response = new JsonObject { ["id"] = "resp_owned_" + (probes + generations), ["object"] = "response", ["model"] = "gpt-6-luna", ["status"] = "completed", ["created_at"] = 1,
                ["output"] = new JsonArray(output) };
            await LoopbackHttp.Reply(stream, 200, "application/json", response.ToJsonString(), ct);
        });
        var root = Path.Combine(data, "owned-project"); Directory.CreateDirectory(root);
        var project = desk.Register(root); Projects.SelectedItem = project;
        desk.Configure(new() { Endpoint = server.BaseUri.AbsoluteUri.TrimEnd('/') + "/v1", Deployment = "gpt-6-luna", Protocol = ProviderProtocol.GatewayAuto }, "SYNTHETIC_RELAY_SEND_ONLY");
        desk.RememberModel("gpt-6-luna"); Refresh();
        if (CurrentTask != null || desk.HasPendingModel || !StartButton.IsEnabled) throw new IOException("New conversation send remains disabled");
        UpdateShellState(); if (!StartButton.IsEnabled) throw new IOException("Shell overwrote send readiness");
        var drmDirectory = Path.Combine(root, ".agents", "skills", "drm");
        Directory.CreateDirectory(drmDirectory);
        File.WriteAllText(Path.Combine(drmDirectory, "SKILL.md"), "---\nname: drm\ndescription: Synthetic authorized document workflow\n---\nHERMES_TEST_AUTHORIZED_DRM\n");
        var officeFile = Path.Combine(data, "사내 보고서.docx");
        File.WriteAllText(officeFile, "Synthetic document path routing only.");
        var attachmentFile = Path.Combine(data, "선택 파일.txt"); File.WriteAllText(attachmentFile, "UI_SEND_FILE_087");
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(2, 2, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[16], 8);
        var originalClipboard = readClipboard;
        try
        {
            var capture = new DataObject(); capture.SetData(DataFormats.Bitmap, bitmap); readClipboard = () => capture;
            System.Windows.Input.ApplicationCommands.Paste.Execute(null, Prompt);
            AddAttachmentPaths([attachmentFile, officeFile]); Prompt.Clear();
            if (pendingImages.Count != 1 || pendingFiles.Count != 2) throw new IOException("Attachment-only UI draft was lost");
        }
        finally { readClipboard = originalClipboard; }
        StartButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        var until = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < until && !desk.State.Tasks.Any(t => t.Status == "완료" && t.Transcript.Any(x => x.Text.Contains("OWNED_RELAY_SEND_OK"))))
            await Task.Delay(100);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (!imageSent || !fileSent || !drmSkillSent || pendingImages.Count != 0 || pendingFiles.Count != 0 || generations != 2 || probes != 1 || models != 0 || !desk.State.Tasks.Any(t => t.Status == "완료")) throw new IOException("Relay send did not complete: " + desk.AccountStatus);
        File.WriteAllText(Path.Combine(data, "relay-send-report.json"), JsonSerializer.Serialize(new { passed = true, exactModel = "gpt-6-luna", probes, generations, models, noExistingTask = true, missingUsageSupported = true, actualOfficialEngine = true, imageSent, fileSent, drmSkillSent, attachmentOnlySend = true, programmaticWpfButton = true, realCompanyRequests = false }));
    }
}
