"""Download the CC0 Poly Haven furniture used by RealisticUpgrade.cs.

FBX + JPG/PNG maps only (EXR normals are swapped for the JPG versions Unity imports directly).
Usage: python -I tools/download_polyhaven_models.py
"""
import json
import os
import sys
import urllib.request

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "QuestAI", "Assets", "ThirdParty", "PolyHaven", "Models")
# asset id -> texture resolution (large, close-up pieces get 2K)
ASSETS = {
    "sofa_03": "2k",
    "mid_century_lounge_chair": "1k",
    "modern_coffee_table_01": "1k",
    "wooden_display_shelves_01": "1k",
    "potted_plant_02": "1k",
    "potted_plant_04": "1k",
    "GothicBed_01": "2k",
    "ClassicNightstand_01": "1k",
    "vintage_oil_lamp": "1k",
    "modern_wooden_cabinet": "1k",
    "GreenChair_01": "1k",
    "round_wooden_table_01": "1k",
    "book_encyclopedia_set_01": "1k",
    "throw_pillows_01": "1k",
    "ceramic_vase_01": "1k",
    # Botanica cafe
    "dining_chair_02": "1k",
    "round_wooden_table_02": "1k",
    "modern_arm_chair_01": "1k",
    "potted_plant_01": "1k",
}
# Editor-only sources for the wind-swayed tree impostors outside the windows (never referenced by scenes).
TREES = {"fir_sapling": "1k", "tree_small_02": "1k"}
TREE_ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "QuestAI", "Assets", "ThirdParty", "PolyHaven", "Trees")
WANTED = ("diff", "nor_gl", "rough", "metal", "ao", "alpha", "opacity")


def fetch(url, dest):
    if os.path.exists(dest) and os.path.getsize(dest) > 0:
        return
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    req = urllib.request.Request(url, headers={"User-Agent": "QuestAI-asset-fetch"})
    with urllib.request.urlopen(req, timeout=120) as r, open(dest + ".part", "wb") as f:
        f.write(r.read())
    os.replace(dest + ".part", dest)


def main():
    jobs = [(a, r, ROOT) for a, r in ASSETS.items()] + [(a, r, TREE_ROOT) for a, r in TREES.items()]
    for asset, res, root in jobs:
        req = urllib.request.Request("https://api.polyhaven.com/files/" + asset, headers={"User-Agent": "QuestAI-asset-fetch"})
        with urllib.request.urlopen(req, timeout=60) as r:
            files = json.loads(r.read().decode("utf-8"))
        folder = os.path.join(root, asset)
        fbx = files["fbx"][res]["fbx"]
        fetch(fbx["url"], os.path.join(folder, asset + ".fbx"))
        # Top-level map keys look like "Diffuse"/"nor_gl" or "<part>_diff"; take JPG (PNG for alpha) at the chosen resolution.
        for key, entry in files.items():
            if key in ("blend", "gltf", "usd", "fbx"):
                continue
            label = {"Diffuse": "diff", "Rough": "rough", "Metal": "metal", "AO": "ao"}.get(key, key)
            if not any(label == w or label.endswith("_" + w) for w in WANTED):
                continue
            formats = entry.get(res) or entry.get("1k")
            if not formats:
                continue
            fmt = "png" if ("alpha" in label or "opacity" in label) and "png" in formats else ("jpg" if "jpg" in formats else "png")
            if fmt not in formats:
                continue
            fetch(formats[fmt]["url"], os.path.join(folder, "textures", asset + "_" + label + "." + fmt))
        print("ok", asset, flush=True)
    with open(os.path.join(ROOT, "LICENSE.txt"), "w", encoding="utf-8") as f:
        f.write("Models from Poly Haven (https://polyhaven.com), CC0 1.0 Universal. Assets: " + ", ".join(ASSETS) + "\n")
        f.write("Tree impostor sources (../Trees, editor only): " + ", ".join(TREES) + "\n")
        f.write("../snowy_forest_path_01_panorama.jpg: HDRI 'Snowy Forest Path 01' (tonemapped JPG). ../Textures: poly_wool_herringbone, bark_brown_02. All CC0 1.0.\n")


if __name__ == "__main__":
    sys.exit(main())
