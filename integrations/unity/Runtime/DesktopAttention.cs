using System;
using System.Runtime.InteropServices;
namespace Yumi.Desktop
{
    // Geometry and input timestamps only: never reads characters or installs hooks.
    public sealed class DesktopAttention
    {
        [StructLayout(LayoutKind.Sequential)] struct Input {public uint size,time;}
        [StructLayout(LayoutKind.Sequential)] struct Rect {public int left,top,right,bottom;}
        [StructLayout(LayoutKind.Sequential)] struct Gui {public uint size,flags;public IntPtr active,focus,capture,menu,move,caret;public Rect rect;}
        [StructLayout(LayoutKind.Sequential)] struct Point {public int x,y;}
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref Input info);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
        [DllImport("user32.dll")] static extern bool GetGUIThreadInfo(uint thread,ref Gui info);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window,ref Point point);
        uint lastInput;int lastX,lastY;double until;Point attention;
        public bool Target(int cursorX,int cursorY,double now,out int x,out int y)
        {
            x=cursorX;y=cursorY;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            Input input=new Input {size=(uint)Marshal.SizeOf(typeof(Input))};
            if(GetLastInputInfo(ref input))
            {
                if(lastInput!=0&&input.time!=lastInput&&Math.Abs(cursorX-lastX)<2&&Math.Abs(cursorY-lastY)<2)
                {
                    uint process;uint thread=GetWindowThreadProcessId(GetForegroundWindow(),out process);
                    Gui gui=new Gui {size=(uint)Marshal.SizeOf(typeof(Gui))};
                    if(GetGUIThreadInfo(thread,ref gui)&&gui.caret!=IntPtr.Zero)
                    {Point point=new Point {x=gui.rect.left,y=gui.rect.top};if(ClientToScreen(gui.caret,ref point)){attention=point;until=now+3;}}
                }
                lastInput=input.time;
            }
            if(Math.Abs(cursorX-lastX)>3||Math.Abs(cursorY-lastY)>3)until=0;
            lastX=cursorX;lastY=cursorY;
            if(now<until){x=attention.x;y=attention.y;return true;}
#endif
            return false;
        }
    }
}
