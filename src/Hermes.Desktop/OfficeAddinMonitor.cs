using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Hermes.Core;

namespace Hermes.Desktop;

internal sealed record OfficeAddinState(DateTime Updated, bool Running, string Message, List<OfficeAddinResult> Addins);
internal static class OfficeAddinMonitor
{
    internal static string? FixtureRoot { get; set; }
    internal static string Root => FixtureRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HermesDevDesk", "office-addins");
    internal static string StatePath => Path.Combine(Root, "status.json");
    internal static string DisabledPath => Path.Combine(Root, "disabled");
    private static string MutexName => "Local\\HermesDevDesk.OfficeAddinMonitor." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Root.ToUpperInvariant())))[..16];
    internal static void Start()
    {
        Directory.CreateDirectory(Root); File.Delete(DisabledPath);
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "--office-addin-monitor" } })?.Dispose();
    }
    internal static void Stop() { Directory.CreateDirectory(Root); File.WriteAllText(DisabledPath, "사용자가 자동 로드를 중지했습니다."); }
    private static void Save(OfficeAddinState state)
    {
        var temporary = StatePath + ".new"; File.WriteAllText(temporary, JsonSerializer.Serialize(state)); File.Move(temporary, StatePath, true);
    }
    internal static async Task<int> Run(bool scanApplications = true)
    {
        // A named semaphore is safe across async continuations (unlike thread-owned mutexes).
        using var singleton = new Semaphore(1, 1, MutexName);
        if (!singleton.WaitOne(0)) return 0;
        try
        {
            Directory.CreateDirectory(Root);
            var seen = new Dictionary<(int Pid, long Start), (int Attempts, DateTime Next, bool Done)>();
            var rows = new Dictionary<(int Pid, long Start), List<OfficeAddinResult>>();
            var memory = new ExecutionMemory(Path.Combine(Root, "knowledge"));
            using var own = Process.GetCurrentProcess(); var sessionId = own.SessionId;
            Save(new(DateTime.UtcNow, true, "Office 실행을 기다리는 중 · 5초 간격 감지", []));
            while (!File.Exists(DisabledPath))
            {
                var alive = new HashSet<(int Pid, long Start)>();
                foreach (var name in scanApplications ? OfficeAddinNative.Applications.Keys.AsEnumerable() : [])
                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        if (File.Exists(DisabledPath)) break;
                        try
                        {
                            if (process.SessionId != sessionId) continue;
                            var key = (process.Id, process.StartTime.ToUniversalTime().Ticks); alive.Add(key);
                            if (seen.TryGetValue(key, out var previous) && (previous.Done || previous.Next > DateTime.UtcNow)) continue;
                            var report = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".json");
                            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                            foreach (var arg in new[] { "--office-addin-load", OfficeAddinNative.Applications[name], key.Id.ToString(), key.Ticks.ToString(), report }) start.ArgumentList.Add(arg);
                            using var child = Process.Start(start) ?? throw new IOException("Office 로더를 시작하지 못했습니다.");
                            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                            bool timedOut = false;
                            try { await child.WaitForExitAsync(timeout.Token); }
                            catch (OperationCanceledException) { timedOut = true; if (!child.HasExited) child.Kill(true); await child.WaitForExitAsync(); }
                            var attempts = previous.Attempts + 1;
                            // Only attachment failures are retried. Add-in failures are recorded once per Office process.
                            var attached = !timedOut && child.ExitCode == 0;
                            var done = attached || timedOut;
                            seen[key] = (attempts, DateTime.UtcNow.AddSeconds(attempts >= 24 ? 60 : 5), done);
                            if (File.Exists(report))
                            {
                                try { rows[key] = JsonSerializer.Deserialize<List<OfficeAddinResult>>(File.ReadAllText(report)) ?? []; }
                                finally { File.Delete(report); }
                            }
                            else rows[key] = [new(OfficeAddinNative.Applications[name], key.Id, "Office", "자동 연결", done ? "실패" : "시작 대기", timedOut ? "30초 응답 제한 · 같은 세션에서 재시도하지 않습니다." : "Office가 아직 연결을 제공하지 않습니다.")];
                            if (done)
                                foreach (var row in rows[key].Where(r => r.Status is "실패" or "로드됨"))
                                    memory.Record("office-addins", key.Id + ":" + key.Ticks, row.Kind + ":" + row.Name, row.Application + " 추가 기능 로드", "office_addin", row.Application + " / " + row.Kind + " / " + row.Name, row.Status == "로드됨", row.Status + " " + row.Detail);
                        }
                        catch (Exception ex) when (ex is not OutOfMemoryException) { AppLog.Write(ex, "office-addin-monitor"); }
                    }
                    if (File.Exists(DisabledPath)) break;
                }
                foreach (var key in seen.Keys.Where(k => !alive.Contains(k)).ToList()) { seen.Remove(key); rows.Remove(key); }
                Save(new(DateTime.UtcNow, true, $"감지 중 · 실행 중인 Office {alive.Count}개 · 실패 항목은 아래 상세 확인", rows.Values.SelectMany(v => v).ToList()));
                await Task.Delay(5000);
            }
            Save(new(DateTime.UtcNow, false, "자동 로드 중지됨 · 이미 로드된 Office 추가 기능은 유지", rows.Values.SelectMany(v => v).ToList()));
            return 0;
        }
        catch (Exception ex) { AppLog.Write(ex, "office-addin-monitor-fatal"); return 1; }
        finally { singleton.Release(); }
    }
}
