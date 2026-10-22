using System.IO;
using System.Text.Json;

namespace Hermes.Desktop;

internal static class OfficeAddinChecks
{
    internal static int Live(string application, string report)
    {
        object? app = null; object? documents = null; object? document = null; System.Diagnostics.Process? owned = null;
        void Release(object? value) { if (value != null && System.Runtime.InteropServices.Marshal.IsComObject(value)) System.Runtime.InteropServices.Marshal.ReleaseComObject(value); }
        object? Call(object target, string method, params object[] args) => target.GetType().InvokeMember(method, System.Reflection.BindingFlags.InvokeMethod | System.Reflection.BindingFlags.OptionalParamBinding, null, target, args);
        try
        {
            var name = OfficeAddinNative.Applications.Single(p => p.Value == application).Key;
            var existing = System.Diagnostics.Process.GetProcessesByName(name); foreach (var process in existing) process.Dispose();
            if (existing.Length != 0) throw new IOException("Live check requires no existing " + application + " process; no user documents were accessed.");
            var type = Type.GetTypeFromProgID(application + ".Application") ?? throw new IOException("Office is not registered");
            app = Activator.CreateInstance(type) ?? throw new IOException("Office did not start");
            var created = System.Diagnostics.Process.GetProcessesByName(name);
            if (created.Length != 1) { foreach (var process in created) process.Dispose(); throw new IOException("Could not establish a single owned Office process"); }
            owned = created[0];
            File.WriteAllText(report, JsonSerializer.Serialize(new { phase = "owned-office-started", processId = owned.Id, startTicks = owned.StartTime.ToUniversalTime().Ticks }));
            documents = OfficeAddinNative.Get(app, application == "Excel" ? "Workbooks" : application == "Word" ? "Documents" : "Presentations") ?? throw new IOException("No document collection");
            document = application == "PowerPoint" ? Call(documents, "Add", 0) : Call(documents, "Add");
            var nativeReport = report + ".modules.json";
            int status = 2;
            for (int attempt = 0; attempt < 10 && status == 2; attempt++) { status = OfficeAddinNative.Run(application, owned.Id, owned.StartTime.ToUniversalTime().Ticks, nativeReport); if (status == 2) Thread.Sleep(300); }
            if (status != 0) throw new IOException("Native attachment status=" + status);
            var modules = JsonSerializer.Deserialize<List<Hermes.Core.OfficeAddinResult>>(File.ReadAllText(nativeReport)) ?? [];
            File.WriteAllText(report, JsonSerializer.Serialize(new { application, processId = owned.Id, nativeAttachment = true, moduleCount = modules.Count, failures = modules.Count(r => r.Status == "실패"), modules, realOfficeInvoked = true }, new JsonSerializerOptions { WriteIndented = true }));
            return modules.Any(r => r.Status == "실패") ? 2 : 0;
        }
        catch (Exception ex) { File.WriteAllText(report + ".error.txt", ex.ToString()); return 1; }
        finally
        {
            if (document != null) { try { if (application == "Excel") Call(document, "Close", false); else if (application == "Word") Call(document, "Close", 0); else Call(document, "Close"); } catch { } }
            Release(document); Release(documents);
            if (app != null && owned != null) { try { Call(app, "Quit"); } catch { } }
            Release(app); owned?.Dispose();
        }
    }
    public sealed class Module(string name, bool connected = false, bool fail = false, string? deferredPath = null, bool rejectInstalled = false)
    {
        private bool connected = connected;
        public string ProgId => name;
        public string Name => name;
        public int Writes { get; private set; }
        public bool Connect { get => connected; set { Writes++; if (fail) throw new IOException("Fixture missing library"); connected = value; } }
        public bool Installed { get => deferredPath != null || Connect; set { if (rejectInstalled) throw new System.Runtime.InteropServices.COMException("Fixture Installed rejected", unchecked((int)0x800A03EC)); if (deferredPath == null) Connect = value; } }
        public bool IsOpen => Connect;
        public string FullName => deferredPath ?? name;
        public void Activate() => connected = true;
        public bool Loaded { get => Connect; set => Connect = value; }
    }
    public sealed class Collection(params Module[] modules)
    {
        public int Count => modules.Length;
        public Module this[int index] => modules[index - 1];
    }
    public sealed class Application(Collection com, Collection native)
    {
        public MethodCollection COMAddIns => new(com);
        public Collection AddIns => native;
        public bool RegisterXLL(string path) { for (int i = 1; i <= native.Count; i++) if (native[i].FullName == path) native[i].Activate(); return true; }
        public WorkbookCollection Workbooks => new(native);
    }
    public sealed class MethodCollection(Collection source)
    {
        public int Count => source.Count;
        public Module Item(int index) => source[index];
    }
    public sealed class WorkbookCollection(Collection native)
    {
        public object Open(string path) { for (int i = 1; i <= native.Count; i++) if (native[i].FullName == path) native[i].Activate(); return new object(); }
    }
    internal static int Run(string report)
    {
        try
        {
            var checks = new List<string>();
            void Check(bool value, string label) { if (!value) throw new IOException(label); checks.Add(label); }
            Check(OfficeAddinNative.CheckWindowEnumeration(), "native window enumeration callbacks marshal successfully without accessing Office");
            foreach (var application in new[] { "Excel", "Word", "PowerPoint", "Outlook" })
            {
                var active = new Module("already", true); var broken = new Module("missing", fail: true); var working = new Module("available"); var native = new Module("native");
                var results = OfficeAddinNative.LoadApplication(new Application(new(active, broken, working), new(native)), application, 42);
                Check(active.Writes == 0, application + " avoids reconnecting already loaded COM extension");
                Check(broken.Writes == 1 && results.Single(r => r.Name == "missing").Detail.Contains("Fixture missing"), application + " records a failing module and stops retrying it");
                Check(working.Connect && results.Single(r => r.Name == "available").Status == "로드됨", application + " still loads the following COM/VSTO module");
                Check(native.Connect == (application != "Outlook"), application + " uses the correct native add-in API");
            }
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            Check(OfficeAddinNative.Run("Excel", process.Id, process.StartTime.ToUniversalTime().Ticks, report + ".unexpected") == 3 && !File.Exists(report + ".unexpected"), "rejects mismatched process before accessing COM");
            var empty = OfficeAddinNative.LoadApplication(new Application(new(), new()), "Excel", 42);
            Check(empty.Count == 0, "empty installed collections do not create failures");
            var xll = new Module("registered-xll", deferredPath: "fixture.xll"); var xlam = new Module("registered-xlam", deferredPath: "fixture.xlam");
            var deferred = OfficeAddinNative.LoadApplication(new Application(new(), new(xll, xlam)), "Excel", 42);
            Check(xll.IsOpen && deferred[0].Status == "로드됨", "registered but unopened XLL is actually registered with Excel");
            Check(xlam.IsOpen && deferred[1].Status == "로드됨", "registered but unopened XLAM is opened rather than trusting Installed alone");
            var alternate = new Module("alternate", deferredPath: "alternate.xlam", rejectInstalled: true);
            var fallback = OfficeAddinNative.LoadApplication(new Application(new(), new(alternate)), "Excel", 42);
            Check(alternate.IsOpen && fallback.Single().Status == "로드됨", "Excel Installed rejection falls back to the direct load API");
            var fixture = report + ".monitor-" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(fixture);
            System.Diagnostics.Process StartFixture()
            {
                var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("--office-addin-monitor-fixture"); start.ArgumentList.Add(fixture);
                return System.Diagnostics.Process.Start(start) ?? throw new IOException("Fixture did not start");
            }
            using (var first = StartFixture())
            {
                try
                {
                    var status = Path.Combine(fixture, "status.json");
                    var until = DateTime.UtcNow.AddSeconds(15); while (!File.Exists(status) && DateTime.UtcNow < until && !first.HasExited) Thread.Sleep(50);
                    Check(File.Exists(status) && !first.HasExited, "background monitor starts independently of the WPF window");
                    using (var second = StartFixture()) Check(second.WaitForExit(5000) && second.ExitCode == 0, "duplicate monitor exits without creating a second loader");
                    Check(JsonSerializer.Deserialize<OfficeAddinState>(File.ReadAllText(status))?.Running == true, "monitor publishes its running status");
                    File.WriteAllText(Path.Combine(fixture, "disabled"), "Owned fixture stop");
                    Check(first.WaitForExit(10000) && first.ExitCode == 0, "stop request terminates the background monitor");
                    Check(JsonSerializer.Deserialize<OfficeAddinState>(File.ReadAllText(status))?.Running == false, "monitor records its stopped state");
                    using (var paused = StartFixture()) Check(paused.WaitForExit(5000) && paused.ExitCode == 0, "persisted stop survives the next automatic launch");
                }
                finally { if (!first.HasExited) { first.Kill(true); first.WaitForExit(); } }
            }
            File.WriteAllText(report, JsonSerializer.Serialize(new { passed = checks.Count, checks, realOfficeInvoked = false }, new JsonSerializerOptions { WriteIndented = true })); return 0;
        }
        catch (Exception ex) { File.WriteAllText(report, ex.ToString()); return 1; }
    }
}
