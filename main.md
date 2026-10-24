# VR AI Character — Quest 3S単体動作仕様書

## 1. 概要

本アプリは、**Meta Quest 3S単体で起動・利用できるVR AIキャラクターアプリ**とする。

ユーザーはVR空間内でAIキャラクターと自然な音声会話を行う。

AIキャラクターは単純に音声で返答するだけではなく、

* ユーザーを見る
* 表情を変える
* ジェスチャーを行う
* ユーザーとの距離に反応する
* 沈黙や視線方向に反応する
* 過去の会話を記憶する
* 自発的に話しかける

といった振る舞いを行い、VR空間内に「存在している」感覚を目指す。

完成したアプリはAndroid APKとしてMeta Quest 3Sへインストールし、**PCなしで単体動作**させる。

---

# 2. 最重要要件

## 2.1 Quest単体動作

アプリ実行時には以下を使用しない。

* PCVR
* Quest Link
* Air Link
* SteamVR
* PC上のサーバープログラム

開発にはWindows PCとUnityを利用するが、実行時はQuest 3Sのみで完結する。

```text
Meta Quest 3S
    │
    │ Android APK
    ▼
Unity Application
    │
    ├─ VR Rendering
    ├─ Character Control
    ├─ Microphone
    ├─ Memory
    ├─ UI
    └─ AI API Communication
            │
            │ HTTPS
            ▼
      External AI APIs
```

---

# 3. 3Dモデルについて

3Dキャラクターモデルの制作工程は、本仕様書の対象外とする。

3Dモデルは別途制作し、最終的にUnityへインポート可能な形式として提供する。

本アプリ側では、キャラクターモデルに以下が存在することを前提とする。

* Humanoid Rig
* Animator対応
* 表情用BlendShape
* LipSync用BlendShapeまたはViseme
* Eye Boneまたは視線制御可能な構造
* Head Bone
* Upper Body Bone
* Quest向けに最適化されたMesh
* Quest向けMaterial / Shader

アプリ側ではモデルを差し替え可能な構造とする。

---

# 4. 技術スタック

## 4.1 基本構成

| 項目                | 採用候補                    |
| ----------------- | ----------------------- |
| Game Engine       | Unity 6系                |
| XR Runtime        | OpenXR                  |
| Quest SDK         | Meta XR Core SDK        |
| Interaction       | Meta XR Interaction SDK |
| Rendering         | URP                     |
| Audio             | Meta XR Audio SDK       |
| Database          | SQLite                  |
| Build Target      | Android                 |
| CPU Architecture  | ARM64                   |
| Scripting Backend | IL2CPP                  |
| AI通信              | HTTPS / WebSocket       |
| STT               | 外部AI API                |
| LLM               | 外部AI API                |
| Behavior AI       | Jev                     |
| TTS               | 外部AI API                |

---

# 5. システム全体構成

```text
┌──────────────────────────────────┐
│         Meta Quest 3S            │
│                                  │
│        Unity Android APK         │
│                                  │
│ ┌──────────────────────────────┐ │
│ │ XR Layer                     │ │
│ │                              │ │
│ │ OpenXR                       │ │
│ │ Meta XR Core                 │ │
│ │ Interaction SDK              │ │
│ │ Audio SDK                    │ │
│ └──────────────┬───────────────┘ │
│                │                 │
│ ┌──────────────▼───────────────┐ │
│ │ Perception System            │ │
│ │                              │ │
│ │ Head Pose                    │ │
│ │ Head Direction               │ │
│ │ Hand / Controller Input      │ │
│ │ Distance                     │ │
│ │ Microphone                   │ │
│ │ Silence Duration             │ │
│ └──────────────┬───────────────┘ │
│                │                 │
│         CharacterBrain          │
│                │                 │
│      ┌─────────┼──────────┐      │
│      ▼         ▼          ▼      │
│     Jev       LLM       Memory   │
│  Behavior    Speech     SQLite   │
│      │         │                 │
│      └────┬────┘                 │
│           ▼                      │
│       Action System              │
│                                  │
│ Speech / Face / Look / Gesture   │
│ Movement / LipSync               │
│                                  │
└──────────────┬───────────────────┘
               │
               │ HTTPS
               ▼
        External AI APIs
```

---

# 6. メインメニュー

メインメニューはシンプルな構成とする。

