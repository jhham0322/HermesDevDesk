using Hermes.Core;
namespace Hermes.Desktop;
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--computer-change-self-test") return ComputerChangeNativeChecks.Run(args[1]).GetAwaiter().GetResult();
        if (args.Contains("--computer-change-wait")) return ComputerChangeWait.Main();
        if (args.Contains("--computer-worker")) return ComputerNative.WorkerMain();
        if (args.Length == 3 && args[0] == "--computer-fixture") return ComputerNativeChecks.Fixture(args[1], int.Parse(args[2]));
        if (args.Length is 2 or 3 && args[0] == "--computer-self-test")
        {
            if (args.Length == 3 && args[2] != "--approve-owned-test-start") throw new ArgumentException("Unknown owned-test option");
            return ComputerNativeChecks.Run(args[1], args.Length == 3).GetAwaiter().GetResult();
        }
        if (args.Contains("--mcp-local-stdio"))
        {
            try { new ProjectMcpServer(args[Array.IndexOf(args, "--root") + 1], args.Contains("--allow-write")).RunAsync().GetAwaiter().GetResult(); return 0; }
            catch { return 1; }
        }
        var app = new App(); app.InitializeComponent(); return app.Run();
    }
}
