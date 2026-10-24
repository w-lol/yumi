using System;
namespace Yumi.Desktop
{
    public static class TwoBoneReach
    {
        // Law of cosines with a reachable target and a deliberately bent elbow.
        public static bool Solve(double upper,double lower,double requested,out double distance,out double along,out double bend)
        {
            distance=along=bend=0;
            if(Double.IsNaN(upper+lower+requested)||Double.IsInfinity(upper+lower+requested)||upper<=.00001||lower<=.00001)return false;
            double minimum=Math.Abs(upper-lower)+.00001,maximum=(upper+lower)*.94;
            if(minimum>=maximum)return false;
            distance=Math.Max(minimum,Math.Min(maximum,requested));
            along=(upper*upper-lower*lower+distance*distance)/(2*distance);
            bend=Math.Sqrt(Math.Max(0,upper*upper-along*along));return true;
        }
    }
}
