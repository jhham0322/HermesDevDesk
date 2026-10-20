using Hermes.Core;

internal static class FileAttachmentTests
{
    internal static void Run(string root, Action<bool, string> check)
    {
        Directory.CreateDirectory(root);
        var source = Path.Combine(Path.GetDirectoryName(root)!, "첨부 문서.txt");
        File.WriteAllText(source, "ATTACHMENT_TEXT_087");
        var attached = FileAttachments.Import(root, source);
        check(attached.Name == "첨부 문서.txt" && attached.RelativePath.StartsWith(".hermes-attachments"), "external file copied with original display name");
        File.WriteAllText(source, "source edited after import");
        check(File.ReadAllText(Safety.Resolve(root, attached.RelativePath)) == "ATTACHMENT_TEXT_087", "file attachment uses independent copy without changing source");
        check(FileAttachments.ModelContext(root, [attached]).Contains("ATTACHMENT_TEXT_087"), "text attachment content reaches model context");
        void Reject(Action action, string label) { try { action(); } catch (IOException) { check(true, label); return; } throw new IOException(label); }
        Reject(() => FileAttachments.Validate(root, [attached, attached]), "duplicate file attachment rejected");
        var secret = Path.Combine(Path.GetDirectoryName(root)!, ".env"); File.WriteAllText(secret, "SYNTHETIC=1");
        Reject(() => FileAttachments.Import(root, secret), "secret filename rejected before import");
        Reject(() => FileAttachments.Validate(root, Enumerable.Range(0, 11).Select(n => attached with { RelativePath = n.ToString() })), "file count limit enforced before reading");
        Reject(() => FileAttachments.Validate(root, [attached with { Bytes = FileAttachments.MaxTotalBytes + 1L }]), "total attachment size enforced");
        File.WriteAllText(Safety.Resolve(root, attached.RelativePath), "tampered");
        Reject(() => FileAttachments.Validate(root, [attached]), "changed attached file blocked before sending");
        var binary = Path.Combine(Path.GetDirectoryName(root)!, "document.pdf"); File.WriteAllBytes(binary, "%PDF-1.4\nfixture"u8.ToArray());
        var pdf = FileAttachments.Import(root, binary);
        check(FileAttachments.ModelContext(root, [pdf]).Contains("document.pdf") && !FileAttachments.ModelContext(root, [pdf]).Contains("%PDF"), "binary document passed as tool-readable file rather than corrupted text");
        var state = new AppState { Tasks = [new() { FileAttachments = [new(pdf, DateTime.UtcNow)] }] };
        var store = new StateStore(Path.Combine(root, "file-history")); store.Save(state);
        check(store.Load().Tasks.Single().FileAttachments.Single().File == pdf, "file attachment metadata survives restart");
        check(ImageAttachments.MaxImageBytes >= 20 * 1024 * 1024 && ImageAttachments.MaxTotalBytes >= 40 * 1024 * 1024, "large capture byte limits increased");
    }
}
