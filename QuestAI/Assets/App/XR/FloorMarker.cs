using UnityEngine;

namespace QuestAI
{
    // A flat ring on the floor: where the character was told to go, or where the user is about to teleport.
    public class FloorMarker : MonoBehaviour
    {
        public static readonly Color Amber = new Color(.95f, .72f, .35f), Mint = new Color(.45f, .9f, .75f), Blocked = new Color(.85f, .3f, .25f);
        const int Segments = 40; const float Lift = .012f;
        Material material; Mesh mesh; float shown;
        public bool Visible => gameObject.activeSelf;
        public static FloorMarker Create(string name, Color color, float radius = .2f)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); var marker = go.AddComponent<FloorMarker>();
            marker.Build(radius); marker.Tint(color); go.SetActive(false); return marker;
        }
        // An outer ring plus a small centre dot, in one mesh facing up.
        void Build(float radius)
        {
            mesh = new Mesh { name = "Floor marker" };
            var vertices = new Vector3[Segments * 4 + Segments + 1]; var triangles = new int[Segments * 12 + Segments * 3];
            float inner = radius * .78f, dot = radius * .22f;
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2 / Segments, b = (i + 1) * Mathf.PI * 2 / Segments;
                Vector3 da = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)), db = new Vector3(Mathf.Sin(b), 0, Mathf.Cos(b));
                int v = i * 4, t = i * 6;
                vertices[v] = da * inner; vertices[v + 1] = da * radius; vertices[v + 2] = db * radius; vertices[v + 3] = db * inner;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2; triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
            }
            int centre = Segments * 4, tri = Segments * 6; vertices[centre] = Vector3.zero;
            for (int i = 0; i < Segments; i++) { float a = i * Mathf.PI * 2 / Segments; vertices[centre + 1 + i] = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * dot; }
            for (int i = 0; i < Segments; i++) { triangles[tri++] = centre; triangles[tri++] = centre + 1 + i; triangles[tri++] = centre + 1 + (i + 1) % Segments; }
            System.Array.Resize(ref triangles, tri);
            mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            var shader = Shader.Find("Universal Render Pipeline/Unlit"); material = new Material(shader ? shader : Shader.Find("Sprites/Default"));
            // Seen from any side, including from below floor level while crouching.
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0);
            var renderer = GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        }
        public void Tint(Color color) { if (!material) return; material.color = color; if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color); }
        public void Show(Vector3 point)
        {
            transform.SetPositionAndRotation(point + Vector3.up * Lift, Quaternion.identity);
            if (!gameObject.activeSelf) { shown = Time.time; gameObject.SetActive(true); }
        }
        public void Hide() { if (gameObject.activeSelf) gameObject.SetActive(false); }
        // A slow pulse so the ring reads as a live target rather than part of the floor.
        void Update() { transform.localScale = Vector3.one * (1 + .12f * Mathf.Sin((Time.time - shown) * 5)); }
        void OnDestroy() { if (material) Destroy(material); if (mesh) Destroy(mesh); }
    }
}
