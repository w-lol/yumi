using System;
using System.Linq;
using UnityEngine;

namespace Yumi.Desktop
{
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
        AvatarState state;
        DesktopRect desktop;
        string pending="",pendingType="";
        float deadline,lastReport;
        bool lastStationary=true,lastActive,follow;
        const float Unit=.005f;
        void Start()
        {
            Application.runInBackground=true;QualitySettings.vSyncCount=0;
            Application.targetFrameRate=frameRate;
            if(backend==null||rig==null||view==null||placement==null) {Debug.LogError("Incomplete Yumi desktop scene");enabled=false;return;}
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
        void Ready() {backend.ReportReady(rig.Ready&&geometry!=null,"Rig/executor loaded; Windows appearance requires acceptance");}
        void Receive(AvatarState value)
        {
            state=value;rig.State(value);
            overlay.Topmost=value.topmost;overlay.ClickThrough=value.clickthrough;
            placement.gameObject.SetActive(value.visible);
            Application.targetFrameRate=Mathf.Clamp(value.frame_rate,15,60);
            rig.MotionSpeed=Mathf.Clamp(value.animation_speed,.3f,2);
            motion.Speed=Math.Max(40,Math.Min(500,value.movement_speed));
            motion.JumpHeight=Math.Max(30,Math.Min(400,value.jump_height));
            if(!value.active) {motion.Stay();follow=false;Finish(false,"Backend paused; movement stopped");}
            else if(value.stationary!=lastStationary||!lastActive) {motion.Stationary=value.stationary;if(value.stationary){motion.Stay();follow=false;Finish(false,"Stationary mode enabled");}}
            lastStationary=value.stationary;lastActive=value.active;
        }
        void Disconnect()
        {state=null;follow=false;motion.Stay();rig.ResetSpeech();pending="";rig.Motion="Idle";}
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
                    else if(pendingType!="wave"&&(motion.State==CharacterMotion.Idle||motion.State==CharacterMotion.Sitting)&&!motion.Navigating)Finish(true,"Movement completed");
                }
                if(Time.unscaledTime-lastReport>2&&backend.Connected)
                {lastReport=Time.unscaledTime;backend.ReportReady(rig.Ready,"Motion="+motion.State+"; support="+motion.SupportId+"; "+motion.Message);}
            }
            catch(Exception e) {motion.Stay();follow=false;Finish(false,"Desktop executor error");Debug.LogException(e);backend.ReportReady(false,e.Message);enabled=false;}
        }
        void LateUpdate()
        {
            if(desktop.Height<=0)return;
            placement.position=new Vector3((float)(motion.X-desktop.X-desktop.Width/2)*Unit,(float)(desktop.Y+desktop.Height/2-motion.Y)*Unit,0);
            float scale=state==null?1:Mathf.Clamp(state.scale,.25f,3);
            placement.localScale=Vector3.one*(320*Unit*scale/Mathf.Max(.1f,originalHeight));
            rig.Motion=motion.State.ToString();
            if(motion.State==CharacterMotion.JumpAirborne)rig.Motion="Jumping";
            if(motion.State==CharacterMotion.JumpStart)rig.Motion="Jumping";
            if(state!=null&&state.speaking&&motion.State==CharacterMotion.Idle)rig.Motion="Talking";
            DesktopGeometry.POINT cursor;
            if(DesktopGeometry.GetCursorPos(out cursor))rig.Gaze=new Vector3(Mathf.Clamp((float)(cursor.X-motion.X)/60,-12,12),Mathf.Clamp((float)(cursor.Y-motion.Y+220)/80,-8,8),0);
        }
        void Action(string id,AvatarAction action)
        {
            if(action.type!="set_expression") {Finish(false,"Superseded by user command");follow=false;}
            if(state==null||!state.active) {backend.CompleteAction(id,false,"Start Yumi before moving the avatar");return;}
            bool accepted=false;string message="Action is unavailable for this rig";
            switch(action.type)
            {
                case "stop_movement":case "stay":motion.Stay();backend.CompleteAction(id,true,"Movement stopped");return;
                case "reset_avatar":motion.Recover();backend.CompleteAction(id,true,motion.Message);return;
                case "set_expression":bool supported=rig.PreviewExpression(action.expression);backend.CompleteAction(id,supported,supported?"Expression preview started (5 seconds)":"Unsupported expression");return;
                case "stand":motion.Stop();backend.CompleteAction(id,true,"Standing");return;
                case "wave":motion.Wave();accepted=motion.State==CharacterMotion.Waving;break;
                case "sit":
                    if((String.IsNullOrEmpty(action.target)||action.target=="cursor")) {motion.Sit();backend.CompleteAction(id,motion.State==CharacterMotion.Sitting,"Sit requested on current support");return;}
                    accepted=motion.RequestWindow(action.target,true);break;
                case "move_to_window":case "jump_to_platform":accepted=motion.RequestWindow(action.target);break;
                case "follow_cursor":follow=true;motion.Stationary=false;backend.CompleteAction(id,true,"Following cursor along current supporting edge; stop to cancel");return;
                case "other_monitor":
                    var current=motion.Desktop.Surfaces.FirstOrDefault(s=>s.Id==motion.SupportId);
                    var destination=motion.Desktop.Surfaces.FirstOrDefault(s=>s.Id.StartsWith("floor:")&&(current==null||s.MonitorId!=current.MonitorId));
                    accepted=destination!=null&&motion.RequestSurface(destination.Id);break;
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
