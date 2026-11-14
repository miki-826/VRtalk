using System.Collections.Generic;
using UnityEngine;

namespace QuestAI
{
    // Runs after the toys have moved what the hands hold, so the hands can reach for it in the same frame.
    [DefaultExecutionOrder(50)]
    public class BodySystem : MonoBehaviour
    {
        public CharacterRigBinding rig;
        public Transform user;
        public AudioSource voice;
        public float lookStrength = 1;
        // World point used when the decision's gaze is "object" (set by the brain from the scene model).
        public Vector3? lookPoint;
        public enum Pose { stand, sit, crouch, point, floor_sit }
        public Pose CurrentPose { get; private set; }
        // Height of the hips above the root in the bind pose; seats lower the root by this much.
        public float HipHeight { get; private set; } = .45f;
        Vector3? poseTarget; float poseWeight; Pose displayedPose; Vector3 hipsRest, hipsWritten, hipsBase;
        readonly Dictionary<Transform,Quaternion> rest = new Dictionary<Transform,Quaternion>();
        BehaviorDecision decision = new BehaviorDecision();
        float gestureTime, gestureRate = 1, nextBlink, blinkStarted = -10, mouth, gazeYaw, gazePitch, walkPhase;
        readonly float[] audio = new float[256];
        static readonly string[] EmotionNames=System.Enum.GetNames(typeof(Emotion));
        CharacterLocomotion locomotion; ClipAnimator clips; FaceDriver face; bool clipGesture;
        void Awake()
        {
            if (!rig) rig = GetComponent<CharacterRigBinding>();
            if (!rig) return;
            clips = GetComponent<ClipAnimator>(); face = GetComponent<FaceDriver>();
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
            leftHand=Hand(rig.leftForearm,HumanBodyBones.LeftHand);rightHand=Hand(rig.rightForearm,HumanBodyBones.RightHand);
            var mouthObject=new GameObject("Spatial voice");mouthObject.transform.SetParent(rig.head?rig.head:transform,false);
            voice=mouthObject.AddComponent<AudioSource>();voice.playOnAwake=false;voice.spatialBlend=1;voice.minDistance=.5f;voice.maxDistance=12;voice.rolloffMode=AudioRolloffMode.Logarithmic;voice.spatialize=true;
            mouthObject.AddComponent<MetaXRAudioSource>();
        }
        public void Apply(BehaviorDecision value)
        {
            decision=value;gestureTime=Time.time;gestureRate=1;
            clipGesture=clips&&clips.Play(ClipFor(value.gesture));
        }
        public void PerformGesture(Gesture gesture, float rate)
        {
            Apply(new BehaviorDecision { emotion = CurrentEmotion, gesture = gesture });
            gestureRate = Mathf.Max(.05f, rate);
            if (clipGesture) clips.Play(ClipFor(gesture), 2.4f / gestureRate, gestureRate);
        }
        // Authored clips stand in for the procedural gestures where a model has a fitting one.
        static string ClipFor(Gesture gesture)
        {
            switch(gesture){case Gesture.wave:return "Wave";case Gesture.nod:case Gesture.big_nod:return "Yes";case Gesture.shrug:return "No";case Gesture.laugh:return "Jump";default:return null;}
        }
        public void ResetPose() { decision=new BehaviorDecision();mouth=0;StopSpeech();SetPose(Pose.stand,null);displayedPose=Pose.stand;poseWeight=0;ClearMotion();SetHeldArms(null);heldArmWeight=0; }
        // Changes only the expression, keeping the current gaze and any running motion.
        public void SetEmotion(Emotion emotion){decision=new BehaviorDecision{emotion=emotion,gaze=decision.gaze};}
        public Emotion CurrentEmotion => decision.emotion;
        // A whole-body keyframe (MotionPerformer) layered on top of everything else; null when no motion runs.
        MotionKey motion;
        public MotionKey Motion => motion;
        public void SetMotion(MotionKey key)=>motion=key;
        // Arms held forward around something carried, whenever no other motion is playing.
        public bool Holding;
        public static readonly MotionKey HoldKey=new MotionKey{left_arm_up=65,right_arm_up=65,left_arm_out=-12,right_arm_out=-12,left_elbow=35,right_elbow=35};
        public void ClearMotion()=>motion=null;
        MotionKey heldArms, lastHeldArms; float heldArmWeight;
        public MotionKey HeldArms => heldArms;
        public void SetHeldArms(MotionKey pose) { heldArms = pose; if (pose != null) lastHeldArms = pose; }
        public void SetPose(Pose pose,Vector3? target)
        {
            CurrentPose=pose;poseTarget=target;
            if(pose!=Pose.stand)displayedPose=pose;
            // Models with a crouch animation use it instead of the procedural fold.
            if(clips){if(pose==Pose.crouch)clips.Hold("Crouch_Idle_Loop");else clips.Release();}
        }
        // Where each hand should reach (null: wherever the animation puts it). Eased in and out.
        public Vector3? leftHandTarget,rightHandTarget;
        // Something held in both hands: its centre and half its width.
        public Vector3? holdCenter;public float holdWidth=.15f;
        Transform leftHand,rightHand;float leftReach,rightReach;Vector3 leftLast,rightLast;
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
            float t=Time.time, age=(t-gestureTime)*gestureRate, envelope=Mathf.Sin(Mathf.Clamp01(age/2.4f)*Mathf.PI), walk=locomotion?locomotion.Speed01:0;
            if(clips){clips.SetSpeed(walk);clips.SetPace(locomotion?locomotion.PaceScale:1);clips.SetGroundSpeed(locomotion?locomotion.GroundSpeed:0);clips.SetHolding(Holding);}
            poseWeight=Mathf.MoveTowards(poseWeight,CurrentPose==Pose.stand||walk>.3f?0:1,Time.deltaTime/.45f);
            // Undo last frame's crouch offset unless a clip already rewrote the hips this frame.
            if(rig.hips){if(!clips)rig.hips.localPosition=hipsRest;else if(rig.hips.localPosition==hipsWritten)rig.hips.localPosition=hipsBase;hipsBase=rig.hips.localPosition;}
            if(clips&&rig.hips&&rig.animationYawOffset!=0)rig.hips.rotation=Quaternion.AngleAxis(rig.animationYawOffset,transform.up)*rig.hips.rotation;
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
                if(rig.armSpread!=0){Arm(rig.leftArm,null,0,rig.armSpread,0,-1);Arm(rig.rightArm,null,0,rig.armSpread,0,1);}
                // Gestures the clip library has no clip for are layered on the animated pose about the character's own axes.
                if(proceduralGesture)
                    switch(decision.gesture)
                    {
                        // Upper arm out to the side, forearm raised and waving from the elbow, so the hand stays beside a big head.
                        case Gesture.wave:Turn(rig.rightArm,Quaternion.AngleAxis(85*envelope,transform.forward));Turn(rig.rightForearm,Quaternion.AngleAxis((50+Mathf.Sin(age*10)*22)*envelope,transform.forward));break;
                        case Gesture.shrug:Arm(rig.leftArm,rig.leftForearm,0,25*envelope,75*envelope,-1);Arm(rig.rightArm,rig.rightForearm,0,25*envelope,75*envelope,1);break;
                        case Gesture.laugh:Turn(rig.chest,Quaternion.AngleAxis((Mathf.Sin(age*22)*4-6)*envelope,transform.right));break;
                    }
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
            ApplyPose();ApplyMotion();ApplyHeldArms();Reach();
            if(t>=nextBlink){blinkStarted=t;nextBlink=t+Random.Range(2.2f,5.5f);}
            float blinkAge=t-blinkStarted,blink=blinkAge<.18f?Mathf.Sin(blinkAge/.18f*Mathf.PI)*100:0;
            float volume=0;if(IsSpeaking){voice.GetOutputData(audio,0);foreach(var sample in audio)volume+=sample*sample;volume=Mathf.Clamp01(Mathf.Sqrt(volume/audio.Length)*9);}
            mouth=Mathf.Lerp(mouth,volume,1-Mathf.Exp(-20*Time.deltaTime));
            // The head already turns by most of the gaze; the eyes cover the rest and lead slightly.
            if(face)face.Drive(decision.emotion,blink/100,mouth,gazeYaw*.6f,-gazePitch*.6f);
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
            // A floor sit unfolds over the same transition interval used to lower the body.
            switch(CurrentPose==Pose.stand&&displayedPose==Pose.floor_sit?displayedPose:CurrentPose)
            {
                case Pose.sit:
                    Swing(rig.leftLeg,-80*w);Swing(rig.rightLeg,-80*w);Swing(rig.leftKnee,80*w);Swing(rig.rightKnee,80*w);break;
                case Pose.floor_sit:
                    if(rig.hips){rig.hips.position-=Vector3.up*HipHeight*.5f*w;hipsWritten=rig.hips.localPosition;}
                    Swing(rig.leftLeg,-70*w);Swing(rig.rightLeg,-70*w);Swing(rig.leftKnee,110*w);Swing(rig.rightKnee,110*w);Swing(rig.chest,5*w);break;
                case Pose.crouch:
                    // Drop the hips, fold the legs and lean in so the head comes close to what is being studied.
                    // Clip models (the robot) have short, mostly hidden legs: a small dip and lean reads better than folded legs.
                    if(clips&&clips.Posing)break;
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
        // Rotations are about the character's own axes, so one keyframe means the same thing on every rig.
        void ApplyMotion()
        {
            var m=motion??(Holding?HoldKey:null);if(m==null)return;Vector3 r=transform.right,u=transform.up,f=transform.forward;
            if(rig.hips&&(m.lift!=0||m.hips_drop!=0)){rig.hips.position+=u*(m.lift-HipHeight*.5f*m.hips_drop);hipsWritten=rig.hips.localPosition;}
            Turn(rig.chest,Quaternion.AngleAxis(m.chest_yaw,u)*Quaternion.AngleAxis(m.chest_pitch,r)*Quaternion.AngleAxis(-m.chest_roll,f));
            Turn(rig.head,Quaternion.AngleAxis(m.head_yaw,u)*Quaternion.AngleAxis(m.head_pitch,r)*Quaternion.AngleAxis(-m.head_roll,f));
            Arm(rig.leftArm,rig.leftForearm,m.left_arm_up,m.left_arm_out,m.left_elbow,-1);Arm(rig.rightArm,rig.rightForearm,m.right_arm_up,m.right_arm_out,m.right_elbow,1);
            Leg(rig.leftLeg,rig.leftKnee,m.left_leg,m.left_knee);Leg(rig.rightLeg,rig.rightKnee,m.right_leg,m.right_knee);
        }
        static void Turn(Transform bone,Quaternion rotation){if(bone)bone.rotation=rotation*bone.rotation;}
        void ApplyHeldArms()
        {
            heldArmWeight=Mathf.MoveTowards(heldArmWeight,heldArms!=null?1:0,Time.deltaTime/.3f);
            if(lastHeldArms==null||heldArmWeight<=0)return;
            var h=lastHeldArms;float w=Mathf.SmoothStep(0,1,heldArmWeight);
            HeldArm(rig.leftArm,rig.leftForearm,h.left_arm_up,h.left_arm_out,h.left_elbow,-1,w);
            HeldArm(rig.rightArm,rig.rightForearm,h.right_arm_up,h.right_arm_out,h.right_elbow,1,w);
        }
        void HeldArm(Transform upper,Transform lower,float up,float outward,float elbow,float side,float weight)
        {
            if(!upper||!lower||(up==0&&outward==0&&elbow==0)||!rest.TryGetValue(lower,out var b))return;
            Quaternion upperBefore=upper.localRotation,lowerBefore=lower.localRotation;
            // Aim the actual shoulder-to-elbow chain. Bind-pose Euler offsets differ between models.
            Vector3 direction=lower.position-upper.position;
            if(direction.sqrMagnitude<1e-8f)return;
            Vector3 wanted=Quaternion.AngleAxis(-up,transform.right)*Quaternion.AngleAxis(side*outward,transform.forward)*(-transform.up);
            upper.rotation=Quaternion.FromToRotation(direction,wanted)*upper.rotation;
            lower.localRotation=b;
            lower.rotation=Quaternion.AngleAxis(-elbow,transform.right)*lower.rotation;
            upper.localRotation=Quaternion.Slerp(upperBefore,upper.localRotation,weight);lower.localRotation=Quaternion.Slerp(lowerBefore,lower.localRotation,weight);
        }
        Transform Hand(Transform forearm,HumanBodyBones bone)
        {
            if(rig.animator&&rig.animator.isHuman){var hand=rig.animator.GetBoneTransform(bone);if(hand)return hand;}
            return forearm&&forearm.childCount>0?forearm.GetChild(0):null;
        }
        // Hands go where they are sent: to a held object's sides, a block being placed, a ball being pushed.
        void Reach()
        {
            Vector3? left=leftHandTarget,right=rightHandTarget;
            if(holdCenter.HasValue&&Holding){var side=transform.right*holdWidth;left=left??holdCenter.Value-side;right=right??holdCenter.Value+side;}
            float step=Time.deltaTime/.25f;
            leftReach=Mathf.MoveTowards(leftReach,left.HasValue?1:0,step);rightReach=Mathf.MoveTowards(rightReach,right.HasValue?1:0,step);
            if(left.HasValue)leftLast=left.Value;if(right.HasValue)rightLast=right.Value;
            // Elbows bend down, back and outward like a person's.
            if(leftReach>0&&leftHand)SolveTwoBone(rig.leftArm,rig.leftForearm,leftHand,leftLast,rig.leftForearm.position-transform.up*.3f-transform.right*.25f-transform.forward*.1f,Mathf.SmoothStep(0,1,leftReach));
            if(rightReach>0&&rightHand)SolveTwoBone(rig.rightArm,rig.rightForearm,rightHand,rightLast,rig.rightForearm.position-transform.up*.3f+transform.right*.25f-transform.forward*.1f,Mathf.SmoothStep(0,1,rightReach));
        }
        // Analytic two-bone IK (upper arm, forearm, hand): bends the elbow by the law of cosines, then swings the chain at the target.
        public static void SolveTwoBone(Transform a,Transform b,Transform c,Vector3 target,Vector3 hint,float weight)
        {
            if(!a||!b||!c||weight<=0)return;
            Quaternion aStart=a.rotation,bStart=b.rotation;
            Vector3 pa=a.position,pb=b.position,pc=c.position;
            float lab=(pb-pa).magnitude,lcb=(pc-pb).magnitude;if(lab<1e-4f||lcb<1e-4f)return;
            float lat=Mathf.Clamp((target-pa).magnitude,.01f,(lab+lcb)*.999f);
            float Angle(Vector3 x,Vector3 y)=>Mathf.Acos(Mathf.Clamp(Vector3.Dot(x.normalized,y.normalized),-1,1));
            float acAb0=Angle(pc-pa,pb-pa),baBc0=Angle(pa-pb,pc-pb);
            float acAb1=Mathf.Acos(Mathf.Clamp((lcb*lcb-lab*lab-lat*lat)/(-2*lab*lat),-1,1)),baBc1=Mathf.Acos(Mathf.Clamp((lat*lat-lab*lab-lcb*lcb)/(-2*lab*lcb),-1,1));
            // The bend plane is the arm's current one; the hint decides it only when the arm is straight.
            Vector3 axis0=Vector3.Cross(pc-pa,pb-pa);if(axis0.sqrMagnitude<1e-6f)axis0=Vector3.Cross(pc-pa,hint-pa);if(axis0.sqrMagnitude<1e-8f)axis0=Vector3.up;axis0.Normalize();
            b.rotation=Quaternion.AngleAxis((baBc1-baBc0)*Mathf.Rad2Deg,axis0)*b.rotation;
            a.rotation=Quaternion.AngleAxis((acAb1-acAb0)*Mathf.Rad2Deg,axis0)*a.rotation;
            // With the elbow bent to the right reach, swing the whole arm so the hand lands on the target.
            pc=c.position;var swing=Quaternion.FromToRotation(pc-pa,target-pa);a.rotation=swing*a.rotation;
            if(weight<1){a.rotation=Quaternion.Slerp(aStart,a.rotation,weight);b.rotation=Quaternion.Slerp(bStart,b.rotation,weight);}
        }
        // Out raises the arm sideways (about the forward axis, mirrored per side); up swings it forward and overhead.
        void Arm(Transform upper,Transform lower,float up,float outward,float elbow,float side)
        {
            if(upper)upper.rotation=Quaternion.AngleAxis(-up,transform.right)*Quaternion.AngleAxis(side*outward,transform.forward)*upper.rotation;
            if(lower)lower.rotation=Quaternion.AngleAxis(-elbow,transform.right)*lower.rotation;
        }
        void Leg(Transform upper,Transform knee,float lift,float bend)
        {
            if(upper)upper.rotation=Quaternion.AngleAxis(-lift,transform.right)*upper.rotation;
            if(knee)knee.rotation=Quaternion.AngleAxis(bend,transform.right)*knee.rotation;
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
