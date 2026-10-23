using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Yumi.Desktop
{
    // Native DWM glass overlay. Transparency must be accepted in a D3D11 Windows player.
    // Controls live in Qt; full click-through includes both empty space and the character.
    public sealed class YumiWindowsOverlay : MonoBehaviour
    {
        [StructLayout(LayoutKind.Sequential)] struct Margins { public int Left,Right,Top,Bottom; }
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr h,ref Margins margins);
        [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr h,int index);
        [DllImport("user32.dll",EntryPoint="SetWindowLongW")] static extern int SetWindowLong(IntPtr h,int index,int value);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h,IntPtr order,int x,int y,int width,int height,uint flags);
        IntPtr handle;
        int previousStyle,previousExtended;
        public DesktopRect Bounds { get; private set; }
        public bool Topmost=true,ClickThrough=true;
        public bool Available { get { return handle!=IntPtr.Zero; } }
        bool priorTop,priorClick;
        int settleFrames;
        public void Configure(DesktopRect bounds)
        {
            Bounds=bounds;settleFrames=3;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if(handle==IntPtr.Zero)
            {
                handle=Process.GetCurrentProcess().MainWindowHandle;
                if(handle==IntPtr.Zero)return;
                previousStyle=GetWindowLong(handle,-16);previousExtended=GetWindowLong(handle,-20);
                SetWindowLong(handle,-16,unchecked((int)0x80000000));
                Margins glass=new Margins { Left=-1,Right=-1,Top=-1,Bottom=-1 };
                if(DwmExtendFrameIntoClientArea(handle,ref glass)!=0) {UnityEngine.Debug.LogError("DWM transparency unavailable");}
            }
            priorTop=!Topmost;priorClick=!ClickThrough;
            Apply();
#endif
        }
        void Update() { if(Available&&(settleFrames-->0||priorTop!=Topmost||priorClick!=ClickThrough))Apply(); }
        void Apply()
        {
            // TOOLWINDOW | NOACTIVATE. Never SetForegroundWindow/SetFocus/SendInput.
            int style=previousExtended|0x80|0x08000000;
            if(ClickThrough)style|=0x20|0x80000;
            SetWindowLong(handle,-16,unchecked((int)0x80000000));
            SetWindowLong(handle,-20,style);
            IntPtr previousDpi=SetThreadDpiAwarenessContext(new IntPtr(-4));
            try { SetWindowPos(handle,new IntPtr(Topmost?-1:-2),(int)Bounds.X,(int)Bounds.Y,
                (int)Bounds.Width,(int)Bounds.Height,0x0010|0x0020|0x0040); }
            finally {if(previousDpi!=IntPtr.Zero)SetThreadDpiAwarenessContext(previousDpi);}
            priorTop=Topmost;priorClick=ClickThrough;
        }
        void OnDisable()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if(Available) {SetWindowLong(handle,-16,previousStyle);SetWindowLong(handle,-20,previousExtended);}
#endif
        }
    }
}
