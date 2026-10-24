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
#if UNITY_EDITOR || UNITY_STANDALONE
            ray=app.Head.GetComponent<Camera>().ScreenPointToRay(Input.mousePosition);pressed=Input.GetMouseButton(0);valid=true;bool ptt=Input.GetKey(KeyCode.Space);
            if(!app.UI.IsEditing&&app.Brain)WalkDesktop();
            if(Input.GetMouseButton(1)){app.Head.Rotate(-Input.GetAxis("Mouse Y")*2,Input.GetAxis("Mouse X")*2,0);var angles=app.Head.eulerAngles;app.Head.rotation=Quaternion.Euler(angles.x,angles.y,0);}
#else
            valid=hands.TryGetPointer(out ray,out pressed)||controller.TryGetPointer(out ray,out pressed);
            var right=InputDevices.GetDeviceAtXRNode(XRNode.RightHand);right.TryGetFeatureValue(CommonUsages.primaryButton,out bool ptt);
#endif
            if(app.UI.IsEditing)ptt=false;
            var body=app.Body;
#if UNITY_EDITOR || UNITY_STANDALONE
            // PC stand-ins for body movement: H waves, F points at what the mouse is over, C toggles crouching.
            bool typing=app.UI.IsEditing;
            if(body&&!typing&&Input.GetKeyDown(KeyCode.H))body.Wave();
            if(!typing&&Input.GetKeyDown(KeyCode.C)){var p=app.Head.localPosition;app.Head.localPosition=new Vector3(p.x,p.y>1.2f?.9f:1.55f,p.z);}
            if(body)body.Pointer(ray,!typing&&Input.GetKeyDown(KeyCode.F));
#else
            if(body)body.Pointer(valid?ray:(Ray?)null,false);
#endif
            if(ptt&&!pttPrevious)app.Brain?.BeginRecording();if(!ptt&&pttPrevious)app.Brain?.EndRecording();pttPrevious=ptt;
            var canvas=app.UI.Canvas;var plane=new Plane(canvas.transform.forward,canvas.transform.position);
            hits.Clear();GameObject target=null;Vector3 point=ray.origin+ray.direction*3;
            if(valid&&plane.Raycast(ray,out float distance)&&distance>0&&distance<8)
            {
                point=ray.GetPoint(distance);Vector2 screen=canvas.worldCamera.WorldToScreenPoint(point);pointer.position=screen;events.RaycastAll(pointer,hits);if(hits.Count>0){target=hits[0].gameObject;pointer.pointerCurrentRaycast=hits[0];}
            }
            line.enabled=valid;line.SetPosition(0,ray.origin);line.SetPosition(1,point);
            if(hovered!=target){if(hovered)ExecuteEvents.ExecuteHierarchy(hovered,pointer,ExecuteEvents.pointerExitHandler);hovered=target;if(hovered)ExecuteEvents.ExecuteHierarchy(hovered,pointer,ExecuteEvents.pointerEnterHandler);}
            if(pressed&&!previous&&target){pointer.pressPosition=pointer.position;pointer.pointerPressRaycast=pointer.pointerCurrentRaycast;pressedTarget=ExecuteEvents.ExecuteHierarchy(target,pointer,ExecuteEvents.pointerDownHandler);if(!pressedTarget)pressedTarget=ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);pointer.pointerPress=pressedTarget;}
            if((!pressed||!valid)&&previous&&pressedTarget){ExecuteEvents.Execute(pressedTarget,pointer,ExecuteEvents.pointerUpHandler);var click=target?ExecuteEvents.GetEventHandler<IPointerClickHandler>(target):null;if(pressedTarget==click)ExecuteEvents.Execute(pressedTarget,pointer,ExecuteEvents.pointerClickHandler);pressedTarget=null;pointer.pointerPress=null;}
            previous=pressed&&valid;
        }
#if UNITY_EDITOR || UNITY_STANDALONE
        // PC only: WASD/arrow keys move the viewpoint across the room floor so the character can react to distance.
        void WalkDesktop()
        {
            var move=new Vector3((Input.GetKey(KeyCode.D)||Input.GetKey(KeyCode.RightArrow)?1:0)-(Input.GetKey(KeyCode.A)||Input.GetKey(KeyCode.LeftArrow)?1:0),0,(Input.GetKey(KeyCode.W)||Input.GetKey(KeyCode.UpArrow)?1:0)-(Input.GetKey(KeyCode.S)||Input.GetKey(KeyCode.DownArrow)?1:0));
            if(move.sqrMagnitude<.01f)return;
            var forward=app.Head.forward;forward.y=0;if(forward.sqrMagnitude<.01f)forward=Vector3.forward;var yaw=Quaternion.LookRotation(forward.normalized);
            var next=app.Head.position+yaw*move.normalized*1.4f*Time.deltaTime;
            var world=app.Brain.world;if(world!=null){var room=world.floor;room.Expand(new Vector3(-1,0,-1));if(!room.Contains(new Vector3(next.x,room.center.y,next.z)))return;}
            var character=app.Brain.body.transform.position;if(new Vector2(next.x-character.x,next.z-character.z).sqrMagnitude<.25f)return;
            app.Head.position=new Vector3(next.x,app.Head.position.y,next.z);
        }
#endif
        void OnDisable(){if(pressedTarget)ExecuteEvents.Execute(pressedTarget,pointer,ExecuteEvents.pointerUpHandler);previous=pttPrevious=false;app?.Brain?.EndRecording();}
    }
}
