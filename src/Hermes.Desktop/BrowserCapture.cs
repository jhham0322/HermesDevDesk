using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hermes.Core;
using WebView2 = Microsoft.Web.WebView2.Wpf.WebView2CompositionControl;

namespace Hermes.Desktop;
public partial class MainWindow
{
    private sealed record BrowserPageCapture(string RelativePath, int Width, int Height, IReadOnlyList<BitmapSource> Tiles, string Description);
    private static string PngDataUrl(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = new MemoryStream(); encoder.Save(output);
        return "data:image/png;base64," + Convert.ToBase64String(output.ToArray());
    }
    private async Task<BrowserPageCapture> CaptureBrowserPage(WebView2 view, string root, CancellationToken ct)
    {
        if (view.Source == null || view.Source.Scheme is not ("http" or "https")) throw new Hermes.Core.HermesException("브라우저에서 캡처할 페이지를 먼저 여세요.");
        browserStatus.Text = "전체 페이지 캡처 중…";
        using var metrics = JsonDocument.Parse(await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.getLayoutMetrics", "{}").WaitAsync(ct));
        var size = metrics.RootElement.Property("cssContentSize") ?? metrics.RootElement.GetProperty("contentSize");
        var width = Math.Ceiling(size.GetProperty("width").GetDouble()); var height = Math.Ceiling(size.GetProperty("height").GetDouble());
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 1 || height < 1 || width > 32767 || height > 32767 || width * height > 64_000_000)
            throw new Hermes.Core.HermesException("전체 캡처 범위는 한 변 32767px, 합계 64MP까지입니다. 현재 화면 캡처를 사용하거나 페이지 범위를 줄이세요.");
        var parameters = JsonSerializer.Serialize(new { format = "png", fromSurface = true, captureBeyondViewport = true, clip = new { x = 0, y = 0, width, height, scale = 1 } });
        using var capture = JsonDocument.Parse(await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", parameters).WaitAsync(ct));
        var encoded = capture.RootElement.GetProperty("data").GetString()!;
        if (encoded.Length > 128_000_000) throw new Hermes.Core.HermesException("전체 캡처 파일이 너무 큽니다. 페이지 범위를 줄이세요.");
        var bytes = Convert.FromBase64String(encoded);
        using var stream = new MemoryStream(bytes);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0]; frame.Freeze();
        if ((long)frame.PixelWidth * frame.PixelHeight > 64_000_000 || frame.PixelHeight > 32767) throw new Hermes.Core.HermesException("캡처 이미지가 64MP 또는 높이 제한을 초과했습니다.");
        var relative = Path.Combine(".hermes-captures", $"page-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.png");
        var destination = Safety.Resolve(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllBytesAsync(destination, bytes, ct);
        // Preserve the full original and split a width-limited copy; never shrink a long page into a tiny overview.
        var scale = Math.Min(1.0, 1600.0 / frame.PixelWidth);
        BitmapSource readable = scale < 1 ? new TransformedBitmap(frame, new ScaleTransform(scale, scale)) : frame;
        readable.Freeze();
        var tiles = new List<BitmapSource>(); var spans = new List<object>();
        var tileHeight = Math.Min(8192, Math.Max(1600, (int)Math.Ceiling(readable.PixelHeight / 4.0)));
        for (int y = 0; y < readable.PixelHeight; y += tileHeight)
        {
            var tile = new CroppedBitmap(readable, new Int32Rect(0, y, readable.PixelWidth, Math.Min(tileHeight, readable.PixelHeight - y))); tile.Freeze();
            tiles.Add(tile); spans.Add(new { part = tiles.Count, x = 0, y, width = tile.PixelWidth, height = tile.PixelHeight });
        }
        var description = JsonSerializer.Serialize(new { kind = "fullPage", url = Safety.Redact(view.Source.AbsoluteUri), originalPath = destination, originalWidth = frame.PixelWidth, originalHeight = frame.PixelHeight, imageScale = scale, tiles = spans, note = "이미지는 위에서 아래 순서이며 현재 로드된 페이지 전체를 빠짐없이 나눈 것입니다." });
        if (activeBrowserView == view) browserStatus.Text = $"전체 캡처 완료 · {frame.PixelWidth}×{frame.PixelHeight} · 이미지 {tiles.Count}개 · 원본 저장: {relative}";
        return new(relative, frame.PixelWidth, frame.PixelHeight, tiles, description);
    }
    private async Task CaptureBrowserToComposerAsync()
    {
        EnsureAttachmentTask();
        var task = CurrentTask!; var root = CurrentProject!.Root;
        if (!browserLocks.TryGetValue(task.Id, out var gate)) browserLocks[task.Id] = gate = new(1, 1);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        await gate.WaitAsync(timeout.Token);
        try
        {
            var view = await EnsureChatBrowser(task, timeout.Token); SetBrowserView(view); UpdateLayout();
            var page = await CaptureBrowserPage(view, root, timeout.Token);
            if (CurrentTask != task || CurrentProject?.Root != root) throw new Hermes.Core.HermesException("대화가 바뀌었습니다. 캡처 원본은 저장했으며 현재 대화에는 첨부하지 않았습니다.");
            if (pendingImages.Count + page.Tiles.Count > 4) throw new Hermes.Core.HermesException("캡처 원본은 저장했습니다. 이미지 첨부를 비운 뒤 다시 캡처하세요. 최대 4개입니다.");
            var original = pendingImages.ToList();
            try { foreach (var tile in page.Tiles) AddClipboardImage(tile); }
            catch { pendingImages.Clear(); pendingImages.AddRange(original); RefreshAttachments(); throw; }
            Footer.Text = $"전체 페이지 캡처 이미지 {page.Tiles.Count}개를 AI 입력에 첨부했습니다. 전송 버튼으로 보내세요. 원본: {page.RelativePath}";
        }
        finally { gate.Release(); ShowChatBrowser(); }
    }
}
