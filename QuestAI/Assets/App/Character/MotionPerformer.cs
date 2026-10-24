using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace QuestAI
{
    // Runs a MotionPlan step by step: walking, turning, poses, keyframed whole-body motions and toy play.
    // While it runs, the brain's own autonomy and Jev movement stay out of the way.
    public sealed class MotionPerformer : MonoBehaviour
    {
        CharacterBrain brain; int version; bool frozen;
        public bool Busy { get; private set; }
        public MotionPlan Last { get; private set; }
        public string Request { get; private set; }
        public MotionStep Current { get; private set; }
        public Action<string> Status, Report;
        void Awake() { brain = GetComponent<CharacterBrain>(); }
        CompanionPlayground Toys => brain.playground;
        SceneAwareness World => brain.world;
        CharacterLocomotion Legs => brain.locomotion;
        BodySystem Body => brain.body;
        Vector3 Self => brain.transform.position;
        Vector3 User => brain.user.position;
        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        public void Run(MotionPlan plan, string request)
        {
            Stop();
            if (plan == null || plan.Empty) return;
            if (plan.steps.All(s => s.action == MotionAction.stop)) { Report?.Invoke("ユーザーに言われて動きを止めました。"); return; }
            Last = plan; Request = request; Busy = true; int v = ++version;
            if (Legs) { Legs.Frozen = true; frozen = true; }
            StartCoroutine(Execute(plan, v));
        }
        public void Stop()
        {
            version++; StopAllCoroutines(); Current = null;
            if (!Busy) return;
            Busy = false;
            if (Legs) { Legs.Stop(); if (frozen) Legs.Frozen = false; }
            frozen = false;
            if (Body) { Body.ClearMotion(); Body.lookPoint = null; if (Body.CurrentPose == BodySystem.Pose.crouch || Body.CurrentPose == BodySystem.Pose.point) Body.SetPose(BodySystem.Pose.stand, null); }
            if (Toys) Toys.Settle();
        }
        IEnumerator Execute(MotionPlan plan, int v)
        {
            foreach (var step in plan.steps)
            {
                if (v != version) yield break;
                if (step.action == MotionAction.stop) break;
                Current = step;
                if (step.emotion != Emotion.neutral) brain.ShowEmotion(step.emotion);
                yield return Perform(step, v);
            }
            if (v != version) yield break;
            Body.ClearMotion(); Current = null; Busy = false;
            if (Legs && frozen) Legs.Frozen = false; frozen = false;
            Report?.Invoke("頼まれた動き（" + (string.IsNullOrEmpty(plan.summary) ? "動作" : plan.summary) + "）を終えました。" + Seen());
        }
        string Seen() { var seen = brain.sight ? brain.sight.VisibleSummary() : Array.Empty<string>(); return seen.Length > 0 ? "今見えているもの: " + string.Join("、", seen.Take(5)) + "。" : ""; }
        void Fail(string message) { Status?.Invoke(message); Report?.Invoke("頼まれた動きの一部ができませんでした: " + message); }

        // Resolves an id to a world point: the user, a toy or a room object.
        bool Point(string id, out Vector3 point, out SceneObject item)
        {
            point = default; item = null;
            if (id == "user") { point = User; return true; }
            var toy = Toys ? Toys.Find(id) : null; if (toy != null) { item = toy.item; point = toy.Center; return true; }
            item = World?.Find(id); if (item != null) { point = item.bounds.center; return true; }
            return false;
        }
        Vector3 Direction(MotionStep step, Vector3 from)
        {
            Vector3 forward = Flat(brain.transform.forward).normalized, right = Vector3.Cross(Vector3.up, forward);
            switch (step.direction)
            {
                case MotionDirection.forward: return forward;
                case MotionDirection.back: return -forward;
                case MotionDirection.left: return -right;
                case MotionDirection.right: return right;
                case MotionDirection.away_from_user: return Flat(from - User).normalized;
                case MotionDirection.to_object: if (Point(step.toward, out var p, out _)) return Flat(p - from).normalized; break;
            }
            if (!string.IsNullOrEmpty(step.toward) && Point(step.toward, out var q, out _)) return Flat(q - from).normalized;
            return Flat(User - from).normalized;
        }

        IEnumerator Perform(MotionStep step, int v)
        {
            float amount = step.amount;
            switch (step.action)
            {
                case MotionAction.walk_to:
                    if (step.target == "user") { yield return ApproachUser(step.pace, v); break; }
                    if (!Point(step.target, out var at, out var item) || item == null) { Fail("行き先が見つかりません。"); break; }
                    var toy = Toys ? Toys.Find(step.target) : null;
                    yield return toy != null ? WalkToToy(toy, Self, step.pace, v) : WalkToObject(item, step.pace, v); break;
                case MotionAction.approach_user: yield return ApproachUser(step.pace, v); break;
                case MotionAction.move:
                    if (World == null) { Fail("この場所では歩けません。"); break; }
                    yield return Walk(World.grid.Snap(Self + Direction(step, Self) * (amount > 0 ? amount : 1)), "指示された場所", null, step.pace, v); break;
                case MotionAction.turn: yield return Turn(step, v); break;
                case MotionAction.look_at:
                    if (Point(string.IsNullOrEmpty(step.target) ? "user" : step.target, out var look, out _)) { Look(look); yield return Hold(amount > 0 ? amount : 2.5f, v); }
                    else Fail("見る物が見つかりません。");
                    break;
                case MotionAction.sit:
                    if (Point(step.target, out _, out var seat) && seat != null && World != null)
                    {
                        yield return WalkToObject(seat, step.pace, v); if (v != version) yield break;
                        brain.Arrive(seat, ArrivalAction.sit); yield return Hold(1.2f, v);
                    }
                    else yield return Keys(MotionLibrary.Get(MotionPreset.sit_floor), 1, v);
                    break;
                case MotionAction.crouch:
                    brain.HoldPose(BodySystem.Pose.crouch, Point(step.target, out var low, out _) ? low : (Vector3?)null, amount > 0 ? amount : 4);
                    yield return Hold(amount > 0 ? Mathf.Min(amount, 3) : 1.5f, v); break;
                case MotionAction.stand: brain.EndPose(); yield return Hold(.5f, v); break;
                case MotionAction.point:
                    if (Point(string.IsNullOrEmpty(step.target) ? "user" : step.target, out var shown, out _)) { Face(shown); brain.HoldPose(BodySystem.Pose.point, shown, 4); yield return Hold(2.5f, v); }
                    else Fail("指さす物が見つかりません。");
                    break;
                case MotionAction.gesture:
                    for (int i = 0; i < Mathf.Clamp(Mathf.RoundToInt(amount), 1, 6); i++) { if (v != version) yield break; Body.Apply(new BehaviorDecision { emotion = Body.CurrentEmotion, gesture = step.gesture }); yield return Hold(2.4f, v); }
                    break;
                case MotionAction.motion:
                    if (step.motion == MotionPreset.spin) { yield return Spin(Mathf.Clamp(Mathf.RoundToInt(amount), 1, 5), v); break; }
                    var keys = step.motion == MotionPreset.custom ? step.keys : MotionLibrary.Get(step.motion);
                    if (keys == null || keys.Length == 0) { Fail("その動きの形がわかりませんでした。"); break; }
                    yield return Keys(keys, Mathf.Clamp(Mathf.RoundToInt(amount), 1, 6), v); break;
                case MotionAction.follow_user: yield return Follow(amount > 0 ? amount : 20, v); break;
                case MotionAction.wait: yield return Hold(amount > 0 ? amount : 1.5f, v); break;
                case MotionAction.spawn_toy:
                    {
                        var kind = CompanionPlayground.KindOf(step.target) ?? CompanionPlayground.ToyKind.Ball;
                        var made = Toys ? Toys.Spawn(kind) : null;
                        if (made == null) { Fail(CompanionPlayground.Label(kind) + "を置ける場所がありません。"); break; }
                        Look(made.Center); Report?.Invoke(made.item.id + "を床に出しました。"); yield return Hold(.6f, v); break;
                    }
                case MotionAction.roll: case MotionAction.kick: case MotionAction.throw_ball: case MotionAction.bounce:
                    yield return PlayBall(step, v); break;
                case MotionAction.fetch: yield return Fetch(step, v); break;
                case MotionAction.pick_up:
                    {
                        var held = NeedToy(step.target, null); if (held == null) break;
                        yield return WalkToToy(held, Self, step.pace, v); if (v != version) yield break;
                        yield return Keys(MotionLibrary.PickUp, 1, v); Toys.PickUp(held); break;
                    }
                case MotionAction.put_down:
                    {
                        var held = Toys ? Toys.Carried : null; if (held == null) { Fail("何も持っていません。"); break; }
                        yield return Keys(MotionLibrary.PickUp, 1, v); Toys.PutDown(held, Self + Flat(brain.transform.forward).normalized * .5f); break;
                    }
                case MotionAction.stack: case MotionAction.knock_down: case MotionAction.line_up: yield return PlayBlocks(step, v); break;
                case MotionAction.spin_toy:
                    {
                        var top = NeedToy(step.target, CompanionPlayground.ToyKind.Spinner); if (top == null) break;
                        yield return WalkToToy(top, Self, step.pace, v); if (v != version) yield break;
                        brain.HoldPose(BodySystem.Pose.crouch, top.Center, 3); yield return Hold(.5f, v); Toys.Spin(top, 30); yield return Hold(1.5f, v); break;
                    }
                case MotionAction.put_away: if (Toys) Toys.ClearToys(); yield return Hold(.3f, v); break;
                case MotionAction.look_closely:
                    if (Point(step.target, out var detail, out _)) { Face(detail); Look(detail); yield return Hold(.6f, v); }
                    var looking = brain.LookClosely(Request);
                    while (!looking.IsCompleted && v == version) yield return null;
                    break;
            }
        }
        // The toy the step names; a missing one of the needed kind is put out first.
        CompanionPlayground.Toy NeedToy(string id, CompanionPlayground.ToyKind? kind)
        {
            if (!Toys || World == null) { Fail("この場所ではおもちゃを使えません。"); return null; }
            var toy = Toys.Find(id, kind);
            if (toy == null) { var wanted = CompanionPlayground.KindOf(id) ?? kind ?? CompanionPlayground.ToyKind.Ball; toy = Toys.Spawn(wanted); if (toy != null) Report?.Invoke(toy.item.id + "を床に出しました。"); }
            if (toy == null) Fail("おもちゃを置ける場所がありません。");
            return toy;
        }

        IEnumerator Walk(Vector3 goal, string place, Vector3? face, Pace pace, int v)
        {
            if (!Legs) yield break;
            bool? arrived = null; brain.EndPose();
            if (!Legs.MoveTo(goal, place, face, ok => arrived = ok, pace)) { Fail(place + "へ行く道がありません。"); yield break; }
            while (arrived == null && v == version) yield return null;
            if (arrived == false && v == version) Fail(place + "へ行けませんでした。");
        }
        IEnumerator WalkToObject(SceneObject item, Pace pace, int v)
        {
            if (World == null || !World.Approach(item, Self, .3f, out var spot)) { Fail(item.id + "のそばに行けません。"); yield break; }
            yield return Walk(spot, item.id, item.bounds.center, pace, v);
        }
        IEnumerator WalkToToy(CompanionPlayground.Toy toy, Vector3 preferred, Pace pace, int v)
        {
            if (toy.carried) yield break;
            if (SceneAwareness.FlatDistance(toy.item, Self) < .9f && Vector3.Distance(Flat(Self), Flat(preferred)) < .35f) { Face(toy.Center); yield break; }
            if (!Toys.StandSpot(toy, preferred, out var spot)) { Fail(toy.item.id + "のそばに行けません。"); yield break; }
            yield return Walk(spot, toy.item.id, toy.Center, pace, v);
            if (v == version) { Face(toy.Center); yield return Hold(.35f, v); }
        }
        IEnumerator ApproachUser(Pace pace, int v)
        {
            if (World == null) yield break;
            Vector3 away = Flat(Self - User); if (away.sqrMagnitude < .04f) away = -brain.transform.forward;
            yield return Walk(World.grid.Snap(Flat(User) + away.normalized * Mathf.Max(.8f, brain.profile.personalSpace + .3f) + Vector3.up * Self.y), "ユーザーのそば", User, pace, v);
        }
        IEnumerator Follow(float seconds, int v)
        {
            for (float end = Time.time + seconds; Time.time < end && v == version;)
            {
                if (World != null && Legs && !Legs.Moving && Vector3.Distance(Flat(Self), Flat(User)) > 1.5f) { Vector3 away = Flat(Self - User).normalized; Legs.MoveTo(World.grid.Snap(Flat(User) + away * 1.0f), "ユーザーのそば", User, null, Pace.normal); }
                yield return new WaitForSeconds(.5f);
            }
        }
        void Face(Vector3 point)
        {
            var d = Flat(point - Self); if (d.sqrMagnitude < .01f) return;
            StartCoroutine(TurnTo(Quaternion.LookRotation(d.normalized).eulerAngles.y, version));
        }
        void Look(Vector3 point) { Body.lookPoint = point; Body.Apply(new BehaviorDecision { emotion = Body.CurrentEmotion, gaze = Gaze.@object }); }
        IEnumerator TurnTo(float yaw, int v)
        {
            while (v == version && Mathf.Abs(Mathf.DeltaAngle(brain.transform.eulerAngles.y, yaw)) > 1)
            { brain.transform.rotation = Quaternion.Euler(0, Mathf.MoveTowardsAngle(brain.transform.eulerAngles.y, yaw, 240 * Time.deltaTime), 0); yield return null; }
        }
        IEnumerator Turn(MotionStep step, int v)
        {
            float yaw = brain.transform.eulerAngles.y, degrees = step.amount > 0 ? step.amount : 90;
            switch (step.direction)
            {
                case MotionDirection.left: yaw -= degrees; break;
                case MotionDirection.right: yaw += degrees; break;
                case MotionDirection.back: yaw += 180; break;
                default: var d = Direction(step, Self); if (d.sqrMagnitude > .01f) yaw = Quaternion.LookRotation(d).eulerAngles.y; break;
            }
            // Large turns are done in pieces so 360° and more really go all the way round.
            float total = step.direction == MotionDirection.left ? -degrees : step.direction == MotionDirection.right ? degrees : Mathf.DeltaAngle(brain.transform.eulerAngles.y, yaw);
            for (float done = 0; Mathf.Abs(done) < Mathf.Abs(total) - .5f && v == version;)
            {
                float delta = Mathf.Sign(total) * Mathf.Min(Mathf.Abs(total) - Mathf.Abs(done), 200 * Time.deltaTime);
                brain.transform.Rotate(0, delta, 0); done += delta; yield return null;
            }
        }
        IEnumerator Spin(int turns, int v)
        {
            for (float done = 0; done < 360 * turns && v == version;)
            { float delta = Mathf.Min(360 * turns - done, 420 * Time.deltaTime); brain.transform.Rotate(0, delta, 0); done += delta; Body.SetMotion(new MotionKey { left_arm_out = 70, right_arm_out = 70 }); yield return null; }
            Body.ClearMotion();
        }
        IEnumerator Hold(float seconds, int v) { for (float end = Time.time + seconds; Time.time < end && v == version;) yield return null; }
        // Plays keyframes smoothly; the body eases back to its normal pose afterwards.
        IEnumerator Keys(MotionKey[] keys, int repeat, int v, Action<float> at = null)
        {
            if (keys.Length == 0) yield break;
            float length = Mathf.Max(.2f, keys[keys.Length - 1].time);
            for (int r = 0; r < repeat && v == version; r++)
                for (float t = 0; t <= length && v == version; t += Time.deltaTime) { Body.SetMotion(MotionLibrary.Sample(keys, t)); at?.Invoke(t); yield return null; }
            var last = Body.Motion ?? new MotionKey();
            for (float t = 0; t < .3f && v == version; t += Time.deltaTime) { Body.SetMotion(MotionKey.Lerp(last, new MotionKey(), t / .3f)); yield return null; }
            if (v == version) Body.ClearMotion();
        }

        // Where a thrown or rolled thing should end up.
        Vector3 Destination(MotionStep step, Vector3 from, float fallback)
        {
            if (!string.IsNullOrEmpty(step.toward) && Point(step.toward, out var goal, out _)) return goal;
            if (step.direction == MotionDirection.to_user || step.direction == MotionDirection.none) return Flat(User) + Flat(from - User).normalized * .45f + Vector3.up * World.floor.max.y;
            return from + Direction(step, from) * fallback;
        }
        IEnumerator PlayBall(MotionStep step, int v)
        {
            if (step.action == MotionAction.throw_ball) { yield return Throw(step, v); yield break; }
            var ball = NeedToy(step.target, CompanionPlayground.ToyKind.Ball); if (ball == null || ball.kind != CompanionPlayground.ToyKind.Ball) { if (ball != null) Fail(ball.item.id + "はボールではありません。"); yield break; }
            if (ball.carried) Toys.PutDown(ball, Self + Flat(brain.transform.forward).normalized * .5f);
            if (step.action == MotionAction.bounce)
            {
                yield return WalkToToy(ball, Self, step.pace, v); if (v != version) yield break;
                brain.HoldPose(BodySystem.Pose.crouch, ball.Center, 1.2f); yield return Keys(MotionLibrary.PickUp, 1, v); if (v != version) yield break;
                Look(ball.Center); yield return Toys.Bounce(ball, Mathf.Clamp(Mathf.RoundToInt(step.amount), 2, 8)); Report?.Invoke(ball.item.id + "を弾ませました。"); yield break;
            }
            var target = Destination(step, ball.Center, 2.5f); Vector3 aim = Flat(target - ball.Center).normalized;
            // Stand on the far side of the ball from where it should go.
            yield return WalkToToy(ball, Flat(ball.Center) - aim * .6f + Vector3.up * Self.y, step.pace, v); if (v != version) yield break;
            aim = Flat(target - ball.Center).normalized; float distance = Vector3.Distance(Flat(ball.Center), Flat(target));
            bool pushed = false;
            if (step.action == MotionAction.kick)
                yield return Keys(MotionLibrary.Kick, 1, v, t => { if (!pushed && t >= .42f) { pushed = true; Toys.Push(ball, aim * Mathf.Max(2.5f, CompanionPlayground.SpeedFor(distance) * 1.5f) + Vector3.up * 1.2f); } });
            else
            {
                brain.HoldPose(BodySystem.Pose.crouch, ball.Center, 2.5f); yield return Hold(.5f, v);
                yield return Keys(MotionLibrary.Roll, 1, v, t => { if (!pushed && t >= .35f) { pushed = true; Toys.Push(ball, aim * Mathf.Clamp(CompanionPlayground.SpeedFor(distance), .7f, 4)); } });
            }
            if (!pushed && v == version) Toys.Push(ball, aim * 1.5f);
            brain.EndPose();
            yield return Watch(ball, 4, v);
            Report?.Invoke(ball.item.id + "を" + (step.action == MotionAction.kick ? "蹴りました" : "転がしました") + "。");
        }
        IEnumerator Watch(CompanionPlayground.Toy toy, float seconds, int v)
        {
            yield return null;
            for (float end = Time.time + seconds; Time.time < end && v == version && toy.Moving;) { Look(toy.Center); yield return null; }
        }
        // Picks the toy up (one block of a set), winds up and throws it so it lands on the target.
        IEnumerator Throw(MotionStep step, int v)
        {
            var toy = NeedToy(step.target, CompanionPlayground.ToyKind.Ball); if (toy == null) yield break;
            List<Rigidbody> grip;
            if (toy.carried) grip = toy.bodies.Where(b => b.isKinematic).ToList();
            else
            {
                yield return WalkToToy(toy, Self, step.pace, v); if (v != version) yield break;
                brain.HoldPose(BodySystem.Pose.crouch, toy.Center, 1); yield return Keys(MotionLibrary.PickUp, 1, v, null); if (v != version) yield break;
                grip = Toys.PickUp(toy, single: true); brain.EndPose();
            }
            var target = Destination(step, Self, 3);
            Face(target); yield return Hold(.45f, v); if (v != version) yield break;
            float distance = Vector3.Distance(Flat(Self), Flat(target)), flight = Mathf.Clamp(distance / 3.2f, .6f, 1.1f); bool thrown = false;
            yield return Keys(MotionLibrary.Throw, 1, v, t => { if (!thrown && t >= .55f) { thrown = true; Toys.Throw(grip, target, flight); } });
            if (!thrown && v == version) Toys.Throw(grip, target, flight);
            yield return Watch(toy, 4, v);
            Report?.Invoke(toy.item.id + "を投げました。");
        }
        IEnumerator Fetch(MotionStep step, int v)
        {
            var toy = NeedToy(step.target, null); if (toy == null) yield break;
            yield return WalkToToy(toy, Self, step.pace, v); if (v != version || toy.carried) yield break;
            yield return Keys(MotionLibrary.PickUp, 1, v); if (v != version) yield break;
            Toys.PickUp(toy);
            yield return ApproachUser(step.pace, v); if (v != version) yield break;
            yield return Keys(MotionLibrary.PickUp, 1, v);
            Toys.PutDown(toy, Self + Flat(User - Self).normalized * .5f);
            Body.Apply(new BehaviorDecision { emotion = Emotion.happy, gesture = Gesture.nod }); Report?.Invoke(toy.item.id + "をユーザーのところへ持ってきました。");
        }
        IEnumerator PlayBlocks(MotionStep step, int v)
        {
            var blocks = NeedToy(step.target, CompanionPlayground.ToyKind.Blocks); if (blocks == null || blocks.kind != CompanionPlayground.ToyKind.Blocks) { if (blocks != null) Fail(blocks.item.id + "は積み木ではありません。"); yield break; }
            if (blocks.carried) Toys.PutDown(blocks, Self + Flat(brain.transform.forward).normalized * .5f);
            yield return WalkToToy(blocks, Self, step.pace, v); if (v != version) yield break;
            brain.HoldPose(BodySystem.Pose.crouch, blocks.Center, 30); Look(blocks.Center);
            switch (step.action)
            {
                case MotionAction.stack:
                    if (blocks.stacked) yield return Toys.LineUp(blocks, brain.transform.right, .2f);
                    yield return Toys.Stack(blocks, Mathf.RoundToInt(step.amount)); Report?.Invoke(blocks.item.id + "を積み上げました。"); break;
                case MotionAction.line_up: yield return Toys.LineUp(blocks, brain.transform.right); Report?.Invoke(blocks.item.id + "を一列に並べました。"); break;
                default:
                    if (!blocks.stacked) yield return Toys.Stack(blocks, 5, .25f);
                    yield return Keys(MotionLibrary.Kick, 1, v, null); Toys.KnockDown(blocks, Direction(step, blocks.Center));
                    yield return Hold(1.2f, v); Report?.Invoke(blocks.item.id + "の塔を崩しました。"); break;
            }
            brain.EndPose();
        }
    }

    // Ready-made whole-body motions in the same keyframe format the planner uses for custom moves.
    public static class MotionLibrary
    {
        static MotionKey K(float time, Action<MotionKey> set = null) { var k = new MotionKey { time = time }; set?.Invoke(k); return k; }
        public static MotionKey Sample(MotionKey[] keys, float t)
        {
            if (t <= keys[0].time) return MotionKey.Lerp(new MotionKey(), keys[0], keys[0].time <= 0 ? 1 : t / keys[0].time);
            for (int i = 1; i < keys.Length; i++) if (t <= keys[i].time) return MotionKey.Lerp(keys[i - 1], keys[i], Mathf.SmoothStep(0, 1, (t - keys[i - 1].time) / Mathf.Max(.001f, keys[i].time - keys[i - 1].time)));
            return keys[keys.Length - 1];
        }
        public static readonly MotionKey[] Kick = { K(0), K(.25f, k => { k.right_leg = -25; k.chest_pitch = -5; }), K(.42f, k => { k.right_leg = 65; k.left_arm_up = 40; k.chest_pitch = -8; }), K(.7f, k => k.right_leg = 20), K(.9f) };
        public static readonly MotionKey[] Roll = { K(0), K(.2f, k => { k.right_arm_up = 35; k.chest_pitch = 15; }), K(.35f, k => { k.right_arm_up = 75; k.chest_pitch = 25; }), K(.6f, k => { k.right_arm_up = 80; k.chest_pitch = 20; }), K(.9f) };
        public static readonly MotionKey[] Throw = { K(0), K(.3f, k => { k.left_arm_up = 150; k.right_arm_up = 150; k.chest_pitch = -10; }), K(.55f, k => { k.left_arm_up = 80; k.right_arm_up = 80; k.chest_pitch = 15; }), K(.85f) };
        public static readonly MotionKey[] PickUp = { K(0), K(.35f, k => { k.chest_pitch = 35; k.hips_drop = .35f; k.left_arm_up = 50; k.right_arm_up = 50; k.left_knee = 30; k.right_knee = 30; }), K(.6f, k => { k.chest_pitch = 30; k.hips_drop = .3f; k.left_arm_up = 60; k.right_arm_up = 60; }), K(.9f) };
        static readonly Dictionary<MotionPreset, MotionKey[]> presets = new Dictionary<MotionPreset, MotionKey[]>
        {
            [MotionPreset.jump] = new[] { K(0), K(.25f, k => { k.hips_drop = .5f; k.chest_pitch = 15; k.left_knee = 40; k.right_knee = 40; }), K(.45f, k => { k.lift = .35f; k.left_arm_up = 160; k.right_arm_up = 160; }), K(.62f, k => { k.lift = .15f; k.left_arm_up = 120; k.right_arm_up = 120; }), K(.8f, k => { k.hips_drop = .35f; k.left_knee = 30; k.right_knee = 30; }), K(1.05f) },
            [MotionPreset.bow] = new[] { K(0), K(.6f, k => { k.chest_pitch = 50; k.head_pitch = 20; }), K(1.4f, k => { k.chest_pitch = 50; k.head_pitch = 20; }), K(2f) },
            [MotionPreset.banzai] = new[] { K(0), K(.4f, k => { k.left_arm_up = 175; k.right_arm_up = 175; k.left_arm_out = 15; k.right_arm_out = 15; k.head_pitch = -15; }), K(.7f, k => { k.left_arm_up = 160; k.right_arm_up = 160; k.lift = .08f; }), K(1f, k => { k.left_arm_up = 175; k.right_arm_up = 175; }), K(1.4f) },
            [MotionPreset.clap] = new[] { K(0), K(.3f, k => { k.left_arm_up = 80; k.right_arm_up = 80; k.left_arm_out = 35; k.right_arm_out = 35; k.left_elbow = 40; k.right_elbow = 40; }), K(.45f, k => { k.left_arm_up = 80; k.right_arm_up = 80; k.left_arm_out = 3; k.right_arm_out = 3; k.left_elbow = 40; k.right_elbow = 40; }), K(.6f, k => { k.left_arm_up = 80; k.right_arm_up = 80; k.left_arm_out = 35; k.right_arm_out = 35; k.left_elbow = 40; k.right_elbow = 40; }), K(.75f, k => { k.left_arm_up = 80; k.right_arm_up = 80; k.left_arm_out = 3; k.right_arm_out = 3; k.left_elbow = 40; k.right_elbow = 40; }), K(1.1f) },
            [MotionPreset.dance] = new[] { K(0), K(.4f, k => { k.chest_roll = 12; k.left_arm_up = 140; k.right_arm_out = 60; k.hips_drop = .2f; k.head_roll = 10; }), K(.8f, k => { k.chest_roll = -12; k.right_arm_up = 140; k.left_arm_out = 60; k.head_roll = -10; }), K(1.2f, k => { k.chest_roll = 12; k.left_arm_up = 140; k.right_arm_out = 60; k.hips_drop = .2f; k.right_leg = 25; }), K(1.6f, k => { k.chest_roll = -12; k.right_arm_up = 140; k.left_arm_out = 60; k.left_leg = 25; }), K(2f) },
            [MotionPreset.stretch] = new[] { K(0), K(.8f, k => { k.left_arm_up = 180; k.right_arm_up = 180; k.chest_pitch = -12; k.head_pitch = -20; k.lift = .05f; }), K(1.6f, k => { k.left_arm_up = 180; k.right_arm_up = 180; k.chest_pitch = -12; k.head_pitch = -20; k.lift = .05f; }), K(2.2f, k => { k.left_arm_up = 175; k.right_arm_up = 175; k.chest_roll = 18; }), K(2.8f, k => { k.left_arm_up = 175; k.right_arm_up = 175; k.chest_roll = -18; }), K(3.4f) },
            [MotionPreset.shake_head] = new[] { K(0), K(.15f, k => k.head_yaw = 30), K(.35f, k => k.head_yaw = -30), K(.55f, k => k.head_yaw = 30), K(.75f, k => k.head_yaw = -30), K(.9f) },
            [MotionPreset.nod] = new[] { K(0), K(.2f, k => k.head_pitch = 22), K(.4f), K(.6f, k => k.head_pitch = 22), K(.8f) },
            [MotionPreset.wave_both] = new[] { K(0), K(.3f, k => { k.left_arm_out = 150; k.right_arm_out = 150; }), K(.5f, k => { k.left_arm_out = 150; k.right_arm_out = 150; k.left_elbow = 45; k.right_elbow = 45; }), K(.7f, k => { k.left_arm_out = 150; k.right_arm_out = 150; }), K(.9f, k => { k.left_arm_out = 150; k.right_arm_out = 150; k.left_elbow = 45; k.right_elbow = 45; }), K(1.1f, k => { k.left_arm_out = 150; k.right_arm_out = 150; }), K(1.4f) },
            [MotionPreset.sit_floor] = new[] { K(0), K(.8f, k => { k.hips_drop = 1; k.left_leg = 70; k.right_leg = 70; k.left_knee = 60; k.right_knee = 60; k.chest_pitch = 8; }), K(3.6f, k => { k.hips_drop = 1; k.left_leg = 70; k.right_leg = 70; k.left_knee = 60; k.right_knee = 60; k.chest_pitch = 8; }), K(4.4f) },
            [MotionPreset.think] = new[] { K(0), K(.5f, k => { k.right_arm_up = 70; k.right_elbow = 130; k.head_roll = 15; k.head_pitch = 10; }), K(2.2f, k => { k.right_arm_up = 70; k.right_elbow = 130; k.head_roll = 15; k.head_pitch = 10; }), K(2.7f) },
            [MotionPreset.cheer] = new[] { K(0), K(.25f, k => { k.hips_drop = .3f; }), K(.45f, k => { k.lift = .25f; k.left_arm_up = 170; k.right_arm_up = 170; k.left_arm_out = 25; k.right_arm_out = 25; }), K(.7f, k => k.hips_drop = .2f), K(.95f, k => { k.lift = .25f; k.left_arm_up = 170; k.right_arm_up = 170; k.left_arm_out = 25; k.right_arm_out = 25; }), K(1.3f) },
        };
        public static MotionKey[] Get(MotionPreset preset) => presets.TryGetValue(preset, out var keys) ? keys : Array.Empty<MotionKey>();
    }
}
