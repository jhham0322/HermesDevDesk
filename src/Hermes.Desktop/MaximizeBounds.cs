using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Hermes.Core;

namespace Hermes.Desktop;

public partial class MainWindow
{
    private HwndSource? maximizeSource;
    private bool maximizeRefreshQueued;
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect
    { public int Left,Top,Right,Bottom; public readonly DesktopPixelRect Pixels=>new(Left,Top,Right,Bottom); }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo
    { public NativePoint Reserved,MaxSize,MaxPosition,MinTrackSize,MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo
    { public uint Size;public NativeRect Monitor,Work;public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct AppBarData
    { public uint Size;public IntPtr Window;public uint Callback,Edge;public NativeRect Rect;public IntPtr Parameter; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    [DllImport("shell32.dll")] private static extern UIntPtr SHAppBarMessage(uint message,ref AppBarData data);

    private void InitializeMaximizeBounds()
    {
        SourceInitialized+=(_,_)=>{maximizeSource=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);maximizeSource?.AddHook(MaximizeHook);};
        Closed+=(_,_)=>{maximizeSource?.RemoveHook(MaximizeHook);maximizeSource=null;};
    }
    private static bool GetWorkPlacement(IntPtr hwnd,out MonitorInfo monitor,out MaximizePlacement placement)
    {
        monitor=new(){Size=(uint)Marshal.SizeOf<MonitorInfo>()};placement=default;
        if(!GetMonitorInfo(MonitorFromWindow(hwnd,2),ref monitor))return false;
        AutoHideEdges edges=AutoHideEdges.None;
        for(uint edge=0;edge<4;edge++)
        {
            var bar=new AppBarData{Size=(uint)Marshal.SizeOf<AppBarData>(),Edge=edge,Rect=monitor.Monitor};
            // ABM_GETAUTOHIDEBAREX is a read-only query for this monitor, not a taskbar setting change.
            if(SHAppBarMessage(0xB,ref bar)!=UIntPtr.Zero)edges|=(AutoHideEdges)(1<<(int)edge);
        }
        try{placement=MaximizedWorkArea.Calculate(monitor.Monitor.Pixels,monitor.Work.Pixels,edges);return true;}
        catch(System.IO.IOException){return false;}
    }
    private IntPtr MaximizeHook(IntPtr hwnd,int message,IntPtr wparam,IntPtr lparam,ref bool handled)
    {
        if(message==0x24&&GetWorkPlacement(hwnd,out _,out var p))
        {
            var info=Marshal.PtrToStructure<MinMaxInfo>(lparam);
            info.MaxPosition=new(){X=p.OffsetX,Y=p.OffsetY};info.MaxSize=new(){X=p.Width,Y=p.Height};
            Marshal.StructureToPtr(info,lparam,false);handled=true;
        }
        // Let WPF process DPI/display changes, then update only our maximized HWND without activation.
        if(message is 0x2E0 or 0x1A or 0x7E && !maximizeRefreshQueued)
        {
            maximizeRefreshQueued=true;Dispatcher.BeginInvoke(DispatcherPriority.Loaded,()=>
            {
                maximizeRefreshQueued=false;
                if(WindowState==WindowState.Maximized&&maximizeSource!=null&&GetWorkPlacement(hwnd,out var m,out var p))
                    SetWindowPos(hwnd,IntPtr.Zero,m.Monitor.Left+p.OffsetX,m.Monitor.Top+p.OffsetY,p.Width,p.Height,0x214);
            });
        }
        return IntPtr.Zero;
    }
}
