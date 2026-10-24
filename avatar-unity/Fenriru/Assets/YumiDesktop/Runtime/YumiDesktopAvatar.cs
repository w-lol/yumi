using System;
using System.Linq;
using UnityEngine;

namespace Yumi.Desktop
{
    [DefaultExecutionOrder(100)]
    public sealed class YumiDesktopAvatar : MonoBehaviour
    {
        public YumiBackendClient backend;
        public YumiAvatarRig rig;
        public YumiWindowsOverlay overlay;
        public Camera view;
        public Transform placement;
        public float originalHeight=1.3f;
        public int frameRate=30;
        DesktopGeometry geometry;
        readonly DesktopMotion motion=new DesktopMotion();
        readonly DesktopAttention attention=new DesktopAttention();
        AvatarState state;
        DesktopRect desktop;
        string pending="",pendingType="";
        float deadline,lastReport;
        bool lastStationary=true,lastActive,follow;
        float travelYaw=180;
        readonly PoseAnchor contactAnchor=new PoseAnchor();
        readonly AvatarSize size=new AvatarSize();
        readonly System.Collections.Generic.Dictionary<Transform,Vector3> authoredScales=new System.Collections.Generic.Dictionary<Transform,Vector3>();
        float lookUntil,lastScaleWarning=-10;
        Vector2 lookPoint;
        bool pointerWasDown,dragging;
        Vector2 dragOffset;
        Renderer[] avatarRenderers;
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        const float Unit=.005f;
        void Start()
        {
            Application.runInBackground=true;QualitySettings.vSyncCount=0;
            Application.targetFrameRate=frameRate;
            if(backend==null||rig==null||view==null||placement==null) {Debug.LogError("Incomplete Yumi desktop scene");enabled=false;return;}
            authoredScales[rig.transform]=rig.transform.localScale;
            Transform skeleton=rig.animator.GetBoneTransform(HumanBodyBones.Hips);
            for(int i=0;skeleton!=null&&skeleton!=placement&&i<32;i++,skeleton=skeleton.parent)authoredScales[skeleton]=skeleton.localScale;
            avatarRenderers=rig.GetComponentsInChildren<Renderer>(true);
            backend.StateReceived+=Receive;backend.ActionReceived+=Action;
            backend.Disconnected+=Disconnect;backend.ConnectedToBackend+=Ready;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            geometry=new DesktopGeometry();
            Refresh();
#else
            Debug.LogWarning("Desktop geometry requires Windows; Editor preview only.");
#endif
            motion.Stationary=true;Ready();
        }
        bool ExecutorReady {get {return rig.Ready&&geometry!=null&&geometry.Current.Monitors.Length>0&&overlay.Available;}}
        void Ready() {backend.ReportReady(ExecutorReady,"Rig="+rig.Ready+"; overlay="+overlay.Available+"; monitors="+(geometry==null?0:geometry.Current.Monitors.Length));}
        void Receive(AvatarState value)
        {
            state=value;size.Request(value.scale);rig.State(value);rig.IdleGestures=value.idle_gestures;
            var hair=rig.GetComponent<YumiHairDynamics>();
            if(hair!=null){hair.SimulationEnabled=value.hair_physics;hair.Damping=Mathf.Clamp(value.hair_damping,.01f,.3f);hair.Spring=Mathf.Clamp(value.hair_spring,5,200);}
            overlay.Topmost=value.topmost;overlay.ClickThrough=value.clickthrough;
            placement.gameObject.SetActive(value.visible);
            Application.targetFrameRate=Mathf.Clamp(value.frame_rate,15,60);
            rig.MotionSpeed=Mathf.Clamp(value.animation_speed,.3f,2);
            motion.Speed=Math.Max(40,Math.Min(500,value.movement_speed));
            motion.JumpHeight=Math.Max(30,Math.Min(400,value.jump_height));
            motion.AdaptiveJumps=value.adaptive_jumps;motion.MaximumJumpHeight=Math.Max(motion.JumpHeight,Math.Min(1200,value.jump_limit));
            motion.MinimumHeadroom=320*Mathf.Clamp(value.scale,.25f,3);
            rig.MaximumHeadYaw=Mathf.Clamp(value.head_yaw,20,85);rig.MaximumHeadPitch=Mathf.Clamp(value.head_pitch,10,45);
            if(!value.active&&!motion.Dragging) {motion.Stay();follow=false;Finish(false,"Backend paused; movement stopped");}
            else if(value.active&&(value.stationary!=lastStationary||!lastActive)) {motion.Stationary=value.stationary;if(value.stationary){motion.Stay();follow=false;Finish(false,"Stationary mode enabled");}else motion.ResumeRoaming();}
            lastStationary=value.stationary;lastActive=value.active;
        }
        void Disconnect()
        {state=null;lastActive=false;follow=false;if(!motion.Dragging)motion.Stay();rig.ResetSpeech();pending="";rig.Motion="Idle";}
        void Refresh()
        {
            if(geometry==null)return;
            DesktopSnapshot snap=geometry.Current;
            if(snap.Monitors.Length==0) {snap=geometry.Refresh();if(snap.Monitors.Length==0)return;}
            motion.SetGeometry(snap);
            double x=snap.Monitors.Min(m=>m.Bounds.X),y=snap.Monitors.Min(m=>m.Bounds.Y);
            DesktopRect next=new DesktopRect(x,y,snap.Monitors.Max(m=>m.Bounds.Right)-x,snap.Monitors.Max(m=>m.Bounds.Bottom)-y);
            if(next.Width!=desktop.Width||next.Height!=desktop.Height||next.X!=desktop.X||next.Y!=desktop.Y||!overlay.Available)
            {
                desktop=next;
#if !UNITY_EDITOR
                Screen.SetResolution((int)desktop.Width,(int)desktop.Height,FullScreenMode.Windowed);
#endif
                view.orthographicSize=(float)desktop.Height*Unit/2;
                view.aspect=(float)(desktop.Width/desktop.Height);
                overlay.Configure(desktop);
            }
        }
        void Update()
        {
            if(geometry==null)return;
            try
            {
                if(geometry.RefreshIfNeeded(Time.unscaledTime)||!overlay.Available)Refresh();
                if(HandleDragging())return;
                bool active=state!=null&&state.active;
                if(follow&&active)
                {
                    DesktopGeometry.POINT cursor;
                    if(DesktopGeometry.GetCursorPos(out cursor))motion.WalkToCursor(cursor.X);
                }
                motion.Tick(Time.unscaledDeltaTime,active&&state.autonomous,!active||state.speaking,state==null ? .3 : state.frequency);
                if(!String.IsNullOrEmpty(pending))
                {
                    if(Time.unscaledTime>deadline) {motion.Stop();Finish(false,"Movement timed out");}
                    else if(motion.State==CharacterMotion.Recovering)Finish(false,motion.Message);
                    else if(pendingType=="wave"&&motion.State!=CharacterMotion.Waving)Finish(true,"Wave completed");
                    else if(pendingType!="wave"&&(motion.State==CharacterMotion.Idle||motion.State==CharacterMotion.Sitting||motion.State==CharacterMotion.Lying)&&!motion.Navigating&&!motion.SettlingPose)Finish(true,"Movement completed");
                }
                if(Time.unscaledTime-lastReport>2&&backend.Connected)
                {
                    lastReport=Time.unscaledTime;backend.ReportReady(ExecutorReady,"Motion="+motion.State+"; monitors="+geometry.Current.Monitors.Length+"; windows="+geometry.Current.Windows.Length+"; overlay="+overlay.Available+"; support="+motion.SupportId+"; "+motion.Message);
                    var supportInfo=geometry.Current.Surfaces.FirstOrDefault(s=>s.Id==motion.SupportId);
                    DesktopGeometry.POINT point;string pointed="";
                    if(DesktopGeometry.GetCursorPos(out point)) {var window=geometry.Current.Windows.FirstOrDefault(w=>w.Bounds.Contains(point.X,point.Y));if(window!=null)pointed=window.Id;}
                    backend.ReportScene(geometry.Current,supportInfo==null?"":supportInfo.WindowId,pointed);
                }
            }
            catch(Exception e) {motion.Stay();follow=false;Finish(false,"Desktop executor error");Debug.LogException(e);backend.ReportReady(false,e.Message);enabled=false;}
        }
        bool HandleDragging()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            DesktopGeometry.POINT pointer;
            if(!DesktopGeometry.GetCursorPos(out pointer))return false;
            bool down=(GetAsyncKeyState(1)&0x8000)!=0,alt=(GetAsyncKeyState(0x12)&0x8000)!=0;
            bool allowed=state==null||state.drag_enabled;
            bool hover=false;
            if(allowed&&avatarRenderers!=null)
                foreach(Renderer renderer in avatarRenderers)
                {
                    if(renderer==null||renderer is ParticleSystemRenderer||!renderer.enabled||!renderer.gameObject.activeInHierarchy)continue;
                    Bounds bounds=renderer.bounds;Vector3 lo=view.WorldToScreenPoint(bounds.min),hi=view.WorldToScreenPoint(bounds.max);
                    double x=pointer.X-desktop.X,y=desktop.Height-pointer.Y+desktop.Y;
                    if(x>=Math.Min(lo.x,hi.x)&&x<=Math.Max(lo.x,hi.x)&&y>=Math.Min(lo.y,hi.y)&&y<=Math.Max(lo.y,hi.y)) {hover=true;break;}
                }
            overlay.TemporaryInteractive=dragging||(allowed&&hover&&alt);
            if(!dragging&&allowed&&hover&&down&&!pointerWasDown&&(alt||!overlay.ClickThrough))
            {
                follow=false;Finish(false,"User picked up the avatar");motion.BeginDrag();dragging=true;
                dragOffset=new Vector2((float)(motion.X-pointer.X),(float)(motion.Y-pointer.Y));
            }
            if(dragging)
            {
                motion.DragTo(pointer.X+dragOffset.x,pointer.Y+dragOffset.y);
                if(!down||!allowed) {motion.EndDrag();dragging=false;overlay.TemporaryInteractive=false;}
            }
            pointerWasDown=down;return dragging;
#else
            return false;
#endif
        }
        void LateUpdate()
        {
            if(desktop.Height<=0)return;
            placement.position=new Vector3((float)(motion.X-desktop.X-desktop.Width/2)*Unit,(float)(desktop.Y+desktop.Height/2-motion.Y)*Unit,0);
            foreach(var entry in authoredScales)
                if(entry.Key!=null&&Vector3.SqrMagnitude(entry.Key.localScale-entry.Value)>.00000001f)
                {
                    if(Time.unscaledTime-lastScaleWarning>5){Debug.LogWarning("Blocked animated model/skeleton scale change on "+entry.Key.name+"; settings own avatar size");lastScaleWarning=Time.unscaledTime;}
                    entry.Key.localScale=entry.Value;
                }
            float scale=(float)size.Step(Time.unscaledDeltaTime);
            placement.localScale=Vector3.one*(320*Unit*scale/Mathf.Max(.1f,originalHeight));
            Transform leftFoot=rig.animator.GetBoneTransform(HumanBodyBones.LeftFoot),rightFoot=rig.animator.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform leftToe=rig.animator.GetBoneTransform(HumanBodyBones.LeftToes),rightToe=rig.animator.GetBoneTransform(HumanBodyBones.RightToes);
            if(leftFoot!=null&&rightFoot!=null)
            {
                float bottom=Mathf.Min(leftFoot.position.y,rightFoot.position.y);
                if(leftToe!=null)bottom=Mathf.Min(bottom,leftToe.position.y);
                if(rightToe!=null)bottom=Mathf.Min(bottom,rightToe.position.y);
                if(motion.State==CharacterMotion.Lying){Transform lyingHips=rig.animator.GetBoneTransform(HumanBodyBones.Hips);if(lyingHips!=null)bottom=Mathf.Min(bottom,lyingHips.position.y-.08f*rig.transform.lossyScale.y);}
                var support=motion.Desktop.Surfaces.FirstOrDefault(s=>s.Id==motion.SupportId);
                bool perched=motion.State==CharacterMotion.Sitting&&support!=null&&!String.IsNullOrEmpty(support.WindowId);
                Transform hips=rig.animator.GetBoneTransform(HumanBodyBones.Hips);
                if(perched&&hips!=null)bottom=hips.position.y;
                float localContact=(bottom-placement.position.y)/Mathf.Max(.001f,placement.lossyScale.y);
                if(!perched)localContact-=rig.SoleDepth*rig.transform.lossyScale.y/Mathf.Max(.001f,placement.lossyScale.y);
                string pose=perched?"Perched":motion.State==CharacterMotion.Lying?"Lying":motion.State==CharacterMotion.Sitting?"Sitting":"Standing";
                int bodyLayer=rig.animator.GetLayerIndex("YumiMotion");
                bool settled=motion.SupportId!=""&&!motion.SettlingPose&&(bodyLayer<0||!rig.animator.IsInTransition(bodyLayer));
                float offset=(float)contactAnchor.Sample(pose,localContact,settled,Time.unscaledDeltaTime);
                placement.position-=Vector3.up*offset*placement.lossyScale.y;
            }
            rig.Motion=motion.State.ToString();
            rig.TravelWorldSpeed=(float)Math.Abs(motion.VelocityX)*Unit;
            bool travelling=motion.State==CharacterMotion.Walking||motion.State==CharacterMotion.Running||motion.State==CharacterMotion.JumpStart||motion.State==CharacterMotion.JumpAirborne;
            if(travelling&&motion.Sidestepping)rig.Motion=motion.VelocityX>0?"SidestepLeft":"SidestepRight";
            if(travelling&&Math.Abs(motion.VelocityX)>1)travelYaw=motion.VelocityX>0?90:270;
            float desiredYaw=travelling&&!motion.Sidestepping?travelYaw:180;
            rig.transform.localRotation=Quaternion.RotateTowards(rig.transform.localRotation,Quaternion.Euler(0,desiredYaw,0),360*Time.unscaledDeltaTime);
            if(motion.State==CharacterMotion.StandingUp)rig.Motion="Idle";
            if(motion.State==CharacterMotion.JumpAirborne)rig.Motion="Jumping";
            if(motion.State==CharacterMotion.JumpStart)rig.Motion="Jumping";
            if(state!=null&&state.speaking&&motion.State==CharacterMotion.Idle)rig.Motion="Talking";
            if(motion.State==CharacterMotion.Dragging)rig.Motion="Idle";
            var currentSupport=motion.Desktop.Surfaces.FirstOrDefault(s=>s.Id==motion.SupportId);
            if(motion.State==CharacterMotion.Sitting&&currentSupport!=null&&!String.IsNullOrEmpty(currentSupport.WindowId))rig.Motion="SittingEdge";
            DesktopGeometry.POINT cursor;
            // The avatar root faces the camera at 180 degrees: positive local yaw
            // turns toward desktop left. Vertical pitch already follows correctly.
            rig.ReachForCursor=false;
            if(DesktopGeometry.GetCursorPos(out cursor))
            {
                bool watchingCaret=false;int attentionX,attentionY;
                if(state!=null&&state.typing_attention&&attention.Target(cursor.X,cursor.Y,Time.unscaledTime,out attentionX,out attentionY)){cursor.X=attentionX;cursor.Y=attentionY;watchingCaret=true;}
                if(Time.unscaledTime<lookUntil){cursor.X=(int)lookPoint.x;cursor.Y=(int)lookPoint.y;}
                Transform head=rig.animator.GetBoneTransform(HumanBodyBones.Head);
                Vector3 headPixel=view.WorldToScreenPoint(head==null?rig.transform.position:head.position);
                double headX=desktop.X+headPixel.x,headY=desktop.Y+desktop.Height-headPixel.y;
                rig.Gaze=travelling&&!motion.Sidestepping?Vector3.zero:new Vector3(Mathf.Atan2((float)(headX-cursor.X),120*scale)*Mathf.Rad2Deg,Mathf.Atan2((float)(cursor.Y-headY),150*scale)*Mathf.Rad2Deg,0);
                rig.ReachForCursor=state!=null&&state.cursor_reach&&!watchingCaret&&Time.unscaledTime>=lookUntil&&state!=null&&state.active&&!motion.Navigating&&(motion.State==CharacterMotion.Idle||motion.State==CharacterMotion.Sitting)&&Math.Abs(cursor.X-motion.X)<90*scale&&cursor.Y>motion.Y-300*scale&&cursor.Y<motion.Y-80*scale;
                float depth=view.WorldToScreenPoint(rig.transform.position).z;
                rig.ReachTarget=view.ScreenToWorldPoint(new Vector3((float)(cursor.X-desktop.X),(float)(desktop.Height-cursor.Y+desktop.Y),depth));
            }
        }
        void Action(string id,AvatarAction action)
        {
            if(dragging) {backend.CompleteAction(id,false,"Release the avatar before issuing movement commands");return;}
            if(action.type!="set_expression") {Finish(false,"Superseded by user command");follow=false;}
            if(state==null||!state.active) {backend.CompleteAction(id,false,"Start Yumi before moving the avatar");return;}
            bool accepted=false;string message="Action is unavailable for this rig";
            switch(action.type)
            {
                case "stop_movement":case "stay":motion.Stay();backend.CompleteAction(id,true,motion.SupportId==""?"Route stopped; finishing a safe landing":"Movement stopped");return;
                case "reset_avatar":size.ResetProximity();motion.Recover();backend.CompleteAction(id,true,motion.Message);return;
                case "set_expression":bool supported=rig.PreviewExpression(action.expression);backend.CompleteAction(id,supported,supported?"Expression preview started (5 seconds)":"Unsupported expression");return;
                case "stand":motion.Stand();accepted=true;break;
                case "lie_down":motion.Lie();accepted=motion.State==CharacterMotion.Lying||motion.State==CharacterMotion.Sitting;break;
                case "resume_roaming":motion.ResumeRoaming();backend.CompleteAction(id,true,"Autonomous movement resumed");return;
                case "come_closer":case "step_back":size.Nearer(action.type=="come_closer");backend.CompleteAction(id,true,"Appearance scale target adjusted");return;
                case "look_at":
                    var window=motion.Desktop.Windows.FirstOrDefault(w=>w.Id==action.target);
                    if(window==null){backend.CompleteAction(id,false,"Target window is not visible");return;}
                    lookPoint=new Vector2((float)(window.Bounds.X+window.Bounds.Width/2),(float)(window.Bounds.Y+window.Bounds.Height/2));lookUntil=Time.unscaledTime+8;backend.CompleteAction(id,true,"Looking toward the window for eight seconds");return;
                case "wave":motion.Wave();accepted=motion.State==CharacterMotion.Waving;break;
                case "sit":
                    if(action.target=="cursor_window") {DesktopGeometry.POINT seat;if(DesktopGeometry.GetCursorPos(out seat))accepted=motion.RequestWindowAt(seat.X,seat.Y,true);break;}
                    if((String.IsNullOrEmpty(action.target)||action.target=="cursor")) {motion.Sit();backend.CompleteAction(id,motion.State==CharacterMotion.Sitting,"Sit requested on current support");return;}
                    accepted=motion.RequestWindow(action.target,true);break;
                case "move_to_window":case "jump_to_platform":
                    if(action.target=="cursor") {DesktopGeometry.POINT targetPoint;if(DesktopGeometry.GetCursorPos(out targetPoint))accepted=motion.RequestWindowAt(targetPoint.X,targetPoint.Y);}
                    else accepted=motion.RequestWindow(action.target);break;
                case "follow_cursor":follow=true;motion.Stationary=false;backend.CompleteAction(id,true,"Following cursor along current supporting edge; stop to cancel");return;
                case "other_monitor":
                    var current=motion.Desktop.Surfaces.FirstOrDefault(s=>s.Id==motion.SupportId);
                    var floors=motion.Desktop.Surfaces.Where(s=>s.Id.StartsWith("floor:")&&(current==null||s.MonitorId!=current.MonitorId));
                    if(action.target=="left")floors=floors.Where(s=>(s.Left+s.Right)/2<motion.X).OrderByDescending(s=>s.Right);
                    else if(action.target=="right")floors=floors.Where(s=>(s.Left+s.Right)/2>motion.X).OrderBy(s=>s.Left);
                    else floors=floors.OrderBy(s=>Math.Abs((s.Left+s.Right)/2-motion.X));
                    var destination=floors.FirstOrDefault();
                    if(action.target=="next")
                    {
                        var ordered=motion.Desktop.Surfaces.Where(s=>s.Id.StartsWith("floor:")).OrderBy(s=>s.Left).ThenBy(s=>s.Y).ToArray();
                        int index=Array.FindIndex(ordered,s=>current!=null&&s.MonitorId==current.MonitorId);
                        destination=ordered.Length>1?ordered[(index+1)%ordered.Length]:null;
                    }
                    if(destination==null) {backend.CompleteAction(id,false,"No other monitor in that direction. Detected monitors: "+motion.Desktop.Monitors.Length);return;}
                    accepted=motion.RequestSurface(destination.Id);break;
                case "walk_to_position":case "run_to_position":
                    DesktopGeometry.POINT point;if(DesktopGeometry.GetCursorPos(out point))accepted=motion.WalkToCursor(point.X,action.type=="run_to_position");break;
                default:backend.CompleteAction(id,false,message);return;
            }
            if(!accepted) {backend.CompleteAction(id,false,String.IsNullOrEmpty(motion.Message)?message:motion.Message);return;}
            pending=id;pendingType=action.type;deadline=Time.unscaledTime+45;
        }
        void Finish(bool success,string message)
        {if(pending!="")backend.CompleteAction(pending,success,message);pending="";pendingType="";}
        void OnDestroy()
        {if(backend!=null){backend.StateReceived-=Receive;backend.ActionReceived-=Action;backend.Disconnected-=Disconnect;backend.ConnectedToBackend-=Ready;}if(geometry!=null)geometry.Dispose();}
    }
}
