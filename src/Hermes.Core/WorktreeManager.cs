using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Hermes.Core;
public sealed record WorktreeCreated(string Root, string Branch, string Commit, int Files);
public static class WorktreeManager
{
    private const string Marker = "HermesDevDesk-worktree-storage-v1";
    public static async Task<WorktreeCreated> CreateAsync(string source, string storage, IEnumerable<string> registeredRoots)
    {
        source = Safety.ProjectRoot(source); storage = Safety.ProjectRoot(storage);
        if (registeredRoots.Append(source).Any(r => Safety.Within(r, storage) || Safety.Within(storage, r))) throw new IOException("Worktree 보관 폴더는 등록 프로젝트와 겹치지 않아야 합니다.");
        var marker = Path.Combine(storage, ".hermes-worktree-storage");
        if (Directory.EnumerateFileSystemEntries(storage).Any() && (!File.Exists(marker) || File.ReadAllText(marker) != Marker)) throw new IOException("Worktree 보관 폴더는 비어 있거나 Hermes 소유여야 합니다.");
        await GitRunner.RepositoryRoot(source);
        var head = await GitRunner.Run(source, "rev-parse", "--verify", "HEAD^{commit}");
        if (head.Exit != 0) throw new IOException("커밋이 없는 저장소는 worktree를 만들 수 없습니다.");
        var commit = head.Output.Trim();
        var tree = await GitRunner.Run(source, "ls-tree", "-r", "-z", commit);
        if (tree.Exit != 0) throw new IOException(tree.Error);
        var blobs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in tree.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = record.Split('\t', 2); var metadata = parts[0].Split(' ');
            if (parts.Length != 2 || metadata.Length != 3 || metadata[1] != "blob" || metadata[0] is not ("100644" or "100755")) throw new IOException("심볼릭 링크·submodule·알 수 없는 트리 항목은 worktree 복제를 차단합니다.");
            Safety.Resolve(source, parts[1]); // Secrets are rejected before any Git mutation.
            if (!blobs.TryAdd(parts[1], metadata[2])) throw new IOException("Windows에서 충돌하는 파일 이름입니다.");
        }
        if (blobs.Count > 5000) throw new IOException("worktree는 5000개 이하 파일만 지원합니다.");
        var archive = await GitRunner.Archive(source, commit);
        using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
        var contents = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase); long size = 0;
        foreach (var entry in zip.Entries.Where(e => e.Name.Length > 0))
        {
            Safety.Resolve(source, entry.FullName); size += entry.Length;
            if (size > 100_000_000 || entry.Length > 20_000_000) throw new IOException("worktree 크기 제한: 전체 100 MB, 파일별 20 MB.");
            using var stream = entry.Open(); using var bytes = new MemoryStream(); stream.CopyTo(bytes); var content = bytes.ToArray();
            if (!blobs.TryGetValue(entry.FullName, out var oid) || !contents.TryAdd(entry.FullName, content) || BlobHash(content, oid.Length == 64) != oid) throw new IOException("Git export 속성이 원본 파일을 변경/제외했습니다. 안전한 복제를 차단합니다.");
        }
        if (contents.Count != blobs.Count) throw new IOException("Git export-ignore 등으로 파일이 빠져 복제를 차단합니다.");
        var id = Guid.NewGuid().ToString("N"); var target = Path.Combine(storage, id); var branch = "hermes/" + id;
        if (Directory.Exists(target) || File.Exists(target)) throw new IOException("기존 대상은 덮어쓰지 않습니다.");
        if (!File.Exists(marker)) File.WriteAllText(marker, Marker);
        var added = await GitRunner.Run(source, "worktree", "add", "--no-checkout", "--lock", "--reason", "Hermes DevDesk user-owned isolated task; never auto-delete", "-b", branch, target, commit);
        if (added.Exit != 0) throw new IOException("Worktree 생성 실패. 부분 상태를 보존했습니다: " + target + "\n" + added.Error);
        try
        {
            foreach (var entry in contents)
            {
                var path = Safety.Resolve(target, entry.Key); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); file.Write(entry.Value);
            }
            var index = await GitRunner.Run(target, "read-tree", commit); if (index.Exit != 0) throw new IOException(index.Error);
            File.WriteAllText(Path.Combine(target, ".hermes-worktree.json"), System.Text.Json.JsonSerializer.Serialize(new { source, branch, commit }));
            return new(target, branch, commit, contents.Count);
        }
        catch (Exception ex) { throw new IOException("부분 worktree를 삭제하지 않았습니다: " + target + "\n" + ex.Message, ex); }
    }
    private static string BlobHash(byte[] content, bool sha256)
    {
        using var hash = IncrementalHash.CreateHash(sha256 ? HashAlgorithmName.SHA256 : HashAlgorithmName.SHA1);
        hash.AppendData(Encoding.UTF8.GetBytes($"blob {content.Length}\0")); hash.AppendData(content);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
