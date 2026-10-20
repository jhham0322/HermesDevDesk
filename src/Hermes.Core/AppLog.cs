using System.Text;

namespace Hermes.Core;
public static class AppLog
{
    private static readonly object Gate = new();
    public static string DirectoryPath { get; private set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HermesDevDesk", "logs");
    public static void Configure(string dataDirectory)
    {
        lock (Gate) { DirectoryPath = Path.Combine(Path.GetFullPath(dataDirectory), "logs"); }
    }
    public static string Write(Exception error, string operation)
    {
        lock (Gate)
        {
            var path = Path.Combine(DirectoryPath, "errors-" + DateTime.UtcNow.ToString("yyyy-MM-dd") + ".log");
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                if (File.Exists(path) && new FileInfo(path).Length > 2_000_000) File.Move(path, path + ".previous", true);
                var details = Safety.Redact(error.ToString()); if (details.Length > 40_000) details = details[..40_000];
                var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
                File.AppendAllText(path, $"[{DateTimeOffset.Now:O}] version={version} operation={operation}\n{details}\n\n", new UTF8Encoding(false));
            }
            catch (Exception) { /* Error reporting must never replace the original failure. */ }
            return path;
        }
    }
}
