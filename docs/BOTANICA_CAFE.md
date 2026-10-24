# ボタニカ・カフェ

2026-10-03: Blenderで作成した10m×7mのカフェを、既存の3環境に加えて4番目の環境として追加。

- メインメニューのお部屋選択から「ボタニカ・カフェ」を選択。
- Scene: `QuestAI/Assets/Scenes/BotanicaCafe.unity`
- Profile: `QuestAI/Assets/Resources/Profiles/BotanicaCafe.asset`
- 再利用用Prefab: `QuestAI/Assets/Generated/Botanica/BotanicaCafe.prefab`
- Blender原本: `model/Botanica/Botanica_Cafe.blend`
- Blender書き出し: `tools/export-botanica.py`。Blenderのbackgroundモードで実行。
- Unity側の再作成: `Quest AI > Import Botanica cafe`。開いているシーンを保存してから実行。4時間帯のライトマップと反射を生成する。

Blenderから三角形化済みジオメトリ・法線・UV・色を移し、UnityのURP材質に変換。文字込み98,012三角形、38メッシュ。床・壁・カウンター・家具には簡略化BoxCollider。Blenderのプロシージャル木目・バンプはUnityの材質にはベイクしていない。透明材質はURPの透過描画に変換。カメラとBlenderのライトは持ち込まず、既存アプリのカメラ・時間帯照明を使用する。

検証済み: 4時間帯のライトマップ、キャラクター位置の家具との非干渉、床の当たり判定、Unityの4時間帯プレビュー。PlayMode 5件成功、0件失敗 (`artifacts/botanica-playmode.xml`)。既存のアプリ統合テストで4環境すべての切り替え・キャラクター表示・セーブ再開を確認。アプリ表示画像: `artifacts/previews/BotanicaCafe-Session.png`。

2026-10-03 見た目のリアル化: 床・壁・木部にPoly Haven(CC0)の色/法線/ラフネス、布と鉢に法線/ラフネスを適用（`BotanicaCafeInstaller.Surfaces`）。Blender出力のUVは使わず、インポート時に三角形ごとのワールド座標ボックス投影UVを生成し、木目は面の長辺方向に揃える。葉は自作シェーダー `QuestAI/Assets/Art/Shaders/BotanicaFoliage.shader`（微風の揺れ・葉ごとの色ムラ・反射プローブを使った透過光）。空はPoly Havenのパノラマ（Skybox/Panoramic）で、`EnvironmentLighting` が時間帯で色と露出を変える。

2026-10-17: APKにも追加シーンを収録。ピクセル調キャラクターと合わせて4環境のPlayModeテストを実施。最新検証結果は docs/VALIDATION.md。Quest実機での性能は未確認。
