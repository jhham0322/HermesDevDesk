using Hermes.Core;
internal static class BrowserLibraryTests
{
    internal static void Run(Action<bool, string> check)
    {
        var state = new BrowserPreferences();
        BrowserLibrary.Visit(state, "https://example.com/first", "First");
        BrowserLibrary.Visit(state, "https://example.com/second", "Second");
        BrowserLibrary.Visit(state, "https://example.com/first", "Updated");
        check(state.History.Count == 2 && state.History[0].Title == "Updated", "browser visits deduplicate and refresh recency/title");
        check(BrowserLibrary.ToggleBookmark(state, state.LastUrl, "Saved") && !BrowserLibrary.ToggleBookmark(state, state.LastUrl, "Saved") && state.Bookmarks.Count == 0, "browser bookmark toggle removes saved item");
        check(BrowserLibrary.PageUrl("https://example.com/callback?code=private#secret") == "https://example.com/callback", "browser history strips callback credentials");
        for (var i = 0; i < 510; i++) BrowserLibrary.Visit(state, "https://example.com/page/" + i, "Page");
        check(state.History.Count == 500 && state.History[0].Url.EndsWith("/509"), "browser history remains bounded to latest 500 pages");
    }
}
