using System.Diagnostics;
using System.Runtime.InteropServices;
using Hermes.Core;

// Diagnostic fixture only: observe this engine's descendants; never read command lines,
// environment, unrelated process details, credentials, engine homes or session files.
internal sealed class CommandStartupProbe : IAsyncDisposable
{
    private readonly AppServerClient engine;
    private readonly Action<string, object> trace;
    private readonly string root;
    private readonly int engineId;
    private readonly Task monitor;
    private readonly HashSet<int> seen = [];
    private readonly HashSet<string> markers = [];
    private int responding, disposed;
    private long lastSample;
    internal CommandStartupProbe(AppServerClient engine, string root, Action<string, object> trace)
    {
        this.engine = engine; this.root = root; this.trace = trace;
        engineId = engine.DiagnosticProcessId ?? throw new IOException("Diagnostic engine process is absent");
        engine.CommandTiming += Stamp;
        monitor = Monitor();
    }
    private void Stamp(string stage)
    {
        trace("command-timing", new { stage });
        if (stage == "rpc-response") Interlocked.Exchange(ref responding, 1);
    }
    private async Task Monitor()
    {
        try
        {
            while (Volatile.Read(ref disposed) == 0)
            {
                Sample();
                if (Volatile.Read(ref responding) == 1) return;
                await Task.Delay(100);
            }
        }
        catch (Exception ex) { trace("process-probe-error", new { exceptionType = ex.GetType().Name }); }
    }
    private void Sample()
    {
        var parents = new Dictionary<int, (int Parent, string Name)>();
        var handle = CreateToolhelp32Snapshot(2, 0);
        if (handle == new IntPtr(-1)) throw new IOException("Process snapshot unavailable");
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (Process32First(handle, ref entry))
                do { parents[(int)entry.Id] = ((int)entry.ParentId, entry.Name); } while (Process32Next(handle, ref entry));
        }
        finally { CloseHandle(handle); }
        var descendants = new HashSet<int> { engineId };
        bool changed;
        do { changed = false; foreach (var pair in parents) if (descendants.Contains(pair.Value.Parent)) changed |= descendants.Add(pair.Key); } while (changed);
        var shells = parents.Where(p => descendants.Contains(p.Key) && p.Value.Name.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase)).Select(p => p.Key).ToArray();
        foreach (var id in shells)
            if (seen.Add(id))
            {
                try { using var shell = Process.GetProcessById(id); trace("powershell-observed", new { createdAt = shell.StartTime.ToUniversalTime(), shellOrdinal = seen.Count }); }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { trace("powershell-probe-race", new { exceptionType = ex.GetType().Name }); }
            }
        if (Environment.TickCount64 - lastSample >= 1000)
        {
            lastSample = Environment.TickCount64;
            double? cpu = null;
            try { using var process = Process.GetProcessById(engineId); cpu = process.TotalProcessorTime.TotalMilliseconds; }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            trace("owned-process-sample", new { engineCpuMs = cpu, livePowerShellCount = shells.Length, observedPowerShellCount = seen.Count });
        }
        foreach (var name in new[] { "first-body-start.ticks", "first-after-file.ticks", "first-before-output.ticks", "first-body-end.ticks" })
        {
            if (markers.Contains(name)) continue;
            var file = Path.Combine(root, name);
            try
            {
                if (File.Exists(file) && long.TryParse(File.ReadAllText(file), out var ticks) && ticks >= DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks)
                { markers.Add(name); trace("command-body-marker", new { stage = name[6..^6], at = new DateTime(ticks, DateTimeKind.Utc) }); }
            }
            catch (IOException) { /* A partially written fixture marker is sampled again. */ }
        }
    }
    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref disposed, 1);
        engine.CommandTiming -= Stamp;
        await monitor;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size, Usage, Id;
        public UIntPtr Heap;
        public uint Module, Threads, ParentId;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint id);
    [DllImport("kernel32.dll", EntryPoint = "Process32FirstW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", EntryPoint = "Process32NextW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}
