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
}
