# ピクセル調キャラクター

**現在は未使用の試作です。** 2026-10-17の追加指示「キャラも最初の感じに戻して」により、calm/brightは滑らかなWhiteChibiへ復元。ピクセル版と接地影・表情差分は比較用に保存しています。現行アプリへ自動適用しないでください。

2026-10-17: ユーザー指定の参考動画 https://www.youtube.com/watch?v=GZUdsaH7uZk&t=761s の角ばったレトロ3D表現を参考に、既存の白いちびキャラクターをボクセル調へ変換。動画のモデル自体は使用していません。

- プレイ内容、音声会話、記憶、プロフィールIDと声、環境選択、Continueは維持。
- 新Prefab: `QuestAI/Assets/Generated/VoxelCharacter/PixelChibi.prefab`。
- 元Prefab: `QuestAI/Assets/Generated/WhiteChibi.prefab`。元モデルとBlenderデータは保存。
- 体の格子は2.2cm、顔は約1cm。外側に露出するセル面のみ生成し、合計24,732三角形。
- 元のスキンウェイトから各セルの主骨を選び、セル単位で剛体追従させる。バインド姿勢とBlendShapeチャンネルを引き継ぎ、頭・腕の動作、瞬き、7感情、口パク用の既存制御を使用。セル表面は微小に重ねて、継ぎ目の隙間を抑える。
- 材質は元の配色を使うURP Lit、低い光沢、輪郭が読みやすいフラット法線。
- 足元には32×32の段階的な接地影を追加。2三角形の透明Quadで、リアルタイムシャドウを追加せず床との接地感を補う。平坦な床向けの表現であり、物理的に正確な影ではない。
- 「心配」は眉の上がり方を変更し、「悲しい」と区別。7感情を実際に適用して変形後メッシュを検査し、`artifacts/previews/Pixel-*.png` に確認画像を生成する。
- UIの日本語は読みやすさを維持。全画面の低解像度化やVR画面のモザイク処理は行わない。
- 今回の変更対象はキャラクターモデル。4環境の建物・家具は既存内容を維持。

再生成・適用: Unityメニュー `Quest AI > Use pixel character`（ユーザーが再採用を希望した場合のみ）。元に戻す場合は `Quest AI > Restore original character`。通常の `Generate content and configure` は元のWhiteChibiを設定する。

検証結果と配布APKは docs/VALIDATION.md。Quest実機性能・実API会話は未確認。
