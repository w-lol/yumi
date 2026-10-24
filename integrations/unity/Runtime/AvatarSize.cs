using System;
namespace Yumi.Desktop
{
    // Network disconnects and omitted/invalid values must never resize the avatar.
    public sealed class AvatarSize
    {
        public double Current {get;private set;}=1;
        public double Target {get;private set;}=1;
        double proximity=1;
        public void Request(double value)
        {if(!Double.IsNaN(value)&&!Double.IsInfinity(value)&&value>0)Target=Math.Max(.25,Math.Min(3,value));}
        public void Nearer(bool nearer){proximity=Math.Max(.5,Math.Min(1.8,proximity*(nearer?1.2:1/1.2)));}
        public void ResetProximity(){proximity=1;}
        public double Step(double dt)
        {
            double target=Math.Max(.25,Math.Min(3,Target*proximity)),step=Math.Max(0,Math.Min(.1,dt))*.5;
            Current+=Math.Max(-step,Math.Min(step,target-Current));return Current;
        }
    }
}
