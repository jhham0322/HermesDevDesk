using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;

namespace Hermes.Desktop;

public partial class MainWindow
{
    private delegate bool MonitorCallback(IntPtr monitor,IntPtr dc,ref NativeRect rect,IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorCallback callback,IntPtr data);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    internal async Task RunMaximizeChecks(string directory)
    {
        var results=new List<string>();var samples=new List<object>();
        void Check(bool ok,string label){if(!ok)throw new IOException(label);results.Add("PASS "+label);}
        var monitors=new List<IntPtr>();
        EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,(IntPtr monitor,IntPtr dc,ref NativeRect rect,IntPtr data)=>{monitors.Add(monitor);return true;},IntPtr.Zero);
        var hwnd=new WindowInteropHelper(this).Handle;
        try
        {
            foreach(var handle in monitors)
            {
                var info=new MonitorInfo{Size=(uint)Marshal.SizeOf<MonitorInfo>()};if(!GetMonitorInfo(handle,ref info))continue;
                WindowState=WindowState.Normal;
                SetWindowPos(hwnd,IntPtr.Zero,info.Work.Left+30,info.Work.Top+30,Math.Min(1600,info.Work.Pixels.Width-60),Math.Min(1100,info.Work.Pixels.Height-60),0x214);
                await Task.Delay(400);GetWindowRect(hwnd,out var before);
                Check(MonitorFromWindow(hwnd,2)==handle,"owned window actually moved to requested monitor");
                maximizeSource?.RemoveHook(MaximizeHook);WindowState=WindowState.Maximized;await Task.Delay(300);GetWindowRect(hwnd,out var baseline);WindowState=WindowState.Normal;await Task.Delay(200);maximizeSource?.AddHook(MaximizeHook);
                Maximize_Click(this,new RoutedEventArgs());await Task.Delay(400);
                GetWindowRect(hwnd,out var maximized);GetWorkPlacement(hwnd,out var actual,out var expected);
                Check(MonitorFromWindow(hwnd,2)==handle,"maximized window remains on the requested monitor");
                Check(WindowState==WindowState.Maximized,"real WPF maximize state reached");
                Check(Math.Abs(maximized.Left-(actual.Monitor.Left+expected.OffsetX))<=2&&Math.Abs(maximized.Top-(actual.Monitor.Top+expected.OffsetY))<=2&&Math.Abs(maximized.Pixels.Width-expected.Width)<=2&&Math.Abs(maximized.Pixels.Height-expected.Height)<=2,"actual maximized HWND matches this monitor work area");
                Check(!Topmost,"taskbar fix does not enable always-on-top");
                // Exercise the same deferred bounds update used by DPI/work-area notifications.
                var handled=false;MaximizeHook(hwnd,0x1A,IntPtr.Zero,IntPtr.Zero,ref handled);await Task.Delay(300);
                Maximize_Click(this,new RoutedEventArgs());await Task.Delay(400);GetWindowRect(hwnd,out var after);
                Check(WindowState==WindowState.Normal&&Math.Abs(after.Pixels.Width-before.Pixels.Width)<=2&&Math.Abs(after.Pixels.Height-before.Pixels.Height)<=2&&Math.Abs(after.Left-before.Left)<=2&&Math.Abs(after.Top-before.Top)<=2,"restore preserves the previous normal size and position after work-area refresh");
                samples.Add(new{baselineWidth=baseline.Pixels.Width,baselineHeight=baseline.Pixels.Height,baselineInvadedWorkArea=baseline.Left<actual.Work.Left||baseline.Top<actual.Work.Top||baseline.Right>actual.Work.Right||baseline.Bottom>actual.Work.Bottom,dpi=GetDpiForWindow(hwnd),workWidth=actual.Work.Pixels.Width,workHeight=actual.Work.Pixels.Height,maxWidth=maximized.Pixels.Width,maxHeight=maximized.Pixels.Height,normalWidth=before.Pixels.Width,normalHeight=before.Pixels.Height,restoreWidth=after.Pixels.Width,restoreHeight=after.Pixels.Height});
            }
            File.WriteAllText(Path.Combine(directory,"maximize-report.json"),JsonSerializer.Serialize(new{passed=true,checks=results.Count,results,monitorCount=monitors.Count,samples,programmaticWindowStateChanges=true,osSettingsChanged=false,realTaskbarPositionsNotChanged=true,autoHideAndUnattachedDpiCases="logic tests only unless present in monitor samples",userSettingsRead=false},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception ex){File.WriteAllText(Path.Combine(directory,"maximize-report.json"),JsonSerializer.Serialize(new{passed=false,error=ex.Message,results,monitorCount=monitors.Count,samples},new JsonSerializerOptions{WriteIndented=true}));throw;}
        finally{WindowState=WindowState.Normal;}
    }
}
