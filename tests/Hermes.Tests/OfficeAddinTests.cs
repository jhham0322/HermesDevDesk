using Hermes.Core;

internal static class OfficeAddinTests
{
    private sealed class Addin(string name, bool loaded = false, bool fails = false, bool ignored = false) : IOfficeAddin
    {
        public string Kind => "COM / VSTO";
        public string Name => name;
        public bool Loaded { get; private set; } = loaded;
        public int Calls { get; private set; }
        public void Load() { Calls++; if (fails) throw new IOException("Missing add-in file"); if (!ignored) Loaded = true; }
    }
    public static void Run(Action<bool, string> check)
    {
        var active = new Addin("active", true); var broken = new Addin("missing", fails: true); var next = new Addin("following"); var ignored = new Addin("blocked", ignored: true);
        var results = OfficeAddins.Load("Excel", 123, [active, broken, next, ignored]);
        check(active.Calls == 0 && results[0].Status == "이미 로드됨", "Office loader preserves already connected add-ins without reconnecting");
        check(broken.Calls == 1 && results[1].Status == "실패" && results[1].Detail.Contains("Missing"), "Office loader records the failed module and its actual cause once");
        check(next.Loaded && next.Calls == 1 && results[2].Status == "로드됨", "one broken Office module does not prevent later modules loading");
        check(results[3].Status == "실패" && ignored.Calls == 1, "Office refusing a load is reported as failure rather than success");
        check(results.All(r => r.Application == "Excel" && r.ProcessId == 123), "Office module results retain the owning application and process");
        check(OfficeAddins.Load("Word", 124, []).Count == 0, "Office without registered add-ins is a valid empty result");
    }
}
