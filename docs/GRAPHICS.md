# グラフィック改善

## 屋外の動きと環境音（2026-10-18）

- **風で揺れる木**: Poly Haven の `fir_sapling`（雪の部屋）と `tree_small_02`（Botanica）をエディタ内で撮影したインポスター（十字の板）を窓の外に配置。`QuestAI/Wind Impostor` が突風に合わせて幹ごと傾き、枝先が震える。雪の部屋では上向きの枝に雪を乗せる。元モデル（`ThirdParty/PolyHaven/Trees`）はエディタ専用でAPKに含まれない。
- **雪 / 落ち葉**: 窓の外だけに降るパーティクル。雪の部屋は雪（約3000粒）、Botanicaは景色の写真が秋なので落ち葉。風の強さで流れる向きが変わり、時間帯の明るさに合わせて色を調整。
- **鳥**: 朝〜夕方に15〜40秒間隔で1〜6羽が窓の外を横切る（羽ばたきと滑空）。夜は出ない。
- **音（録音素材なし・実行時に合成）**: ガラス越しの風音（突風で大きくなる）、木からの鳥のさえずり（朝が最も多く、夜はなし）、暖炉の薪のはぜる音。Meta XR Audioで立体音響。
- **炎の揺れ**: 炎の板の揺れ幅を拡大（左右＋前後）。実行時は火の粉と揺らぐ暖炉光も動く。
- 実装: `App/Core/AmbientLife.cs`（風・鳥・音の制御）、`App/Core/ProceduralAudio.cs`（音の合成）、`Editor/RealisticUpgrade.Ambient.cs`（配置）。再現: `Quest AI > Add ambient life (snow, wind trees, birds, sounds) and render`。静止プレビューでは木の揺れ・鳥・音は確認できない。

## Botanica Cafe と炎のリアル化（2026-10-17）

- **Botanica Cafe**: Blenderの統合メッシュ（`FURNITURE_*`、`PLANTS_*`）を非表示にし、当たり判定（`Simple collisions`）の位置・向きから Poly Haven モデルを配置。テーブル `round_wooden_table_02`（天板高さを元に合わせカップ類を維持）、椅子 `dining_chair_02`（背もたれ→座面の向き）、ラウンジ `modern_arm_chair_01`、床置き植物 `potted_plant_01/02`、棚・カウンター・小鉢 `potted_plant_04`。当たり判定・キャラクター位置は変更なし。全素材が `QuestAI/Realistic Surface`。
- **炎 `QuestAI/Hearth Fire`**: 記事の考え方（高さ・中心のグラデーション、先端透過、ノイズ歪み）を維持し、4オクターブ乱流、炎の舌の分裂・ちぎれ、黒体放射の色、白熱した芯、HDR出力（ブルームで発光）、揺らぎを追加。60°ずつ3枚の板で立体感。
- **薪・熾火 `QuestAI/Hearth Embers`**: 樹皮テクスチャ（bark_brown_02）に脈動する赤熱のひび割れ。下部ほど焦げて高温。交差した薪2本を追加。
- **火の粉**（パーティクル、加算合成）と **揺らぐ暖炉光**（リアルタイム点光源＋`FireFlicker`、影なし）。
- 全Poly HavenモデルでUnity 6の自動Mesh LODを有効化（遠景のポリゴン削減）。
- 再現: `Quest AI > Realistic upgrade (models, shaders, post) and bake`。

## リアル化（2026-10-17）

目標はPS5級の見た目だが、Quest 3Sはモバイル GPU で両眼描画のため、同等の表現は物理的に不可能。ベイク照明前提で「写実的な素材・形状・質感・色調」を最大限に寄せた。

- **3Dモデル**: Kenneyのローポリ家具を Poly Haven の CC0 フォトスキャン/高精細モデル（実寸）に置換。ソファ、ラウンジチェア、コーヒーテーブル、棚、キャビネット、ゴシックベッド、ナイトスタンド、オイルランプ、椅子、丸テーブル、観葉植物、百科事典、花瓶。Kenney原本は各シーンの `Kenney originals`（非アクティブ）に保持し再実行可能。コーヒーメーカーとフロアランプは対応モデルがないためKenneyのまま新シェーダーを適用。
- **シェーダー `QuestAI/Realistic Surface`**（`Assets/Art/Shaders/RealisticSurface.*`）: URP Litの入力をそのまま使い SRP Batcher 互換。追加要素は、AO・ライトマップ輝度・水平線による鏡面遮蔽、ベイク面へのキーライトGGXハイライト（`EnvironmentLighting` が時間帯ごとに `_QuestAIKeyLightDir/Color` を設定）、クリアコート（床・木製家具のニス）、布のシーン（Charlie分布）、肌の散乱（キャラクター）、葉の透過。キーワード `_QUESTAI_FABRIC/_SKIN/_FOLIAGE` と `_CLEARCOAT`。
- **ポストプロセス**: HDR(R11G11B10)、ACESトーンマッピング、1/4解像度ブルーム、露出・コントラスト・色温度。プロファイル `Assets/Resources/Graphics/CinematicGrade.asset` を `AppRoot` が実行時に適用。
- **その他**: 窓の外を写真パノラマ（snowy_forest_path_01）に変更し手続き針葉樹・雪面を非表示。反射プローブのボックス投影。ラグにウールのヘリンボーン素材。キャラクター足元に接地影（ベイク部屋では動的キャラの影が出ないため）。
- 再現: `python -I tools/download_polyhaven_models.py` → Unityメニュー `Quest AI > Realistic upgrade (models, shaders, post) and bake`（`QuestAI.Editor.RealisticUpgrade.ApplyBakeAndRender`）。素材だけの調整は `RealisticUpgrade.RefreshMaterialsAndPreview`。
- 既知の制限: 観葉植物・丸テーブルの一部メッシュはライトマップUVの自動生成に失敗（"Could not pack uvs"）。目立つまだらは見られないが要観察。APKは約108MB→151MB。

