# 検証結果

## 最新: 2026-10-24 指示どおりの動き・視覚・ロボット声

- EditMode **69/69成功**: `artifacts/EditMode-20261024-171156.xml`。新規MotionTests（計画の解析と不正値除去、スキーマ、停止語、プリセット、ロボット声、視野と遮蔽、ボール物理）。
- PlayMode **18/18成功**: `artifacts/PlayMode-20261024-171241.xml`。新規: ボールを出してユーザーへ転がす→視野内外の判定→積み木5段→崩す→持ってくる→目のカメラ画像、腕上げ・ジャンプ・一回転・右90°・前進・追従と停止、指示文から計画→実行中はJevが体を動かさない、ロボット声の設定反映。画像 `artifacts/previews/Motion-arms-up.png`、`Play-blocks-stacked.png`、`Vision-eye.jpg`。
- APK 167,706,371 bytes、SHA256 `8615307AD2D2C941CA8BFEF3BFD1BE99A5187CEB6863757DA83E56C26FDEE3C4`。Windowsビルド・`--desktop-smoke` 成功。Release v1.0.0の添付を差し替え済み（zip SHA256 `56E579C1…EC83A`）。

## 最新: 2026-10-24 文字入力・Jev状態表示

- EditMode **61/61成功**: `artifacts/EditMode-20261024-162800.xml`。新規TextInputTests（゛゜小の変換、200文字上限、送信指示に入力文を含む）とJev状態表示。
- PlayMode **14/14成功**: `artifacts/PlayMode-20261024-162916.xml`。新規: 未接続から文字送信→接続後に送信、二重送信の拒否、ユーザー発言→返答の順で保存、200文字への切り詰め。
- APKビルド成功: `QuestAI/Builds/WhiteRoom-Quest3S.apk`（167,603,007 bytes）。SHA256: `CD4402BC212E576B3263FF35D15E40BC3D2A1B7DD0A9431E005D7D2D5FF9772A`。今回は署名の再検証を実施していない。
- Windowsビルド成功（`artifacts/windows-build-report.txt`）、`--desktop-smoke` 終了コード0・`DESKTOP_SMOKE_OK`（`artifacts/windows-smoke.log`）。
- 実APIで文字入力に声で返答するか、Questでのひらがなキー操作性は未確認。

## 最新: 2026-10-24 モデル別発話・Uキー・配置遊び

- EditMode **53/53成功**: `artifacts/EditMode-20261024-155258.xml`。配置の家具・壁・人・他おもちゃとの重なり拒否、モデル別発話指示／カスタム上書き。
- PlayMode **13/13成功**: `artifacts/PlayMode-20261024-155436.xml`。新規: 実部屋へのボール配置、接近・遊び、再操作のキャンセル、片づけ後の物件登録復元。画像 `artifacts/previews/Playground-ball.png`。
- Windowsビルド成功: `artifacts/windows-build-report.txt`。実行ファイルの `--desktop-smoke` 終了コード0、`DESKTOP_SMOKE_OK`。キー・ネットワーク・実マイクを使わない起動確認。
- APKビルド成功、署名v2検証成功: `QuestAI/Builds/WhiteRoom-Quest3S.apk`（167,586,811 bytes）。SHA256: `FC04D43117C12C9A1DF6E59A102F87DD8794D7B04CCD47397CE8611AAC1A7BF5`。
- 実際の声の試聴、Uキーを使う実マイク会話、Questでのポインター配置と動作負荷は未実施。
- 最新の改修はローカルのみ。以前のGitHub Releaseは更新していない。


## 最新: 2026-10-24 Jev行動の細分化・ユーザーの動き

- EditMode51/51: `artifacts/EditMode-20261024-140010.xml`（新Jev項目の既定値・不正値、しゃがみ/立ち上がり、手振り、指さしの保持時間・PCキー）。
- PlayMode12/12: `artifacts/PlayMode-20261024-140035.xml`。新規: ロボが急いで弧を描いてソファへ行き着席→ユーザーのしゃがみで立ってゆっくり接近→暖炉の指さしで移動ししゃがんで見る。画像 `artifacts/previews/Pose-robot-*.png`。
- Windows版ビルド・`--desktop-smoke` 成功。APK未再ビルド。


## 最新: 2026-10-24 環境認識・歩行・ロボ

