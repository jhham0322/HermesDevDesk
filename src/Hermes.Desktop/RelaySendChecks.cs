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
        int probes = 0, generations = 0, models = 0, unexpectedRoutes = 0; bool imageSent = false, fileSent = false, drmSkillSent = false;
        await using var server = new LoopbackHttp(async (request, stream, ct) =>
        {
            if (request.Path.EndsWith("/models")) models++;
            if (request.Path != "/v1/responses") { unexpectedRoutes++; await LoopbackHttp.Reply(stream, 404, "application/json", "{}", ct); return; }
            using var body = JsonDocument.Parse(request.Body);
            if (body.RootElement.Text("model") != "gpt-6-luna") throw new IOException("Wrong relay model");
            bool probe = body.RootElement.Property("max_output_tokens")?.GetInt32() == 1;
            if (probe) probes++; else { generations++; imageSent |= body.RootElement.GetRawText().Contains("data:image/png;base64,"); fileSent |= body.RootElement.GetRawText().Contains("UI_SEND_FILE_087"); drmSkillSent |= body.RootElement.GetRawText().Contains("HERMES_TEST_AUTHORIZED_DRM"); }
            JsonObject Message(string text) => new() { ["id"] = "msg_owned_" + (probes + generations), ["type"] = "message", ["role"] = "assistant", ["status"] = "completed", ["phase"] = "final_answer", ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = text, ["annotations"] = new JsonArray() }) };
            var response = new JsonObject { ["id"] = "resp_owned_" + (probes + generations), ["object"] = "response", ["model"] = "gpt-6-luna", ["status"] = "completed", ["created_at"] = 1,
                ["output"] = new JsonArray(probe ? Message("OK") : Message("OWNED_RELAY_SEND_OK")) };
            await LoopbackHttp.Reply(stream, 200, "application/json", response.ToJsonString(), ct);
        });
        var root = Path.Combine(data, "owned-project"); Directory.CreateDirectory(root);
        var project = desk.Register(root); Projects.SelectedItem = project;
        desk.Configure(new() { Endpoint = server.BaseUri.AbsoluteUri.TrimEnd('/') + "/v1/responses", Deployment = "gpt-6-luna", Protocol = ProviderProtocol.GatewayAuto }, "SYNTHETIC_RELAY_SEND_ONLY");
        desk.RememberModel("gpt-6-luna"); Refresh();
        if (CurrentTask != null || desk.HasPendingModel || !StartButton.IsEnabled) throw new IOException("New conversation send remains disabled");
        if (ProviderHint.Text != "회사 API · 선택한 모델로 전송") throw new IOException("Unexpected company connection hint");
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
        if (!imageSent || !fileSent || !drmSkillSent || pendingImages.Count != 0 || pendingFiles.Count != 0 || generations != 1 || probes != 0 || models != 0 || unexpectedRoutes != 0 || !desk.State.Tasks.Any(t => t.Status == "완료") || !desk.Processes.Any(p => p.Items is [{ Id: "quick-answer", State: "verified" }])) throw new IOException("Relay send did not complete: " + desk.AccountStatus);
        await ConnectSelectedEngineAsync();
        if (generations != 1 || probes != 0 || models != 0 || unexpectedRoutes != 0 || !desk.SubscriptionReady)
            throw new IOException("Engine connect triggered a provider request");
        File.WriteAllText(Path.Combine(data, "relay-send-report.json"), JsonSerializer.Serialize(new { passed = true, exactModel = "gpt-6-luna", probes, generations, models, unexpectedRoutes, noExistingTask = true, missingUsageSupported = true, actualOfficialEngine = true, imageSent, fileSent, drmSkillSent, attachmentOnlySend = true, programmaticWpfButton = true, engineConnectSentNoRequests = true, realCompanyRequests = false }));
    }
}
