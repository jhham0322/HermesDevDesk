using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Hermes.Core;

namespace Hermes.Desktop;
public partial class MainWindow
{
    private readonly DispatcherTimer windowSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private bool restoringWindow;
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);
    private void InitializeWindowPersistence()
    {
        if (desk.State.Desktop.WindowBounds is { } saved && WindowGeometry.Valid(saved)) WindowStartupLocation = WindowStartupLocation.Manual;
        Loaded += (_, _) => { RestoreWindowGeometry(); SaveWindowGeometry(); };
        LocationChanged += (_, _) => QueueWindowSave(); SizeChanged += (_, _) => QueueWindowSave(); StateChanged += (_, _) => QueueWindowSave();
        windowSaveTimer.Tick += (_, _) => { windowSaveTimer.Stop(); SaveWindowGeometry(); };
        Closed += (_, _) => windowSaveTimer.Stop();
    }
    private void QueueWindowSave()
    {
        if (restoringWindow || closing || !IsLoaded) return;
        windowSaveTimer.Stop(); windowSaveTimer.Start();
    }
    private void RestoreWindowGeometry()
    {
        var preferences = desk.State.Desktop;
        if (preferences.WindowBounds is not { } saved || !WindowGeometry.Valid(saved)) return;
        restoringWindow = true;
        try
        {
            var rect = new NativeRect { Left = saved.Left, Top = saved.Top, Right = saved.Left + saved.Width, Bottom = saved.Top + saved.Height };
            var monitor = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(MonitorFromRect(ref rect, 2), ref monitor)) return;
            var fitted = WindowGeometry.Fit(saved, monitor.Work.Pixels);
            var hwnd = new WindowInteropHelper(this).Handle;
            WindowState = WindowState.Normal;
            SetWindowPos(hwnd, IntPtr.Zero, fitted.Left, fitted.Top, fitted.Width, fitted.Height, 0x14);
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            var width = fitted.Width / dpi.DpiScaleX; var height = fitted.Height / dpi.DpiScaleY;
            MinWidth = Math.Min(MinWidth, width); MinHeight = Math.Min(MinHeight, height);
            Width = width; Height = height;
            SetWindowPos(hwnd, IntPtr.Zero, fitted.Left, fitted.Top, fitted.Width, fitted.Height, 0x14);
            if (preferences.WindowMaximized) WindowState = WindowState.Maximized;
        }
        finally { restoringWindow = false; }
    }
    private void SaveWindowGeometry()
    {
        if (restoringWindow || !IsLoaded || WindowState == WindowState.Minimized) return;
        try
        {
            var preferences = desk.State.Desktop;
            if (WindowState == WindowState.Normal && GetWindowRect(new WindowInteropHelper(this).Handle, out var rect))
            {
                var bounds = new SavedWindowBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
                if (WindowGeometry.Valid(bounds)) preferences.WindowBounds = bounds;
            }
            preferences.WindowMaximized = WindowState == WindowState.Maximized;
            desk.Store.Save(desk.State);
        }
        catch (Exception ex) { AppLog.Write(ex, "window-placement-save"); }
    }
}
