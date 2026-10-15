using Hermes.Core;

internal static class MaximizeBoundsTests
{
    internal static void Run(Action<bool,string> check)
    {
        foreach(var scale in new[]{1.0,1.25,1.5,2.0})
        {
            int width=(int)(1920*scale),height=(int)(1080*scale),bar=(int)(40*scale);
            foreach(var origin in new[]{(0,0),(-width,0),(0,-height)})
            {
                var m=new DesktopPixelRect(origin.Item1,origin.Item2,origin.Item1+width,origin.Item2+height);
                foreach(var edge in new[]{AutoHideEdges.Left,AutoHideEdges.Top,AutoHideEdges.Right,AutoHideEdges.Bottom})
                {
                    var w=edge switch{AutoHideEdges.Left=>m with{Left=m.Left+bar},AutoHideEdges.Top=>m with{Top=m.Top+bar},AutoHideEdges.Right=>m with{Right=m.Right-bar},_=>m with{Bottom=m.Bottom-bar}};
                    var p=MaximizedWorkArea.Calculate(m,w,AutoHideEdges.None);
                    check(m.Left+p.OffsetX==w.Left&&m.Top+p.OffsetY==w.Top&&p.Width==w.Width&&p.Height==w.Height,"physical WorkArea respects taskbar edge, negative monitor origin and DPI");
                    var auto=MaximizedWorkArea.Calculate(m,m,edge);
                    check(auto.Width==width-(edge is AutoHideEdges.Left or AutoHideEdges.Right?2:0)&&auto.Height==height-(edge is AutoHideEdges.Top or AutoHideEdges.Bottom?2:0),"auto-hide activation strip is reserved without changing taskbar settings");
                    check(MaximizedWorkArea.Calculate(m,w,edge)==p,"already reserved edge is not deducted twice");
                }
            }
        }
    }
}
