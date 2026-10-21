using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
namespace Hermes.Core;
public sealed record ProjectSkill(string Name, string Description, string RelativePath, string Sha256, long Bytes)
{ public override string ToString() => Name + " · " + RelativePath; }
public sealed record SkillRecord(ProjectSkill Skill, DateTime At);
public static class ProjectSkills
{
    public static string Resolve(string root, string relative)
    {
        root = Safety.ProjectRoot(root);
        if (Path.IsPathRooted(relative) || relative.Contains(':')) throw new HermesException("프로젝트 상대 스킬 경로 필요");
        var normalized = relative.Replace('\\', '/');
        var segments = normalized.Split('/');
        if (segments.Any(s => s.Length == 0 || s is "." or "..") || segments[^1] != "SKILL.md") throw new HermesException("명시적인 프로젝트 SKILL.md 경로 필요");
        var projectAgents = normalized.StartsWith(".agents/skills/", StringComparison.Ordinal);
        if (segments.Select((s, i) => (s, i)).Any(p => Safety.IsSecret(p.s) && !(projectAgents && p.i == 0 && p.s == ".agents"))) throw new HermesException("비밀/전역 스킬 경로 금지");
        if (!projectAgents && !normalized.StartsWith("skills/", StringComparison.Ordinal)) throw new HermesException("프로젝트 skills 또는 .agents/skills 아래의 스킬만 선택하세요.");
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!Safety.Within(root, path)) throw new HermesException("프로젝트 밖 스킬 금지");
        Safety.RejectReparseAncestors(path); return path;
    }
    public static ProjectSkill Read(string root, string relative)
    {
        var path = Resolve(root, relative); var file = new FileInfo(path);
        if (!file.Exists || file.Length is < 20 or > 65536) throw new HermesException("SKILL.md는 64KB 이하 UTF-8 파일이어야 합니다.");
        var bytes = File.ReadAllBytes(path); var text = new UTF8Encoding(false, true).GetString(bytes);
        if (bytes.Length > 65536) throw new HermesException("전송 전 스킬 크기가 변경되었습니다.");
        if (text.Contains('\0') || Safety.Redact(text) != text) throw new HermesException("바이너리/민감 값이 있는 스킬을 전송하지 않습니다.");
        var front = Regex.Match(text, @"\A---\r?\n([\s\S]*?)\r?\n---(?:\r?\n|$)");
        if (!front.Success) throw new HermesException("스킬 YAML frontmatter 필요");
        string Field(string field) => Regex.Match(front.Groups[1].Value, @"(?m)^" + field + @":\s*(.+?)\s*$").Groups[1].Value.Trim().Trim('"', '\'');
        var name = Field("name"); var description = Field("description");
        if (!Regex.IsMatch(name, "^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$") || description.Length is < 1 or > 1500) throw new HermesException("유효한 name/한 줄 description 필요");
        return new(name, description, relative.Replace('\\', '/'), Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
    }
    public static List<ProjectSkill> List(string root, out List<string> errors)
    {
        root = Safety.ProjectRoot(root); errors = []; var skills = new List<ProjectSkill>();
        foreach (var folder in new[] { ".agents/skills", "skills" })
        {
            var start = Path.Combine(root, folder); if (!Directory.Exists(start)) continue; Safety.RejectReparseAncestors(start);
            var queue = new Queue<(string Path, int Depth)>(); queue.Enqueue((start, 0)); int count = 0;
            while (queue.Count > 0 && count++ < 500)
            {
                var entry = queue.Dequeue();
                foreach (var path in Directory.EnumerateFileSystemEntries(entry.Path).Take(200))
                {
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) { errors.Add("링크 스킬 제외: " + Path.GetRelativePath(root, path)); continue; }
                    if (Directory.Exists(path)) { if (entry.Depth < 3 && !Safety.IsSecret(Path.GetFileName(path))) queue.Enqueue((path, entry.Depth + 1)); }
                    else if (Path.GetFileName(path) == "SKILL.md")
                    { try { skills.Add(Read(root, Path.GetRelativePath(root, path))); } catch (Exception e) when (e is IOException or DecoderFallbackException) { errors.Add(Path.GetRelativePath(root, path) + ": " + e.Message); } }
                }
            }
        }
        return skills.OrderBy(s => s.Name).ThenBy(s => s.RelativePath).ToList();
    }
    public static List<ProjectSkill> Validate(string root, IEnumerable<ProjectSkill> selected)
    {
        var skills = selected.ToList();
        if (skills.Count > 3 || skills.Sum(s => s.Bytes) > 128000 || skills.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != skills.Count) throw new HermesException("스킬은 최대 3개/128KB/서로 다른 이름");
        foreach (var skill in skills) if (Read(root, skill.RelativePath) != skill) throw new HermesException("스킬이 변경되었습니다. 다시 선택하세요.");
        return skills;
    }
    public static List<ProjectSkill> FilterOfficial(string root, JsonElement response)
    {
        var result = new List<ProjectSkill>();
        if (response.Property("data") is { ValueKind: JsonValueKind.Array } data) foreach (var entry in data.EnumerateArray())
            if (entry.Property("skills") is { ValueKind: JsonValueKind.Array } skills) foreach (var item in skills.EnumerateArray())
            {
                var path = item.Text("path");
                if (!Path.IsPathFullyQualified(path) || !Safety.Within(root, path)) continue;
                try { var skill = Read(root, Path.GetRelativePath(root, path)); if (skill.Name == item.Text("name")) result.Add(skill); } catch (Exception e) when (e is IOException or DecoderFallbackException) { }
            }
        return result.DistinctBy(s => s.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
