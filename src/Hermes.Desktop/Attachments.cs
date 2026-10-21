using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Hermes.Core;
using Microsoft.Win32;

namespace Hermes.Desktop;
public partial class MainWindow
{
    private bool pastingAttachment;
    private Func<IDataObject?> readClipboard = Clipboard.GetDataObject;
    private void InitializeAttachments()
    {
        // TextBox's default CanExecute disables Paste for image-only clipboards, before Pasting fires.
        Prompt.CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste,
            async (_, e) => { e.Handled = true; await Guard(PasteClipboardAsync, "attachment-paste"); },
            (_, e) => { e.CanExecute = !pastingAttachment; e.Handled = true; }));
        Prompt.AllowDrop = true;
        Prompt.PreviewDragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Prompt.PreviewDrop += (_, e) =>
        {
            e.Handled = true;
            _ = Guard(() => { AddAttachmentData(e.Data, false); return Task.CompletedTask; }, "attachment-drop");
        };
        RefreshAttachments();
    }
    private async void PasteAttachment_Click(object sender, RoutedEventArgs e) => await Guard(PasteClipboardAsync, "attachment-paste");
    private async Task PasteClipboardAsync()
    {
        if (pastingAttachment) return;
        pastingAttachment = true;
        var target = CurrentTask?.Id; var project = CurrentProject?.Id;
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                if (target != CurrentTask?.Id || project != CurrentProject?.Id) throw new Hermes.Core.HermesException("대화가 바뀌었습니다. 선택한 대화에 다시 붙여넣으세요.");
                try
                {
                    var data = readClipboard();
                    if (data == null || !AddAttachmentData(data, true)) Footer.Text = "클립보드에 붙여넣을 이미지·파일·텍스트가 없습니다.";
                    break;
                }
                catch (Exception ex) when ((ex is ExternalException or System.Threading.ThreadStateException) && attempt < 4)
                { await Task.Delay(60 * (attempt + 1)); }
            }
        }
        finally { pastingAttachment = false; }
    }
    private bool AddAttachmentData(IDataObject data, bool includeText)
    {
        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] paths)
        { AddAttachmentPaths(paths); return paths.Length > 0; }
        // Snipping Tool, browsers and editors may expose PNG rather than WPF Bitmap.
        foreach (var format in new[] { "PNG", "image/png" })
        {
            if (!data.GetDataPresent(format)) continue;
            var value = data.GetData(format);
            if (value is byte[] bytes) { using var stream = new MemoryStream(bytes); AddClipboardImage(DecodeClipboardImage(stream)); return true; }
            if (value is Stream source)
            {
                var position = source.CanSeek ? source.Position : 0;
                try { if (source.CanSeek) source.Position = 0; AddClipboardImage(DecodeClipboardImage(source)); return true; }
                finally { if (source.CanSeek) source.Position = position; }
            }
        }
        if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
        { AddClipboardImage(bitmap); return true; }
        if (data.GetDataPresent(DataFormats.Dib) || data.GetDataPresent(DataFormats.Bitmap))
        {
            var fallback = Clipboard.GetImage();
            if (fallback != null) { AddClipboardImage(fallback); return true; }
        }
        if (includeText && (data.GetData(DataFormats.UnicodeText) ?? data.GetData(DataFormats.Text)) is string text)
        {
            var start = Prompt.SelectionStart; Prompt.SelectedText = text;
            Prompt.CaretIndex = start + text.Length; Prompt.Focus(); return true;
        }
        return false;
    }
    private static BitmapSource DecodeClipboardImage(Stream stream)
    {
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var image = decoder.Frames[0]; image.Freeze(); return image;
    }
    private void EnsureAttachmentTask()
    {
        if (CurrentProject == null) throw new Hermes.Core.HermesException("첨부할 프로젝트를 먼저 선택하세요.");
        if (CurrentTask == null)
        {
            var draft = Prompt.Text; NewTask_Click(this, new()); Prompt.Text = draft;
        }
        if (CurrentTask?.Archived == true) throw new Hermes.Core.HermesException("대화를 복원한 뒤 첨부하세요.");
    }
    private void ChooseAttachments(bool imagesOnly) => _ = Guard(() =>
    {
        var project = CurrentProject ?? throw new Hermes.Core.HermesException("첨부할 프로젝트를 먼저 선택하세요.");
        var dialog = new OpenFileDialog
        {
            Filter = imagesOnly ? "이미지|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|모든 파일|*.*" : "모든 파일|*.*|문서와 텍스트|*.pdf;*.docx;*.xlsx;*.txt;*.md;*.csv;*.json",
            Multiselect = true, InitialDirectory = project.Root, Title = imagesOnly ? "이미지 첨부" : "파일 첨부 · 프로젝트 밖 파일도 가능"
        };
        if (dialog.ShowDialog(this) == true) AddAttachmentPaths(dialog.FileNames);
        return Task.CompletedTask;
    }, "attachment-import");
    private void AddAttachmentPaths(IEnumerable<string> paths)
    {
        var imagesBefore = pendingImages.ToList(); var filesBefore = pendingFiles.ToList();
        try { foreach (var path in paths) AddAttachmentPath(path); }
        catch { pendingImages.Clear(); pendingImages.AddRange(imagesBefore); pendingFiles.Clear(); pendingFiles.AddRange(filesBefore); RefreshAttachments(); throw; }
    }
    private void AddAttachmentPath(string path)
    {
        EnsureAttachmentTask();
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".png" or ".jpg" or ".jpeg") { AddImage(path); return; }
        if (extension is ".bmp" or ".gif" or ".tif" or ".tiff")
        {
            Safety.RejectReparseAncestors(path);
            if (new FileInfo(path).Length > ImageAttachments.MaxImageBytes) throw new Hermes.Core.HermesException("이미지 파일당 최대 20MB입니다.");
            using var stream = File.OpenRead(path); AddClipboardImage(DecodeClipboardImage(stream)); return;
        }
        var project = CurrentProject!;
        if (pendingFiles.Count >= FileAttachments.MaxFiles) throw new Hermes.Core.HermesException("파일은 최대 10개까지 첨부할 수 있습니다.");
        var file = FileAttachments.Import(project.Root, path);
        try { var selected = FileAttachments.Validate(project.Root, pendingFiles.Append(file)); pendingFiles.Clear(); pendingFiles.AddRange(selected); }
        catch { File.Delete(Safety.Resolve(project.Root, file.RelativePath)); throw; }
        RefreshAttachments();
    }
    private void RefreshAttachments()
    {
        Attachments.ItemsSource = pendingImages.Cast<object>().Concat(pendingFiles).ToList();
        AttachmentHint.Text = pendingImages.Count + pendingFiles.Count == 0
            ? "Ctrl+V 캡처 붙여넣기 · 이미지 4개/40MB · 파일 10개/50MB"
            : $"이미지 {pendingImages.Count}개 · 파일 {pendingFiles.Count}개 · 이미지는 모델로 전송, 파일은 작업 폴더 사본과 텍스트 미리보기로 전달";
    }
}
