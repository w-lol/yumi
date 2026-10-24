using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yumi.Desktop
{
    [Serializable] public class FaceBinding
    {
        public SkinnedMeshRenderer mesh;
        public string aa="VRC.v_aa",ih="VRC.v_ih",ou="VRC.v_ou",blink="blink",smile="smile",squint="jitome";
    }
    [Serializable] public class MotionClip { public string state;public AnimationClip clip; }
    public sealed class YumiAvatarRig : MonoBehaviour
    {
        public Animator animator;
        public FaceBinding face=new FaceBinding();
        public MotionClip[] clips=new MotionClip[0];
        public string Diagnostic { get; private set; }
        public bool Ready {get {return animator!=null&&animator.isHuman&&face.mesh!=null&&face.mesh.sharedMesh!=null;} }
        public string Motion="Idle";
        public Vector3 Gaze;
        public float MotionSpeed=1;
        public float TravelWorldSpeed;
        public float SoleDepth=.035f;
        public bool ReachForCursor;
        public bool IdleGestures;
        public Vector3 ReachTarget;
        float reachBlend;
        readonly ReachLatch reachChoice=new ReachLatch();
        int idleArmLayer=-1;
        float nextArmGesture,armGestureUntil,armLayerWeight;
        public float MaximumHeadYaw=70,MaximumHeadPitch=35;
        float gazeYaw,gazePitch;
        readonly PlaybackTimeline timeline=new PlaybackTimeline();
        readonly Dictionary<Transform,Quaternion> restore=new Dictionary<Transform,Quaternion>();
        readonly Dictionary<string,int> shapes=new Dictionary<string,int>();
        float nextBlink,blinkStart=-10,smile,phase,lookTimer,yaw,pitch,targetYaw,targetPitch,previewUntil;
        string preview="neutral";
        string currentClip="";
        System.Random random=new System.Random();
        static long Now { get {return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();} }
        void Start()
        {
            if(!Ready) {Diagnostic="Missing humanoid animator or face mesh";enabled=false;return;}
            foreach(string name in new[]{face.aa,face.ih,face.ou,face.blink,face.smile,face.squint})
                if(!String.IsNullOrEmpty(name))shapes[name]=face.mesh.sharedMesh.GetBlendShapeIndex(name);
            Diagnostic="Playback mouth estimates; original rig; unverified Windows appearance";
            nextBlink=Time.unscaledTime+2+(float)random.NextDouble()*4;
            idleArmLayer=animator.GetLayerIndex("YumiIdleArms");nextArmGesture=Time.unscaledTime+8;
            animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        }
        public void State(AvatarState state)
        {
            timeline.Push(new PlaybackFrame {Epoch=state.audio_epoch,Due=state.mouth_time_ms,Speaking=state.speaking,
                Aa=state.mouth==null?0:state.mouth.aa,Ih=state.mouth==null?0:state.mouth.ih,
                Ou=state.mouth==null?0:state.mouth.ou,Style=state.style},Now);
        }
        public bool PreviewExpression(string expression)
        {
            switch(expression)
            {
                case "neutral":case "happy":case "curious":case "mock-annoyed":preview=expression;previewUntil=Time.unscaledTime+5;return true;
                default:return false;
            }
        }
        public void ResetSpeech() {timeline.Clear();Set(face.aa,0);Set(face.ih,0);Set(face.ou,0);}
        void Update()
        {
            // Restore last frame's additive offsets before Animator evaluates this frame.
            foreach(var item in restore)if(item.Key!=null)item.Key.localRotation=item.Value;
            restore.Clear();
            if(!Ready)return;
            string selected=Motion=="Talking"?"Idle":Motion;
            if(selected!=currentClip&&HasClip(selected))
            {
                bool resting=selected=="Sitting"||selected=="SittingEdge"||selected=="Lying"||currentClip=="Sitting"||currentClip=="SittingEdge"||currentClip=="Lying";
                animator.CrossFadeInFixedTime("YumiMotion."+selected,selected=="Lying"||currentClip=="Lying"?1.35f:resting?1.1f:selected=="Jumping"?.18f:.35f);currentClip=selected;
            }
            float strideRate=1;
            if(Motion=="Walking"||Motion=="Running"||Motion.StartsWith("Sidestep"))
                foreach(var entry in clips)
                    if(entry.state==Motion&&entry.clip!=null&&entry.clip.averageSpeed.magnitude>.05f)
                    {strideRate=Mathf.Clamp(TravelWorldSpeed/Mathf.Max(.01f,entry.clip.averageSpeed.magnitude*transform.lossyScale.x),.45f,2);break;}
            animator.speed=Mathf.Clamp(MotionSpeed*strideRate,.3f,2.5f);
        }
        bool HasClip(string state) {foreach(var c in clips)if(c.state==state&&c.clip!=null)return true;return false;}
        void LateUpdate()
        {
            if(!Ready)return;
            float now=Time.unscaledTime,dt=Time.unscaledDeltaTime;
            if(now>nextArmGesture){armGestureUntil=now+3;nextArmGesture=now+12+(float)random.NextDouble()*12;}
            if(idleArmLayer>=0)
            {
                float targetWeight=IdleGestures&&(Motion=="Idle"||Motion=="Talking")&&!ReachForCursor&&now<armGestureUntil?.55f:0;
                armLayerWeight=Mathf.MoveTowards(armLayerWeight,targetWeight,dt*1.5f);animator.SetLayerWeight(idleArmLayer,armLayerWeight);
            }
            PlaybackFrame frame=timeline.Sample(Now);
            float mix=1-Mathf.Exp(-dt*25);
            Smooth(face.aa,frame.Aa*100,mix);Smooth(face.ih,frame.Ih*100,mix);Smooth(face.ou,frame.Ou*100,mix);
            if(now>=nextBlink) {blinkStart=now;nextBlink=now+3+(float)random.NextDouble()*5;}
            float blink=Mathf.Clamp01(1-Mathf.Abs((now-blinkStart)/.16f-1));
            Set(face.blink,blink*100);
            string style=now<previewUntil?(preview=="happy"?"playful":preview):frame.Style;
            float target=(frame.Speaking||now<previewUntil)&&(style=="excited"||style=="playful"||style=="amused")?35:0;
            smile=Mathf.Lerp(smile,target,1-Mathf.Exp(-dt*4));Set(face.smile,smile);
            Set(face.squint,(frame.Speaking||now<previewUntil)&&style=="mock-annoyed"?20:0);
            if(now>lookTimer) {lookTimer=now+1.5f+(float)random.NextDouble()*3; targetYaw=(float)(random.NextDouble()-.5)*10;targetPitch=(float)(random.NextDouble()-.5)*5;}
            float gazeMix=1-Mathf.Exp(-dt*3);yaw=Mathf.Lerp(yaw,targetYaw,gazeMix);pitch=Mathf.Lerp(pitch,targetPitch,gazeMix);
            Vector3 gaze=Gaze;
            gazeYaw=Mathf.LerpAngle(gazeYaw,Mathf.Clamp(gaze.x+yaw,-MaximumHeadYaw,MaximumHeadYaw),1-Mathf.Exp(-dt*7));
            gazePitch=Mathf.LerpAngle(gazePitch,Mathf.Clamp(gaze.y+pitch,-MaximumHeadPitch,MaximumHeadPitch),1-Mathf.Exp(-dt*7));
            Add(HumanBodyBones.Neck,Quaternion.Euler(gazePitch*.22f,gazeYaw*.22f,0));
            Add(HumanBodyBones.Head,Quaternion.Euler(gazePitch*.78f,gazeYaw*.78f,style=="curious"?6:0));
            Add(HumanBodyBones.LeftEye,Quaternion.Euler(Mathf.Clamp(gazePitch*.35f,-12,12),Mathf.Clamp(gazeYaw*.35f,-18,18),0));
            Add(HumanBodyBones.RightEye,Quaternion.Euler(Mathf.Clamp(gazePitch*.35f,-12,12),Mathf.Clamp(gazeYaw*.35f,-18,18),0));
            phase+=dt*Mathf.Clamp(MotionSpeed,.3f,2);
            Add(HumanBodyBones.Chest,Quaternion.Euler(Mathf.Sin(phase*1.5f)*.6f,0,0));
            if(!HasClip(Motion)&&(Motion=="Walking"||Motion=="Running"))
            {
                float stride=Mathf.Sin(phase*(Motion=="Running"?11:7)),amount=Motion=="Running"?24:15;
                Add(HumanBodyBones.LeftUpperLeg,Quaternion.Euler(stride*amount,0,0));
                Add(HumanBodyBones.RightUpperLeg,Quaternion.Euler(-stride*amount,0,0));
                Add(HumanBodyBones.LeftLowerLeg,Quaternion.Euler(Mathf.Max(0,-stride)*25,0,0));
                Add(HumanBodyBones.RightLowerLeg,Quaternion.Euler(Mathf.Max(0,stride)*25,0,0));
                Add(HumanBodyBones.LeftUpperArm,Quaternion.Euler(-stride*10,0,0));
                Add(HumanBodyBones.RightUpperArm,Quaternion.Euler(stride*10,0,0));
            }
            if(Motion=="Waving"&&!HasClip("Waving"))
            {Add(HumanBodyBones.RightUpperArm,Quaternion.Euler(0,0,65));Add(HumanBodyBones.RightLowerArm,Quaternion.Euler(0,0,40+Mathf.Sin(phase*9)*15));}
            if(Motion=="SittingEdge")
            {
                Add(HumanBodyBones.LeftLowerLeg,Quaternion.Euler(Mathf.Sin(phase*2.2f)*4,0,0));
                Add(HumanBodyBones.RightLowerLeg,Quaternion.Euler(Mathf.Sin(phase*2.2f+.8f)*4,0,0));
            }
            if(Motion=="Sidestepping")
            {
                float step=Mathf.Sin(phase*7);
                Add(HumanBodyBones.LeftUpperLeg,Quaternion.Euler(0,0,step*12));
                Add(HumanBodyBones.RightUpperLeg,Quaternion.Euler(0,0,-step*12));
                Add(HumanBodyBones.LeftLowerLeg,Quaternion.Euler(Mathf.Max(0,-step)*12,0,0));
                Add(HumanBodyBones.RightLowerLeg,Quaternion.Euler(Mathf.Max(0,step)*12,0,0));
            }
            reachBlend=Mathf.MoveTowards(reachBlend,ReachForCursor?1:0,dt*3);
            float leftRatio=ReachRatio(false),rightRatio=ReachRatio(true);
            reachChoice.Tick(ReachForCursor,rightRatio<leftRatio,leftRatio,rightRatio,dt);
            if(reachBlend>0)
            {
                ReachArm(reachChoice.Right,reachBlend);
                if(reachChoice.Both)ReachArm(!reachChoice.Right,reachBlend);
            }
        }
        float ReachRatio(bool right)
        {
            Transform arm=animator.GetBoneTransform(right?HumanBodyBones.RightUpperArm:HumanBodyBones.LeftUpperArm),elbow=animator.GetBoneTransform(right?HumanBodyBones.RightLowerArm:HumanBodyBones.LeftLowerArm),hand=animator.GetBoneTransform(right?HumanBodyBones.RightHand:HumanBodyBones.LeftHand);
            return arm==null||elbow==null||hand==null?100:Vector3.Distance(ReachTarget,arm.position)/Mathf.Max(.001f,Vector3.Distance(arm.position,elbow.position)+Vector3.Distance(elbow.position,hand.position));
        }
        void ReachArm(bool right,float weight)
        {
                Transform arm=animator.GetBoneTransform(right?HumanBodyBones.RightUpperArm:HumanBodyBones.LeftUpperArm);
                Transform elbow=animator.GetBoneTransform(right?HumanBodyBones.RightLowerArm:HumanBodyBones.LeftLowerArm);
                Transform hand=animator.GetBoneTransform(right?HumanBodyBones.RightHand:HumanBodyBones.LeftHand);
                if(arm!=null&&elbow!=null&&hand!=null&&arm.parent!=null)
                {
                    Transform finger=animator.GetBoneTransform(right?HumanBodyBones.RightMiddleDistal:HumanBodyBones.LeftMiddleDistal);
                    float palmLength=finger==null?Vector3.Distance(hand.position,elbow.position)*.3f:Vector3.Distance(finger.position,hand.position);
                    Vector3 aim=(ReachTarget-arm.position).normalized;
                    Vector3 wristTarget=ReachTarget-aim*palmLength*.85f;
                    Vector3 current=elbow.position-arm.position,reachDirection=wristTarget-arm.position;
                    double distance,along,bend;
                    if(reachDirection.sqrMagnitude>.0001f&&TwoBoneReach.Solve(current.magnitude,(hand.position-elbow.position).magnitude,reachDirection.magnitude,out distance,out along,out bend))
                    {
                        Vector3 direction=reachDirection.normalized;
                        Vector3 pole=animator.transform.TransformDirection(new Vector3(right?1:-1,-.6f,.4f));
                        pole=Vector3.ProjectOnPlane(pole,direction).normalized;
                        if(pole.sqrMagnitude<.001f)pole=Vector3.Cross(direction,Vector3.up).normalized;
                        Vector3 elbowTarget=arm.position+direction*(float)along+pole*(float)bend;
                        Vector3 handTarget=arm.position+direction*(float)distance;
                        Quaternion desired=Quaternion.FromToRotation(current,elbowTarget-arm.position)*arm.rotation;
                        Quaternion local=Quaternion.Inverse(arm.parent.rotation)*desired;
                        Quaternion offset=Quaternion.Inverse(arm.localRotation)*local;
                        Add(right?HumanBodyBones.RightUpperArm:HumanBodyBones.LeftUpperArm,Quaternion.Slerp(Quaternion.identity,offset,weight));
                        Quaternion elbowDesired=Quaternion.FromToRotation(hand.position-elbow.position,handTarget-elbow.position)*elbow.rotation;
                        Quaternion elbowLocal=Quaternion.Inverse(elbow.parent.rotation)*elbowDesired;
                        Add(right?HumanBodyBones.RightLowerArm:HumanBodyBones.LeftLowerArm,Quaternion.Slerp(Quaternion.identity,Quaternion.Inverse(elbow.localRotation)*elbowLocal,weight));
                        Transform proximal=animator.GetBoneTransform(right?HumanBodyBones.RightMiddleProximal:HumanBodyBones.LeftMiddleProximal);
                        if(proximal!=null&&hand.parent!=null)
                        {
                            Quaternion palm=Quaternion.FromToRotation(proximal.position-hand.position,ReachTarget-hand.position)*hand.rotation;
                            Quaternion localPalm=Quaternion.Inverse(hand.parent.rotation)*palm;
                            Add(right?HumanBodyBones.RightHand:HumanBodyBones.LeftHand,Quaternion.Slerp(Quaternion.identity,Quaternion.Inverse(hand.localRotation)*localPalm,weight));
                        }
                    }
                }
        }
        void Add(HumanBodyBones bone,Quaternion offset)
        {Transform t=animator.GetBoneTransform(bone);if(t==null)return;if(!restore.ContainsKey(t))restore[t]=t.localRotation;t.localRotation=t.localRotation*offset;}
        void Set(string name,float value) {int i;if(name!=null&&shapes.TryGetValue(name,out i)&&i>=0)face.mesh.SetBlendShapeWeight(i,value);}
        void Smooth(string name,float value,float amount) {int i;if(name!=null&&shapes.TryGetValue(name,out i)&&i>=0)Set(name,Mathf.Lerp(face.mesh.GetBlendShapeWeight(i),value,amount));}
        void OnDisable() {if(face.mesh!=null)ResetSpeech();foreach(var p in restore)if(p.Key!=null)p.Key.localRotation=p.Value;restore.Clear();}
    }
}
