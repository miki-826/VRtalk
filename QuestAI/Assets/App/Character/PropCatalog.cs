using System;
using UnityEngine;

namespace QuestAI
{
    // Room props the character can pick up and throw, built from CC0 Kenney furniture-kit models (AnimationLibraryBuilder fills it).
    public sealed class PropCatalog : ScriptableObject
    {
        [Serializable] public struct Entry { public string label; public GameObject model; public float size, mass; }
        public Entry[] entries = Array.Empty<Entry>();
        public Entry Get(string label) { foreach (var e in entries) if (e.label == label) return e; return default; }
    }
}