- EditMode47/47: `artifacts/EditMode-20261024-130154.xml`（ラベル付け、部品統合、経路が家具を通らない、ユーザー回避、説明文の方向、Jevの移動・存在しない行き先の拒否）。
- PlayMode11/11: `artifacts/PlayMode-20261024-130230.xml`。新規LocomotionTests: ロボ（リビング→暖炉、ボタニカ→カウンター）としろ（寝室→ベッド）が到着・家具外・対象を向く・ユーザーへ0.5〜1.6mまで戻る。画像 `artifacts/previews/Walk-*.png`、`Robot-*.png`。
- Windows版ビルド成功（`artifacts/windows-build-report.txt`）、`WhiteRoom.exe --desktop-smoke` で `DESKTOP_SMOKE_OK`。
- APK未再ビルド。Jev実API・Quest実機での歩行は未確認。


## 最新: 2026-10-24 貼り付け・Windows版

- EditMode30/30: `artifacts/EditMode-20261024-085110.xml`。キー貼り付けの空白・改行・長さ検証、Windows DPAPI暗号化・復号・更新・削除をテスト用文字列で確認。
- PlayMode10/10: `artifacts/PlayMode-20261024-085320.xml`。キーを貼り付けた後、伏字表示と保存結果を確認。画像 `artifacts/previews/Keyboard-UI.png`。
- 元Editorが開いていたため、保存済みソースの検証用コピーでテスト。元Editorをユーザーが閉じた後、元プロジェクトでAPKを生成。
- APK167,271,943 bytes、SHA256 `D647DB34E39617BB3273C54171615E8AA24795BF421F607AEABD0DC90CCD81E4`。v2署名検証成功。Questクリップボード・実マイク・実APIの音声往復は未確認。

## 最新: 2026-10-24 交流の遊び

- EditMode28/28成功: `artifacts/EditMode-20261024-010519.xml`。交流記録の保存・キャラ分離・部屋の重複防止・全データ削除を確認。
- PlayMode10/10成功: `artifacts/PlayMode-20261024-010544.xml`。既存の統合テストに遊び画面、挨拶スタンプ、クイズ3問と結果、お題カード、会話への復帰を追加。
- APK生成・v2署名確認成功。167,269,387 bytes。SHA256 `3DD06C47FBFEED3EB7C4EB88EF8E4E9886790929ABE129373CD0496B5E3DF5D3`。
- 画像 `artifacts/previews/Activities-UI.png`。Quest実機・実API会話は未確認。

## 2026-10-24: 音声会話の開始操作・診断

- APKビルド成功: `QuestAI/Builds/WhiteRoom-Quest3S.apk`、167,264,919 bytes。SHA256 `A995CD52AD17D8774792220C48C7BD5D35FFBDC727548D62B84CC1912250D0AD`。ログ `artifacts/android-build.log`。

- EditMode26/26成功: `artifacts/EditMode-20261024-005231.xml`。
- PlayMode10/10成功: `artifacts/PlayMode-20261024-005352.xml`。PTTを押し続けて接続完了した時の録音開始、接続中に離した場合の非録音を追加。
- マイク診断（4秒録音・再生・メーター）は実装・コンパイル済み。実マイクとスピーカーでの検証は未実施。
- adb接続端末なし。ユーザーも現在はQuestを接続できないと回答。実API接続・音声往復・72Hz試験は未実施。
- GPT-Live仕様を公式 https://developers.openai.com/api/docs/guides/voice-websockets で再確認。既存BYOK方式・モデルは維持。

## 最新: 屋外の動きと環境音（2026-10-18）

- `artifacts/ambient-life.log` 正常終了。中断ベイクの影響で壊れていたBedroom朝のライトマップを再ベイクで修復（`artifacts/bedroom-rebake.log`）。全シーンのプレビューを目視確認。
- EditMode 26/26（新規: 合成音の検証、Wind Impostor・Hearth Embersシェーダー）: `artifacts/EditMode-20261018-093451.xml`。PlayMode 8/8: `artifacts/PlayMode-20261018-093508.xml`。
- APK 167,259,499 bytes、SHA256 `5108B90DEA8A95781A2C903D4D86B3F06C56093AAB96A1B473C401C38B69F527`。
- 音の聞こえ方・鳥の見え方・パーティクル負荷はQuest実機で未確認。

## 最新: Botanica Cafe・炎のリアル化（2026-10-18）

