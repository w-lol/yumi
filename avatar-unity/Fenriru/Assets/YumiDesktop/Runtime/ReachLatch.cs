namespace Yumi.Desktop
{
    // Choose an arm once per interaction; crossing the torso does not swap it.
    public sealed class ReachLatch
    {
        public bool HasHand {get;private set;}
        public bool Right {get;private set;}
        public bool Both {get;private set;}
        float inactive;
        public void Tick(bool active,bool nearestRight,float leftRatio,float rightRatio,float dt)
        {
            if(!active){inactive+=dt;if(inactive>1){HasHand=false;Both=false;}return;}
            inactive=0;
            if(!HasHand){HasHand=true;Right=nearestRight;}
            float ratio=Right?rightRatio:leftRatio;
            if(ratio>.94f)Both=true;
            else if(ratio<.78f)Both=false;
        }
    }
}
