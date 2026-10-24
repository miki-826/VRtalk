using System.Collections.Generic;
using UnityEngine;

namespace QuestAI
{
    public class BodySystem : MonoBehaviour
    {
        public CharacterRigBinding rig;
        public Transform user;
        public AudioSource voice;
        public float lookStrength = 1;
        // World point used when the decision's gaze is "object" (set by the brain from the scene model).
        public Vector3? lookPoint;
        public enum Pose { stand, sit, crouch, point }
        public Pose CurrentPose { get; private set; }
        // Height of the hips above the root in the bind pose; seats lower the root by this much.
        public float HipHeight { get; private set; } = .45f;
        Vector3? poseTarget; float poseWeight; Vector3 hipsRest, hipsWritten, hipsBase;
        readonly Dictionary<Transform,Quaternion> rest = new Dictionary<Transform,Quaternion>();
        BehaviorDecision decision = new BehaviorDecision();
        float gestureTime, nextBlink, blinkStarted = -10, mouth, gazeYaw, gazePitch, walkPhase;
        readonly float[] audio = new float[256];
        static readonly string[] EmotionNames=System.Enum.GetNames(typeof(Emotion));
        CharacterLocomotion locomotion; ClipAnimator clips; bool clipGesture;
        void Awake()
        {
            if (!rig) rig = GetComponent<CharacterRigBinding>();
            if (!rig) return;
            clips = GetComponent<ClipAnimator>();
            if(rig.animator&&rig.animator.isHuman&&!rig.hips)rig.hips=rig.animator.GetBoneTransform(HumanBodyBones.Hips);
            if(rig.hips){hipsRest=rig.hips.localPosition;HipHeight=Mathf.Max(.1f,rig.hips.position.y-transform.position.y);}
            if(rig.animator&&rig.animator.isHuman&&!rig.leftLeg)
            {
                rig.leftLeg=rig.animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);rig.rightLeg=rig.animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                rig.leftKnee=rig.animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);rig.rightKnee=rig.animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            }
            // Models without clips use procedural poses; an empty Humanoid controller must not retarget the bind pose.
            if(rig.animator&&!clips)rig.animator.enabled=false;
            foreach(var bone in new[]{rig.head,rig.chest,rig.leftArm,rig.rightArm,rig.leftForearm,rig.rightForearm,rig.leftLeg,rig.rightLeg,rig.leftKnee,rig.rightKnee}) if(bone && !rest.ContainsKey(bone)) rest.Add(bone,bone.localRotation);
            nextBlink=Time.time+Random.Range(2f,5f);
            var mouthObject=new GameObject("Spatial voice");mouthObject.transform.SetParent(rig.head?rig.head:transform,false);
            voice=mouthObject.AddComponent<AudioSource>();voice.playOnAwake=false;voice.spatialBlend=1;voice.minDistance=.5f;voice.maxDistance=12;voice.rolloffMode=AudioRolloffMode.Logarithmic;voice.spatialize=true;
            mouthObject.AddComponent<MetaXRAudioSource>();
        }
        public void Apply(BehaviorDecision value)
        {
            decision=value;gestureTime=Time.time;
            clipGesture=clips&&clips.Play(ClipFor(value.gesture));
        }
        // Authored clips stand in for the procedural gestures where a model has a fitting one.
        static string ClipFor(Gesture gesture)
        {
            switch(gesture){case Gesture.wave:return "Wave";case Gesture.nod:case Gesture.big_nod:return "Yes";case Gesture.shrug:return "No";case Gesture.laugh:return "Jump";default:return null;}
        }
        public void ResetPose() { decision=new BehaviorDecision();mouth=0;StopSpeech();SetPose(Pose.stand,null); }
        public void SetPose(Pose pose,Vector3? target){CurrentPose=pose;poseTarget=target;}
        public void Play(SpeechAudio data,float volume)
        {
            StopSpeech(); var clip=AudioClip.Create("Generated speech",data.samples.Length,1,data.sampleRate,false);clip.SetData(data.samples,0);voice.clip=clip;voice.volume=volume;voice.Play();
        }
        public void StopSpeech() { if (!voice)return;voice.Stop();if(voice.clip){var clip=voice.clip;voice.clip=null;Destroy(clip);} }
        public bool IsSpeaking => voice && voice.isPlaying;
        void PoseBone(Transform bone,Vector3 euler)
        {
            if(!bone)return;
            // Clip-driven bones already hold this frame's animated pose; procedural ones restart from the bind pose.
            if(clips)bone.localRotation*=Quaternion.Euler(euler);else if(rest.TryGetValue(bone,out var q))bone.localRotation=q*Quaternion.Euler(euler);
        }
        // Swings a limb about the character's own right axis, independent of how the bone's local axes are oriented.
        void Swing(Transform bone,float angle){if(bone)bone.rotation=Quaternion.AngleAxis(angle,transform.right)*bone.rotation;}
        void LateUpdate()
        {
            if(!rig)return;
            if(!locomotion)locomotion=GetComponent<CharacterLocomotion>();
            float t=Time.time, age=t-gestureTime, envelope=Mathf.Sin(Mathf.Clamp01(age/2.4f)*Mathf.PI), walk=locomotion?locomotion.Speed01:0;
            if(clips){clips.SetSpeed(walk);clips.SetPace(locomotion?locomotion.PaceScale:1);}
            poseWeight=Mathf.MoveTowards(poseWeight,CurrentPose==Pose.stand||walk>.3f?0:1,Time.deltaTime/.45f);
            // Undo last frame's crouch offset unless a clip already rewrote the hips this frame.
            if(rig.hips){if(!clips)rig.hips.localPosition=hipsRest;else if(rig.hips.localPosition==hipsWritten)rig.hips.localPosition=hipsBase;hipsBase=rig.hips.localPosition;}
            float yaw=0,pitch=0;
            Vector3? focus=decision.gaze==Gaze.@object&&lookPoint.HasValue?lookPoint:user?user.position:(Vector3?)null;
            if(focus.HasValue && rig.head && decision.gaze!=Gaze.away)
            {
                Vector3 local=transform.InverseTransformDirection(focus.Value-rig.head.position);
                yaw=Mathf.Clamp(Mathf.Atan2(local.x,local.z)*Mathf.Rad2Deg,-45,45);
                pitch=Mathf.Clamp(-Mathf.Atan2(local.y,new Vector2(local.x,local.z).magnitude)*Mathf.Rad2Deg,-25,25);
            }
            if(decision.gaze==Gaze.away){yaw=35;pitch=5;}else if(decision.gaze==Gaze.@object&&!lookPoint.HasValue){yaw=-35;pitch=12;}
            // While walking the head looks where the feet are going.
            yaw*=1-walk*.8f;pitch*=1-walk*.8f;
            gazeYaw=Mathf.Lerp(gazeYaw,yaw*lookStrength,1-Mathf.Exp(-3*Time.deltaTime));gazePitch=Mathf.Lerp(gazePitch,pitch*lookStrength,1-Mathf.Exp(-3*Time.deltaTime));
            Vector3 head=new Vector3(gazePitch,gazeYaw*.8f,Mathf.Sin(t*.7f)*1.2f),chest=new Vector3(Mathf.Sin(t*1.4f)*.6f,gazeYaw*.2f,0);
            Vector3 left=rig.leftArmRestEuler,right=rig.rightArmRestEuler,leftLower=Vector3.zero,rightLower=Vector3.zero;
            bool proceduralGesture=age<2.4f&&!clipGesture;
            if(proceduralGesture)
            {
                switch(decision.gesture)
                {
                    case Gesture.nod:head.x+=Mathf.Sin(age*8)*10*envelope;break;
                    case Gesture.big_nod:head.x+=Mathf.Sin(age*7)*20*envelope;break;
                    case Gesture.head_tilt:head.z+=18*envelope;break;
                    case Gesture.wave:right.z-=95*envelope;rightLower.y+=Mathf.Sin(age*14)*25*envelope;break;
                    case Gesture.look_away:head.y+=30*envelope;break;
                    case Gesture.laugh:head.x-=8*envelope;chest.x+=Mathf.Sin(age*22)*3*envelope;break;
                    case Gesture.shrug:left.z+=25*envelope;right.z-=25*envelope;leftLower.y=35*envelope;rightLower.y=-35*envelope;break;
                }
            }
            float speech=IsSpeaking?mouth:0;
            if(clips)
            {
                // Without a mouth shape, a small speech-driven head bob shows who is talking.
                if(string.IsNullOrEmpty(rig.mouthShape))head.x+=speech*6;
                PoseBone(rig.head,head);PoseBone(rig.chest,new Vector3(0,chest.y,0));
            }
            else
            {
                PoseBone(rig.head,head);PoseBone(rig.chest,chest);PoseBone(rig.leftArm,left);PoseBone(rig.rightArm,right);PoseBone(rig.leftForearm,leftLower);PoseBone(rig.rightForearm,rightLower);
                foreach(var leg in new[]{rig.leftLeg,rig.rightLeg,rig.leftKnee,rig.rightKnee})if(leg&&rest.TryGetValue(leg,out var q))leg.localRotation=q;
                if(walk>.01f)
                {
                    walkPhase+=Time.deltaTime*Mathf.Lerp(4,9,walk)*(locomotion?Mathf.Sqrt(locomotion.PaceScale):1);
                    float s=Mathf.Sin(walkPhase)*28*walk,k=Mathf.Max(0,Mathf.Sin(walkPhase+1.4f))*30*walk,k2=Mathf.Max(0,-Mathf.Sin(walkPhase+1.4f))*30*walk;
                    Swing(rig.leftLeg,s);Swing(rig.rightLeg,-s);Swing(rig.leftKnee,-k);Swing(rig.rightKnee,-k2);
                    if(!proceduralGesture||decision.gesture!=Gesture.wave)Swing(rig.rightArm,s*.7f);
                    if(!proceduralGesture||decision.gesture!=Gesture.shrug)Swing(rig.leftArm,-s*.7f);
                }
            }
            ApplyPose();
            if(t>=nextBlink){blinkStarted=t;nextBlink=t+Random.Range(2.2f,5.5f);}
            float blinkAge=t-blinkStarted,blink=blinkAge<.18f?Mathf.Sin(blinkAge/.18f*Mathf.PI)*100:0;
            float volume=0;if(IsSpeaking){voice.GetOutputData(audio,0);foreach(var sample in audio)volume+=sample*sample;volume=Mathf.Clamp01(Mathf.Sqrt(volume/audio.Length)*9);}
            mouth=Mathf.Lerp(mouth,volume,1-Mathf.Exp(-20*Time.deltaTime));
            foreach(var mesh in rig.faceMeshes)
            {
                if(!mesh)continue;
                Set(mesh,rig.blinkShape,blink);Set(mesh,rig.mouthShape,mouth*100);Set(mesh,rig.lookXShape,gazeYaw/45*100);Set(mesh,rig.lookYShape,-gazePitch/25*100);
                if(rig.emotionShapes==null||rig.emotionShapes.Length==0){for(int i=0;i<EmotionNames.Length;i++)Set(mesh,EmotionNames[i],i==(int)decision.emotion?70:0);}
                else Express(mesh);
            }
        }
        // Sit, crouch and point layer on top of whatever the clip or procedural pose produced this frame.
        void ApplyPose()
        {
            if(poseWeight<=.001f)return;float w=Mathf.SmoothStep(0,1,poseWeight);
            switch(CurrentPose)
            {
                case Pose.sit:
                    Swing(rig.leftLeg,-80*w);Swing(rig.rightLeg,-80*w);Swing(rig.leftKnee,80*w);Swing(rig.rightKnee,80*w);break;
                case Pose.crouch:
                    // Drop the hips, fold the legs and lean in so the head comes close to what is being studied.
                    // Clip models (the robot) have short, mostly hidden legs: a small dip and lean reads better than folded legs.
                    if(rig.hips){rig.hips.position-=Vector3.up*HipHeight*(clips?.25f:.45f)*w;hipsWritten=rig.hips.localPosition;}
                    if(!clips){Swing(rig.leftLeg,-70*w);Swing(rig.rightLeg,-70*w);Swing(rig.leftKnee,110*w);Swing(rig.rightKnee,110*w);}
                    Swing(rig.chest,(clips?10:25)*w);break;
                case Pose.point:
                    if(poseTarget.HasValue&&rig.rightArm&&rig.rightForearm)
                    {
                        // Rotate the whole arm so the upper-arm → forearm direction aims at the target.
                        Vector3 current=(rig.rightForearm.position-rig.rightArm.position).normalized,wanted=(poseTarget.Value-rig.rightArm.position).normalized;
                        rig.rightArm.rotation=Quaternion.Slerp(Quaternion.identity,Quaternion.FromToRotation(current,wanted),w)*rig.rightArm.rotation;
                    }
                    break;
            }
        }
        readonly Dictionary<string,float> shapeWeights=new Dictionary<string,float>();
        // Mapped shapes ease toward their target so expression changes do not pop.
        void Express(SkinnedMeshRenderer mesh)
        {
            foreach(var entry in rig.emotionShapes)shapeWeights[entry.shape]=0;
            foreach(var entry in rig.emotionShapes)if(entry.emotion==decision.emotion)shapeWeights[entry.shape]=Mathf.Max(shapeWeights[entry.shape],entry.weight);
            foreach(var pair in shapeWeights)
            {
                int index=mesh.sharedMesh.GetBlendShapeIndex(pair.Key);if(index<0)continue;
                mesh.SetBlendShapeWeight(index,Mathf.Lerp(mesh.GetBlendShapeWeight(index),pair.Value,1-Mathf.Exp(-6*Time.deltaTime)));
            }
        }
        static void Set(SkinnedMeshRenderer renderer,string name,float value){if(string.IsNullOrEmpty(name))return;int index=renderer.sharedMesh.GetBlendShapeIndex(name);if(index>=0)renderer.SetBlendShapeWeight(index,value);}
        void OnDestroy()=>StopSpeech();
    }
}
