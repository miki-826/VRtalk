白いちびキャラ / Unity用

使い方
1. WhiteChibi_Unity.unitypackage をUnityプロジェクトへインポートします。
2. Assets/WhiteChibi/WhiteChibi.prefab をシーンにドラッグしてください。
3. Animator Controller は用途に合わせて割り当ててください。アニメーションクリップは含めていません。

仕様
・22ボーン、全頂点ウェイト設定済み、最大4ウェイト/頂点。
・Humanoid Avatar設定済み、Tポーズ、身長約1.1m。
・顔パーツはHeadボーンに追従します。指や表情専用ボーンはありません。
・FBX、頬のカラーテクスチャ、マテリアル、Prefabを同梱。
・Unity 2022.3.22f1でHumanoid Avatarの有効性とスキニングを検証。
・マテリアルはBuilt-inのStandardです。URP/HDRPではプロジェクト側のマテリアル変換を行ってください。
・FBXだけを別途使用する場合はテクスチャも一緒に取り込んでください。RigをHumanoid / Create From This Modelに設定します。
・Blender編集元は white_chibi_rigged.blend です。

参考: https://docs.unity3d.com/ja/2022.3/Manual/ConfiguringtheAvatar.html
