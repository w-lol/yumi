using System;
using System.Linq;
using System.Runtime.InteropServices;
using Yumi.Desktop;

static class Program
{
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern IntPtr CreateWindowEx(uint ex,string type,string title,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    static void Check(bool value,string message) { if(!value)throw new Exception(message); }
    static void Main()
    {
        TestMotion();
        DesktopMonitor monitor=new DesktopMonitor {Id="left",Bounds=new DesktopRect(-1000,0,1000,800),Work=new DesktopRect(-1000,0,1000,760)};
        DesktopWindow front=new DesktopWindow {Id="front",Bounds=new DesktopRect(-800,50,200,350)};
        DesktopWindow back=new DesktopWindow {Id="back",Bounds=new DesktopRect(-950,200,850,400)};
        DesktopSurface[] surfaces=DesktopGeometry.BuildSurfaces(new[]{monitor},new[]{front,back});
        Check(surfaces.Count(s=>s.WindowId=="back")==2,"Occluded top edge must split into two visible surfaces");
        Check(surfaces.Single(s=>s.Id=="floor:left").Y==760,"Taskbar work-area floor is incorrect");
        Check(surfaces.Where(s=>s.WindowId=="back").All(s=>s.Right<=-800||s.Left>=-600),"Occluded top-edge pixels leaked");
        Console.WriteLine("Pure geometry: negative coordinates, taskbar work area and z-order occlusion passed.");
        if(Environment.OSVersion.Platform!=PlatformID.Win32NT)return;
        string title="Yumi owned geometry fixture "+Guid.NewGuid().ToString("N");
        // Create only this test's no-activate window; never mutate another process's window.
        IntPtr window=CreateWindowEx(0x08000000,"STATIC",title,0x10CF0000,100,100,420,240,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        Check(window!=IntPtr.Zero,"Owned fixture creation failed: "+Marshal.GetLastWin32Error());
        try
        {
            using(DesktopGeometry geometry=new DesktopGeometry(0,false))
            {
                DesktopSnapshot first=geometry.Refresh();
                Check(first.Monitors.Length>0,"Monitor enumeration failed");
                DesktopWindow found=first.Windows.SingleOrDefault(w=>w.Title==title);
                Check(found!=null,"Actual owned window was not enumerated");
                Check(found.Bounds.Width>100&&found.Bounds.Height>100,"Native visible-frame geometry is invalid");
                Check(geometry.Refresh().Windows.Single(w=>w.Title==title).Id==found.Id,"Visible window identity is unstable");
                DestroyWindow(window);window=IntPtr.Zero;
                Check(!geometry.Refresh().Windows.Any(w=>w.Title==title),"Destroyed window remained in geometry");
            }
        }
        finally { if(window!=IntPtr.Zero)DestroyWindow(window); }
        Console.WriteLine("Windows geometry: actual owned-window bounds, monitors, stable identity and disappearance passed.");
    }

    static DesktopSnapshot MotionFixture(bool destination=true,int revision=1,double shift=0)
    {
        DesktopMonitor a=new DesktopMonitor {Id="a",Bounds=new DesktopRect(0,0,1400,1000),Work=new DesktopRect(0,0,1400,960)};
        DesktopMonitor b=new DesktopMonitor {Id="b",Bounds=new DesktopRect(1400,0,1200,1000),Work=new DesktopRect(1400,0,1200,960)};
        DesktopWindow one=new DesktopWindow {Id="one",Application="Browser",Title="Browser",Bounds=new DesktopRect(100,820,250,100)};
        DesktopWindow two=new DesktopWindow {Id="two",Application="Editor",Title="Editor",Bounds=new DesktopRect(450,700,250,200)};
        DesktopWindow three=new DesktopWindow {Id="three",Application="Discord",Title="Friends - Discord",Bounds=new DesktopRect(800+shift,530+shift/2,300,300)};
        DesktopWindow[] windows=destination?new[]{one,two,three}:new[]{one,two};
        DesktopMonitor[] monitors={a,b};
        return new DesktopSnapshot {Monitors=monitors,Windows=windows,Surfaces=DesktopGeometry.BuildSurfaces(monitors,windows),Revision=revision};
    }
    static void Simulate(DesktopMotion m,double seconds,double rate=60)
    {for(int i=0;i<(int)(seconds*rate);i++)m.Tick(1/rate);}
    static void TestMotion()
    {
        DesktopMotion m=new DesktopMotion();m.SetGeometry(MotionFixture());Simulate(m,.5);
        Check(m.RequestWindow("Discord",true),"Multi-hop route was not found");
        bool walking=false,jumping=false,landing=false;
        for(int i=0;i<2400;i++)
        {
            m.Tick(1.0/60);
            walking|=m.State==CharacterMotion.Walking;jumping|=m.State==CharacterMotion.JumpAirborne;landing|=m.State==CharacterMotion.Landing;
        }
        Check(walking&&jumping&&landing,"Navigation did not use walking, flight and landing states");
        Check(m.State==CharacterMotion.Sitting&&m.SupportId.StartsWith("three:"),"Multi-hop target was not reached and seated");
        int recoveries=m.Recoveries;double oldX=m.X;
        m.SetGeometry(MotionFixture(true,2,40));
        Check(Math.Abs(m.X-oldX-40)<.001&&Math.Abs(m.Y-550)<.001,"Moving support did not carry the character");
        m.SetGeometry(MotionFixture(false,3));Simulate(m,4);
        Check(m.SupportId.StartsWith("floor:"),"Closing support did not produce a safe physical landing");
        Check(m.Recoveries==recoveries,"Normal navigation/falling secretly used recovery teleportation");
        m.Stay();double stayed=m.X;
        for(int i=0;i<3000;i++)m.Tick(1.0/60,true,false,1);
        Check(m.X==stayed,"Stationary command did not override autonomous activity");
        DesktopMotion other=new DesktopMotion();other.SetGeometry(MotionFixture());Simulate(other,.5);
        Check(other.RequestSurface("floor:b"),"Adjacent monitor floor should be reachable");Simulate(other,20,30);
        Check(other.SupportId=="floor:b"&&other.Recoveries==1,"Cross-monitor motion failed or teleported");
        DesktopMotion faster=new DesktopMotion();faster.SetGeometry(MotionFixture());Simulate(faster,.5);
        Check(faster.RequestSurface("floor:b"),"Cross-monitor route missing at 60 Hz");Simulate(faster,20,60);
        Check(Math.Abs(faster.X-other.X)<.01&&Math.Abs(faster.Y-other.Y)<.01,"Motion depends on frame rate");
        Check(!faster.RequestSurface("not-a-surface"),"Invalid surface was accepted");
        Console.WriteLine("Motion: multi-hop ballistic navigation, state transitions, support movement/disappearance, stationary priority and 30/60 Hz cross-monitor results passed.");
    }
}
