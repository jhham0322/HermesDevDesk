namespace Hermes.Core;
public sealed class BrowserPreferences
{
    public string LastUrl { get; set; } = "";
    public bool ControlsCollapsed { get; set; }
    public bool SavePasswords { get; set; } = true;
    public bool LocalDevelopmentMode { get; set; } = true;
    public List<BrowserPageEntry> Bookmarks { get; set; } = [];
    public List<BrowserPageEntry> History { get; set; } = [];
}
public sealed record BrowserPageEntry(string Url, string Title, DateTimeOffset At)
{
    public override string ToString() => Title + "\n" + Url;
}
public static class BrowserLibrary
{
    public static string PageUrl(string url)
    {
        var uri = BrowserTools.ValidateUrl(url);
        // OAuth callback secrets and passwords should not become saved navigation entries.
        var query = uri.Query.ToLowerInvariant();
        if (new[] { "access_token=", "id_token=", "password=", "token=", "code=" }.Any(query.Contains))
            return new UriBuilder(uri) { Query = "", Fragment = "" }.Uri.AbsoluteUri;
        return Safety.Redact(uri.AbsoluteUri) == uri.AbsoluteUri ? uri.AbsoluteUri : new UriBuilder(uri) { Query = "", Fragment = "" }.Uri.AbsoluteUri;
    }
    public static void Visit(BrowserPreferences state, string url, string title)
    {
        url = PageUrl(url); state.LastUrl = url;
        state.History.RemoveAll(p => p.Url == url);
        state.History.Insert(0, new(url, Safety.Redact(title[..Math.Min(title.Length, 300)]), DateTimeOffset.UtcNow));
        if (state.History.Count > 500) state.History.RemoveRange(500, state.History.Count - 500);
    }
    public static bool ToggleBookmark(BrowserPreferences state, string url, string title)
    {
        url = PageUrl(url);
        if (state.Bookmarks.RemoveAll(p => p.Url == url) > 0) return false;
        state.Bookmarks.Insert(0, new(url, Safety.Redact(title[..Math.Min(title.Length, 300)]), DateTimeOffset.UtcNow)); return true;
    }
}