```text
VR AI Character

[ Continue ]

[ Character ]

[ Environment ]

[ Settings ]

[ Start ]
```

---

# 7. Continue

前回のプレイ状態を保存し、次回起動時に復元可能とする。

保存対象例：

* 選択中キャラクター
* 選択中Environment
* CharacterState
* 最終会話時間
* 関係性
* 長期記憶
* 各種設定

Continueを選択すると、

```text
前回Character
+
前回Environment
+
前回State
```

を復元してセッションを開始する。

---

# 8. Character選択

複数キャラクターを切り替え可能とする。

```text
Character

[ Character A ]

[ Character B ]

[ Character C ]
```

キャラクターごとにCharacterProfileを保持する。

## CharacterProfile

```text
CharacterProfile

id
name
model
voice
personality
systemPrompt
animationSet
defaultEmotion
personalSpace
initiative
memoryId
```

キャラクターモデルそのものとは分離する。

---

# 9. Environment選択

複数のVR Environmentを選択可能とする。

MVP候補：

* Bedroom
* Living Room
* Cafe

```text
Environment

[ Bedroom ]

[ Living Room ]

[ Cafe ]
```

各EnvironmentにはEnvironmentProfileを持たせる。

## EnvironmentProfile

```text
id

sceneName

defaultCharacterPosition

lightingProfile

audioProfile

navigationArea

semanticObjects

timeMode
```

---

# 10. 時間帯

Environmentに時間帯を設定可能にする。

```text
Time

Auto
Morning
Day
Evening
Night
```

AutoではQuestのローカル時間に合わせて変更する。

例：

```text
23:00

↓

Bedroom Night
```

---

# 11. Settings

Settingsは以下のカテゴリーに分割する。

```text
Settings

AI

Speech

Voice

Character

Behavior

Memory

VR

Privacy
```

---

# 12. AI Provider

AIサービスを固定しない。

以下のProvider Interfaceを作成する。

```text
ILLMProvider

ISTTProvider

ITTSProvider

IBehaviorProvider
```

実装例：

```text
OpenAILLMProvider

GeminiLLMProvider

ClaudeLLMProvider


OpenAISTTProvider


OpenAITTSProvider

ElevenLabsTTSProvider


JevBehaviorProvider
```

これにより将来的にサービス変更が可能。

---

# 13. API Key

APIキーはユーザー自身が取得し、Quest上の設定画面から入力する方式とする。

いわゆるBYOK方式とする。

例：

```text
AI Provider

Provider
[ OpenAI ▼ ]

Model
[ Model Name ▼ ]

API Key
[ ******************** ]

[Test Connection]
```

Behavior AI：

```text
Behavior Provider

Provider
[ Jev ]

API Key
[ ******************** ]

[Test Connection]
```

TTS：

```text
Voice Provider

Provider
[ OpenAI ▼ ]

Voice
[ Voice A ▼ ]

API Key
[ ******************** ]
```

---

# 14. API Key保存

APIキーを以下へ直接保存しない。

```text
Source Code

Plain Text

PlayerPrefs
```

Android側の安全な保存領域を利用する。

候補：

```text
Android Keystore
       ↓
Encryption Key
       ↓
Encrypted Local Storage
```

完全な秘匿を保証するものではないが、平文保存は避ける。

以下も実装する。

```text
[ Delete API Keys ]

[ Delete All Local Data ]
```

---

# 15. CharacterBrain

AIキャラクター全体の中心となるシステム。

ConversationManagerのみを中心とせず、キャラクター全体をCharacterBrainが管理する。

```text
CharacterBrain
│
├── PerceptionSystem
│
├── CognitionSystem
│
├── MemorySystem
│
├── SpeechSystem
│
└── BodySystem
```

---

# 16. PerceptionSystem

ユーザーや周囲の状態をCharacterBrainへ渡す。

```text
PerceptionSystem

HeadTracker

HandTracker

DistanceSensor

HeadGazeDetector

VoiceActivityDetector

EnvironmentSensor
```

---

# 17. Quest 3Sの視線判定

Quest 3SにはEye Trackingがないため、ユーザーの実際の眼球視線は取得しない。

代わりにHMDの向きから擬似的な視線を取得する。

```text
HMD Camera

↓

Forward Ray

↓

Raycast

↓

Character
```

例：

```text
headLookingAtCharacter = true
```

