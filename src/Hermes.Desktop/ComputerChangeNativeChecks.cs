using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Hermes.Core;

namespace Hermes.Desktop;

internal static class ComputerChangeNativeChecks
{
    internal static async Task<int> Run(string root)
    {
        root = Path.GetFullPath(root); Directory.CreateDirectory(root); Safety.ProjectRoot(root);
        if (Directory.EnumerateFileSystemEntries(root).Any()) throw new IOException("Change-test root must be empty");
        var results = new List<string>(); var reports = new List<ComputerWaitMetrics>(); Process? fixture = null;
        using var client = new ComputerChangeWaitClient();
        void Check(bool ok, string name) { if (!ok) throw new IOException("FAIL " + name); results.Add("PASS " + name); }
        try
        {
            var directory = Path.Combine(root, "read-only-window"); Directory.CreateDirectory(directory);
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "--computer-fixture", directory, Environment.ProcessId.ToString() }) info.ArgumentList.Add(arg);
            fixture = Process.Start(info) ?? throw new IOException("Fixture launch failed");
            var record = await fixture.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var target = JsonSerializer.Deserialize<ComputerWindow>(record ?? "") ?? throw new IOException("Fixture handshake failed");
            var frame = ComputerNative.ObserveForChange(target, false);
            ComputerWaitRequest Request(int timeout) => new(target, frame.ImageUrl, frame.Cursor, null, timeout, new());
            var unchanged = await client.Execute(Request(1500), CancellationToken.None); reports.Add(unchanged.Metrics);
            Check(unchanged.Success && unchanged.Reason == "timeout" && unchanged.Frame == null && unchanged.Metrics.Captures >= 3 && unchanged.Metrics.Notifications == 0, "actual unchanged native captures time out without image or model notification");
            var changedTask = client.Execute(Request(10000), CancellationToken.None);
            await Task.Delay(800); File.WriteAllText(Path.Combine(directory, "visual-change.signal"), "owned visual fixture only");
            var changed = await changedTask; reports.Add(changed.Metrics);
            Check(changed.Success && changed.Frame != null && changed.Metrics.Notifications == 1 && changed.Reason.Contains("pixels"), "actual WPF visual change returns exactly one latest captured frame");
            Check(changed.Metrics.Captures > changed.Metrics.Notifications, "300ms polling stays local; frames are not accumulated or sent to a model per tick");
            frame = changed.Frame!;
            using (var cancel = new CancellationTokenSource())
            {
                var pending = client.Execute(Request(30000), cancel.Token); await Task.Delay(900); var clock = Stopwatch.StartNew(); cancel.Cancel();
                try { await pending; Check(false,"cancelled wait completed"); }
                catch (OperationCanceledException) { reports.Add(client.Latest); Check(clock.ElapsedMilliseconds < 2000,"native pending wait cancellation stops worker within two seconds"); }
            }
            var resumed = await client.Execute(Request(900), CancellationToken.None); reports.Add(resumed.Metrics);
            Check(resumed.Success && resumed.Frame == null && resumed.Metrics.Notifications == 0,"native resumed wait uses last delivered baseline and sends no unchanged image");
            File.WriteAllText(Path.Combine(directory, "blank-capture.signal"), "owned blank fixture only"); await Task.Delay(400);
            var blocked = await client.Execute(Request(900), CancellationToken.None); reports.Add(blocked.Metrics);
            Check(!blocked.Success && blocked.Frame == null && blocked.Error.Contains("CaptureBlockedOrBlank"),"owned black capture fails once with explicit ambiguous blocked/blank result");
            Report(true,"");return 0;
        }
        catch(Exception ex) { Report(false,ex.Message);return 1; }
        finally { client.Stop(); if(fixture!=null){try{if(!fixture.HasExited){fixture.CloseMainWindow();if(!fixture.WaitForExit(2000))fixture.Kill(true);}}finally{fixture.Dispose();}} }
        void Report(bool passed,string error) => File.WriteAllText(Path.Combine(root,"change-native-report.json"),JsonSerializer.Serialize(new{passed,error,results,metrics=reports,realModelCalls=0,mockModelCalls=0,actualNativeInputEvents=0,framesPersisted=0,hostModelLoopVerified=false},new JsonSerializerOptions{WriteIndented=true}));
    }
}
