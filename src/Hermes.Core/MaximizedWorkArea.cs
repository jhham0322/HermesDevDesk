namespace Hermes.Core;

public readonly record struct DesktopPixelRect(int Left,int Top,int Right,int Bottom)
{
    public int Width=>Right-Left;
    public int Height=>Bottom-Top;
}
[Flags] public enum AutoHideEdges { None=0, Left=1, Top=2, Right=4, Bottom=8 }
public readonly record struct MaximizePlacement(int OffsetX,int OffsetY,int Width,int Height);

public static class MaximizedWorkArea
{
    // Win32 monitor/work rectangles and WM_GETMINMAXINFO use physical pixels.
    // Do not mix these with primary-monitor SystemParameters.WorkArea DIPs.
    public static MaximizePlacement Calculate(DesktopPixelRect monitor,DesktopPixelRect work,AutoHideEdges autoHide)
    {
        if(monitor.Width<=0||monitor.Height<=0||work.Width<=0||work.Height<=0)throw new IOException("Invalid monitor work area");
        var left=Math.Max(monitor.Left,work.Left);var top=Math.Max(monitor.Top,work.Top);
        var right=Math.Min(monitor.Right,work.Right);var bottom=Math.Min(monitor.Bottom,work.Bottom);
        // Reserve the normal activation edge only when WorkArea did not already reserve it.
        if(autoHide.HasFlag(AutoHideEdges.Left)&&left==monitor.Left)left+=2;
        if(autoHide.HasFlag(AutoHideEdges.Top)&&top==monitor.Top)top+=2;
        if(autoHide.HasFlag(AutoHideEdges.Right)&&right==monitor.Right)right-=2;
        if(autoHide.HasFlag(AutoHideEdges.Bottom)&&bottom==monitor.Bottom)bottom-=2;
        if(right<=left||bottom<=top)throw new IOException("Empty monitor work area");
        return new(left-monitor.Left,top-monitor.Top,right-left,bottom-top);
    }
}
