// Pure, testable screen-space mechanics. Does not move any operating-system window.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Yumi.Desktop
{
    public enum CharacterMotion { Idle,Listening,Thinking,Talking,Walking,Running,JumpStart,JumpAirborne,Landing,Sitting,Turning,Waving,Excited,Surprised,Recovering }
    public sealed class DesktopMotion
    {
        public double X,Y,VelocityX,VelocityY,Distance;
        public double Speed=150,JumpHeight=170,Gravity=950,FootMargin=18;
        public string SupportId="",TargetWindow="",Message="",Behavior="RESTING";
        public CharacterMotion State=CharacterMotion.Idle;
        public bool Stationary,FollowCursor;
        public bool AutonomousActive { get; private set; }
        public bool Navigating { get { return goal.HasValue||pending!=null||route.Count>0||SupportId==""; } }
        public int Recoveries { get; private set; }
        public DesktopSnapshot Desktop { get; private set; }
        readonly Queue<Edge> route=new Queue<Edge>();
        double? goal;
        Edge pending;
        bool run,sitAfter;
        double clock,manualUntil,timer,nextAutonomy=30;
        sealed class Edge { public string From,To;public double LaunchX,EndX,Duration,VX,VY; }
        public DesktopMotion() { Desktop=new DesktopSnapshot(); }
        static bool Contains(DesktopSurface s,double x,double margin) { return x>=s.Left+margin-1e-6&&x<=s.Right-margin+1e-6; }
        static double Clamp(double x,double a,double b) { return Math.Max(a,Math.Min(b,x)); }
        DesktopSurface Surface(string id) { return Desktop.Surfaces.FirstOrDefault(p=>p.Id==id); }

        public void SetGeometry(DesktopSnapshot snapshot)
        {
            DesktopSurface old=Surface(SupportId);DesktopSnapshot previous=Desktop;Desktop=snapshot;
            DesktopSurface support=Surface(SupportId);
            if(old!=null)
            {
                DesktopWindow before=previous.Windows.FirstOrDefault(w=>w.Id==old.WindowId);
                DesktopWindow after=snapshot.Windows.FirstOrDefault(w=>w.Id==old.WindowId);
                double dx=before!=null&&after!=null?after.Bounds.X-before.Bounds.X:0;
                double newX=X+dx;
                if(support==null)support=snapshot.Surfaces.FirstOrDefault(p=>p.WindowId==old.WindowId&&p.MonitorId==old.MonitorId&&Contains(p,newX,FootMargin));
                if(support!=null&&Contains(support,newX,FootMargin)) {X=newX;Y=support.Y;SupportId=support.Id;}
                else {SupportId="";VelocityY=0;State=CharacterMotion.Recovering;route.Clear();goal=null;pending=null;Message="Supporting window changed; falling to a valid surface.";}
            }
            if(snapshot.Surfaces.Length>0&&previous.Surfaces.Length==0)Recover();
            if(TargetWindow!=""&&SupportId!=""&&Surface(SupportId).WindowId!=TargetWindow&&snapshot.Revision!=previous.Revision)
            {
                string target=TargetWindow;
                if(!RequestWindowId(target,sitAfter)) {Stop();Message="Destination changed or is no longer reachable.";}
            }
        }
        public void Recover()
        {
            DesktopSurface floor=Desktop.Surfaces.Where(p=>p.Id.StartsWith("floor:")).OrderBy(p=>Math.Abs((p.Left+p.Right)/2-X)).FirstOrDefault();
            if(floor==null)return;
            Stop();X=(floor.Left+floor.Right)/2;Y=floor.Y;SupportId=floor.Id;VelocityX=VelocityY=0;
            State=CharacterMotion.Recovering;timer=.3;Recoveries++;Message="Explicit recovery reposition to a safe desktop surface.";
        }
        public void Stop()
        {
            AutonomousActive=false;route.Clear();goal=null;pending=null;VelocityX=0;TargetWindow="";FollowCursor=false;sitAfter=false;
            if(SupportId!="")State=CharacterMotion.Idle;
        }
        public void Stay() { Stop();Stationary=true;manualUntil=Double.PositiveInfinity; }
        public bool RequestWindow(string query,bool sit=false)
        {
            Stop();Stationary=false;manualUntil=clock+30;
            if(String.IsNullOrWhiteSpace(query))return false;
            string q=query.Trim().ToLowerInvariant();if(q.EndsWith(".exe"))q=q.Substring(0,q.Length-4);
            DesktopWindow[] exact=Desktop.Windows.Where(w=>String.Equals(w.Application,q,StringComparison.OrdinalIgnoreCase)||String.Equals(w.Title,q,StringComparison.OrdinalIgnoreCase)).ToArray();
            DesktopWindow[] matches=exact.Length>0?exact:Desktop.Windows.Where(w=>(w.Application+" "+w.Title).ToLowerInvariant().Contains(q)).ToArray();
            if(matches.Length!=1) {Message=matches.Length==0?"No matching visible application window.":"Several windows match; specify a window title.";return false;}
            return RequestWindowId(matches[0].Id,sit);
        }
        bool RequestWindowId(string id,bool sit)
        {
            foreach(DesktopSurface target in Desktop.Surfaces.Where(p=>p.WindowId==id).OrderBy(p=>Math.Abs((p.Left+p.Right)/2-X)+Math.Abs(p.Y-Y)))
                if(Navigate(target)) {TargetWindow=id;sitAfter=sit;return true;}
            Message="Window surface is occluded or unreachable at the configured jump strength.";return false;
        }
        public bool RequestSurface(string id)
        {
            Stop();Stationary=false;manualUntil=clock+30;
            DesktopSurface target=Surface(id);
            if(target==null) {Message="Destination no longer exists.";return false;}
            return Navigate(target);
        }
        public bool WalkToCursor(double x,bool running=false)
        {
            DesktopSurface source=Surface(SupportId);if(source==null)return false;
            Stop();Stationary=false;manualUntil=clock+30;run=running;
            goal=Clamp(x,source.Left+FootMargin,source.Right-FootMargin);return true;
        }
        public void Sit()
        {
            if(SupportId=="")return;
            Stop();State=CharacterMotion.Sitting;manualUntil=clock+30;
        }
        public void Wave() { if(SupportId!=""){Stop();State=CharacterMotion.Waving;timer=2;manualUntil=clock+30;} }

        Edge Flight(DesktopSurface a,DesktopSurface b,double start,double end)
        {
            double launch=Math.Sqrt(2*Gravity*JumpHeight),disc=launch*launch+2*Gravity*(b.Y-a.Y);
            if(disc<-1e-6)return null;
            disc=Math.Max(0,disc);
            double duration=(launch+Math.Sqrt(disc))/Gravity;
            if(duration<.08)return null;
            double vx=(end-start)/duration;if(Math.Abs(vx)>Speed*1.8)return null;
            foreach(DesktopSurface p in Desktop.Surfaces)
            {
                if(p.Id==b.Id)continue;
                double d=launch*launch+2*Gravity*(p.Y-a.Y);if(d<0)continue;
                double when=(launch+Math.Sqrt(d))/Gravity;
                if(when>.02&&when<duration-.02&&Contains(p,start+vx*when,FootMargin))return null;
            }
            return new Edge {From=a.Id,To=b.Id,LaunchX=start,EndX=end,Duration=duration,VX=vx,VY=-launch};
        }
        Edge BestEdge(DesktopSurface a,DesktopSurface b)
        {
            Edge best=null;
            double[] starts={Clamp((b.Left+b.Right)/2,a.Left+FootMargin,a.Right-FootMargin),a.Left+FootMargin,a.Right-FootMargin};
            foreach(double start in starts)
                foreach(double end in new[]{Clamp(start,b.Left+FootMargin,b.Right-FootMargin),b.Left+FootMargin,b.Right-FootMargin})
                {
                    Edge e=Flight(a,b,start,end);
                    if(e!=null&&(best==null||e.Duration+Math.Abs(e.EndX-e.LaunchX)/Speed<best.Duration+Math.Abs(best.EndX-best.LaunchX)/Speed))best=e;
                }
            return best;
        }
        bool Navigate(DesktopSurface target)
        {
            DesktopSurface source=Surface(SupportId);
            if(source==null) {Message="Land before choosing a route.";return false;}
            if(source.Id==target.Id)
            {
                route.Clear();pending=null;goal=(target.Left+target.Right)/2;Stationary=false;manualUntil=clock+30;return true;
            }
            Dictionary<string,double> costs=new Dictionary<string,double>{{source.Id,0}};
            Dictionary<string,Edge> parents=new Dictionary<string,Edge>();HashSet<string> done=new HashSet<string>();
            while(true)
            {
                string key=costs.Where(p=>!done.Contains(p.Key)).OrderBy(p=>p.Value).Select(p=>p.Key).FirstOrDefault();
                if(key==null)break;if(key==target.Id)break;done.Add(key);
                DesktopSurface a=Surface(key);
                foreach(DesktopSurface b in Desktop.Surfaces)
                {
                    if(b.Id==a.Id||done.Contains(b.Id))continue;
                    Edge edge=BestEdge(a,b);if(edge==null)continue;
                    double next=costs[key]+edge.Duration+Math.Abs(edge.LaunchX-(a.Left+a.Right)/2)/Speed;
                    if(!costs.ContainsKey(b.Id)||next<costs[b.Id]) {costs[b.Id]=next;parents[b.Id]=edge;}
                }
            }
            if(!parents.ContainsKey(target.Id)) {Message="No physical route reaches this surface.";return false;}
            List<Edge> reversed=new List<Edge>();string cursor=target.Id;
            while(cursor!=source.Id) {Edge e=parents[cursor];reversed.Add(e);cursor=e.From;}
            reversed.Reverse();route.Clear();foreach(Edge e in reversed)route.Enqueue(e);
            pending=null;goal=null;Stationary=false;manualUntil=clock+30;run=false;Message="Reachable route planned.";return true;
        }

        public void Tick(double delta,bool autonomous=false,bool busy=false,double frequency=.3)
        {
            if(AutonomousActive&&(!autonomous||busy||Stationary))Stop();
            delta=Math.Max(0,Math.Min(.1,delta));int steps=Math.Max(1,(int)Math.Ceiling(delta*120));
            for(int i=0;i<steps;i++)Step(delta/steps);
            clock+=delta;
            if(autonomous&&!busy&&!Stationary&&clock>Math.Max(nextAutonomy,manualUntil)&&SupportId!="")
            {
                nextAutonomy=clock+Math.Max(30,90/Math.Max(.1,frequency));Behavior="OBSERVING";
                DesktopWindow active=Desktop.Windows.FirstOrDefault(w=>w.Foreground);
                if(active!=null&&RequestWindowId(active.Id,false)) {Behavior="EXPLORING";AutonomousActive=true;}
            }
        }
        void Step(double dt)
        {
            timer=Math.Max(0,timer-dt);
            if(SupportId!="")
            {
                if((State==CharacterMotion.Landing||State==CharacterMotion.Recovering||State==CharacterMotion.Waving)&&timer>0)return;
                if(State==CharacterMotion.Landing||State==CharacterMotion.Recovering||State==CharacterMotion.Waving)State=CharacterMotion.Idle;
                if(sitAfter&&route.Count==0&&pending==null&&!goal.HasValue)
                {State=CharacterMotion.Sitting;sitAfter=false;}
                if(pending!=null)
                {
                    if(timer>0)return;
                    DesktopSurface from=Surface(pending.From),to=Surface(pending.To);
                    Edge current=from==null||to==null?null:Flight(from,to,X,pending.EndX);
                    pending=null;
                    if(current==null) {Stop();Message="Route changed before launch.";return;}
                    VelocityX=current.VX;VelocityY=current.VY;SupportId="";State=CharacterMotion.JumpAirborne;return;
                }
                if(route.Count>0)goal=route.Peek().LaunchX;
                if(goal.HasValue)
                {
                    double difference=goal.Value-X;
                    if(Math.Abs(difference)>1)
                    {
                        double dx=Math.Sign(difference)*Math.Min(Math.Abs(difference),Speed*(run?1.6:1)*dt);
                        X+=dx;Distance+=Math.Abs(dx);VelocityX=dt>0?dx/dt:0;State=run?CharacterMotion.Running:CharacterMotion.Walking;
                    }
                    else
                    {
                        goal=null;VelocityX=0;
                        if(route.Count>0) {pending=route.Dequeue();State=CharacterMotion.JumpStart;timer=.15;}
                        else {State=sitAfter?CharacterMotion.Sitting:CharacterMotion.Idle;sitAfter=false;}
                    }
                }
                return;
            }
            DesktopSurface landing=null;double hit=dt+1;
            foreach(DesktopSurface p in Desktop.Surfaces)
            {
                double disc=VelocityY*VelocityY+2*Gravity*(p.Y-Y);if(disc<-1e-6)continue;
                disc=Math.Max(0,disc);
                double t=(-VelocityY+Math.Sqrt(disc))/Gravity;
                if(t>=-1e-8&&t<=dt+1e-8&&t<hit&&VelocityY+Gravity*t>=-1e-6&&Contains(p,X+VelocityX*t,FootMargin)) {landing=p;hit=Clamp(t,0,dt);}
            }
            if(landing!=null)
            {
                X+=VelocityX*hit;Y=landing.Y;SupportId=landing.Id;VelocityX=VelocityY=0;State=CharacterMotion.Landing;timer=.18;
                if(TargetWindow!=""&&landing.WindowId!=TargetWindow)RequestWindowId(TargetWindow,sitAfter);
                return;
            }
            X+=VelocityX*dt;Distance+=Math.Abs(VelocityX*dt);Y+=VelocityY*dt+Gravity*dt*dt/2;VelocityY+=Gravity*dt;
            if(Desktop.Monitors.Length>0&&Y>Desktop.Monitors.Max(m=>m.Bounds.Bottom)+500)Recover();
        }
    }
}