とする。

---

# 18. Head Tracking

OpenXRから以下を取得する。

```text
Head Position

Head Rotation

Head Velocity
```

これにより、

* キャラクターへ近づいた
* 離れた
* 横を向いた
* 後ろを向いた
* しゃがんだ

などを推定する。

---

# 19. Hand Tracking

Meta XR Interaction SDKを利用し、ハンドトラッキングに対応する。

検出対象例：

```text
Left Hand Position

Right Hand Position

Pinch

Pointing

Open Palm
```

将来的には簡単なGesture Detectionを実装する。

例：

```text
User Wave

↓

Gesture Detector

↓

Jev

↓

wave_back

↓

AI Character Wave
```

---

# 20. Controller

Quest Controllerにも対応する。

```text
IXRInputProvider
│
├─ ControllerInputProvider
└─ HandInputProvider
```

ユーザーはController / Hand Trackingのどちらでも操作可能とする。

---

# 21. 音声入力

Quest内蔵マイクを利用する。

```text
Quest Microphone

↓

AudioCaptureService

↓

Audio Buffer

↓

STT Provider
```

マイク権限としてAndroidのRECORD_AUDIOを利用する。

---

# 22. MVPの会話方式

MVPではPush To Talk方式を採用する。

```text
Button Down

↓

Recording Start


Button Up

↓

Recording Stop

↓

STT
```

この方式により誤認識や環境音問題を減らす。

---

# 23. 将来の自然会話

MVP後はVoice Activity Detectionを導入する。

```text
Microphone

↓

Ring Buffer

↓

VAD

├─ Speech Start

└─ Speech End
```

ユーザーが話し始めた場合、自動で録音を開始する。

---

# 24. Barge-in

AIが喋っている途中でもユーザーが発言した場合、AIの発話を停止可能とする。

```text
AI Speaking

↓

User Speech Detected

↓

TTS Stop

↓

Character LookAt User

↓

Listening State
```

これをBarge-inとして実装する。

---

# 25. 会話処理

基本フロー：

```text
User Speech

↓

Quest Microphone

↓

STT

↓

Transcript

↓

CharacterBrain
```

CharacterBrainでは、

```text
User Text

User State

Character State

Environment State

Relevant Memory
```

を統合する。

---

# 26. Jev

JevはBehavior AIとして利用する。

Jevには文章生成をさせず、

**キャラクターがどう反応するか**

を判断させる。

入力例：

```text
User

text

distance

headDirection

speaking

silenceDuration


Character

emotion

energy

relationship

currentAction


Environment

location

time
```

---

# 27. Jev出力

Jevには有限の選択肢から判断させる。

## Emotion

```text
neutral

happy

sad

concerned

excited

embarrassed

curious
```

## Gesture

```text
none

nod

big_nod

head_tilt

wave

look_away

laugh

shrug
```

## Gaze

```text
user

object

away
```

## Movement

```text
stay

approach

step_back

sit

stand
```

## Conversation Behavior

```text
speak

wait

ask_question

change_topic
```

---

# 28. Jevの呼び出しタイミング

Jevは毎フレーム呼ばない。

イベント駆動とする。

例：

```text
User spoke

User stopped speaking

User approached

User moved away

User looked toward character

User looked away

Silence > N seconds

Character finished speaking
```

このタイミングでBehaviorDecisionを生成する。

---

# 29. LLM

LLMは主に以下を担当する。

```text
Natural Language Generation

Conversation Reasoning

Context Understanding

Memory Interpretation

Long-term Reasoning
```

JevのBehaviorDecisionをコンテキストとして利用する。

例：

```text
User:

今日は疲れた


Behavior:

emotion = concerned

gesture = small_nod

responseStyle = empathetic
```

↓

LLM

↓

```text
「お疲れさま。今日は何が大変だった？」
```

---

# 30. AI並列処理

必要に応じて処理を並列化する。

```text
Transcript
   │
   ├──────→ Jev
   │
   └──────→ Memory Search

           ↓

          LLM
```

将来的には、

```text
Transcript

├─ Jev
└─ LLM
```

を並列実行する構成も検討する。

---

# 31. TTS

LLMから返された文章をTTSへ送る。

```text
LLM Text

↓

TTS Provider

↓

Audio Data

↓

Unity AudioSource
```

---

# 32. 3D Audio