- 適用とBedroomベイク `artifacts/realistic-cafe-bake.log`（途中で中断）、残り4シーンのベイクとプレビュー `artifacts/realistic-cafe-bake2.log`（`RealisticUpgrade.BakeRemainingAndRender`）。Botanica・LivingRoomの画像を目視確認。
- EditMode 23/23: `artifacts/EditMode-20261018-090106.xml`。PlayMode 8/8: `artifacts/PlayMode-20261018-090129.xml`。
- APKビルド成功。166,583,787 bytes、SHA256 `3428AB653451C2857DDFA478072163E08F1DC93BDA1FAF7960886458ECD8310A`。署名の再検証は未実施。
- 火の粉（パーティクル）と炎の揺らぎ光は実行時のみ動作し、静止プレビューには写らない。Quest実機での72Hzは未確認。

## 最新: リアル家具・独自シェーダー・ポストプロセス（2026-10-17）

- `RealisticUpgrade.ApplyBakeAndRender` 正常終了（5シーン×4時間帯を再ベイク）。ログ `artifacts/realistic-bake.log`、微調整 `artifacts/realistic-refresh.log`。全プレビュー画像を目視確認。
- EditMode 23/23: `artifacts/EditMode-20261017-230743.xml`。PlayMode 8/8: `artifacts/PlayMode-20261017-230817.xml`。
- APKビルド成功。150,982,665 bytes、SHA256 `4153C6361F1877E254ADDBD3C98C406AC7BCC89F2176777CCCE68D073F73C35F`。署名の再検証は未実施。
- HDR・トーンマッピング・ブルーム追加とポリゴン数増加により、Quest 3Sの72Hz維持は未確認。実機で最優先に確認すること。

## 最新: 指定note資料に合わせた炎（2026-10-17）

- 根元と外縁の色、グラデーション、先端透過、2層ノイズを更新。`artifacts/fire-gradient.log` 正常終了。LivingRoom-Eveningの生成画像を目視確認。
- EditMode 22/22: `artifacts/EditMode-20261017-212650.xml`。PlayMode 8/8: `artifacts/PlayMode-20261017-212715.xml`。
- APKビルド成功、v2署名検証成功。108,609,073 bytes、SHA256 `682053F9F0EC15C5FDD59E4E63148CA35E21FE36FFD19BB41E7D5B19EC5E75D7`。パスは `QuestAI/Builds/WhiteRoom-Quest3S.apk`。
- Quest実機・実API・72Hz/30分試験は未実施。以下は以前の版の履歴。

## 現行版: 火・ガラス・草木（2026-10-17）

- 最終APKビルド成功: `QuestAI/Builds/WhiteRoom-Quest3S.apk`、108,608,673 bytes。ログ `artifacts/android-build.log`。Vulkan用シェーダーを含むAndroid ARM64 IL2CPPビルド。
- SHA256: `9B3A7D25F31BDC212ABA53BF6F1C7A582DC585E74C9D9EAA70B05D9A27414740`。
- Android SDK apksignerでv2署名検証成功、aapt2で`arm64-v8a`を確認。

- 専用URPシェーダー3種、草木・薪・窓ガラスを実装。適用と20反射更新・20プレビュー出力は `artifacts/natural-delivery.log`、正常終了。
- EditMode **22/22成功**: `artifacts/EditMode-20261017-212037.xml`。火・ガラス・葉のShader.Find / isSupported / コンパイルエラー検査を含む。
- PlayMode **8/8成功**: `artifacts/PlayMode-20261017-212102.xml`。4環境、メニュー、キーボード、Continue、7感情、会話の異常系を確認。通信はテスト用プロバイダー。
- 目視確認: `artifacts/previews/LivingRoom-Evening.png`、`BotanicaCafe-Day.png`。炎・ガラス・鉢植えを確認。遠景の針葉が点状に消える問題を調整後、LivingRoomを再確認。
- この節が以下の旧版より新しい検証記録。元のWhiteChibiを維持。実API・Quest実機・両眼描画・72Hz/30分試験は未実施。MVP完成判定は保留。

## 現行版: グラフィック向上（2026-10-17）

- 5シーン×4時間帯＝20ベイク完了: `artifacts/graphics-upgrade.log`（素材・照明）、`artifacts/graphics-furniture.log`（家具ライトマップUV修正後の再ベイク）。
- EditMode 19/19成功: `artifacts/EditMode-20261017-113803.xml`。
- PlayMode 8/8成功: `artifacts/PlayMode-20261017-113830.xml`。
- 目視確認: `artifacts/previews/LivingRoom-Session.png` ほか各環境のSession/時間帯画像。変更前は `Graphics-Before.png`。家具のまだら模様が解消。
- APKビルド成功: `QuestAI/Builds/WhiteRoom-Quest3S.apk`、108,138,820 bytes。
- SHA256: `B152CE48C07EBDBC3FD720D34795C14750B7C16AB0227F916D7903EFB4699685`。
- キャラクターはWhiteChibiのまま。実機・実API・72Hz/30分試験は未実施（ライトマップ・反射解像度増によるメモリ増を実機で確認すること）。

