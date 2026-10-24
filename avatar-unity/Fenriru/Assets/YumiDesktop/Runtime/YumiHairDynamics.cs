using System;
using UnityEngine;

namespace Yumi.Desktop
{
    [Serializable] public class HairLink
    {
        public Transform bone;
        public Vector3 localTip;
        [NonSerialized] public Quaternion rest,animated;
        [NonSerialized] public Vector3 current,previous,anchor;
    }

    // Locally simulated spring chains bound to original hair bones. No SDK runtime.
    [DefaultExecutionOrder(200)]
    public sealed class YumiHairDynamics:MonoBehaviour
    {
        public HairLink[] links=new HairLink[0];
        public Animator animator;
        public bool SimulationEnabled=false;
        public float Damping=.06f,Spring=60,Gravity=.6f;
        float accumulator;
        bool initialized,applied;
        Transform head,neck,hips;
        float headRadius;
        void Start()
        {
            if(animator==null){enabled=false;return;}
            head=animator.GetBoneTransform(HumanBodyBones.Head);neck=animator.GetBoneTransform(HumanBodyBones.Neck);hips=animator.GetBoneTransform(HumanBodyBones.Hips);
            Transform left=animator.GetBoneTransform(HumanBodyBones.LeftEye),right=animator.GetBoneTransform(HumanBodyBones.RightEye);
            headRadius=left!=null&&right!=null?Vector3.Distance(left.position,right.position)*1.9f/Mathf.Max(.01f,transform.lossyScale.x):.12f;
            foreach(var link in links)
                if(link.bone!=null){link.rest=link.animated=link.bone.localRotation;link.current=link.previous=link.bone.TransformPoint(link.localTip);link.anchor=link.bone.position;}
            initialized=true;
            Debug.Log("Standalone hair dynamics bound to "+links.Length+" original bone links");
        }
        void Update()
        {
            // Remove last frame's simulation before Animator updates the original pose.
            if(applied)foreach(var link in links)if(link.bone!=null)link.bone.localRotation=link.animated;
            applied=false;
        }
        void LateUpdate()
        {
            if(!SimulationEnabled){ResetParticles();return;}
            applied=true;
            foreach(var link in links)if(link.bone!=null)link.animated=link.bone.localRotation;
            accumulator=Mathf.Min(.08f,accumulator+Time.unscaledDeltaTime);
            const float step=1f/90;
            while(accumulator>=step)
            {
                accumulator-=step;
                foreach(var link in links)
                {
                    if(link.bone==null||!link.bone.gameObject.activeInHierarchy)continue;
                    Quaternion basis=link.bone.parent==null?link.animated:link.bone.parent.rotation*link.animated;
                    Vector3 restTip=link.bone.position+basis*Vector3.Scale(link.localTip,link.bone.lossyScale);
                    float length=Vector3.Distance(restTip,link.bone.position);
                    if(length<.0001f)continue;
                    float scale=Mathf.Max(.01f,transform.lossyScale.x);
                    if(Vector3.Distance(link.anchor,link.bone.position)>Mathf.Max(length*4,.25f*scale))link.current=link.previous=restTip;
                    link.anchor=link.bone.position;
                    Vector3 next=link.current+(link.current-link.previous)*(1-Mathf.Clamp01(Damping))+
                        ((restTip-link.current)*Mathf.Clamp(Spring,5,200)+Vector3.down*Gravity*scale)*(step*step);
                    for(int pass=0;pass<3;pass++)
                    {
                        next=link.bone.position+(next-link.bone.position).normalized*length;
                        if(head!=null)next=OutsideSphere(next,head.position,headRadius*scale);
                        if(neck!=null&&hips!=null)
                        {
                            Vector3 line=hips.position-neck.position;
                            float t=line.sqrMagnitude<.00001f?0:Mathf.Clamp01(Vector3.Dot(next-neck.position,line)/line.sqrMagnitude);
                            next=OutsideSphere(next,neck.position+line*t,.10f*scale);
                        }
                    }
                    Vector3 expected=restTip-link.bone.position,desired=next-link.bone.position;
                    if(expected.sqrMagnitude>.000001f&&desired.sqrMagnitude>.000001f)link.bone.rotation=Quaternion.FromToRotation(expected,desired)*basis;
                    link.previous=link.current;link.current=link.bone.TransformPoint(link.localTip);
                }
            }
        }
        static Vector3 OutsideSphere(Vector3 point,Vector3 center,float radius)
        {
            Vector3 delta=point-center;
            return delta.sqrMagnitude<radius*radius?center+(delta.sqrMagnitude>.000001f?delta.normalized:Vector3.forward)*radius:point;
        }
        void ResetParticles()
        {
            accumulator=0;
            foreach(var link in links)if(link.bone!=null){link.current=link.previous=link.bone.TransformPoint(link.localTip);link.anchor=link.bone.position;}
        }
        void OnDisable(){if(initialized&&applied)foreach(var link in links)if(link.bone!=null)link.bone.localRotation=link.animated;}
    }
}
