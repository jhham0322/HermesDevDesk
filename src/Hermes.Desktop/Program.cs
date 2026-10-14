using Hermes.Core;
namespace Hermes.Desktop;
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--mcp-local-stdio"))
        {
            try { new ProjectMcpServer(args[Array.IndexOf(args, "--root") + 1], args.Contains("--allow-write")).RunAsync().GetAwaiter().GetResult(); return 0; }
            catch { return 1; }
        }
        var app = new App(); app.InitializeComponent(); return app.Run();
    }
}
