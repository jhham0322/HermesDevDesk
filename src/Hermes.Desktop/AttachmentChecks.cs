using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hermes.Core;

namespace Hermes.Desktop;
public partial class MainWindow
{
    private async Task<int> RunAttachmentChecks()
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new IOException("Attachment UI check failed: " + label); checks++; }
        var originalRead = readClipboard; var originalTask = CurrentTask!;
        var root = CurrentProject!.Root;
        var tiny = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
        var bitmapData = new DataObject(); bitmapData.SetData(DataFormats.Bitmap, tiny);
        try
        {
            ImageClear_Click(this, new());
            readClipboard = () => bitmapData;
            Check(ApplicationCommands.Paste.CanExecute(null, Prompt), "image-only clipboard paste command is enabled");
            ApplicationCommands.Paste.Execute(null, Prompt);
            Check(pendingImages.Count == 1 && Attachments.Items.Count == 1, "routed Paste command attaches bitmap without text");
            ImageClear_Click(this, new());
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(tiny));
            using var pngStream = new MemoryStream(); encoder.Save(pngStream);
            var pngData = new DataObject(); pngData.SetData("PNG", pngStream);
            readClipboard = () => pngData;
            ApplicationCommands.Paste.Execute(null, Prompt);
            Check(pendingImages.Count == 1 && pngStream.Position == pngStream.Length, "PNG-only clipboard with nonzero stream position attaches");
            ImageClear_Click(this, new());
            int attempts = 0;
            readClipboard = () => { if (++attempts < 3) throw new System.Runtime.InteropServices.ExternalException("busy clipboard fixture"); return bitmapData; };
            await PasteClipboardAsync();
            Check(attempts == 3 && pendingImages.Count == 1, "transient clipboard contention retried without losing capture");
            ImageClear_Click(this, new());
            var textData = new DataObject(DataFormats.UnicodeText, "붙여넣은 텍스트"); readClipboard = () => textData;
            Prompt.Text = "시작 끝"; Prompt.Select(3, 1); ApplicationCommands.Paste.Execute(null, Prompt);
            Check(Prompt.Text == "시작 붙여넣은 텍스트", "ordinary text paste replaces selected text");
            var path = Path.Combine(root, "첨부 문서.txt"); File.WriteAllText(path, "UI_ATTACHMENT_TEXT_087");
            var imagePath = Path.Combine(root, "capture-test.png"); File.WriteAllBytes(imagePath, pngStream.ToArray());
            var drop = new DataObject(DataFormats.FileDrop, new[] { imagePath, path });
            Check(AddAttachmentData(drop, false) && pendingImages.Count == 1 && pendingFiles.Count == 1 && Attachments.Items.Count == 2, "mixed image and file drop reaches unified attachment list");
            var second = desk.NewTask(CurrentProject!, "첨부 초안 분리"); SelectTask(second);
            Check(pendingImages.Count == 0 && pendingFiles.Count == 0, "second conversation has separate attachments");
            SelectTask(originalTask);
            Check(pendingImages.Count == 1 && pendingFiles.Count == 1, "switching conversations restores both image and file drafts");
            var before = Attachments.Items.Count;
            try { AddAttachmentPaths([path, Path.Combine(root, "missing-file.txt")]); } catch (IOException) { }
            Check(Attachments.Items.Count == before && pendingFiles.Count == 1, "failed multi-file selection preserves existing attachment draft");
            var context = FileAttachments.ModelContext(root, pendingFiles);
            Check(context.Contains("UI_ATTACHMENT_TEXT_087"), "UI-selected file context includes actual text content");
            ImageClear_Click(this, new());
            var bytes = new byte[1024 * 1024 * 3]; new Random(87).NextBytes(bytes);
            var large = BitmapSource.Create(1024, 1024, 96, 96, PixelFormats.Rgb24, null, bytes, 1024 * 3);
            AddClipboardImage(large);
            Check(pendingImages.Single().Bytes > 2 * 1024 * 1024, "capture above previous 2MB limit attaches successfully");
            ImageClear_Click(this, new());
            var emptyRoot = Path.Combine(Path.GetDirectoryName(root)!, "new-attachment-project"); Directory.CreateDirectory(emptyRoot);
            var empty = desk.Register(emptyRoot); Projects.SelectedItem = empty; RenderTask();
            Check(CurrentTask == null, "new project starts without a selected chat");
            Prompt.Text = "이미지 확인"; AddAttachmentData(bitmapData, true);
            Check(CurrentTask != null && pendingImages.Count == 1 && Prompt.Text == "이미지 확인", "attaching before first chat creates draft without clearing image or prompt");
            SelectTask(originalTask);
            ImageClear_Click(this, new());
            AddAttachmentPaths([imagePath, path]); Prompt.Clear();
            File.WriteAllText(Path.Combine(desk.DataDirectory, "attachment-ui-report.txt"), $"PASS: {checks} attachment command, format, draft, file-drop and capture checks. Clipboard source is synthetic; user clipboard is unchanged.");
            return checks;
        }
        finally { readClipboard = originalRead; }
    }
}
