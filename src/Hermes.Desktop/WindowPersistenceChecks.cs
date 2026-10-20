using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;

namespace Hermes.Desktop;
public partial class MainWindow
{
    internal void WriteWindowPlacementReport(string directory)
    {
        GetWindowRect(new WindowInteropHelper(this).Handle, out var rect);
        File.WriteAllText(Path.Combine(directory, "placement-child.json"), JsonSerializer.Serialize(new { state = WindowState.ToString(), left = rect.Left, top = rect.Top, width = rect.Right - rect.Left, height = rect.Bottom - rect.Top, inspectorWidth = InspectorColumn.Width.Value, inspectorActualWidth = InspectorColumn.ActualWidth, inspectorVisible = InspectorPane.Visibility == Visibility.Visible, inspectorTab = InspectorTabs.SelectedIndex }));
    }
    internal async Task RunWindowPersistenceChecks(string directory)
    {
        var results = new List<string>();
        void Check(bool ok, string label) { if (!ok) throw new IOException(label); results.Add(label); }
        async Task<JsonElement> Restart()
        {
            windowSaveTimer.Stop();
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (var argument in new[] { "--window-state-report", "--data-dir", directory }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("Window restart fixture failed to start");
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)); }
            catch { if (!process.HasExited) process.Kill(true); throw; }
            Check(process.ExitCode == 0, "restarted app exits normally");
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "placement-child.json"))); return report.RootElement.Clone();
        }
        WindowState = WindowState.Normal;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!GetWorkPlacement(hwnd, out var monitor, out _)) throw new IOException("Monitor work area unavailable");
        SetWindowPos(hwnd, IntPtr.Zero, monitor.Work.Left + 70, monitor.Work.Top + 60, Math.Min(1100, monitor.Work.Pixels.Width - 140), Math.Min(760, monitor.Work.Pixels.Height - 120), 0x14);
        OpenPanel(8); InspectorColumn.Width = new GridLength(820); UpdateLayout(); SaveInspectorWidth();
        await Task.Delay(400); SaveWindowGeometry(); windowSaveTimer.Stop();
        var saved = desk.State.Desktop.WindowBounds ?? throw new IOException("Window bounds not saved");
        var normal = await Restart();
        Check(normal.GetProperty("inspectorVisible").GetBoolean() && Math.Abs(normal.GetProperty("inspectorActualWidth").GetDouble() - 820) < 2, "user-sized right panel restored after actual process restart");
        Check(normal.GetProperty("inspectorTab").GetInt32() == 8, "right panel selected tab restored after restart");
        Check(normal.GetProperty("state").GetString() == "Normal", "normal state restored after process restart");
        Check(Math.Abs(normal.GetProperty("left").GetInt32() - saved.Left) <= 2 && Math.Abs(normal.GetProperty("top").GetInt32() - saved.Top) <= 2, "window location restored after process restart");
        Check(Math.Abs(normal.GetProperty("width").GetInt32() - saved.Width) <= 2 && Math.Abs(normal.GetProperty("height").GetInt32() - saved.Height) <= 2, "window size restored after process restart");
        WindowState = WindowState.Maximized; await Task.Delay(350); SaveWindowGeometry();
        var maximized = await Restart();
        Check(maximized.GetProperty("state").GetString() == "Maximized", "maximized state restored after process restart");
        Check(desk.State.Desktop.WindowBounds == saved, "maximizing preserves previous normal bounds");
        WindowState = WindowState.Minimized; SaveWindowGeometry();
        Check(desk.State.Desktop.WindowMaximized, "minimizing does not replace the last visible window state");
        WindowState = WindowState.Normal;
        File.WriteAllText(Path.Combine(directory, "window-state-tests.json"), JsonSerializer.Serialize(new { passed = true, checks = results.Count, results }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
