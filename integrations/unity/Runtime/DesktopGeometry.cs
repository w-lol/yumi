// Read-only Win32/DWM geometry. Unity remains responsible for animation/navigation.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Yumi.Desktop
{
    public struct DesktopRect
    {
        public double X,Y,Width,Height;
        public double Right { get { return X+Width; } }
        public double Bottom { get { return Y+Height; } }
        public DesktopRect(double x,double y,double width,double height) { X=x;Y=y;Width=width;Height=height; }
        public bool Contains(double x,double y) { return x>=X && x<=Right && y>=Y && y<=Bottom; }
    }
    public class DesktopMonitor { public string Id; public DesktopRect Bounds,Work; }
    public class DesktopWindow { public string Id,Title,Application; public DesktopRect Bounds; public bool Foreground; }
    public class DesktopSurface { public string Id,WindowId,MonitorId; public double Left,Right,Y; }
    public class DesktopSnapshot
    {
        public DesktopMonitor[] Monitors=new DesktopMonitor[0];
        public DesktopWindow[] Windows=new DesktopWindow[0];
        public DesktopSurface[] Surfaces=new DesktopSurface[0];
        public long Revision;
    }

    public sealed class DesktopGeometry : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left,Top,Right,Bottom; }
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct MONITORINFOEX
        {
            public int Size;public RECT Monitor,Work;public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Device;
        }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
        delegate bool EnumWindow(IntPtr hwnd,IntPtr data);
        delegate bool EnumMonitor(IntPtr monitor,IntPtr hdc,IntPtr rect,IntPtr data);
        delegate void WinEvent(IntPtr hook,uint evt,IntPtr hwnd,int obj,int child,uint thread,uint time);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindow callback,IntPtr data);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc,IntPtr clip,EnumMonitor callback,IntPtr data);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor,ref MONITORINFOEX info);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out RECT rect);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,StringBuilder title,int maximum);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
        [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr hwnd,int index);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd,int attribute,out RECT value,int size);
        [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")] static extern int DwmGetWindowCloaked(IntPtr hwnd,int attribute,out uint value,int size);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEvent callback,uint process,uint thread,uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);

        readonly List<IntPtr> hooks=new List<IntPtr>();
        readonly Dictionary<string,long> identities=new Dictionary<string,long>();
        readonly Dictionary<uint,string> applications=new Dictionary<uint,string>();
        readonly int excludedProcess;
        readonly WinEvent callback;
        volatile bool dirty=true;
        double lastRead=-10;
        long identitySerial,revision;
        public DesktopSnapshot Current { get; private set; }
        public bool HooksAvailable { get; private set; }
        public DesktopGeometry(int excludedProcessId=-1,bool registerHooks=true)
        {
            excludedProcess=excludedProcessId<0?Process.GetCurrentProcess().Id:excludedProcessId;
            Current=new DesktopSnapshot();
            callback=(a,b,c,d,e,f,g)=>{dirty=true;};
            if(registerHooks && Environment.OSVersion.Platform==PlatformID.Win32NT)
            {
                foreach(uint[] range in new[]{new uint[]{0x8000,0x8003},new uint[]{0x800B,0x800B},new uint[]{3,3},new uint[]{0x16,0x17}})
                    hooks.Add(SetWinEventHook(range[0],range[1],IntPtr.Zero,callback,0,0,2));
                HooksAvailable=hooks.All(h=>h!=IntPtr.Zero);
            }
        }
        public bool RefreshIfNeeded(double unscaledTime)
        {
            if((dirty && unscaledTime-lastRead>=.1)||unscaledTime-lastRead>=2)
            { lastRead=unscaledTime;dirty=false;Refresh();return true; }
            return false;
        }
        static DesktopRect Convert(RECT r) { return new DesktopRect(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top); }
        public DesktopSnapshot Refresh()
        {
            if(Environment.OSVersion.Platform!=PlatformID.Win32NT)throw new PlatformNotSupportedException("Windows geometry requires Windows");
            IntPtr previous=SetThreadDpiAwarenessContext(new IntPtr(-4));
            try
            {
                List<DesktopMonitor> monitors=new List<DesktopMonitor>();
                EnumMonitor monitorCallback=(h,dc,r,data)=>{
                    MONITORINFOEX info=new MONITORINFOEX { Size=Marshal.SizeOf(typeof(MONITORINFOEX)) };
                    if(GetMonitorInfo(h,ref info))monitors.Add(new DesktopMonitor {Id=info.Device,Bounds=Convert(info.Monitor),Work=Convert(info.Work)});
                    return true;
                };
                EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,monitorCallback,IntPtr.Zero);
                List<DesktopWindow> windows=new List<DesktopWindow>();HashSet<string> seen=new HashSet<string>();
                HashSet<uint> pids=new HashSet<uint>();IntPtr foreground=GetForegroundWindow();
                EnumWindow windowCallback=(h,data)=>{
                    if(windows.Count>=128 || !IsWindowVisible(h)||IsIconic(h))return true;
                    uint pid;GetWindowThreadProcessId(h,out pid);
                    if(pid==excludedProcess || (GetWindowLong(h,-20)&0x80)!=0)return true;
                    uint cloaked;if(DwmGetWindowCloaked(h,14,out cloaked,4)==0&&cloaked!=0)return true;
                    StringBuilder title=new StringBuilder(513);GetWindowText(h,title,513);if(title.Length==0)return true;
                    RECT native;
                    if(DwmGetWindowAttribute(h,9,out native,Marshal.SizeOf(typeof(RECT)))!=0&&!GetWindowRect(h,out native))return true;
                    DesktopRect r=Convert(native);
                    if(r.Width<64 || r.Height<32 || !monitors.Any(m=>r.Right>m.Bounds.X&&r.X<m.Bounds.Right&&r.Bottom>m.Bounds.Y&&r.Y<m.Bounds.Bottom))return true;
                    string key=h.ToInt64().ToString("X")+":"+pid;seen.Add(key);pids.Add(pid);
                    if(!identities.ContainsKey(key))identities[key]=++identitySerial;
                    if(!applications.ContainsKey(pid))
                    {
                        try { using(Process p=Process.GetProcessById((int)pid))applications[pid]=p.ProcessName; }
                        catch { applications[pid]=""; }
                    }
                    windows.Add(new DesktopWindow {Id=key+":"+identities[key],Title=title.ToString(),Application=applications[pid],Bounds=r,Foreground=h==foreground});
                    return true;
                };
                EnumWindows(windowCallback,IntPtr.Zero);
                foreach(string key in identities.Keys.Where(k=>!seen.Contains(k)).ToArray())identities.Remove(key);
                foreach(uint pid in applications.Keys.Where(p=>!pids.Contains(p)).ToArray())applications.Remove(pid);
                DesktopSnapshot snapshot=new DesktopSnapshot {Monitors=monitors.ToArray(),Windows=windows.ToArray(),Revision=++revision};
                snapshot.Surfaces=BuildSurfaces(snapshot.Monitors,snapshot.Windows);
                Current=snapshot;return snapshot;
            }
            finally { if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous); }
        }
        public static DesktopSurface[] BuildSurfaces(DesktopMonitor[] monitors,DesktopWindow[] windows)
        {
            List<DesktopSurface> surfaces=new List<DesktopSurface>();
            foreach(DesktopMonitor monitor in monitors)surfaces.Add(new DesktopSurface {Id="floor:"+monitor.Id,MonitorId=monitor.Id,Left=monitor.Work.X,Right=monitor.Work.Right,Y=monitor.Work.Bottom});
            for(int i=0;i<windows.Length;i++)
            {
                DesktopWindow window=windows[i];DesktopRect r=window.Bounds;
                foreach(DesktopMonitor monitor in monitors)
                {
                    if(r.Y<monitor.Work.Y||r.Y>monitor.Work.Bottom)continue;
                    List<Tuple<double,double>> segments=new List<Tuple<double,double>> {Tuple.Create(Math.Max(r.X,monitor.Work.X),Math.Min(r.Right,monitor.Work.Right))};
                    for(int j=0;j<i;j++)
                    {
                        DesktopRect f=windows[j].Bounds;if(f.Y>r.Y||f.Bottom<=r.Y)continue;
                        List<Tuple<double,double>> split=new List<Tuple<double,double>>();
                        foreach(var s in segments)
                        {
                            if(Math.Min(s.Item2,f.X)-s.Item1>=48)split.Add(Tuple.Create(s.Item1,Math.Min(s.Item2,f.X)));
                            if(s.Item2-Math.Max(s.Item1,f.Right)>=48)split.Add(Tuple.Create(Math.Max(s.Item1,f.Right),s.Item2));
                        }
                        segments=split;
                    }
                    int part=0;
                    foreach(var s in segments)if(s.Item2-s.Item1>=48)surfaces.Add(new DesktopSurface {Id=window.Id+":"+monitor.Id+":"+(part++),WindowId=window.Id,MonitorId=monitor.Id,Left=s.Item1,Right=s.Item2,Y=r.Y});
                }
            }
            return surfaces.Take(256).ToArray();
        }
        public void Dispose() { foreach(IntPtr hook in hooks)if(hook!=IntPtr.Zero)UnhookWinEvent(hook);hooks.Clear(); }
    }
}
