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
        check(BrowserTools.ValidateUrl("localhost:3020/test?q=1").AbsoluteUri == "http://localhost:3020/test?q=1", "bare localhost address uses HTTP with exact port/path/query");
        check(BrowserTools.ValidateUrl("127.0.0.1:3020").Scheme == "http" && BrowserTools.ValidateUrl("[::1]:3020").Scheme == "http", "IPv4/IPv6 loopback addresses use HTTP without a scheme");
        check(BrowserTools.ValidateUrl("https://localhost:3020").Scheme == "https", "explicit local HTTPS is preserved for development TLS");
        check(BrowserTools.LocalHttpAlternative(new Uri("https://localhost:3020/a?q=2"))?.AbsoluteUri == "http://localhost:3020/a?q=2", "local HTTP retry preserves developer server port and route");
        check(BrowserTools.LocalHttpAlternative(new Uri("https://example.com:3020")) == null && !BrowserTools.IsLocalDevelopmentUrl(new Uri("https://localhost.example.com")) && !BrowserTools.IsLocalDevelopmentUrl(new Uri("https://192.168.1.2")), "development certificate exceptions never include public domains or LAN hosts");
        check(!BrowserTools.IsLocalDevelopmentUrl(new Uri("https://user:password@localhost:3020")), "credentials cannot bypass local development URL policy");
    }
}
