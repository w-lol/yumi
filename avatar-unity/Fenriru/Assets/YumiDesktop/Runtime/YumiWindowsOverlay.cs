using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Yumi.Desktop
{
    // Color-keyed layered window; does not depend on Unity preserving framebuffer alpha.
    // Controls live in Qt; full click-through includes both empty space and the character.
    public sealed class YumiWindowsOverlay : MonoBehaviour
    {
        [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr h,int index);
        [DllImport("user32.dll",EntryPoint="SetWindowLongW")] static extern int SetWindowLong(IntPtr h,int index,int value);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h,IntPtr order,int x,int y,int width,int height,uint flags);
        [DllImport("user32.dll",SetLastError=true)] [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetLayeredWindowAttributes(IntPtr h,uint colorKey,byte alpha,uint flags);
        IntPtr handle;
        int previousStyle,previousExtended;
        public DesktopRect Bounds { get; private set; }
        public bool Topmost=true,ClickThrough=true;
        public bool TemporaryInteractive;
        public bool Available { get { return handle!=IntPtr.Zero; } }
        bool priorTop,priorClick,priorInteractive;
        int settleFrames;
        Camera overlayCamera;
        bool loggedTransparency;
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
            }
            priorTop=!Topmost;priorClick=!ClickThrough;
            Apply();
#endif
        }
        void Update() { if(Available&&(settleFrames-->0||priorTop!=Topmost||priorClick!=ClickThrough||priorInteractive!=TemporaryInteractive))Apply(); }
        void Apply()
        {
            // TOOLWINDOW | NOACTIVATE. Never SetForegroundWindow/SetFocus/SendInput.
            int style=OverlayWindowPolicy.ExtendedStyle(previousExtended,ClickThrough&&!TemporaryInteractive);
            SetWindowLong(handle,-16,unchecked((int)0x80000000));
            SetWindowLong(handle,-20,style);
            if(overlayCamera==null)
            {
                YumiDesktopAvatar desktop=GetComponent<YumiDesktopAvatar>();
                overlayCamera=desktop==null?null:desktop.view;
            }
            if(overlayCamera!=null)
            {
                overlayCamera.clearFlags=CameraClearFlags.SolidColor;
                overlayCamera.backgroundColor=new Color32(255,0,255,255);
                overlayCamera.allowHDR=false;overlayCamera.allowMSAA=false;
            }
            if(!SetLayeredWindowAttributes(handle,OverlayWindowPolicy.BackgroundKey,255,OverlayWindowPolicy.ColorKeyFlag))
                UnityEngine.Debug.LogError("Overlay color-key transparency failed. Win32 error="+Marshal.GetLastWin32Error());
            else if(!loggedTransparency)
            {
                loggedTransparency=true;
                UnityEngine.Debug.Log("Overlay color-key transparency configured; camera="+(overlayCamera!=null)+"; graphics="+SystemInfo.graphicsDeviceType);
            }
            IntPtr previousDpi=SetThreadDpiAwarenessContext(new IntPtr(-4));
            try { SetWindowPos(handle,new IntPtr(Topmost?-1:-2),(int)Bounds.X,(int)Bounds.Y,
                (int)Bounds.Width,(int)Bounds.Height,0x0010|0x0020|0x0040); }
            finally {if(previousDpi!=IntPtr.Zero)SetThreadDpiAwarenessContext(previousDpi);}
            priorTop=Topmost;priorClick=ClickThrough;priorInteractive=TemporaryInteractive;
        }
        void OnDisable()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if(Available) {SetWindowLong(handle,-16,previousStyle);SetWindowLong(handle,-20,previousExtended);}
#endif
        }
    }
}
