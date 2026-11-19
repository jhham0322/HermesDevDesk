using System.Text;
using System.Text.RegularExpressions;

namespace Hermes.Core;

internal static partial class DocumentSkill
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docx", ".docm", ".xls", ".xlsx", ".xlsm", ".xlsb", ".ppt", ".pptx",
        ".pptm", ".hwp", ".hwpx", ".pdf", ".rtf", ".odt", ".ods", ".odp"
    };

    [GeneratedRegex(@"(?:\.(?:doc[xm]?|xls[xmb]?|ppt[xm]?|hwpx?|pdf|rtf|od[tsp])(?=$|[\s""'`),;:!?\]>])|(?:사내|사무실|보호된|DRM)\s*문서|\b(?:Word|Excel|PowerPoint)\s+(?:document|file|workbook|presentation)\b)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DocumentMention();

    private static bool IsDrmName(string name) =>
        name.Split('-', '_').Any(part => part.Equals("drm", StringComparison.OrdinalIgnoreCase));

    internal static ProjectSkill? Find(string root, IReadOnlyList<ProjectSkill> selected)
    {
        if (selected.FirstOrDefault(skill => IsDrmName(skill.Name)) is { } explicitSkill)
            return explicitSkill;

        foreach (var folder in new[] { ".agents/skills", "skills" })
        {
            var directory = Path.Combine(root, folder);
            if (!Directory.Exists(directory)) continue;
            try
            {
                Safety.RejectReparseAncestors(directory);
                var candidates = Directory.EnumerateDirectories(directory)
                    .Where(path => IsDrmName(Path.GetFileName(path)))
                    .OrderBy(path => !Path.GetFileName(path).Equals("drm", StringComparison.OrdinalIgnoreCase))
                    .ThenBy(path => path, StringComparer.OrdinalIgnoreCase);
                foreach (var candidate in candidates)
                {
                    var relativePath = Path.GetRelativePath(root, Path.Combine(candidate, "SKILL.md"));
                    try { return ProjectSkills.Read(root, relativePath); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
                    { AppLog.Write(ex, "document-skill-discovery"); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { AppLog.Write(ex, "document-skill-directory"); }
        }
        return null;
    }

    internal static bool IsDocumentRequest(string prompt, IEnumerable<ProjectFileAttachment> files) =>
        files.Any(file => Extensions.Contains(Path.GetExtension(file.Name))) || DocumentMention().IsMatch(prompt);

    internal static string Instructions(ProjectSkill? skill) => skill == null
        ? " If you encounter a rights-protected office document, use the organization's authorized DRM workflow. If the workflow or access is unavailable, report it before retrying. Never try to bypass protection."
        : $" If you encounter an office document (including when discovered during work), read and follow the registered DRM skill at {skill.RelativePath} before trying to open or extract the document. Use the current user's authorized access and honor the document's restrictions on copying or transfer. If access fails, report the DRM error and do not retry through another reader.";
}