キャラクターの口または頭付近にAudioSourceを配置する。

Meta XR Audio SDKを利用し、Spatial Audio化する。

```text
Character

    🔊

↓

User
```

キャラクター位置から声が聞こえるようにする。

---

# 33. LipSync

TTS音声に合わせてキャラクターの口を動かす。

優先方式：

```text
TTS

↓

Audio + Viseme Timeline

↓

LipSyncController

↓

BlendShape
```

TTSサービスがVisemeを返さない場合は、

```text
Audio

↓

Local LipSync Analysis

↓

Viseme

↓

BlendShape
```

を利用する。

---

# 34. BodySystem

キャラクター身体制御を担当する。

```text
BodySystem

├─ LookAtController

├─ BlinkController

├─ FaceController

├─ LipSyncController

├─ GestureController

├─ LocomotionController

└─ SpeechController
```

---

# 35. LookAt

キャラクターの、

* Eyes
* Head
* Upper Body

をユーザー方向へ自然に追従させる。

```text
User HMD

↓

Look Target

↓

Eyes

↓

Head

↓

Upper Body
```

追従は完全同期させず、補間を使用する。

候補：

```text
Quaternion.Slerp

SmoothDamp
```

---

# 36. Blink

瞬きはAI APIを使用しない。

Unity内部でランダム間隔により制御する。

```text
2.0 sec

4.2 sec

3.1 sec
```

など、一定間隔にしない。

---

# 37. Idle

Idle AnimationはUnity内部で制御する。

例：

```text
Breathing

Blink

Small Eye Movement

Body Sway

Weight Shift

Small Head Movement
```

JevやLLMは利用しない。

---

# 38. Gesture

Jevから、

```text
gesture = nod
```

などの命令を受け取る。

Unity側で、

```text
Animator

↓

Animation State
```

として実行する。

AI側からAnimation名を直接生成させない。

---

# 39. Movement

キャラクター移動にはNavMeshを使用候補とする。

Jev：

```text
movement = approach
```

↓

Unity：

```text
Target Position Calculation

↓

NavMesh

↓

Character Movement
```

実際の移動処理はUnity側が行う。

---

# 40. CharacterState

キャラクター内部には状態を持たせる。

例：

```text
CharacterState

emotion

mood

energy

interest

relationship

curiosity

currentAction

lastInteraction
```

ユーザーには数値として表示しない。

---

# 41. Presence System

ユーザーが話していなくてもキャラクターが存在しているように振る舞う。

例：

```text
Idle

↓

Roomを見る

↓

Userを見る

↓

姿勢を変える

↓

少し歩く

↓

座る
```

ユーザーが一定時間キャラクター方向を見続けた場合、

```text
HeadGaze Detected

↓

Jev

↓

head_tilt

↓

LLM

↓

「どうしたの？」
```

のような自発反応を可能とする。

---

# 42. Personal Space

ユーザーとキャラクターの距離を取得する。

```text
3.0m

1.0m

0.5m

0.2m
```

単純な距離だけではなく、

```text
distance

relationship

personality

currentMood

userVelocity
```

などをJevへ渡して反応を判断する。

---

# 43. Memory

ローカル記憶はQuest本体に保存する。

候補：

```text
character.db
```

SQLiteを使用する。

---

# 44. Memory構成

```text
Short-term Memory

Current Conversation


Medium-term Memory

Recent Topics

Recent Events


Long-term Memory

User Preferences

Important Events

User Information

Relationship Information
```

---

# 45. SQLite Tables

候補：

```text
characters

conversations

messages

memories

relationships

character_states

settings
```

Memoryには例えば、

```text
id

character_id

content

importance

created_at

last_used_at
```

を保持する。

---

# 46. Memory Retrieval

会話履歴全文を毎回LLMへ送信しない。

ユーザー入力から関連記憶を検索する。

```text
User

↓

Current Message

↓

Memory Search

↓

Relevant Memories

↓

LLM Context
```

---

# 47. Memory管理画面

ユーザーからAIの記憶内容を確認できるようにする。

```text
Memory

Character remembers:

・○○
・○○
・○○

[ Edit ]

[ Delete ]

[ Delete All ]
```

将来的に実装する。

---

# 48. 字幕

字幕表示を設定可能とする。

```text
Subtitle

Off

Minimal

Always
```

