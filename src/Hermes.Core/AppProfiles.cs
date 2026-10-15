namespace Hermes.Core;

public static class AppProfiles
{
    // Reuse this app's existing profile in place. Do not copy, decrypt, or import credentials.
    public static string Resolve(string hermesRoot)
    {
        var root = Path.GetFullPath(hermesRoot); Safety.RejectReparseAncestors(root);
        foreach (var name in new[] { "data", "0.8.0", "0.7.3", "0.7.2", "0.7.1", "0.7.0", "0.6.1" })
        {
            var path = Path.Combine(root, name); Safety.RejectReparseAncestors(path);
            if (File.Exists(Path.Combine(path, "state.json"))) return path;
        }
        return Path.Combine(root, "data");
    }
}
