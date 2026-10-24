# アセット出典・ライセンス

取得日: 2026-09-19。購入アセットは使用していません。

|素材|出典|ライセンス|同梱場所|
|---|---|---|---|
|Kenney Furniture Kit|[Kenney Furniture Kit](https://kenney.nl/assets/furniture-kit)|CC0 1.0|`QuestAI/Assets/ThirdParty/Kenney`|
|Poly Haven wood_floor_deck|[Poly Haven wood_floor_deck](https://polyhaven.com/a/wood_floor_deck)|CC0 1.0|`QuestAI/Assets/ThirdParty/PolyHaven/wood_floor_deck.jpg`|
|Poly Haven rock_boulder_dry|[Poly Haven rock_boulder_dry](https://polyhaven.com/a/rock_boulder_dry)|CC0 1.0|`QuestAI/Assets/ThirdParty/PolyHaven/rock_boulder_dry.jpg`|
|Poly Haven concrete_floor_01 / painted_plaster_wall / oak_veneer_01 / rough_linen / clay_plaster（2026-10-03取得）|[Poly Haven Textures](https://polyhaven.com/textures)|CC0 1.0|`QuestAI/Assets/ThirdParty/PolyHaven/Botanica`（ボタニカ・カフェ用。albedoはBlender配色に合わせて色調整、armはURP用マスクPNGへ変換）|
|Poly Haven eilenriede_park（HDRI tonemapped JPG、4096×2048へ縮小、2026-10-03取得）|[Poly Haven eilenriede_park](https://polyhaven.com/a/eilenriede_park)|CC0 1.0|`QuestAI/Assets/ThirdParty/PolyHaven/Botanica/eilenriede_park_panorama.jpg`|
|Poly Haven 3Dモデル15点（sofa_03 / mid_century_lounge_chair / modern_coffee_table_01 / wooden_display_shelves_01 / modern_wooden_cabinet / potted_plant_02 / potted_plant_04 / GothicBed_01 / ClassicNightstand_01 / vintage_oil_lamp / GreenChair_01 / round_wooden_table_01 / book_encyclopedia_set_01 / ceramic_vase_01 / throw_pillows_01 / round_wooden_table_02 / dining_chair_02 / modern_arm_chair_01 / potted_plant_01、FBX＋1K/2K JPG、2026-10-17取得）|[Poly Haven Models](https://polyhaven.com/models)|CC0 1.0|`QuestAI/Assets/ThirdParty/PolyHaven/Models`（取得スクリプト `tools/download_polyhaven_models.py`。roughness/metal/AOはURP用マスクPNGへ変換）|
|Poly Haven fir_sapling / tree_small_02（2026-10-18取得、エディタでの木の撮影専用。APKには撮影画像のみ）|[Poly Haven Models](https://polyhaven.com/models)|CC0 1.0|`QuestAI/Assets/ThirdParty/PolyHaven/Trees`|
|Poly Haven snowy_forest_path_01（HDRI tonemapped JPG、2026-10-17取得）|[Poly Haven snowy_forest_path_01](https://polyhaven.com/a/snowy_forest_path_01)|CC0 1.0|`QuestAI/Assets/ThirdParty/PolyHaven/snowy_forest_path_01_panorama.jpg`|
|Poly Haven poly_wool_herringbone / bark_brown_02（1K、2026-10-17取得）|[Poly Haven Textures](https://polyhaven.com/textures)|CC0 1.0|`QuestAI/Assets/ThirdParty/PolyHaven/Textures`|
|RobotExpressive（Tomás Laulhé / Quaternius作、Don McCurdy改変。three.js r160 examples、2026-10-24取得。glbを `tools/convert-robot.py` でFBXへ変換）|[three.js RobotExpressive](https://github.com/mrdoob/three.js/tree/r160/examples/models/gltf/RobotExpressive)|CC0 1.0|`QuestAI/Assets/ThirdParty/RobotExpressive`（歩行・手振り等14アニメーションと表情モーフ3種。キャラクター「ロボ」）|
|Noto Sans CJK JP|[Noto Sans CJK JP](https://github.com/notofonts/noto-cjk)|SIL Open Font License 1.1|`QuestAI/Assets/Resources/Fonts/NotoSansJP.otf`|

Kenney原文はThirdParty/Kenney内のLicenseファイル、Noto原文はThirdParty/Noto/LICENSE.txtに同梱しています。Poly Havenの[ライセンス](https://polyhaven.com/license)はCC0で、同梱画像からUnityマテリアルを作成しています。無料素材を使用した壁・床・梁・暖炉・窓などの部屋構成とシーン生成コードはこのプロジェクトで制作しています。

白いちびキャラは利用者提供の `model/character/chibi` が原本です。その複製にURPマテリアル・表情BlendShape・ボーン対応を追加しました。提供モデルの権利条件は原本に従います。

Meta XR SDK Core/Interactionは205.0.0、Audioは85.0.0。Unity/OpenXR/URP/Meta/Newtonsoftのパッケージにはそれぞれのライセンスが適用され、Package Managerで取得したパッケージ内のLICENSE等を参照してください。これらSDKをCC0とは扱っていません。

## 取得ファイルのSHA-256

- `wood_floor_deck.jpg`: `1f2c623e87b0ee905f6f4ec8664dd19b4b135207c96e86198ff2919770b47166`
- `rock_boulder_dry.jpg`: `20f653a42fb24f42aa40641663070d578678da49af18cc1049dbdd4ed8679f3c`
- `RobotExpressive.glb`: `047f5e5fb3bb6d378bd1df16ca6137f2a596c99b3a1b5690b4020c05aaf6f319`
- `NotoSansJP.otf`: `68a3fc98800b2a27b371f2fb79991daf3633bd89309d4ffaa6946fd587f375b5`

環境音（風・鳥のさえずり・薪の音）は録音素材を使わず `ProceduralAudio.cs` で実行時に合成しています。
