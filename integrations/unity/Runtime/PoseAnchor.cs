using System;
namespace Yumi.Desktop
{
    // Sample contact once per settled pose, never chase an animation's feet.
    public sealed class PoseAnchor
    {
        string pose="";
        bool initialized,pending;
        double current,target;
        public double Sample(string next,double measured,bool settled,double dt)
        {
            if(Double.IsNaN(measured)||Double.IsInfinity(measured))return current;
            if(!initialized){current=target=measured;initialized=true;}
            if(next!=pose){pose=next;pending=true;}
            if(pending&&settled){target=measured;pending=false;}
            double step=Math.Max(0,Math.Min(.1,dt))*.7;
            current+=Math.Max(-step,Math.Min(step,target-current));
            return current;
        }
    }
}