Minimalではキャラクター付近へ短時間表示する。

---

# 49. Questライフサイクル

QuestではアプリがFocusを失ったりPauseされる可能性がある。

対応対象：

```text
OnApplicationPause

OnApplicationFocus
```

Pause時：

```text
Stop Microphone

Stop TTS

Cancel API Request

Save Character State

Save Conversation

Save Database
```

Resume時：

```text
Restore Character

Restore State

Initialize Microphone

Restore Audio
```

---

# 50. 非同期処理

AI API通信をUnity Main Threadで待機しない。

```text
Main Thread

Rendering

Animation

Tracking


Async

STT

Jev

LLM

TTS
```

`async / await`を利用する。

また、

```text
CancellationToken
```

を利用し、API処理を中断可能にする。

---

# 51. Scene構成

候補：

```text
Bootstrap

MainMenu

Environment Scenes
```

Bootstrapには共通システムを配置する。

```text
Bootstrap

AppManager

SettingsManager

ProviderManager

MemoryManager

AudioManager

XRManager
```

---

# 52. Character

キャラクターはPrefabとして管理する。

```text
Characters

CharacterA.prefab

CharacterB.prefab
```

EnvironmentとCharacterを分離する。

```text
Character × Environment
```

を自由に組み合わせられる構造とする。

---

# 53. Android Build

Quest 3S向けにAndroid APKを生成する。

基本設定：

```text
Platform

Android


Architecture

ARM64


Scripting Backend

IL2CPP
```

必要Permission：

```text
INTERNET

RECORD_AUDIO
```

---

# 54. Quest向け描画最適化

Questはモバイル向けGPUであるため軽量化を前提とする。

利用候補：

```text
URP

Multiview

Fixed Foveated Rendering

Baked Lighting

LOD
```

Quest 3SにはEye Trackingがないため、Eye Tracked Foveated Renderingは前提としない。

---

# 55. Environment描画

MVPでは軽量な1ルーム構成とする。

避けるもの：

```text
Heavy Real-time GI

Large Dynamic Lights

Expensive Transparent Shader

Excessive Post Processing
```

基本は、

```text
Baked Lighting

Light Probe

Reflection Probe
```

を利用する。

---

# 56. MR拡張

MVP後の拡張機能としてMixed Reality対応を検討する。

利用候補：

```text
Meta MRUK

Passthrough

Depth API

Spatial Anchor

Passthrough Camera API
```

---

# 57. Passthrough

VR Environmentではなくユーザーの実際の部屋へキャラクターを表示できるようにする。

```text
Real Room

+

Virtual Character
```

---

# 58. MRUK

MRUKにより、

```text
Floor

Wall

Ceiling

Table

Couch

Bed
```

などのScene情報を利用する。

これによりキャラクターを現実空間に配置可能にする。

---

# 59. Spatial Anchor

キャラクターの位置を現実空間に保存する。

例：

```text
Day 1

Character sitting beside desk

↓

Save Spatial Anchor

↓

Day 2

Application Start

↓

Load Spatial Anchor

↓

Character appears beside same desk
```

---

# 60. Depth API

現実空間のDepth情報を利用してOcclusionを実現する。

例：

```text
AI Character

↓

Moves behind real chair

↓

Real chair hides character
```

より自然なMR表示を可能にする。

---

# 61. Passthrough Camera AI

将来的にはQuest 3Sカメラ映像をAI処理する。

例：

```text
Passthrough Camera

↓

Object Detection

↓

Desk

Bottle

Chair

↓

CharacterBrain
```

ユーザーが、

```text
「これ何？」
```

と質問した場合、Hand Trackingとカメラ認識を組み合わせて対象物を判断する構成を検討する。

---

# 62. オフライン設計

MVPではインターネット接続必須とする。

ただしProviderを抽象化し、

```text
STTProvider

LLMProvider

TTSProvider

BehaviorProvider
```

としておく。

将来的には、

```text
Cloud STT
→ Local STT

Cloud LLM
→ Local LLM

Cloud TTS
→ Local TTS
```

へ変更可能にする。

---

# 63. MVP対象機能

最初の完成版では以下まで実装する。

## 必須

* Quest 3S単体起動
* VR Environment
* Character表示
* Character選択
* Environment選択
* Settings
* API Key入力
* Push To Talk
* STT
* Jev
* LLM
* TTS
* 3D Audio
* LookAt
* Blink
* Idle
* LipSync
* Emotion
* Gesture
* SQLite
* Memory
* Continue
* Questライフサイクル対応

