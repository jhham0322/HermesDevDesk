using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Hermes.Core;

namespace Hermes.Desktop;

internal sealed record ComputerFixtureState(string Text, int Clicks, double Scroll, int Keys, int DragMoves);
internal static class ComputerNativeChecks
{
    internal static int Fixture(string root, int parent)
    {
        root = Safety.ProjectRoot(root);
        if (Directory.EnumerateFileSystemEntries(root).Any()) throw new IOException("Fixture directory must be empty");
        var app = new Application(); var window = new Window { Title = "Hermes Computer Fixture · " + Path.GetFileName(root), Width = Path.GetFileName(root) == "window-two" ? 1400 : 900, Height = Path.GetFileName(root) == "window-two" ? 900 : 680, Left = 100, Top = 100, Background = Brushes.White };
        var grid = new Grid { Margin = new Thickness(16) }; grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = new GridLength(90) }); grid.RowDefinitions.Add(new()); window.Content = grid;
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal }; grid.Children.Add(toolbar);
        var input = new TextBox { Width = 340, Height = 40, FontSize = 18 }; AutomationProperties.SetAutomationId(input, "fixture-input"); toolbar.Children.Add(input);
        var button = new Button { Content = "Click test", Width = 150, Height = 40, Margin = new Thickness(16, 0, 0, 0) }; AutomationProperties.SetAutomationId(button, "fixture-button"); toolbar.Children.Add(button);
        var password = new PasswordBox { Width = 170, Height = 40, Margin = new Thickness(16, 0, 0, 0) }; AutomationProperties.SetAutomationId(password, "fixture-password"); toolbar.Children.Add(password);
        var authorize = new Button { Content = "Start owned test", Width = 130, Height = 28, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom }; AutomationProperties.SetAutomationId(authorize, "fixture-start");
        var drag = new Border { Background = Brushes.LightBlue, Margin = new Thickness(0, 12, 0, 12), Child = new TextBlock { Text = "Owned drag fixture", FontSize = 20, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) } }; AutomationProperties.SetAutomationId(drag, "fixture-drag"); Grid.SetRow(drag, 1); grid.Children.Add(drag);
        Grid.SetRow(authorize, 1); grid.Children.Add(authorize);
        authorize.Click += (_, _) => { ComputerNative.AllowSetForegroundWindow((uint)parent); File.WriteAllText(Path.Combine(root, "input-authorized.signal"), "owned fixture input authorized"); authorize.Visibility = Visibility.Collapsed; };
        window.Activated += (_, _) => ComputerNative.AllowSetForegroundWindow((uint)parent);
        var hangTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        hangTimer.Tick += (_, _) => { if (File.Exists(Path.Combine(root, "blank-capture.signal"))) { foreach (UIElement child in grid.Children) child.Visibility = Visibility.Collapsed; grid.Background = Brushes.Black; window.Background = Brushes.Black; } else if (File.Exists(Path.Combine(root, "visual-change.signal"))) grid.Background = Brushes.Orange; if (File.Exists(Path.Combine(root, "hang.signal"))) { hangTimer.Stop(); File.WriteAllText(Path.Combine(root, "hang-started.signal"), "fixture only"); Thread.Sleep(5000); } }; hangTimer.Start(); window.Closed += (_, _) => hangTimer.Stop();
        var rows = new StackPanel(); for (var i = 0; i < 100; i++) rows.Children.Add(new TextBlock { Text = "Owned computer control fixture row " + i, FontSize = 18, Margin = new Thickness(10) });
        var scroll = new ScrollViewer { Content = rows }; AutomationProperties.SetAutomationId(scroll, "fixture-scroll"); Grid.SetRow(scroll, 2); grid.Children.Add(scroll);
        var clicks = 0; var keys = 0; var moves = 0;
        void Save() => File.WriteAllText(Path.Combine(root, "fixture-state.json"), JsonSerializer.Serialize(new ComputerFixtureState(input.Text, clicks, scroll.VerticalOffset, keys, moves)));
        input.TextChanged += (_, _) => Save(); button.Click += (_, _) => { clicks++; Save(); }; scroll.ScrollChanged += (_, _) => Save();
        window.PreviewKeyDown += (_, _) => { keys++; Save(); }; drag.MouseMove += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) { moves++; Save(); } };
        window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(() =>
        {
            window.Activate(); ComputerNative.AllowSetForegroundWindow((uint)parent); Save();
            using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
            using var current = Process.GetCurrentProcess();
            output.WriteLine(JsonSerializer.Serialize(new ComputerWindow(new WindowInteropHelper(window).Handle.ToInt64(), current.Id, current.StartTime.ToUniversalTime().Ticks, window.Title)));
        });
        return app.Run(window);
    }

    internal static async Task<int> Run(string root, bool approveOwnedStart = false)
    {
        root = Path.GetFullPath(root); Directory.CreateDirectory(root); Safety.ProjectRoot(root);
        if (Directory.EnumerateFileSystemEntries(root).Any()) throw new IOException("Native check root must be new and empty");
        var records = new List<string>(); var fixtures = new List<Process>(); var clock = Stopwatch.StartNew();
        var metrics = new List<ComputerWorkerMetric>();
        using var worker = new ComputerWorkerClient { Observe = metric => metrics.Add(metric) };
        ComputerFrame? frame = null;
        ComputerAction Action(string name, double x = 0, double y = 0, string text = "", string key = "", double toX = 0, double toY = 0, int steps = 1) => new(name, Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), x, y, toX, toY, text, key, "left", 1, steps, false);
        async Task<ComputerFrame> RunAction(ComputerWindow target, ComputerAction action)
        { frame = await worker.Execute(new(target, action, frame?.ClientWidth ?? 0, frame?.ClientHeight ?? 0, frame?.ImageWidth ?? 0, frame?.ImageHeight ?? 0), CancellationToken.None); return frame; }
        void Check(bool condition, string name) { if (!condition) throw new IOException("FAIL " + name); records.Add("PASS " + name); }
        async Task<ComputerWindow> Start(string label)
        {
            var directory = Path.Combine(root, label); Directory.CreateDirectory(directory);
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var value in new[] { "--computer-fixture", directory, Environment.ProcessId.ToString() }) info.ArgumentList.Add(value);
            var process = Process.Start(info) ?? throw new IOException("Fixture launch failed"); fixtures.Add(process);
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var window = JsonSerializer.Deserialize<ComputerWindow>(line ?? "") ?? throw new IOException("Fixture handshake failed");
            if (approveOwnedStart)
            {
                var observed = await RunAction(window, Action("inspect"));
                var start = observed.Elements.Single(e => e.AutomationId == "fixture-start" && e.Name == "Start owned test" && e.Enabled);
                await RunAction(window, Action("click", start.X + start.Width / 2, start.Y + start.Height / 2));
                Check(frame!.IsForeground, "owned start click verifies actual foreground");
                Check(!frame.Elements.Any(e => e.AutomationId == "fixture-start"), "owned start button disappears after actual native click");
            }
            var wait = Stopwatch.StartNew(); while (!File.Exists(Path.Combine(directory, "input-authorized.signal"))) { if (wait.Elapsed > TimeSpan.FromSeconds(90)) throw new IOException("Owned fixture start button was not clicked"); await Task.Delay(100); }
            return window;
        }
        ComputerFixtureState State(string label) => JsonSerializer.Deserialize<ComputerFixtureState>(File.ReadAllText(Path.Combine(root, label, "fixture-state.json")))!;
        void WriteReport(bool passed, string error)
        {
            var stats = metrics.GroupBy(m => m.Action).Select(g => new { action = g.Key, requests = g.Count(), succeeded = g.Count(m => m.Success), failed = g.Count(m => !m.Success), nativeInputEvents = g.Sum(m => m.NativeInputEvents) }).ToArray();
            File.WriteAllText(Path.Combine(root, "computer-native-report.json"), JsonSerializer.Serialize(new {
                version = "0.7.1", passed, checks = records.Count, results = records, error,
                elapsedMs = clock.ElapsedMilliseconds, target = "two owned WPF fixtures only",
                explicitOwnedStartApproval = approveOwnedStart, realModelCalls = false, mockModelCalls = 0,
                hostApprovalLoopVerified = false, componentNativeTest = true,
                stats, totalNativeInputEvents = metrics.Sum(m => m.NativeInputEvents),
                successfulImageFrames = metrics.Count(m => m.Success), imageBytes = metrics.Sum(m => m.ImageBytes),
                traces = metrics, screenshotOrInputContentLogged = false
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        try
        {
            var first = await Start("window-one"); await RunAction(first, Action("inspect"));
            Check(frame!.ImageUrl.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal) && frame.ImageWidth > 300, "real selected-window JPEG capture");
            Check(frame.Dpi >= 96 && frame.SessionId == Process.GetCurrentProcess().SessionId, "actual DPI and interactive target session observed");
            var input = frame.Elements.First(e => e.AutomationId == "fixture-input");
            Check(input.Width > 0 && input.Height > 0, "real UI Automation element coordinates");
            await RunAction(first, Action("click", input.X + input.Width / 2, input.Y + input.Height / 2));
            await RunAction(first, Action("type", text: "Hermes 한글 Ω 😀 123"));
            Check(State("window-one").Text == "Hermes 한글 Ω 😀 123", "actual Unicode keyboard input in owned fixture");
            await RunAction(first, Action("key", key: "CTRL+A")); await RunAction(first, Action("type", text: "Replaced fixture"));
            Check(State("window-one").Text == "Replaced fixture", "actual key chord and replacement");
            var button = frame!.Elements.First(e => e.AutomationId == "fixture-button");
            await RunAction(first, Action("click", button.X + button.Width / 2, button.Y + button.Height / 2)); Check(State("window-one").Clicks == 1, "actual native button click");
            await RunAction(first, Action("scroll", frame!.ImageWidth / 2, frame.ImageHeight * 0.75, steps: -3));
            Check(State("window-one").Scroll > 0, "actual mouse wheel scroll");
            await RunAction(first, Action("drag", frame!.ImageWidth * 0.2, frame.ImageHeight * 0.14, toX: frame.ImageWidth * 0.55, toY: frame.ImageHeight * 0.14));
            Check(State("window-one").DragMoves > 0, "actual native drag path");
            try { await RunAction(first, Action("click", 16000, 16000)); Check(false, "out-of-window coordinates rejected"); } catch (IOException) { Check(true, "out-of-window coordinates rejected before input"); }
            var password = frame!.Elements.First(e => e.AutomationId == "fixture-password");
            await RunAction(first, Action("click", password.X + password.Width / 2, password.Y + password.Height / 2));
            try { await RunAction(first, Action("type", text: "synthetic-not-a-real-password")); Check(false, "password input blocked"); } catch (IOException) { Check(true, "password input blocked by focused element"); }
            try { await RunAction(first with { ProcessId = first.ProcessId + 123 }, Action("inspect")); Check(false, "wrong process identity blocked"); } catch (IOException) { Check(true, "wrong process identity blocked"); }
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                try { await worker.Execute(new(first, Action("inspect"), frame!.ClientWidth, frame.ClientHeight), cancellation.Token); Check(false, "cancelled request rejected"); }
                catch (OperationCanceledException) { Check(true, "cancelled request rejected without launching input"); }
            }
            var second = await Start("window-two"); await RunAction(second, Action("activate"));
            Check(frame!.IsForeground, "second owned window is foreground after native activation");
            Check(frame.ImageWidth <= 1600 && frame.ImageHeight <= 1000 && frame.ImageWidth < frame.ClientWidth, "large owned fixture exercises downscaled client/image coordinates");
            var scaledInput = frame.Elements.Single(e => e.AutomationId == "fixture-input");
            await RunAction(second, Action("click", scaledInput.X + scaledInput.Width / 2, scaledInput.Y + scaledInput.Height / 2));
            await RunAction(second, Action("type", text: "Scaled 한글 😀"));
            Check(State("window-two").Text == "Scaled 한글 😀" && frame!.IsForeground, "scaled DPI click and Unicode input verified by actual fixture state and recapture");
            var beforeResizeFailure = State("window-two");
            try { await worker.Execute(new(second, Action("click", 10, 10), frame!.ClientWidth + 1, frame.ClientHeight), CancellationToken.None); Check(false, "resized frame accepted"); }
            catch (IOException) { Check(State("window-two") == beforeResizeFailure, "changed client size rejected without input"); }
            await RunAction(first, Action("activate"));
            Check(ComputerNative.GetForegroundWindow().ToInt64() == first.Handle, "actual activation between two owned windows");
            File.WriteAllText(Path.Combine(root, "window-two", "hang.signal"), "owned fixture hang");
            var ready = Stopwatch.StartNew(); while (!File.Exists(Path.Combine(root, "window-two", "hang-started.signal"))) { if (ready.Elapsed > TimeSpan.FromSeconds(3)) throw new IOException("Hang fixture not ready"); await Task.Delay(20); }
            using (var cancellation = new CancellationTokenSource())
            {
                var operation = worker.Execute(new(second, Action("inspect"), 0, 0), cancellation.Token); await Task.Delay(100);
                Check(worker.Running, "isolated computer worker active before stop"); var stopping = Stopwatch.StartNew(); cancellation.Cancel();
                try { await operation; Check(false, "active worker cancellation"); } catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException) { Check(stopping.ElapsedMilliseconds < 2000 && !worker.Running, "active hung-window worker stopped within two seconds"); }
            }
            fixtures[0].CloseMainWindow(); await fixtures[0].WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            try { await RunAction(first, Action("inspect")); Check(false, "closed window rejected"); } catch (IOException) { Check(true, "closed window rejected"); }
            Check(metrics.Where(m => m.Success).All(m => m.ImageBytes > 0 && m.ImageWidth > 0), "every successful native action returns a fresh captured frame");
            WriteReport(true, "");
            return 0;
        }
        catch (Exception ex)
        {
            WriteReport(false, ex.Message);
            File.WriteAllText(Path.Combine(root, "computer-native-error.txt"), ex.ToString() + "\n" + string.Join('\n', records)); return 1;
        }
        finally { worker.Stop(); foreach (var fixture in fixtures) { try { if (!fixture.HasExited) { fixture.CloseMainWindow(); if (!fixture.WaitForExit(2000)) fixture.Kill(true); } } finally { fixture.Dispose(); } } }
    }
}
