namespace Hermes.Desktop;
internal static class AppVersion
{
    internal static string Current => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "unknown";
}
