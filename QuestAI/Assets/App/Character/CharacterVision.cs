using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace QuestAI
{
    // The character's sight. Every 0.2 s it works out what is inside its field of view and not hidden behind furniture,
    // and raises events for things that appear, start moving, stop, or come very close. Only what it can see counts:
    // a ball rolling behind its back is not noticed. On request it also renders what its eyes see to an image.
    public sealed class CharacterVision : MonoBehaviour
    {
        public float fieldOfView = 140, range = 7, interval = .2f;
        sealed class Seen { public Vector3 position; public bool visible, moving, isToy, isUser; public float lastSeen = -100, lastMoved = -100; public SceneObject item; }
        readonly Dictionary<string, Seen> seen = new Dictionary<string, Seen>();
        CharacterBrain brain; float next, lastClose = -100, lastHand = -100, lastScan;
        Camera eye; RenderTexture image;
        // kind ("appeared", "moving", "stopped", "close", "hand") and the id it is about ("user" for the user).
        public event Action<string, string> Saw;
        public Vector3? Tracking { get; private set; }
        public string TrackingId { get; private set; }
        void Awake() { brain = GetComponent<CharacterBrain>(); }
        public Vector3 Eye => brain && brain.body && brain.body.rig && brain.body.rig.head ? brain.body.rig.head.position : transform.position + Vector3.up * .9f;
        public Vector3 Forward => brain && brain.body && brain.body.lookPoint.HasValue ? Vector3.Slerp(transform.forward, (brain.body.lookPoint.Value - Eye).normalized, .5f) : transform.forward;
        public static bool CanSee(Vector3 eye, Vector3 forward, Vector3 point, float fieldOfView, float range, IEnumerable<Bounds> occluders)
        {
            var delta = point - eye; float distance = delta.magnitude;
            if (distance < .05f) return true; if (distance > range) return false;
            var flat = new Vector3(delta.x, 0, delta.z); var ahead = new Vector3(forward.x, 0, forward.z);
            if (flat.sqrMagnitude > .0025f && ahead.sqrMagnitude > .0001f && Vector3.Angle(ahead, flat) > fieldOfView / 2) return false;
            if (Mathf.Abs(Mathf.Asin(Mathf.Clamp(delta.y / distance, -1, 1)) * Mathf.Rad2Deg) > 75) return false;
            var ray = new Ray(eye, delta / distance);
            foreach (var b in occluders)
                if (!b.Contains(eye) && !b.Contains(point) && b.IntersectRay(ray, out float hit) && hit > .05f && hit < distance - .1f) return false;
            return true;
        }
        void Update()
        {
            if (!brain || brain.world == null || brain.settings == null || !brain.settings.vision) { Tracking = null; TrackingId = null; return; }
            if (Time.time < next) return; next = Time.time + interval; Scan();
        }
        public void Scan()
        {
            float now = Time.time, dt = Mathf.Max(.01f, now - lastScan); lastScan = now;
            Vector3 eyePoint = Eye, forward = Forward;
            var world = brain.world; var toys = brain.playground ? brain.playground.All : Array.Empty<CompanionPlayground.Toy>();
            var toyItems = new HashSet<SceneObject>(toys.Select(t => t.item));
            var occluders = world.objects.Where(o => o.obstacle && o.bounds.size.y > .3f && !toyItems.Contains(o)).ToList();
            var candidates = new List<(string id, Vector3 point, SceneObject item, bool toy, bool user)>();
            foreach (var toy in toys) candidates.Add((toy.item.id, toy.Center, toy.item, true, false));
            foreach (var o in world.objects) if (!toyItems.Contains(o)) candidates.Add((o.id, o.bounds.center, o, false, false));
            candidates.Add(("user", brain.user.position, null, false, true));
            Vector3? tracking = null; string trackingId = null; float nearest = float.MaxValue;
            foreach (var c in candidates)
            {
                bool visible = CanSee(eyePoint, forward, c.point, fieldOfView, range, occluders.Where(o => o != c.item).Select(o => o.bounds));
                if (!seen.TryGetValue(c.id, out var s)) { s = new Seen { position = c.point, isToy = c.toy, isUser = c.user }; seen[c.id] = s; }
                s.item = c.item;
                float speed = Vector3.Distance(c.point, s.position) / dt; s.position = c.point;
                bool moving = (c.toy || c.user && speed > .8f) && speed > .25f;
                if ((c.toy || c.user) && visible)
                {
                    if (!s.visible && now - s.lastSeen > 4) Saw?.Invoke("appeared", c.id);
                    else if (c.toy && moving && !s.moving && now - s.lastMoved > 2.5f) { s.lastMoved = now; Saw?.Invoke("moving", c.id); }
                    else if (c.toy && !moving && s.moving) Saw?.Invoke("stopped", c.id);
                    if (c.user && Vector3.Distance(eyePoint, c.point) < .65f && now - lastClose > 6) { lastClose = now; Saw?.Invoke("close", "user"); }
                }
                if (c.toy && visible && moving) { float d = Vector3.Distance(eyePoint, c.point); if (d < nearest) { nearest = d; tracking = c.point; trackingId = c.id; } }
                s.visible = visible; if (visible) s.lastSeen = now; s.moving = moving;
            }
            // A hand held up right in front of the character (away from the user's own head).
            var hand = brain.tracker ? brain.tracker.Hand : null;
            if (hand.HasValue && Vector3.Distance(hand.Value, brain.user.position) > .25f && Vector3.Distance(hand.Value, eyePoint) < .9f && CanSee(eyePoint, forward, hand.Value, fieldOfView, range, occluders.Select(o => o.bounds)) && now - lastHand > 6)
            { lastHand = now; Saw?.Invoke("hand", "user"); }
            Tracking = tracking; TrackingId = trackingId;
        }
        public bool Sees(string id) => seen.TryGetValue(id, out var s) && s.visible;
        public Vector3? PositionOf(string id) => seen.TryGetValue(id, out var s) ? s.position : (Vector3?)null;
        public string Describe(string id)
        {
            if (!seen.TryGetValue(id, out var s)) return id;
            string name = id == "user" ? "ユーザー" : id;
            return name + "(" + SceneAwareness.Direction(transform.position, transform.forward, s.position) + (s.moving ? "・動いている" : "") + ")";
        }
        // Toys and the user first, then the nearest visible furniture.
        public string[] VisibleSummary()
        {
            return seen.Where(p => p.Value.visible).OrderBy(p => p.Value.isToy || p.Value.isUser ? 0 : 1).ThenBy(p => Vector3.Distance(p.Value.position, transform.position))
                .Take(8).Select(p => Describe(p.Key)).ToArray();
        }

        // Renders the character's view with a small camera at its eyes and returns it as a JPEG.
        public Task<byte[]> CaptureAsync(int width = 512, int height = 320)
        {
            var done = new TaskCompletionSource<byte[]>();
            StartCoroutine(Capture(width, height, done));
            return done.Task;
        }
        IEnumerator Capture(int width, int height, TaskCompletionSource<byte[]> done)
        {
            if (!eye)
            {
                var go = new GameObject("Character eye"); go.transform.SetParent(transform, false);
                eye = go.AddComponent<Camera>(); eye.enabled = false; eye.fieldOfView = 75; eye.nearClipPlane = .05f; eye.farClipPlane = 30; eye.stereoTargetEye = StereoTargetEyeMask.None; eye.depth = -10;
            }
            if (!image || image.width != width) { if (image) image.Release(); image = new RenderTexture(width, height, 24); image.Create(); }
            var view = Forward; view = (view - Vector3.up * .15f).normalized;
            // In front of the face so the character's own head does not fill the picture.
            eye.transform.position = Eye + view * .25f; eye.transform.rotation = Quaternion.LookRotation(view, Vector3.up);
            eye.targetTexture = image; eye.enabled = true;
            for (int i = 0; i < 3; i++) yield return null;
            var previous = RenderTexture.active; RenderTexture.active = image;
            var picture = new Texture2D(width, height, TextureFormat.RGB24, false); picture.ReadPixels(new Rect(0, 0, width, height), 0, 0); picture.Apply();
            RenderTexture.active = previous; eye.enabled = false; eye.targetTexture = null;
            var jpeg = picture.EncodeToJPG(75); Destroy(picture);
            done.TrySetResult(jpeg);
        }
        void OnDisable() { Tracking = null; TrackingId = null; }
        void OnDestroy() { if (image) { image.Release(); Destroy(image); } }
    }
}
