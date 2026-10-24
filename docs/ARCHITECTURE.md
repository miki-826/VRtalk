# 実装構成

- `QuestAI/Assets/App/Core`: データ型、列挙値、AppRoot、セッション世代によるキャンセル、時間帯切替。
- `AI`: GPTLiveProviderとILiveProvider、HTTPSのOpenAI/Jevプロバイダー。ISTTProvider/ILLMProvider/ITTSProvider/IBehaviorProviderは差し替え可能。
- `Character`: CharacterBrain、マイク入力、PCMストリーム再生、BodySystem、モデルとのボーン・BlendShape対応。
- `Database`: IMemoryRepository/ISecretStore、Editor用JSON/メモリ実装、Android JNIブリッジ。
- `Plugins/Android/LocalStorage.java`: SQLiteスキーマ1、パラメーター付き検索、記憶書込トランザクション、Keystore暗号化。
- `UI/XR`: 日本語UGUI、VRキー入力、コントローラー/手ポインター、PTT。
- `Editor`: シーン・プロフィール・表情の生成、時間帯別ベイク、プレビュー出力、APKビルド。

## 非同期と失敗

接続ごとに世代番号とCancellationTokenを保持し、古い世代のコールバック・記憶抽出結果を破棄します。送信はSemaphoreSlimで直列化。マイクの切り替えはLiveのmute/unmute応答を待ちます。Pause時はマイク・音声キュー・通信を停止。復帰では自動録音しません。

認識テキストと開始/終了時刻を保存し、そのメッセージIDを抽出記憶に紐付けます。記憶はプロフィールで絞り込み、日本語文字bigramの一致・重要度・新しさで順位付けします。Jevの失敗時は中立動作へ戻し、UIに失敗を表示します。Jevの文字列から任意のAnimator状態やメソッドを呼び出しません。

## モデル差し替え

`Assets/Resources/Profiles` のCharacterProfile.prefabを変更し、新しいPrefabにCharacterRigBindingとBodySystemを設定します。頭・胸・腕のボーンとfaceMeshes、各BlendShape名を対応付けます。プロフィールIDを保持すれば記憶は維持されます。現在のアセットは白いちびキャラ専用のボーン姿勢調整を含み、別リグの腕の既定姿勢はCharacterRigBindingのleftArmRestEuler/rightArmRestEulerで調整します。

## 公式資料

- [GPT-Live-1](https://developers.openai.com/api/docs/models/gpt-live-1)
- [Live WebSocket](https://developers.openai.com/api/docs/guides/voice-websockets?api=live)
- [Live conversations](https://developers.openai.com/api/docs/guides/live-conversations)
- [Jev API](https://docs.typesafe.ai/api)
- [Meta Unity XR](https://developers.meta.com/horizon/documentation/unity/unity-xr-plugin/)

依存関係はUnity側のpackages-lock.jsonで固定。APIの可用性・利用枠は利用アカウントで確認してください。

## 描画と姿勢の整合性

時間帯別ライトマップはNonDirectionalでベイクし、照明設定・保存データ・実行時のモードを一致させています。Directionalの方向テクスチャを欠いた切替で起きる白飛びは、実行画像の輝度チェックでも検出します。各時間帯のライトマップUV割り当て、SHプローブ、Reflection Probeを保存します。

MVPではBodySystemがボーン姿勢を制御するため、空のHumanoid Animatorによるリターゲットを停止します。新しいAnimator動作を導入する場合は、この所有関係を変更して競合を避けてください。
