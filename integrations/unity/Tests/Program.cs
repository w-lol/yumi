using System;
using System.Linq;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Runtime.InteropServices;
using Yumi.Desktop;
using Yumi.Desktop.Editor;

static class Program
{
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern IntPtr CreateWindowEx(uint ex,string type,string title,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll",SetLastError=true)] static extern bool SetLayeredWindowAttributes(IntPtr h,uint key,byte alpha,uint flags);
    [DllImport("user32.dll",SetLastError=true)] static extern bool GetLayeredWindowAttributes(IntPtr h,out uint key,out byte alpha,out uint flags);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW")] static extern int SetWindowLong(IntPtr h,int index,int value);
    static void Check(bool value,string message) { if(!value)throw new Exception(message); }
    static void Main()
    {
        TestSourceSyntax();
        TestOverlayTransparency();
        TestPantsRepair();
        TestPlayback();
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

    static void TestOverlayTransparency()
    {
        int through=OverlayWindowPolicy.ExtendedStyle(0,true);
        int interactive=OverlayWindowPolicy.ExtendedStyle(through,false);
        Check((through&0x80000)!=0&&(interactive&0x80000)!=0,"Layered transparency must survive click-through changes");
        Check((through&0x20)!=0&&(interactive&0x20)==0,"Click-through must remain independently configurable");
        Check((through&0x08000080)==0x08000080&&(interactive&0x08000080)==0x08000080,"Overlay must retain tool-window/no-activate styles");
        Check(OverlayWindowPolicy.BackgroundKey==0x00FF00FF,"Camera background and COLORREF must agree");
        if(Environment.OSVersion.Platform!=PlatformID.Win32NT)
        {Console.WriteLine("Overlay style policy passed; native transparency attributes require Windows.");return;}
        // Hidden, owned STATIC window: validate native API round trips without
        // showing/focusing a window or claiming Unity pixels are transparent.
        IntPtr handle=CreateWindowEx((uint)through,"STATIC","Yumi transparency fixture",0,0,0,64,64,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
        Check(handle!=IntPtr.Zero,"Transparency fixture creation failed");
        try
        {
            foreach(int style in new[]{through,interactive})
            {
                SetWindowLong(handle,-20,style);
                Check(SetLayeredWindowAttributes(handle,OverlayWindowPolicy.BackgroundKey,255,OverlayWindowPolicy.ColorKeyFlag),"Native color-key configuration failed");
                uint key,flags;byte alpha;
                Check(GetLayeredWindowAttributes(handle,out key,out alpha,out flags),"Native transparency attribute readback failed");
                Check(key==OverlayWindowPolicy.BackgroundKey&&alpha==255&&flags==OverlayWindowPolicy.ColorKeyFlag,"Native transparency attributes mismatch");
            }
        }
        finally {DestroyWindow(handle);}
        Console.WriteLine("Windows owned-window color-key API round trip passed; Unity rendering remains unverified.");
    }

    static void TestSourceSyntax()
    {
        int count=0;
        foreach(string folder in new[]{"Runtime","Editor","DesktopEditor","EditorTests"})
            foreach(string path in Directory.GetFiles(Path.Combine("integrations/unity",folder),"*.cs"))
                foreach(string[] symbols in new[]{new[]{"UNITY_EDITOR","UNITY_EDITOR_WIN"},new[]{"UNITY_STANDALONE_WIN"}})
                {
                    var tree=CSharpSyntaxTree.ParseText(File.ReadAllText(path),new CSharpParseOptions(LanguageVersion.CSharp9,preprocessorSymbols:symbols));
                    var errors=tree.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error).ToArray();
                    Check(errors.Length==0,path+": "+String.Join("; ",errors.Select(d=>d.ToString())));
                    // Unity references are unavailable here, but local-name conflicts can
                    // still be diagnosed semantically despite unresolved external types.
                    var compilation=CSharpCompilation.Create("LocalScopeCheck",new[]{tree},
                        new[]{MetadataReference.CreateFromFile(typeof(object).Assembly.Location)},
                        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                    var scopeErrors=compilation.GetDiagnostics().Where(d=>d.Id=="CS0136"||d.Id=="CS0128").ToArray();
                    Check(scopeErrors.Length==0,path+": "+String.Join("; ",scopeErrors.Select(d=>d.ToString())));count++;
                }
        Console.WriteLine("C#9 source parsing passed ("+count+" platform variants). This does not validate Unity API references.");
    }

    static void TestPantsRepair()
    {
        Func<string,string,string,string,int,int,bool,bool,bool,bool,bool> match=PantsRepairPolicy.Matches;
        string guid=PantsRepairPolicy.SourceGuid,hash=PantsRepairPolicy.SourceSha256,clothing=PantsRepairPolicy.BeltsGuid;
        Check(match("pants/Body",guid,hash,clothing,3,3,true,true,true,true),"Known donor-geometry signature was not recognized");
        Check(!match("Body",guid,hash,clothing,3,3,true,true,true,true),"Primary face must never be repaired as clothing");
        Check(!match("pants/Body",guid,"modified",clothing,3,3,true,true,true,true),"Changed source FBX must not be pruned");
        Check(!match("pants/Body","other",hash,clothing,3,3,true,true,true,true),"Other mesh GUID must not match");
        Check(!match("pants/Body",guid,hash,"other",3,3,true,true,true,true),"Unknown clothing material must not match");
        Check(!match("pants/Body",guid,hash,clothing,2,3,true,true,true,true),"Unexpected material layout must not match");
        Check(!match("pants/Body",guid,hash,clothing,3,2,true,true,true,true),"Unexpected submesh layout must not match");
        Check(!match("pants/Body",guid,hash,clothing,3,3,false,true,true,true),"Assigned original material must be retained");
        Check(!match("pants/Body",guid,hash,clothing,3,3,true,false,true,true),"Assigned second original material must be retained");
        Check(!match("pants/Body",guid,hash,clothing,3,3,true,true,false,true),"Shared face mesh must never be pruned");
        Check(!match("pants/Body",guid,hash,clothing,3,3,true,true,true,false),"Broken clothing shader must not be hidden");
        Console.WriteLine("Pants repair policy: exact fingerprint accepted; changed mesh, face, material, layout and shader rejected.");
    }

    static void TestPlayback()
    {
        var timeline=new PlaybackTimeline();
        timeline.Push(new PlaybackFrame {Epoch=1,Due=1050,Speaking=true,Aa=.8f,Style="excited"},1000);
        Check(!timeline.Sample(1049).Speaking,"Face must wait for estimated DAC time");
        Check(timeline.Sample(1050).Style=="excited","Voice/face delivery must share playback timing");
        timeline.Push(new PlaybackFrame {Epoch=2,Due=1100,Speaking=false},1060);
        Check(!timeline.Sample(1060).Speaking,"Interruption must immediately close mouth");
        timeline.Push(new PlaybackFrame {Epoch=1,Due=1070,Speaking=true,Aa=1},1070);
        Check(!timeline.Sample(1070).Speaking,"Canceled epoch must never resume mouth motion");
        timeline.Push(new PlaybackFrame {Epoch=3,Due=1110,Speaking=true,Aa=float.NaN,Ih=9},1100);
        Check(timeline.Sample(1110).Aa==0&&timeline.Sample(1110).Ih==1,"Invalid visemes must be bounded");
        Check(!timeline.Sample(1500).Speaking,"Stale/disconnected playback must close mouth");
        timeline.Clear();Check(!timeline.Sample(1501).Speaking,"Reset must clear speech and expression");
        Console.WriteLine("Playback timeline: DAC scheduling, shared style, interruption, stale epochs, bounds and disconnect passed.");
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
        var autonomous=new DesktopMotion();var autoGeometry=MotionFixture();autoGeometry.Windows.Last().Foreground=true;
        autonomous.SetGeometry(autoGeometry);autonomous.Stay();
        for(int i=0;i<200;i++)autonomous.Tick(.1,true,false,1);
        Check(!autonomous.AutonomousActive,"Stationary mode must suppress autonomous routes");
        autonomous.Stationary=false;
        for(int i=0;i<400&&!autonomous.AutonomousActive;i++)autonomous.Tick(.1,true,false,1);
        Check(autonomous.AutonomousActive,"Eligible autonomous route did not start");
        autonomous.Tick(.01,false,false,1);
        Check(!autonomous.AutonomousActive&&!autonomous.Navigating,"Disabling autonomy must cancel an active autonomous route");
        autonomous.RequestWindow("Discord");autonomous.Tick(.01,false,false,1);
        Check(autonomous.Navigating,"Disabling autonomy must not cancel manual movement");
        Console.WriteLine("Motion: multi-hop ballistic navigation, state transitions, support movement/disappearance, stationary priority and 30/60 Hz cross-monitor results passed.");
    }
}