## 炎の資料への調整（2026-10-17）

参考: [T.K.「Unity AIで作った炎シェーダーを改良する」](https://note.com/easy_snipe8792/n/n85fbeaea220b)。高さと中心からの距離による黄→赤のグラデーション、先端の透明化、2層のノイズによる歪みという考え方を既存URPシェーダーへ反映。記事の画像・テクスチャは取り込まず、形状マスクとノイズは手続き計算する独自実装。

`HearthFire.shader` の Root / center color、Outer / tip color、Gradient power、Distortion strength、Tip fade、Noise flow A/B から調整可能。既定値は黄色の中心・赤橙色の外側、Gradient power 1.5、歪み0.12。先端は高さに応じて透過。VR両眼用マクロと既存の交差メッシュを維持。Bloomの追加はしていない。

## 火・ガラス・草木（2026-10-17）

- `HearthFire.shader`: 上昇するノイズ、炎の先端の揺れ、黄白色の芯と赤橙色の外縁、透明な境界。交差する板で横からの見え方も確保し、暖炉に焦げた薪を追加。
- `ArchitecturalGlass.shader`: 視線角度によるフレネル反射、淡い透過色、室内の反射プローブ。窓とBotanicaのガラスへ適用。レイトレーシングや画面屈折は使用しない。
- `NaturalLeaves.shader`: 曲がった葉、葉脈、葉ごとの色差、逆光の透け感、風揺れ。針葉樹・窓外の草・鉢植えを生成し、元の鉢を維持。Botanicaの既存葉にも葉脈を追加。
- 草木はプロシージャル形状で、写真測量モデルではない。新しい葉にはリアルタイムの影を追加していない。Quest実機で透過描画の負荷と遠景のちらつきを確認すること。
- 既存の無料素材を維持し、新しい外部素材は追加していない。5シーン×4時間帯の反射を更新。

再生成: Unityメニュー `Quest AI > Upgrade fire glass and plants`、または `QuestAI.Editor.NaturalSceneUpgrade.ApplyAndRender`。生成先は `Assets/Generated/Natural`。シェーダーは `Assets/Art/Shaders`。

2026-10-17。キャラクターは元の滑らかなWhiteChibi、ゲーム内容は維持。

## 素材と模様

- 既存のPoly Haven CC0素材を再利用。木材・塗り壁・布に法線と粗さ、遮蔽マップを適用。
- 大きな木部・壁の色マップは最大2K、法線/マスクは1K。ミップマップと異方性フィルタ4を使用。
- 古い部屋の背面壁・天井を塗り壁に変更し、木製の梁・床・窓枠との材質差を作る。
- キューブの引き伸ばされたUVをワールド座標基準へ変更。木材1.2m、壁2m、布0.3mの繰り返し。法線マップ用接線も再計算。
- 既存家具とBotanicaCafeの形状・配置、当たり判定は維持。

## 照明

- 暖色の夕方は残し、過度な赤みを抑えた環境光へ調整。
- ベイク解像度12→20 texels/m、直接光16→64サンプル、間接光32→128、環境光16→64。
- 0.3m範囲の軽いベイクAOで家具と床、壁との接点を補強。
- 反射プローブ128→256。朝昼夕夜それぞれを再生成。
- リアルタイムGI・画面空間AOは追加しない。MSAA4、描画解像度倍率1は維持。

## 家具（Claude Code追加分）

- Kenney家具FBXにライトマップ用UVが無く（generateSecondaryUV=0）、重なったUV0へベイクされてソファ・テーブル・ベッドにまだら模様が出ていた。全140モデルでライトマップUV生成を有効化（余白8）。
- Kenney素材を使う家具のライトマップ密度を2倍にし、接地部の陰影を鮮明化。
- 家具素材の質感を分離: 木材 smoothness .32〜.36、布 .04〜.06、金属 metallic .6〜.7。
- 一括実行メソッド `QuestAI.Editor.GraphicsUpgrade.UpgradeBakeAndRender`（適用→全20ベイク→接線更新→確認画像）。ログ `artifacts/graphics-furniture.log`。

## 再現

Unityメニュー `Quest AI > Upgrade room graphics and bake`。既存シーンに適用し、全時間帯をベイクする。`Generate content and configure` の再生成にも素材・UV・照明設定の適用を組み込んだ。再生成後は通常どおりBakeが必要。

変更前の画像は `artifacts/previews/Graphics-Before.png`。改善後の画像、テスト、APKはdocs/VALIDATION.mdを参照。

実機のフレーム時間とメモリは未測定。テクスチャ・反射・ライトマップは前版よりメモリが増える可能性があるため、Questで72Hzと30分試験を確認する。
