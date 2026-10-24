using System;
using System.Collections.Generic;
using UnityEngine;

namespace QuestAI
{
    // Walks the character along NavGrid paths, turns it in place toward what it should face, and climbs onto seats.
    public class CharacterLocomotion : MonoBehaviour
    {
        public SceneAwareness world;
        public Transform user;
        public float speed = .55f, turnSpeed = 220;
        public float Speed01 { get; private set; }
        // 1 for normal, below 1 for a slow stroll, above 1 when hurrying; the body scales its step rhythm with it.
        public float PaceScale { get; private set; } = 1;
        public bool Moving => path != null;
        public bool Seated => seat.HasValue;
        public string Destination { get; private set; }
        // While a performed motion turns the body itself, idle facing must not pull it back toward the user.
        public bool Frozen;
        List<Vector3> path; int index; Vector3? face; Action<bool> done; float stuckTime; Vector3 lastPosition; bool turning;
        Vector3? seat; Vector3 standSpot; float perch; float seatHeight;

        public static float PaceFactor(Pace pace) => pace == Pace.slow ? .6f : pace == Pace.hurry ? 1.7f : 1;
        public bool MoveTo(Vector3 goal, string destination, Vector3? faceAfter = null, Action<bool> arrived = null, Pace pace = Pace.normal, Vector3? via = null)
        {
            if (world == null) return false;
            Vector3 from = seat.HasValue ? standSpot : transform.position;
            Vector3? avoid = user ? user.position : (Vector3?)null;
            List<Vector3> route = null;
            if (via.HasValue && !world.grid.Blocked(via.Value))
            {
                var first = world.grid.FindPath(from, via.Value, avoid); var second = first == null ? null : world.grid.FindPath(via.Value, goal, avoid);
                if (second != null) { route = first; route.AddRange(second); }
            }
            route = route ?? world.grid.FindPath(from, goal, avoid);
            if (route == null) return false;
            Finish(false); Stand();
            path = route; index = 0; Destination = destination; face = faceAfter; done = arrived; stuckTime = 0; lastPosition = transform.position; PaceScale = PaceFactor(pace);
            if (path.Count == 0) Finish(true);
            return true;
        }
        public void Stop() { Finish(false); face = null; }
        public void Face(Vector3? point) { if (!Moving) face = point; }
        // Seat: hop from the stand spot onto a seat surface; the root is lowered so the hips rest on it.
        public void Sit(Vector3 point, float rootHeight) { if (Moving) return; standSpot = transform.position; seat = new Vector3(point.x, rootHeight, point.z); perch = 0; }
        public void Stand() { if (!seat.HasValue) return; transform.position = standSpot; seat = null; }
        void Finish(bool success)
        {
            if (path == null && done == null) return;
            path = null; var callback = done; done = null; Destination = null; callback?.Invoke(success);
        }
        static float Yaw(Vector3 delta) => Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
        void Turn(float targetYaw, float rate)
        {
            float yaw = Mathf.MoveTowardsAngle(transform.eulerAngles.y, targetYaw, rate * Time.deltaTime);
            transform.rotation = Quaternion.Euler(0, yaw, 0);
        }
        void Update()
        {
            float wanted = 0;
            if (seat.HasValue)
            {
                // A short arc onto the seat, then stay put.
                perch = Mathf.MoveTowards(perch, 1, Time.deltaTime / .7f);
                var p = Vector3.Lerp(standSpot, seat.Value, Mathf.SmoothStep(0, 1, perch)); p.y += Mathf.Sin(perch * Mathf.PI) * .12f;
                transform.position = p;
                if (face.HasValue || user) { var d = (face ?? user.position) - transform.position; d.y = 0; if (d.sqrMagnitude > .01f) Turn(Yaw(d), turnSpeed * .5f); }
                Speed01 = Mathf.MoveTowards(Speed01, 0, 4 * Time.deltaTime);
                return;
            }
            if (path != null)
            {
                var target = path[index]; var delta = target - transform.position; delta.y = 0;
                if (delta.magnitude < .06f)
                {
                    if (++index >= path.Count) { Finish(true); }
                }
                else
                {
                    float yaw = Yaw(delta); Turn(yaw, turnSpeed * Mathf.Max(1, PaceScale));
                    // Walk only once roughly facing the next waypoint, so turns read as turns rather than sliding.
                    float facing = Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, yaw));
                    wanted = facing < 55 ? 1 - facing / 110 : 0;
                    float step = Mathf.Min(delta.magnitude, speed * PaceScale * wanted * Time.deltaTime);
                    transform.position += delta.normalized * step;
                    stuckTime = (transform.position - lastPosition).sqrMagnitude < 1e-8f && wanted > 0 ? stuckTime + Time.deltaTime : 0; lastPosition = transform.position;
                    if (stuckTime > 2) Finish(false);
                }
            }
            else
            {
                PaceScale = 1;
                Vector3? point = face ?? (user ? user.position : (Vector3?)null);
                if (point.HasValue && !Frozen)
                {
                    var delta = point.Value - transform.position; delta.y = 0;
                    float off = delta.sqrMagnitude > .01f ? Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y, Yaw(delta))) : 0;
                    // Facing an object is exact; the user is only turned to once they drift well out of view.
                    turning = delta.sqrMagnitude > .01f && (turning ? off > 4 : off > (face.HasValue ? 6 : 55));
                    if (turning) { Turn(Yaw(delta), turnSpeed * .5f); wanted = .25f; }
                }
            }
            Speed01 = Mathf.MoveTowards(Speed01, wanted, 4 * Time.deltaTime);
        }
        void OnDisable() { Stop(); Stand(); }
    }
}