---

# 64. MVPでは後回しにする機能

以下はMVP後。

```text
Full VAD

Barge-in

Character Walking

Advanced Hand Gesture Recognition

Self-Initiated Conversation

Multiple Characters simultaneously

Passthrough

MRUK

Spatial Anchor

Depth API

Camera Object Recognition

Local AI
```

---

# 65. MVP完成条件

Quest 3SへAPKをインストールする。

PC接続を解除する。

その状態で、

1. Questからアプリ起動
2. Characterを選択
3. Environmentを選択
4. API Keyを設定
5. VR Environmentへ入る
6. Characterが表示される
7. Characterがユーザー方向を見る
8. Push To Talkでユーザーが話す
9. STTが成功する
10. JevがBehaviorを決定する
11. LLMが文章を生成する
12. TTSでCharacterが話す
13. 3D Audioとして声が聞こえる
14. LipSyncする
15. Emotionが変化する
16. Gestureを行う
17. 次の会話を継続できる
18. Memoryが保存される
19. アプリを終了する
20. 再起動後Continueで状態が復元される

ここまで成功した時点でMVP完成とする。

---

# 66. 推奨開発順序

## Phase 1 — Quest基盤

```text
Unity Project

↓

OpenXR

↓

Meta XR Core

↓

Quest APK Build
```

まず空のVR EnvironmentをQuest 3S単体で起動する。

---

## Phase 2 — Character

```text
Character Import

↓

Idle

↓

Blink

↓

LookAt
```

---

## Phase 3 — Voice

```text
Microphone

↓

Push To Talk

↓

STT

↓

LLM

↓

TTS

↓

3D Audio
```

---

## Phase 4 — Character Reaction

```text
Jev

↓

Emotion

↓

Gesture

↓

LipSync
```

---

## Phase 5 — Memory

```text
SQLite

↓

Conversation Save

↓

Memory Extraction

↓

Memory Retrieval
```

---

## Phase 6 — Application UI

```text
Main Menu

Character

Environment

Settings

Continue
```

---

## Phase 7 — Natural Conversation

```text
VAD

↓

Barge-in

↓

Self-Initiated Behavior
```

---

## Phase 8 — MR

```text
Passthrough

↓

MRUK

↓

Spatial Anchor

↓

Depth

↓

Camera Recognition
```

---

# 67. 推奨Unity構造

```text
Assets
│
├── App
│   ├── Core
│   │   ├── AppManager
│   │   ├── SettingsManager
│   │   └── ProviderManager
│   │
│   ├── Character
│   │   ├── Brain
│   │   ├── Body
│   │   ├── Perception
│   │   ├── Speech
│   │   └── Memory
│   │
│   ├── AI
│   │   ├── STT
│   │   ├── LLM
│   │   ├── TTS
│   │   └── Behavior
│   │
│   ├── XR
│   │   ├── Input
│   │   ├── Tracking
│   │   └── Interaction
│   │
│   ├── Database
│   │
│   ├── UI
│   │
│   └── Security
│
├── Characters
│
├── Environments
│
├── Animations
│
├── Audio
│
├── Materials
│
├── Scenes
│
└── Plugins
```

3Dモデルについては、

```text
Assets/Characters/
```

以下へ後から追加する。

---

# 68. 設計原則

本アプリではAIにすべてを任せない。

```text
LLM

「何を話すか」


Jev

「どう反応するか」


Unity

「どう身体を動かすか」
```

という責務分離を行う。

---

# 69. 最終目標

本アプリの最終目標は、

> Questを起動してアプリを開くだけで、いつものAIキャラクターがそこにいる。

という体験を実現することである。

PCを起動する必要はない。

Quest Linkも不要。

SteamVRも不要。

最終的には、

```text
User
 ↓
Quest 3S
 ↓
Virtual / Mixed Reality Space
 ↓
AI Character
 ├─ 見る
 ├─ 聞く
 ├─ 覚える
 ├─ 考える
 ├─ 話す
 ├─ 表情を変える
 ├─ 動く
 └─ ユーザーへ反応する
```

という構成を目指す。

**Meta Quest 3Sそのものを、AIキャラクターが存在する場所にする。**
