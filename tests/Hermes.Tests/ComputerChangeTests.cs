using Hermes.Core;

internal static class ComputerChangeTests
{
    internal static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow; byte[] Flat(int value) => Enumerable.Repeat((byte)value, 1000).ToArray();
        var gate = new ComputerChangeGate(); var notifications = 0;
        for (int i=0;i<20;i++) if(gate.Observe(Flat(0),null,now.AddMilliseconds(i*300)).Notify) notifications++;
        check(notifications==0,"unchanged 300ms captures cause zero change notifications/model calls");
        gate.Observe(Flat(0),new(10,10),now.AddSeconds(7));
        check(gate.Observe(Flat(0),new(10,10),now.AddMilliseconds(7300)).Notify,"cursor-only movement notifies after debounce");
        gate.Acknowledge(Flat(0),new(10,10),now.AddMilliseconds(7300));
        var moving=0; for(int i=1;i<=12;i++){var d=gate.Observe(Flat(0),new(10+i,10),now.AddMilliseconds(7300).AddMilliseconds(i*300));if(d.Notify){moving++;gate.Acknowledge(Flat(0),new(10+i,10),now.AddMilliseconds(7300).AddMilliseconds(i*300));}}
        check(moving>0&&moving<=3,"continuous cursor movement coalesces with one-second notification cap");
        gate=new();gate.Observe(Flat(0),null,now);bool accumulated=false;
        for(int i=1;i<=8;i++){var d=gate.Observe(Flat(i*3),null,now.AddMilliseconds(i*300));accumulated|=d.Notify;}
        check(accumulated,"small cumulative pixel changes compare against last delivered frame");
        gate=new();gate.Observe(Flat(0),null,now); bool sparse=false;
        for(int i=1;i<=14;i++){var pixels=Flat(0);Array.Fill(pixels,(byte)64,0,i);sparse|=gate.Observe(pixels,null,now.AddMilliseconds(i*300)).Notify;}
        check(sparse,"individually subthreshold spatial changes accumulate against last delivered frame");
        try { new ComputerChangeGate(new(MinimumNotificationMs:100)); check(false,"invalid notification cap accepted"); } catch(IOException){check(true,"rate below one second is rejected");}
        gate=new();gate.Observe(Flat(0),null,now);int animation=0;
        for(int i=1;i<=30;i++){var pixels=Flat(i%2==0?0:255);var d=gate.Observe(pixels,null,now.AddMilliseconds(i*300));if(d.Notify){animation++;gate.Acknowledge(pixels,null,now.AddMilliseconds(i*300));}}
        check(animation>0&&animation<=9,"animation is bounded and never indefinitely debounced");
        gate.Stop();check(!gate.Observe(Flat(99),new(1,1),now.AddSeconds(20)).Notify,"stop discards pending change and frame references");
        gate=new();check(!gate.Observe(Flat(99),null,now).Notify,"resumed session establishes a new baseline");
        var mapped=ComputerImageCoordinates.ToClient(800,450,1600,900,2400,1350);
        check(mapped==(1200,675),"observed image coordinates map exactly to 150% DPI client pixels");
        try { ComputerImageCoordinates.ToClient(1600,0,1600,900,2400,1350); check(false,"image boundary accepted"); } catch(IOException) { check(true,"image boundary rejected before input"); }
        check(ComputerCaptureGuard.IsBlackOrBlank(new byte[256])&&!ComputerCaptureGuard.IsBlackOrBlank(new byte[]{255}),"black/blank heuristic fails closed and does not classify normal white content as DRM");
        check(new TokenCounts(100,20,70,5,120).UncachedInput==30,"uncached input equals input minus cached; reasoning remains subset of output");
        check(new TokenCounts(100,20,null,5,120).UncachedInput==null,"missing cache usage stays unknown");
        var action=new ComputerAction("click","","",0,0,0,0,"","","left",1,1,false);
        check(ComputerExecutionPolicy.CanAutoExecute(action,true,true,true,false),"explicit scoped auto mode permits general click");
        check(!ComputerExecutionPolicy.CanAutoExecute(action,true,false,true,false),"auto mode never permits an out-of-scope window");
        check(!ComputerExecutionPolicy.CanAutoExecute(action,false,true,true,false),"revoked session rejects auto mode");
        check(!ComputerExecutionPolicy.CanAutoExecute(action,true,true,true,true),"sensitive interaction retains review in auto mode");
        check(!ComputerExecutionPolicy.CanAutoExecute(action with {Action="key"},true,true,true,false),"auto mode does not broaden to arbitrary key chords");
    }
}
