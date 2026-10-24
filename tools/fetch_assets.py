"""Fetch pinned, redistributable inputs. No API keys are used."""
from pathlib import Path
import urllib.request, zipfile, json, shutil, hashlib

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / 'QuestAI/Assets'
CACHE = ROOT / 'artifacts/downloads'
CACHE.mkdir(parents=True, exist_ok=True)

def fetch(url, path):
    path = Path(path); path.parent.mkdir(parents=True, exist_ok=True)
    if not path.exists():
        req = urllib.request.Request(url, headers={'User-Agent':'QuestAI development asset importer (personal project)'})
        with urllib.request.urlopen(req, timeout=90) as response, path.open('wb') as out:
            shutil.copyfileobj(response, out)
    print(path.name, path.stat().st_size, hashlib.sha256(path.read_bytes()).hexdigest(), flush=True)

fetch('https://kenney.nl/media/pages/assets/furniture-kit/440e0608a4-1677580847/kenney_furniture-kit.zip', CACHE/'furniture.zip')
with zipfile.ZipFile(CACHE/'furniture.zip') as z:
    for name in z.namelist():
        if name.endswith('/') or '..' in Path(name).parts: continue
        if '/FBX format/' in name or name.lower().endswith('license.txt'):
            out=ASSETS/'ThirdParty/Kenney'/name
            out.parent.mkdir(parents=True,exist_ok=True);out.write_bytes(z.read(name))
fetch('https://raw.githubusercontent.com/notofonts/noto-cjk/main/Sans/OTF/Japanese/NotoSansCJKjp-Regular.otf', ASSETS/'Resources/Fonts/NotoSansJP.otf')
fetch('https://raw.githubusercontent.com/notofonts/noto-cjk/main/Sans/LICENSE', ASSETS/'ThirdParty/Noto/LICENSE.txt')
for name in ['wood_floor_deck', 'rock_boulder_dry']:
    try:
        fetch(f'https://dl.polyhaven.org/file/ph-assets/Textures/jpg/1k/{name}/{name}_diff_1k.jpg', ASSETS/f'ThirdParty/PolyHaven/{name}.jpg')
    except Exception as exc:
        print('ASSET_DOWNLOAD_FAILED',name,str(exc),flush=True)
source=ROOT/'model/character/chibi/UnityReady/Assets/WhiteChibi'
target=ASSETS/'Characters/WhiteChibi'
shutil.copytree(source,target,dirs_exist_ok=True)
print('Assets ready',flush=True)
