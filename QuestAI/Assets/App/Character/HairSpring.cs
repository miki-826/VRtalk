using System;
using UnityEngine;

namespace QuestAI
{
    // Verlet spring chains for the generated companions' hair, skirt ribbons and the like. Each joint keeps pulling back to
    // its animated rest direction, sags with gravity, trails behind motion and stays outside the collider spheres.
    // Runs after the body and face have posed the skeleton.
    [DefaultExecutionOrder(90)]
    public sealed class HairSpring : MonoBehaviour
    {
        [Serializable] public class Chain { public Transform[] joints = Array.Empty<Transform>(); public float stiffness = 1.2f, drag = .35f, gravity = .25f; }
        public Chain[] chains = Array.Empty<Chain>();
        // Spheres the hair must stay outside of (head, chest); offset and radius in the bone's local units.
        [Serializable] public struct Sphere { public Transform bone; public Vector3 offset; public float radius; }
        public Sphere[] colliders = Array.Empty<Sphere>();
        Quaternion[][] rest; Vector3[][] axis, tail, previous; float[][] length;
        void Awake()
        {
            int n = chains.Length; rest = new Quaternion[n][]; axis = new Vector3[n][]; tail = new Vector3[n][]; previous = new Vector3[n][]; length = new float[n][];
            for (int c = 0; c < n; c++)
            {
                var j = chains[c].joints; int m = Mathf.Max(0, j.Length - 1);
                rest[c] = new Quaternion[m]; axis[c] = new Vector3[m]; tail[c] = new Vector3[m]; previous[c] = new Vector3[m]; length[c] = new float[m];
                for (int i = 0; i < m; i++)
                {
                    rest[c][i] = j[i].localRotation; var local = j[i + 1].localPosition;
                    length[c][i] = local.magnitude; axis[c][i] = local.normalized; tail[c][i] = previous[c][i] = j[i + 1].position;
                }
            }
        }
        void OnEnable() { if (tail != null) Reset(); }
        // Snaps every chain to its rest pose, e.g. after the character is teleported.
        public void Reset()
        {
            for (int c = 0; c < chains.Length; c++) for (int i = 0; i < tail[c].Length; i++) tail[c][i] = previous[c][i] = chains[c].joints[i + 1].position;
        }
        void LateUpdate()
        {
            float dt = Mathf.Min(Time.deltaTime, .05f); if (dt <= 0) return;
            for (int c = 0; c < chains.Length; c++)
            {
                var chain = chains[c]; var j = chain.joints;
                for (int i = 0; i < tail[c].Length; i++)
                {
                    var bone = j[i]; var parent = bone.parent ? bone.parent.rotation : Quaternion.identity;
                    var restRotation = parent * rest[c][i]; Vector3 restDirection = restRotation * axis[c][i], origin = bone.position;
                    float len = length[c][i] * bone.lossyScale.y;
                    // A far jump (teleport, first frame) restarts the chain instead of whipping it across the room.
                    if ((tail[c][i] - origin).sqrMagnitude > len * len * 16) tail[c][i] = previous[c][i] = origin + restDirection * len;
                    var next = tail[c][i] + (tail[c][i] - previous[c][i]) * (1 - chain.drag) + restDirection * (chain.stiffness * dt * len * 10) + Vector3.down * (chain.gravity * dt * len * 10);
                    next = origin + (next - origin).normalized * len;
                    foreach (var sphere in colliders)
                    {
                        if (!sphere.bone) continue;
                        Vector3 center = sphere.bone.TransformPoint(sphere.offset), away = next - center; float radius = sphere.radius * sphere.bone.lossyScale.y;
                        if (away.sqrMagnitude < radius * radius) next = origin + (center + away.normalized * radius - origin).normalized * len;
                    }
                    previous[c][i] = tail[c][i]; tail[c][i] = next;
                    bone.rotation = Quaternion.FromToRotation(restDirection, next - origin) * restRotation;
                }
            }
        }
    }
}
