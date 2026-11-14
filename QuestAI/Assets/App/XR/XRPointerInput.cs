using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR;

namespace QuestAI
{
    public interface IXRInputProvider { bool TryGetPointer(out Ray ray,out bool pressed); }
    public sealed class ControllerInputProvider : IXRInputProvider
    {
        readonly Transform origin;public ControllerInputProvider(Transform origin){this.origin=origin;}
        public bool TryGetPointer(out Ray ray,out bool pressed)
        {
            ray=default;pressed=false;var device=InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if(!device.isValid)device=InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            if(!device.TryGetFeatureValue(CommonUsages.devicePosition,out Vector3 position)||!device.TryGetFeatureValue(CommonUsages.deviceRotation,out Quaternion rotation))return false;
            device.TryGetFeatureValue(CommonUsages.triggerButton,out pressed);ray=new Ray(origin.TransformPoint(position),origin.TransformDirection(rotation*Vector3.forward));return true;
        }
    }
    public sealed class HandInputProvider : IXRInputProvider
    {
        OVRHand[] hands;
        public bool TryGetPointer(out Ray ray,out bool pressed)
        {
            ray=default;pressed=false;if(hands==null||hands.Length==0)hands=Object.FindObjectsByType<OVRHand>(FindObjectsSortMode.None);
            foreach(var hand in hands)if(hand&&hand.IsTracked&&hand.IsPointerPoseValid&&hand.PointerPose){ray=new Ray(hand.PointerPose.position,hand.PointerPose.forward);pressed=hand.GetFingerIsPinching(OVRHand.HandFinger.Index);return true;}
            return false;
        }
    }
    public class XRPointerInput : MonoBehaviour
    {
        AppRoot app;EventSystem events;PointerEventData pointer;GameObject hovered,pressedTarget;bool previous,pttPrevious;LineRenderer line;
        // Quest stand-ins for the PC keys: previous button states and whether the snap turn stick has returned to centre.
        bool gripPrevious,wavePrevious,crouchPrevious,turnReady=true;
        // Teleport: aimed while the stick (PC: T) is held, done on release.
        FloorMarker teleportMarker;bool teleportAiming,teleportValid;Vector3 teleportPoint;
        public const float CharacterGap=.5f;
        IXRInputProvider controller,hands;readonly List<RaycastResult> hits=new List<RaycastResult>();
        public void Initialize(AppRoot root)
        {
            app=root;events=FindFirstObjectByType<EventSystem>();if(!events){var go=new GameObject("UI Events");go.transform.SetParent(transform);events=go.AddComponent<EventSystem>();}
            // This adapter routes the Meta rig's tracked pointer to the same UGUI controls as Editor mouse input.
            pointer=new PointerEventData(events);controller=new ControllerInputProvider(app.TrackingSpace);hands=new HandInputProvider();
            line=new GameObject("Pointer ray").AddComponent<LineRenderer>();line.transform.SetParent(transform);line.positionCount=2;line.startWidth=.002f;line.endWidth=.001f;line.material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));line.material.color=new Color(.9f,.72f,.42f);line.useWorldSpace=true;
        }
        void Update()
        {
            if(app==null||!app.UI||!app.UI.Canvas)return;bool pressed,valid;Ray ray;
            // A script reload during Play mode keeps the Unity object references but drops these plain C# objects.
            if(pointer==null||!events){if(!events)events=FindFirstObjectByType<EventSystem>();if(!events)return;pointer=new PointerEventData(events);controller=new ControllerInputProvider(app.TrackingSpace);hands=new HandInputProvider();}
            bool typing=app.UI.IsEditing,point,wave,crouch,aim;Vector2 move;
#if UNITY_EDITOR || UNITY_STANDALONE
            ray=app.Head.GetComponent<Camera>().ScreenPointToRay(Input.mousePosition);pressed=Input.GetMouseButton(0);valid=true;bool ptt=Input.GetKey(KeyCode.U);
            move=new Vector2((Input.GetKey(KeyCode.D)||Input.GetKey(KeyCode.RightArrow)?1:0)-(Input.GetKey(KeyCode.A)||Input.GetKey(KeyCode.LeftArrow)?1:0),(Input.GetKey(KeyCode.W)||Input.GetKey(KeyCode.UpArrow)?1:0)-(Input.GetKey(KeyCode.S)||Input.GetKey(KeyCode.DownArrow)?1:0));
            if(Input.GetMouseButton(1)){app.Head.Rotate(-Input.GetAxis("Mouse Y")*2,Input.GetAxis("Mouse X")*2,0);var angles=app.Head.eulerAngles;app.Head.rotation=Quaternion.Euler(angles.x,angles.y,0);}
            // PC stand-ins for body movement: H waves, F points at what the mouse is over (a thing, or a spot on the floor),
            // C toggles crouching, holding T aims a teleport at the floor under the mouse.
            wave=Input.GetKeyDown(KeyCode.H);point=Input.GetKeyDown(KeyCode.F);crouch=Input.GetKeyDown(KeyCode.C);aim=Input.GetKey(KeyCode.T);
            var rig=app.Head;
#else
            valid=hands.TryGetPointer(out ray,out pressed)||controller.TryGetPointer(out ray,out pressed);
            var right=InputDevices.GetDeviceAtXRNode(XRNode.RightHand);right.TryGetFeatureValue(CommonUsages.primaryButton,out bool ptt);
            var left=InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            // Left X also talks, so either hand can hold the button.
            if(left.TryGetFeatureValue(CommonUsages.primaryButton,out bool leftTalk)&&leftTalk)ptt=true;
            left.TryGetFeatureValue(CommonUsages.primary2DAxis,out move);if(move.sqrMagnitude<=.04f)move=Vector2.zero;
            right.TryGetFeatureValue(CommonUsages.primary2DAxis,out Vector2 stick);
            // Right grip points at what the ray is on (PC: F), left Y waves (H), left stick click crouches or stands (C).
            right.TryGetFeatureValue(CommonUsages.gripButton,out bool grip);left.TryGetFeatureValue(CommonUsages.secondaryButton,out bool waveButton);left.TryGetFeatureValue(CommonUsages.primary2DAxisClick,out bool crouchButton);
            point=valid&&grip&&!gripPrevious;wave=waveButton&&!wavePrevious;crouch=crouchButton&&!crouchPrevious;
            gripPrevious=grip;wavePrevious=waveButton;crouchPrevious=crouchButton;
            // Right stick: pushed forward it aims a teleport (PC: T) that happens on release; sideways it snap-turns.
            aim=teleportAiming?stick.y>.35f:stick.y>.7f&&Mathf.Abs(stick.x)<.5f;
            var rig=app.TrackingSpace.parent;
            if(!typing&&app.Brain&&!aim&&!teleportAiming)SnapTurn(stick.x);
#endif
            // Typing takes over the controls; an aim in progress is dropped rather than carried out.
            if(typing){ptt=false;point=wave=crouch=aim=false;move=Vector2.zero;teleportAiming=teleportValid=false;if(teleportMarker)teleportMarker.Hide();}
            // The panel under the pointer comes first: pointing and teleporting act on the room only when the ray is off the panel.
            var canvas=app.UI.Canvas;var plane=new Plane(canvas.transform.forward,canvas.transform.position);
            hits.Clear();GameObject target=null;Vector3 end=ray.origin+ray.direction*3;
            if(valid&&plane.Raycast(ray,out float distance)&&distance>0&&distance<8)
            {
                Vector3 onPanel=ray.GetPoint(distance);Vector2 screen=canvas.worldCamera.WorldToScreenPoint(onPanel);pointer.position=screen;events.RaycastAll(pointer,hits);if(hits.Count>0){target=hits[0].gameObject;pointer.pointerCurrentRaycast=hits[0];end=onPanel;}
            }
            var body=app.Body;var world=app.Brain?app.Brain.world:null;
            if(app.Brain)Walk(move,rig);
            if(crouch&&app.Brain)
            {
#if UNITY_EDITOR || UNITY_STANDALONE
                var p=app.Head.localPosition;app.Head.localPosition=new Vector3(p.x,p.y>1.2f?.9f:1.55f,p.z);
#else
                if(body){body.SeatedCrouch=!body.SeatedCrouch;rig.position+=Vector3.up*(body.SeatedCrouch?-UserBodyTracker.SeatedCrouchDrop:UserBodyTracker.SeatedCrouchDrop);}
#endif
            }
            if(body){body.Pointer(valid?ray:(Ray?)null,point&&valid&&!target);if(wave)body.Wave();}
            Teleport(aim,valid&&!target,ray,rig,world);
            if(ptt&&!pttPrevious)app.Brain?.BeginRecording();if(!ptt&&pttPrevious)app.Brain?.EndRecording();pttPrevious=ptt;
            // Off the panel the ray is drawn down to the floor it lands on, so 「あそこ」 can be seen.
            if(teleportAiming&&teleportMarker&&teleportMarker.Visible)end=teleportPoint;
            else if(valid&&!target&&world!=null&&world.FloorPoint(ray,out var floorPoint))end=floorPoint;
            line.enabled=valid;line.SetPosition(0,ray.origin);line.SetPosition(1,end);
            if(valid&&pressed&&!previous&&!target&&app.Brain)app.Brain.GetComponent<CompanionPlayground>()?.PlaceFromRay(ray);
            if(hovered!=target){if(hovered)ExecuteEvents.ExecuteHierarchy(hovered,pointer,ExecuteEvents.pointerExitHandler);hovered=target;if(hovered)ExecuteEvents.ExecuteHierarchy(hovered,pointer,ExecuteEvents.pointerEnterHandler);}
            if(pressed&&!previous&&target){pointer.pressPosition=pointer.position;pointer.pointerPressRaycast=pointer.pointerCurrentRaycast;pressedTarget=ExecuteEvents.ExecuteHierarchy(target,pointer,ExecuteEvents.pointerDownHandler);if(!pressedTarget)pressedTarget=ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);pointer.pointerPress=pressedTarget;}
            if((!pressed||!valid)&&previous&&pressedTarget){ExecuteEvents.Execute(pressedTarget,pointer,ExecuteEvents.pointerUpHandler);var click=target?ExecuteEvents.GetEventHandler<IPointerClickHandler>(target):null;if(pressedTarget==click)ExecuteEvents.Execute(pressedTarget,pointer,ExecuteEvents.pointerClickHandler);pressedTarget=null;pointer.pointerPress=null;}
            previous=pressed&&valid;
        }
        static float FlatSqr(Vector3 a,Vector3 b)=>(a.x-b.x)*(a.x-b.x)+(a.z-b.z)*(a.z-b.z);
        // Where the user may be: on free floor inside the room, and not on top of the character.
        public static bool CanUserStand(SceneAwareness world,Vector3 point,Vector3 character)=>world!=null&&world.CanStand(point)&&FlatSqr(point,character)>=CharacterGap*CharacterGap;
        // A step of the user's own walking is refused only when it leads into a wall, furniture or the character;
        // someone already past those (room-scale walking) can always move, so they are never trapped.
        public static bool CanUserStep(SceneAwareness world,Vector3 from,Vector3 to,Vector3 character)
        {
            if(world!=null&&!world.CanStand(to)&&world.CanStand(from))return false;
            float next=FlatSqr(to,character);
            return next>=CharacterGap*CharacterGap||next>=FlatSqr(from,character);
        }
        // WASD on PC, the left stick on Quest: moves the given rig across the room floor (relative to where the user looks)
        // so the character can react to distance. Walls, furniture and the character stop it; it slides along them.
        void Walk(Vector2 input,Transform rig)
        {
            if(input.sqrMagnitude<.01f||!rig)return;
            var move=new Vector3(input.x,0,input.y);if(move.sqrMagnitude>1)move.Normalize();
            var forward=app.Head.forward;forward.y=0;if(forward.sqrMagnitude<.01f)forward=Vector3.forward;var yaw=Quaternion.LookRotation(forward.normalized);
            var step=yaw*move*1.4f*Time.deltaTime;var head=app.Head.position;
            var world=app.Brain.world;var character=app.Brain.body.transform.position;
            if(!CanUserStep(world,head,head+step,character))
            {
                Vector3 alongX=new Vector3(step.x,0,0),alongZ=new Vector3(0,0,step.z);
                if(Mathf.Abs(step.x)>1e-5f&&CanUserStep(world,head,head+alongX,character))step=alongX;
                else if(Mathf.Abs(step.z)>1e-5f&&CanUserStep(world,head,head+alongZ,character))step=alongZ;
                else return;
            }
            rig.position+=step;
        }
        // Turns the Quest rig by 30 degrees about the user's head each time the right stick is pushed sideways.
        void SnapTurn(float x)
        {
            if(Mathf.Abs(x)<.3f){turnReady=true;return;}
            if(!turnReady||Mathf.Abs(x)<.7f)return;turnReady=false;
            app.TrackingSpace.parent.RotateAround(app.Head.position,Vector3.up,Mathf.Sign(x)*30);
        }
        // While the control is held, a ring shows where the ray meets the floor (green: free, red: not allowed); letting go
        // moves the user's viewpoint there. The height and the facing direction stay as they are. A ray that is on the
        // panel or lost for a moment only hides the ring: nothing happens until the control itself is released.
        void Teleport(bool held,bool usable,Ray ray,Transform rig,SceneAwareness world)
        {
            if(world==null||!rig||!app.Brain){teleportAiming=teleportValid=false;if(teleportMarker)teleportMarker.Hide();return;}
            if(held)
            {
                teleportAiming=true;bool onFloor=usable&&world.FloorPoint(ray,out teleportPoint);
                teleportValid=onFloor&&CanUserStand(world,teleportPoint,app.Brain.body.transform.position);
                if(!teleportMarker){teleportMarker=FloorMarker.Create("Teleport marker",FloorMarker.Mint,.28f);teleportMarker.transform.SetParent(transform,false);}
                if(onFloor){teleportMarker.Tint(teleportValid?FloorMarker.Mint:FloorMarker.Blocked);teleportMarker.Show(teleportPoint);}else teleportMarker.Hide();
                return;
            }
            if(!teleportAiming)return;
            teleportAiming=false;if(teleportMarker)teleportMarker.Hide();
            if(!teleportValid){app.UI.ShowStatus("そこへは移動できません。家具やキャラクターのいない床を選んでください。");return;}
            teleportValid=false;TeleportTo(teleportPoint,rig);
        }
        // Puts the user's head above the given floor point by moving the rig (PC: the camera itself).
        public void TeleportTo(Vector3 point,Transform rig){var head=app.Head.position;rig.position+=new Vector3(point.x-head.x,0,point.z-head.z);}
        void OnDisable(){if(pressedTarget)ExecuteEvents.Execute(pressedTarget,pointer,ExecuteEvents.pointerUpHandler);previous=pttPrevious=false;teleportAiming=false;if(teleportMarker)teleportMarker.Hide();app?.Brain?.EndRecording();}
    }
}