## 現行版: 元キャラクターへ復元（2026-10-17）

ユーザーの最終指示で、2プロフィールともWhiteChibiへ戻しました。ピクセル調モデルは未使用の試作です。通信の安定性改善と4環境は維持しています。

- EditMode 19/19成功: `artifacts/EditMode-20261017-104542.xml`。
- PlayMode 8/8成功: `artifacts/PlayMode-20261017-104608.xml`。
- 7感情を適用した変形後メッシュの有限値・境界検査を含む。`artifacts/previews/Character-happy.png` で元モデルの表示と手振りを目視確認。
- calm/brightが元Prefab GUID `39e02ac9f7851c24abcb382457147e2c` を参照することを確認。
- 最終APKビルド成功: `QuestAI/Builds/WhiteRoom-Quest3S.apk`、108,150,943 bytes（約103 MiB）。ログ `artifacts/android-build.log`。
- SHA256: `180780A6181EB0BF61FAF184F0935E7D62F8251438A1550E2A7FFE00331FA360`。
- 実機・実API・30分/72Hz試験は未実施。

## 2026-10-17 ピクセル調モデル・通信安定性

- EditMode 19/19成功: `artifacts/EditMode-20261017-102619.xml`。
- PlayMode 8/8成功: `artifacts/PlayMode-20261017-102647.xml`。
- 新モデルの骨格・全表情チャンネル・スキンウェイト・2プロフィールへの割り当てを確認。4環境（BotanicaCafe含む）への切り替え、VRキーボード、Continueを確認。
- サーバーからの終了時に録音を停止、古いミュート要求の失敗を破棄、ミュート失敗後に再接続できることをテスト用プロバイダーで確認。
- モデル生成ログ `artifacts/pixel-final.log`。24,732三角形。元WhiteChibiは保持。
- 最新画像: `artifacts/previews/BotanicaCafe-Session.png`。実機ではなくUnityの実行画面。
- ピクセル版のビルドは成功。その後、ユーザー指示により元キャラクターへ復元したため、ここは試作の検証履歴です。

## 2026-10-03 初回ビルド（履歴）

2026-10-03: Unity 6000.6.0f1 の Android APK ビルド成功（8分4秒）。

- 配布ファイル: `QuestAI/Builds/WhiteRoom-Quest3S.apk`（84,645,529 bytes、約80.7 MiB）。Unity BuildReportの totalSize はAPKファイルサイズとは異なります。
- SHA256: `99EBD42E51A2E82D87931DE3E27BC0E9EE013C6E5C32AC0BB6761E2CD43F43BC`
- APK署名検証: apksigner verify 成功、v2署名。
- aapt2確認: `com.vibecodingclub.questai` / 0.1.0 / ARM64のみ / min SDK32 / target SDK36 / UnityPlayerActivity。
- ビルドログ: `artifacts/android-build2.log`、結果: `artifacts/build-report.txt`。
- Android保存プラグインのJavaコンパイル成功。
- EditMode 18件成功（2026-10-03、`artifacts/editmode-delivery.xml`）。
- PlayMode 5件成功（2026-10-03、`artifacts/playmode-delivery.xml`）。最新の寝室配置とVRキーボード入力・保存・削除を含みます。会話系はテスト用Live/録音実装による検証です。
- `artifacts/previews/Bedroom-Session.png` と `Keyboard-UI.png` を目視確認。日本語表示、キーボードの収まり、キャラクターと家具の配置を確認しました。

描画不具合は、DirectionalベイクとNonDirectional実行設定の不一致を修正して全16環境・時間帯を再ベイク。Animatorの初期姿勢によるモデル変形を止め、寝室の家具がキャラクターを遮る配置も修正しました。実行時スクリーンショットによる白飛び検査を統合テストに追加しています。

実サービス用APIキーおよび接続済みQuest 3Sは利用できていないため、API実接続・Android Keystore/SQLiteの実機動作・72Hz/30分連続試験は未実施です。
