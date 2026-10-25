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
        CharacterBrain brain; int version; bool frozen, travelFailed, hadFailure; Coroutine facing;
        public bool Busy { get; private set; }
        public MotionPlan Last { get; private set; }
        public string Request { get; private set; }
        public MotionStep Current { get; private set; }
        public int StepIndex { get; private set; } = -1;
        public Action<string> Status, Report;
        // What happened during the current plan (successes and failures), for the AI's next turn.
        public readonly List<string> Results = new List<string>();
        void Awake() { brain = GetComponent<CharacterBrain>(); }
        void Tell(string message) { Results.Add(message); Report?.Invoke(message); }
        ClipAnimator Clips => brain.GetComponent<ClipAnimator>();
        CompanionPlayground Toys => brain.playground;
        SceneAwareness World => brain.world;
        CharacterLocomotion Legs => brain.locomotion;
        BodySystem Body => brain.body;
        Vector3 Self => brain.transform.position;
        Vector3 User => brain.user.position;
        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        public void Run(MotionPlan plan, string request)
        {
            // A finished pickup keeps its prop across the next request; an explicit stop still releases it.
            Cancel(Busy || plan == null || plan.Empty || plan.steps.Any(s => s.action == MotionAction.stop)); Results.Clear();
            if (plan == null || plan.Empty) return;
            if (plan.steps.All(s => s.action == MotionAction.stop)) { Tell("ユーザーに言われて動きを止めました。"); return; }
            Last = plan; Request = request; Busy = true; hadFailure = false; int v = ++version;
            if (Legs) { Legs.Frozen = true; frozen = true; }
            StartCoroutine(Execute(plan, v));
        }
        public void Stop() { Cancel(true); Status?.Invoke("動きを止めました。"); }
        void Cancel(bool releaseHeld)
        {
            version++; StopAllCoroutines(); Current = null; StepIndex = -1;
            facing = null;
            Busy = false;
            if (Legs) { Legs.Stop(); if (frozen) Legs.Frozen = false; }
            frozen = false;
            if (Clips) Clips.StopAction();
            if (Body) { Body.ClearMotion(); Body.SetHeldArms(null); Body.lookPoint = null; Body.leftHandTarget = Body.rightHandTarget = null; if (Body.CurrentPose == BodySystem.Pose.crouch || Body.CurrentPose == BodySystem.Pose.point) Body.SetPose(BodySystem.Pose.stand, null); }
            if (Toys && releaseHeld) Toys.Settle();
        }
        IEnumerator Execute(MotionPlan plan, int v)
        {
            for (int index = 0; index < plan.steps.Length; index++)
            {
                var step = plan.steps[index];
                if (v != version) yield break;
                if (step.action == MotionAction.stop) break;
                Current = step; StepIndex = index;
                Body.SetHeldArms(MotionPlans.SupportsHeldArms(step.action) && step.keys?.Length == 1 && MotionPlans.ArmsOnly(step.keys[0]) ? step.keys[0] : null);
                Status?.Invoke("動き " + (index + 1) + "/" + plan.steps.Length + "：" + MotionPlans.ActionName(step));
                travelFailed = false;
                if (step.emotion != Emotion.neutral) brain.ShowEmotion(step.emotion);
                yield return Perform(step, v);
                Body.SetHeldArms(null);
                if (travelFailed) break;
            }
            if (v != version) yield break;
            Body.ClearMotion(); Body.leftHandTarget = Body.rightHandTarget = null; Body.lookPoint = null;
            if (Legs) Legs.Stop();
            Current = null; StepIndex = -1; Busy = false;
            if (Legs && frozen) Legs.Frozen = false; frozen = false;
            Tell((hadFailure ? "一部の動作は実行できませんでした。" : "頼まれた動き（" + (string.IsNullOrEmpty(plan.summary) ? "動作" : plan.summary) + "）を終えました。") + Seen());
            Status?.Invoke(hadFailure ? "一部の動作はできませんでした。" : "できたよ：" + plan.summary);
        }
        string Seen() { var seen = brain.sight ? brain.sight.VisibleSummary() : Array.Empty<string>(); return seen.Length > 0 ? "今見えているもの: " + string.Join("、", seen.Take(5)) + "。" : ""; }
        void Fail(string message) { hadFailure = true; Status?.Invoke(message); Tell("頼まれた動きの一部ができませんでした: " + message); }
        float ActionRate(float naturalSeconds = 0, int repeat = 1) => Current != null && Current.duration > 0 && naturalSeconds > 0
            ? naturalSeconds * repeat / Current.duration : Current?.pace == Pace.slow ? .65f : Current?.pace == Pace.hurry ? 1.4f : 1;

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
                    yield return Track(string.IsNullOrEmpty(step.target) ? "user" : step.target, amount > 0 ? amount : 2.5f, v); break;
                case MotionAction.sit:
                    if (Point(step.target, out _, out var seat) && seat != null && World != null)
                    {
                        yield return WalkToObject(seat, step.pace, v); if (v != version || travelFailed) yield break;
                        brain.Arrive(seat, ArrivalAction.sit); yield return Hold(1.2f, v);
                    }
                    else if (!string.IsNullOrEmpty(step.target)) Fail("座る場所が見つかりません。");
                    else yield return SitOnFloor(amount, v);
                    break;
                case MotionAction.crouch:
                    brain.HoldPose(BodySystem.Pose.crouch, Point(step.target, out var low, out _) ? low : (Vector3?)null, amount > 0 ? amount : 4);
                    yield return Hold(amount > 0 ? amount : 1.5f, v); brain.EndPose(); break;
                case MotionAction.stand: brain.EndPose(); yield return Hold(.5f, v); break;
                case MotionAction.point:
                    if (Point(string.IsNullOrEmpty(step.target) ? "user" : step.target, out var shown, out _)) { Face(shown); brain.HoldPose(BodySystem.Pose.point, shown, 4); yield return Hold(2.5f, v); }
                    else Fail("指さす物が見つかりません。");
                    break;
                case MotionAction.gesture:
                    int repetitions = Mathf.Clamp(Mathf.RoundToInt(amount), 1, 10); float gestureRate = ActionRate(2.4f, repetitions);
                    for (int i = 0; i < repetitions; i++) { if (v != version || travelFailed) yield break; Body.PerformGesture(step.gesture, gestureRate); yield return Hold(2.4f / gestureRate, v); }
                    break;
                case MotionAction.motion:
                    if (step.motion == MotionPreset.sit_floor) { yield return SitOnFloor(step.duration, v); break; }
                    if (step.motion == MotionPreset.jump || step.motion == MotionPreset.dance || step.motion == MotionPreset.spin || step.motion == MotionPreset.stretch || step.motion == MotionPreset.cheer) brain.EndPose();
                    if (step.motion == MotionPreset.spin) { yield return Spin(Mathf.Clamp(Mathf.RoundToInt(amount), 1, 10), v); break; }
                    // Real (motion-captured) animation when this model has one, keyframes otherwise.
                    var clipNames = step.motion == MotionPreset.custom ? null : MotionLibrary.ClipsFor(step.motion, Clips);
                    if (clipNames != null && step.motion == MotionPreset.jump && Clips.Has("Jump_Loop")) { for (int i = 0; i < Mathf.Clamp(Mathf.RoundToInt(amount), 1, 10) && v == version; i++) yield return Jump(v); break; }
                    if (clipNames != null) { yield return PlayClips(clipNames, MotionLibrary.LoopSeconds(step.motion), Mathf.Clamp(Mathf.RoundToInt(amount), 1, 10), v); break; }
                    var keys = step.motion == MotionPreset.custom ? step.keys : MotionLibrary.Get(step.motion);
                    if (keys == null || keys.Length == 0) { Fail("その動きの形がわかりませんでした。"); break; }
                    yield return Keys(keys, Mathf.Clamp(Mathf.RoundToInt(amount), 1, 10), v); break;
                case MotionAction.follow_user: yield return Follow(amount > 0 ? amount : 20, v); break;
                case MotionAction.wait: yield return Hold(amount > 0 ? amount : 1.5f, v); break;
                case MotionAction.spawn_toy:
                    {
                        var kind = CompanionPlayground.KindOf(step.target) ?? CompanionPlayground.ToyKind.Ball;
                        var made = Toys ? Toys.Spawn(kind) : null;
                        if (made == null) { Fail(CompanionPlayground.Label(kind) + "を置ける場所がありません。"); break; }
                        Look(made.Center); Tell(made.item.id + "を床に出しました。"); yield return Hold(.6f, v); break;
                    }
                case MotionAction.roll: case MotionAction.kick: case MotionAction.throw_ball: case MotionAction.bounce:
                    yield return PlayBall(step, v); break;
                case MotionAction.fetch: yield return Fetch(step, v); break;
                case MotionAction.pick_up:
                    {
                        var held = NeedToy(step.target, null); if (held == null) break;
                        if (held.carried) break;
                        yield return WalkToToy(held, Self, step.pace, v); if (v != version || travelFailed) yield break;
                        yield return Grab(held, false, v, null); Tell(held.item.id + "を持ち上げました。"); break;
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
                        yield return WalkToToy(top, Self, step.pace, v); if (v != version || travelFailed) yield break;
                        brain.HoldPose(BodySystem.Pose.crouch, top.Center, 3); yield return Hold(.5f, v); Toys.Spin(top, 30); yield return Hold(1.5f, v); break;
                    }
                case MotionAction.put_away: if (Toys) Toys.ClearToys(); yield return Hold(.3f, v); break;
                case MotionAction.put_on:
                    {
                        var thing = NeedToy(step.target, null); if (thing == null) break;
                        if (!Point(step.toward, out _, out var furniture) || furniture == null || Toys.Find(step.toward) != null) { Fail("置く場所が見つかりません。"); break; }
                        if (!thing.carried) { yield return WalkToToy(thing, Self, step.pace, v); if (v != version || travelFailed) yield break; yield return Grab(thing, false, v, null); if (v != version || travelFailed) yield break; }
                        yield return WalkToObject(furniture, step.pace, v); if (v != version || travelFailed) yield break;
                        Face(furniture.bounds.center); yield return Hold(.3f, v); yield return Toys.PutOn(thing, furniture);
                        Tell(thing.item.id + "を" + furniture.id + "の上に置きました。"); break;
                    }
                case MotionAction.touch:
                    {
                        string id = string.IsNullOrEmpty(step.target) ? "user" : step.target;
                        if (!Point(id, out var spot, out var thing)) { Fail("触る物が見つかりません。"); break; }
                        var nearToy = Toys ? Toys.Find(id) : null;
                        if (id == "user") yield return ApproachUser(step.pace, v);
                        else if (nearToy != null) yield return WalkToToy(nearToy, Self, step.pace, v);
                        else yield return WalkToObject(thing, step.pace, v);
                        if (v != version || travelFailed) yield break;
                        // The hand goes to the nearest surface at about chest height (for the user: towards their hand).
                        var hand = id == "user" ? Vector3.Lerp(Self + Vector3.up * .6f, User - Vector3.up * .5f, .6f) : (thing != null ? thing.bounds.ClosestPoint(Self + Vector3.up * .6f) : spot);
                        Face(hand); yield return Hold(.25f, v); Body.rightHandTarget = hand; if (Clips) Clips.Play("Interact");
                        yield return Hold(1.6f, v); Body.rightHandTarget = null;
                        Tell((id == "user" ? "ユーザー" : id) + "に手で触れました。"); break;
                    }
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
            if (toy == null) { var wanted = CompanionPlayground.KindOf(id) ?? kind ?? CompanionPlayground.ToyKind.Ball; toy = Toys.Spawn(wanted); if (toy != null) Tell(toy.item.id + "を床に出しました。"); }
            if (toy == null) Fail("おもちゃを置ける場所がありません。");
            return toy;
        }

        IEnumerator Walk(Vector3 goal, string place, Vector3? face, Pace pace, int v)
        {
            if (!Legs) { travelFailed = true; Fail("歩行の準備ができていません。"); yield break; }
            if (facing != null) { StopCoroutine(facing); facing = null; }
            bool? arrived = null; brain.EndPose();
            if (!Legs.MoveTo(goal, place, face, ok => arrived = ok, pace)) { travelFailed = true; Fail(place + "へ行く道がありません。"); yield break; }
            while (arrived == null && v == version) yield return null;
            if (arrived == false && v == version) { travelFailed = true; Fail(place + "へ行けませんでした。"); }
        }
        IEnumerator WalkToObject(SceneObject item, Pace pace, int v)
        {
            if (World == null || !World.Approach(item, Self, .3f, out var spot)) { travelFailed = true; Fail(item.id + "のそばに行けません。"); yield break; }
            yield return Walk(spot, item.id, item.bounds.center, pace, v);
        }
        IEnumerator WalkToToy(CompanionPlayground.Toy toy, Vector3 preferred, Pace pace, int v)
        {
            if (toy.carried) yield break;
            if (SceneAwareness.FlatDistance(toy.item, Self) < .9f && Vector3.Distance(Flat(Self), Flat(preferred)) < .35f) { Face(toy.Center); yield break; }
            if (!Toys.StandSpot(toy, preferred, out var spot)) { travelFailed = true; Fail(toy.item.id + "のそばに行けません。"); yield break; }
            yield return Walk(spot, toy.item.id, toy.Center, pace, v);
            if (v == version && !travelFailed) { Face(toy.Center); yield return Hold(.35f, v); }
        }
        IEnumerator ApproachUser(Pace pace, int v)
        {
            if (World == null) { travelFailed = true; Fail("この場所では歩けません。"); yield break; }
            Vector3 away = Flat(Self - User); if (away.sqrMagnitude < .04f) away = -brain.transform.forward;
            yield return Walk(World.grid.Snap(Flat(User) + away.normalized * Mathf.Max(.8f, brain.profile.personalSpace + .3f) + Vector3.up * Self.y), "ユーザーのそば", User, pace, v);
        }
        IEnumerator Follow(float seconds, int v)
        {
            for (float end = Time.time + seconds; Time.time < end && v == version;)
            {
                if (World != null && Legs && !Legs.Moving && Vector3.Distance(Flat(Self), Flat(User)) > 1.5f) { Vector3 away = Flat(Self - User).normalized; Legs.MoveTo(World.grid.Snap(Flat(User) + away * 1.0f), "ユーザーのそば", User, null, Current?.pace ?? Pace.normal); }
                yield return new WaitForSeconds(.5f);
            }
            if (Legs) Legs.Stop();
        }
        IEnumerator SitOnFloor(float seconds, int v)
        {
            brain.EndPose();
            brain.HoldPose(BodySystem.Pose.floor_sit, null, seconds > 0 ? seconds : float.PositiveInfinity);
            yield return Hold(seconds > 0 ? seconds : .6f, v);
            if (seconds > 0 && v == version) brain.EndPose();
        }
        IEnumerator Track(string id, float seconds, int v)
        {
            if (!Point(id, out var at, out _)) { Fail("見る物が見つかりません。"); yield break; }
            Look(at);
            for (float end = Time.time + seconds; Time.time < end && v == version;)
            {
                if (!Point(id, out at, out _)) { Fail("見ていた物がなくなりました。"); yield break; }
                Body.lookPoint = at; yield return null;
            }
        }
        void Face(Vector3 point)
        {
            var d = Flat(point - Self); if (d.sqrMagnitude < .01f) return;
            if (facing != null) StopCoroutine(facing);
            facing = StartCoroutine(TurnTo(Quaternion.LookRotation(d.normalized).eulerAngles.y, version));
        }
        void Look(Vector3 point) { Body.lookPoint = point; Body.Apply(new BehaviorDecision { emotion = Body.CurrentEmotion, gaze = Gaze.@object }); }
        IEnumerator TurnTo(float yaw, int v)
        {
            while (v == version && Mathf.Abs(Mathf.DeltaAngle(brain.transform.eulerAngles.y, yaw)) > 1)
            { brain.transform.rotation = Quaternion.Euler(0, Mathf.MoveTowardsAngle(brain.transform.eulerAngles.y, yaw, 240 * ActionRate() * Time.deltaTime), 0); yield return null; }
        }
        IEnumerator Turn(MotionStep step, int v)
        {
            if (facing != null) { StopCoroutine(facing); facing = null; }
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
            float rate = ActionRate(Mathf.Abs(total) / 200);
            for (float done = 0; Mathf.Abs(done) < Mathf.Abs(total) - .5f && v == version;)
            {
                float delta = Mathf.Sign(total) * Mathf.Min(Mathf.Abs(total) - Mathf.Abs(done), 200 * rate * Time.deltaTime);
                brain.transform.Rotate(0, delta, 0); done += delta; yield return null;
            }
        }
        IEnumerator Spin(int turns, int v)
        {
            if (facing != null) { StopCoroutine(facing); facing = null; }
            float rate = ActionRate(360f * turns / 420f);
            for (float done = 0; done < 360 * turns && v == version;)
            { float delta = Mathf.Min(360 * turns - done, 420 * rate * Time.deltaTime); brain.transform.Rotate(0, delta, 0); done += delta; Body.SetMotion(new MotionKey { left_arm_out = 70, right_arm_out = 70 }); yield return null; }
            Body.ClearMotion();
        }
        IEnumerator Hold(float seconds, int v) { for (float end = Time.time + seconds; Time.time < end && v == version;) yield return null; }
        IEnumerator PlayClips(string[] names, float loop, int repeat, int v)
        {
            float natural = names.Sum(n => n.EndsWith("_Loop") || MotionLibrary.Loops(n) ? loop : Clips.Length(n));
            float rate = ActionRate(natural, repeat);
            for (int r = 0; r < repeat && v == version; r++)
                foreach (var clip in names)
                {
                    if (v != version || travelFailed) yield break;
                    Clips.Play(clip, clip.EndsWith("_Loop") || MotionLibrary.Loops(clip) ? loop / rate : 0, rate);
                    yield return null; while (Clips.GestureActive && v == version) yield return null;
                }
        }
        // Take-off crouch, a real arc through the air in the airborne pose, then the landing.
        IEnumerator Jump(int v)
        {
            float crouch = Clips.Length("Jump_Start") * .7f;
            float rate = ActionRate(crouch + .5f + Clips.Length("Jump_Land"), Mathf.Clamp(Mathf.RoundToInt(Current.amount), 1, 10));
            Clips.Play("Jump_Start", rate: rate);
            for (float t = 0; t < crouch && v == version; t += Time.deltaTime * rate) yield return null;
            Clips.Play("Jump_Loop", .6f / rate, rate);
            for (float t = 0; t < .5f && v == version; t += Time.deltaTime * rate) { float phase = t / .5f; Body.SetMotion(new MotionKey { lift = 4 * phase * (1 - phase) * .35f }); yield return null; }
            Body.ClearMotion(); Clips.Play("Jump_Land", rate: rate);
            yield return null; while (Clips.GestureActive && v == version) yield return null;
        }
        // Crouches, reaches both hands to the toy's sides, then lifts it.
        IEnumerator Grab(CompanionPlayground.Toy toy, bool single, int v, Action<List<Rigidbody>> got)
        {
            brain.HoldPose(BodySystem.Pose.crouch, toy.Center, 3); Look(toy.Center);
            var side = Flat(brain.transform.right) * Mathf.Clamp(toy.item.bounds.extents.x * .8f, .06f, .25f);
            Body.leftHandTarget = toy.Center - side; Body.rightHandTarget = toy.Center + side;
            yield return Hold(.6f, v);
            Body.leftHandTarget = Body.rightHandTarget = null; if (v != version || travelFailed) yield break;
            var taken = Toys.PickUp(toy, single); brain.EndPose(); got?.Invoke(taken);
            yield return Hold(.35f, v);
        }
        // Plays keyframes smoothly; the body eases back to its normal pose afterwards.
        IEnumerator Keys(MotionKey[] keys, int repeat, int v, Action<float> at = null)
        {
            if (keys.Length == 0) yield break;
            float length = Mathf.Max(.2f, keys[keys.Length - 1].time);
            float rate = ActionRate(length * repeat + .3f);
            for (int r = 0; r < repeat && v == version; r++)
                for (float t = 0; t <= length && v == version; t += Time.deltaTime * rate) { Body.SetMotion(MotionLibrary.Sample(keys, t)); at?.Invoke(t); yield return null; }
            var last = Body.Motion ?? new MotionKey();
            for (float t = 0; t < .3f && v == version; t += Time.deltaTime * rate) { Body.SetMotion(MotionKey.Lerp(last, new MotionKey(), Mathf.SmoothStep(0, 1, t / .3f))); yield return null; }
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
                yield return WalkToToy(ball, Self, step.pace, v); if (v != version || travelFailed) yield break;
                brain.HoldPose(BodySystem.Pose.crouch, ball.Center, 1.2f); yield return Keys(MotionLibrary.PickUp, 1, v); if (v != version || travelFailed) yield break;
                Look(ball.Center); yield return Toys.Bounce(ball, Mathf.Clamp(Mathf.RoundToInt(step.amount), 2, 8)); Tell(ball.item.id + "を弾ませました。"); yield break;
            }
            var target = Destination(step, ball.Center, 2.5f); Vector3 aim = Flat(target - ball.Center).normalized;
            // Stand on the far side of the ball from where it should go.
            yield return WalkToToy(ball, Flat(ball.Center) - aim * .6f + Vector3.up * Self.y, step.pace, v); if (v != version || travelFailed) yield break;
            aim = Flat(target - ball.Center).normalized; float distance = Vector3.Distance(Flat(ball.Center), Flat(target));
            bool pushed = false;
            if (step.action == MotionAction.kick)
                yield return Keys(MotionLibrary.Kick, 1, v, t => { if (!pushed && t >= .42f) { pushed = true; Toys.Push(ball, aim * Mathf.Max(2.5f, CompanionPlayground.SpeedFor(distance) * 1.5f) + Vector3.up * 1.2f); } });
            else
            {
                // Crouch, put a hand behind the ball and push it off.
                brain.HoldPose(BodySystem.Pose.crouch, ball.Center, 2.5f); Body.rightHandTarget = ball.Center - aim * CompanionPlayground.BallRadius; yield return Hold(.5f, v);
                yield return Keys(MotionLibrary.Roll, 1, v, t => { if (!pushed && t >= .35f) { pushed = true; Toys.Push(ball, aim * Mathf.Clamp(CompanionPlayground.SpeedFor(distance), .7f, 4)); } });
                Body.rightHandTarget = null;
            }
            if (!pushed && v == version) Toys.Push(ball, aim * 1.5f);
            brain.EndPose();
            yield return Watch(ball, 4, v);
            Tell(ball.item.id + "を" + (step.action == MotionAction.kick ? "蹴りました" : "転がしました") + "。");
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
                yield return WalkToToy(toy, Self, step.pace, v); if (v != version || travelFailed) yield break;
                List<Rigidbody> taken = null; yield return Grab(toy, true, v, g => taken = g); if (v != version || travelFailed || taken == null) yield break;
                grip = taken;
            }
            var target = Destination(step, Self, 3);
            Face(target); yield return Hold(.45f, v); if (v != version || travelFailed) yield break;
            float distance = Vector3.Distance(Flat(Self), Flat(target)), flight = Mathf.Clamp(distance / 3.2f, .6f, 1.1f); bool thrown = false;
            if (Clips && Clips.Has("OverhandThrow"))
            {
                // The captured overhand throw: wind up, then let go as the arm comes over.
                float length = Clips.Length("OverhandThrow"); Clips.Play("OverhandThrow");
                for (float t = 0; t < length && v == version; t += Time.deltaTime) { if (!thrown && t >= length * .45f) { thrown = true; Toys.Throw(grip, target, flight); } yield return null; }
            }
            else yield return Keys(MotionLibrary.Throw, 1, v, t => { if (!thrown && t >= .55f) { thrown = true; Toys.Throw(grip, target, flight); } });
            if (!thrown && v == version) Toys.Throw(grip, target, flight);
            yield return Watch(toy, 4, v);
            Tell(toy.item.id + "を投げました。");
        }
        IEnumerator Fetch(MotionStep step, int v)
        {
            var toy = NeedToy(step.target, null); if (toy == null) yield break;
            if (!toy.carried)
            {
                yield return WalkToToy(toy, Self, step.pace, v); if (v != version || travelFailed) yield break;
                yield return Grab(toy, false, v, null); if (v != version || travelFailed) yield break;
            }
            yield return ApproachUser(step.pace, v); if (v != version || travelFailed) yield break;
            // Bend down and set it at the user's feet.
            brain.HoldPose(BodySystem.Pose.crouch, null, 1); yield return Hold(.5f, v);
            Toys.PutDown(toy, Self + Flat(User - Self).normalized * .5f); brain.EndPose();
            Body.Apply(new BehaviorDecision { emotion = Emotion.happy, gesture = Gesture.nod }); Tell(toy.item.id + "をユーザーのところへ持ってきました。");
        }
        IEnumerator PlayBlocks(MotionStep step, int v)
        {
            var blocks = NeedToy(step.target, CompanionPlayground.ToyKind.Blocks); if (blocks == null || blocks.kind != CompanionPlayground.ToyKind.Blocks) { if (blocks != null) Fail(blocks.item.id + "は積み木ではありません。"); yield break; }
            if (blocks.carried) Toys.PutDown(blocks, Self + Flat(brain.transform.forward).normalized * .5f);
            yield return WalkToToy(blocks, Self, step.pace, v); if (v != version || travelFailed) yield break;
            brain.HoldPose(BodySystem.Pose.crouch, blocks.Center, 30); Look(blocks.Center);
            switch (step.action)
            {
                case MotionAction.stack:
                    if (blocks.stacked) yield return Toys.LineUp(blocks, brain.transform.right, .2f);
                    yield return Toys.Stack(blocks, Mathf.RoundToInt(step.amount)); Tell(blocks.item.id + "を積み上げました。"); break;
                case MotionAction.line_up: yield return Toys.LineUp(blocks, brain.transform.right); Tell(blocks.item.id + "を一列に並べました。"); break;
                default:
                    if (!blocks.stacked) yield return Toys.Stack(blocks, 5, .25f);
                    yield return Keys(MotionLibrary.Kick, 1, v, null); Toys.KnockDown(blocks, Direction(step, blocks.Center));
                    yield return Hold(1.2f, v); Tell(blocks.item.id + "の塔を崩しました。"); break;
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
            [MotionPreset.punch] = new[] { K(0), K(.15f, k => { k.right_arm_up = 20; k.right_elbow = 120; k.chest_yaw = -15; }), K(.3f, k => { k.right_arm_up = 90; k.chest_yaw = 20; }), K(.5f, k => { k.right_arm_up = 50; k.right_elbow = 90; }), K(.8f) },
            [MotionPreset.forward_roll] = new[] { K(0), K(.3f, k => { k.hips_drop = .8f; k.chest_pitch = 60; k.head_pitch = 40; k.left_knee = 80; k.right_knee = 80; }), K(.7f, k => { k.hips_drop = .9f; k.chest_pitch = 85; k.head_pitch = 50; k.left_knee = 90; k.right_knee = 90; }), K(1.1f, k => k.hips_drop = .5f), K(1.5f) },
            [MotionPreset.push] = new[] { K(0), K(.4f, k => { k.left_arm_up = 90; k.right_arm_up = 90; k.left_elbow = 30; k.right_elbow = 30; k.chest_pitch = 15; }), K(2f, k => { k.left_arm_up = 90; k.right_arm_up = 90; k.left_elbow = 10; k.right_elbow = 10; k.chest_pitch = 20; }), K(2.4f) },
            [MotionPreset.fold_arms] = new[] { K(0), K(.5f, k => { k.left_arm_up = 70; k.right_arm_up = 70; k.left_arm_out = -30; k.right_arm_out = -30; k.left_elbow = 130; k.right_elbow = 130; }), K(2.5f, k => { k.left_arm_up = 70; k.right_arm_up = 70; k.left_arm_out = -30; k.right_arm_out = -30; k.left_elbow = 130; k.right_elbow = 130; k.head_roll = 8; }), K(3f) },
            [MotionPreset.phone] = new[] { K(0), K(.5f, k => { k.right_arm_up = 40; k.right_elbow = 150; k.head_roll = 10; }), K(2.6f, k => { k.right_arm_up = 40; k.right_elbow = 150; k.head_roll = 12; k.head_yaw = 10; }), K(3.1f) },
            [MotionPreset.zombie] = new[] { K(0), K(.5f, k => { k.left_arm_up = 90; k.right_arm_up = 90; k.head_roll = 15; k.chest_pitch = 10; }), K(1.5f, k => { k.left_arm_up = 95; k.right_arm_up = 85; k.head_roll = -15; k.chest_roll = 8; }), K(2.5f, k => { k.left_arm_up = 85; k.right_arm_up = 95; k.head_roll = 15; k.chest_roll = -8; }), K(3f) },
            [MotionPreset.swim] = new[] { K(0), K(.4f, k => { k.left_arm_up = 180; k.chest_pitch = 15; }), K(.8f, k => { k.right_arm_up = 180; k.left_arm_up = 90; k.chest_pitch = 15; }), K(1.2f, k => { k.left_arm_up = 180; k.right_arm_up = 90; k.chest_pitch = 15; }), K(1.6f, k => { k.right_arm_up = 180; k.chest_pitch = 15; }), K(2f) },
            [MotionPreset.fix] = new[] { K(0), K(.5f, k => { k.hips_drop = .8f; k.left_knee = 80; k.right_knee = 80; k.chest_pitch = 30; k.right_arm_up = 60; k.right_elbow = 60; }), K(1.2f, k => { k.hips_drop = .8f; k.left_knee = 80; k.right_knee = 80; k.chest_pitch = 30; k.right_arm_up = 70; k.right_elbow = 30; }), K(1.9f, k => { k.hips_drop = .8f; k.left_knee = 80; k.right_knee = 80; k.chest_pitch = 30; k.right_arm_up = 60; k.right_elbow = 60; }), K(2.5f) },
            [MotionPreset.eat] = new[] { K(0), K(.4f, k => { k.right_arm_up = 60; k.right_elbow = 140; }), K(.7f, k => { k.right_arm_up = 30; k.right_elbow = 90; }), K(1.1f, k => { k.right_arm_up = 60; k.right_elbow = 140; k.head_pitch = -5; }), K(1.6f) },
            [MotionPreset.harvest] = new[] { K(0), K(.5f, k => { k.chest_pitch = 50; k.hips_drop = .3f; k.left_arm_up = 40; k.right_arm_up = 40; }), K(1.1f, k => { k.chest_pitch = 20; k.left_arm_up = 80; k.right_arm_up = 80; k.left_elbow = 50; k.right_elbow = 50; }), K(1.6f) },
            [MotionPreset.plant] = new[] { K(0), K(.5f, k => { k.hips_drop = .7f; k.left_knee = 60; k.right_knee = 60; k.chest_pitch = 40; k.right_arm_up = 30; }), K(1.4f, k => { k.hips_drop = .7f; k.left_knee = 60; k.right_knee = 60; k.chest_pitch = 45; k.right_arm_up = 20; k.right_elbow = 30; }), K(2f) },
            [MotionPreset.water] = new[] { K(0), K(.5f, k => { k.right_arm_up = 60; k.right_elbow = 20; k.chest_pitch = 10; }), K(2f, k => { k.right_arm_up = 50; k.right_elbow = 20; k.chest_pitch = 15; k.chest_roll = 5; }), K(2.5f) },
            [MotionPreset.chop] = new[] { K(0), K(.4f, k => { k.left_arm_up = 160; k.right_arm_up = 160; k.chest_pitch = -10; }), K(.7f, k => { k.left_arm_up = 40; k.right_arm_up = 40; k.chest_pitch = 30; }), K(1.1f, k => { k.left_arm_up = 160; k.right_arm_up = 160; k.chest_pitch = -10; }), K(1.4f, k => { k.left_arm_up = 40; k.right_arm_up = 40; k.chest_pitch = 30; }), K(1.8f) },
            [MotionPreset.die] = new[] { K(0), K(.5f, k => { k.hips_drop = 1; k.chest_pitch = 70; k.head_pitch = 40; k.left_knee = 90; k.right_knee = 90; k.left_arm_out = 40; k.right_arm_out = 40; }), K(3f, k => { k.hips_drop = 1; k.chest_pitch = 75; k.head_pitch = 45; k.left_knee = 90; k.right_knee = 90; k.left_arm_out = 40; k.right_arm_out = 40; }), K(3.8f) },
            [MotionPreset.surprised] = new[] { K(0), K(.15f, k => { k.left_arm_out = 60; k.right_arm_out = 60; k.left_elbow = 60; k.right_elbow = 60; k.lift = .05f; k.head_pitch = -10; }), K(.8f, k => { k.left_arm_out = 50; k.right_arm_out = 50; k.left_elbow = 60; k.right_elbow = 60; }), K(1.2f) },
            [MotionPreset.knockback] = new[] { K(0), K(.2f, k => { k.chest_pitch = -30; k.head_pitch = -20; k.left_arm_out = 50; k.right_arm_out = 50; k.lift = .05f; }), K(.6f, k => { k.hips_drop = .3f; k.chest_pitch = -10; }), K(1.2f) },
            [MotionPreset.talk] = new[] { K(0), K(.5f, k => { k.right_arm_up = 50; k.right_elbow = 70; k.head_yaw = 8; }), K(1.2f, k => { k.left_arm_up = 50; k.left_elbow = 70; k.right_arm_up = 30; k.right_elbow = 40; k.head_yaw = -8; }), K(2f, k => { k.right_arm_up = 55; k.right_elbow = 70; k.head_roll = 6; }), K(2.6f) },
            [MotionPreset.interact] = new[] { K(0), K(.4f, k => { k.right_arm_up = 80; k.right_elbow = 20; k.chest_pitch = 10; }), K(1.2f, k => { k.right_arm_up = 85; k.right_elbow = 15; k.chest_pitch = 12; }), K(1.6f) },
        };
        public static MotionKey[] Get(MotionPreset preset) => presets.TryGetValue(preset, out var keys) ? keys : Array.Empty<MotionKey>();
        // Captured animations per preset: the CC0 humanoid library (WhiteChibi) and the robot's own clips. Missing ones fall back to keyframes.
        static readonly Dictionary<MotionPreset, (string[] human, string[] robot, float loop)> clips = new Dictionary<MotionPreset, (string[], string[], float)>
        {
            [MotionPreset.jump] = (new[] { "Jump_Start", "Jump_Land" }, new[] { "Jump" }, 0),
            [MotionPreset.dance] = (new[] { "Dance_Loop" }, new[] { "Dance" }, 4),
            [MotionPreset.nod] = (new[] { "Yes" }, new[] { "Yes" }, 0),
            [MotionPreset.shake_head] = (new[] { "Idle_No_Loop" }, new[] { "No" }, 2.5f),
            [MotionPreset.think] = (new[] { "Idle_FoldArms_Loop" }, null, 3),
            [MotionPreset.fold_arms] = (new[] { "Idle_FoldArms_Loop" }, null, 3),
            [MotionPreset.cheer] = (null, new[] { "ThumbsUp" }, 0),
            [MotionPreset.sit_floor] = (null, new[] { "Sitting", "Standing" }, 0),
            [MotionPreset.punch] = (new[] { "Punch_Jab", "Punch_Cross" }, new[] { "Punch" }, 0),
            [MotionPreset.forward_roll] = (new[] { "Roll" }, null, 0),
            [MotionPreset.push] = (new[] { "Push_Loop" }, null, 2.5f),
            [MotionPreset.phone] = (new[] { "Idle_TalkingPhone_Loop" }, null, 3),
            [MotionPreset.zombie] = (new[] { "Zombie_Idle_Loop", "Zombie_Scratch" }, null, 2),
            [MotionPreset.swim] = (new[] { "Swim_Idle_Loop" }, null, 3),
            [MotionPreset.fix] = (new[] { "Fixing_Kneeling" }, null, 0),
            [MotionPreset.eat] = (new[] { "Consume" }, null, 0),
            [MotionPreset.harvest] = (new[] { "Farm_Harvest" }, null, 0),
            [MotionPreset.plant] = (new[] { "Farm_PlantSeed" }, null, 0),
            [MotionPreset.water] = (new[] { "Farm_Watering" }, null, 0),
            [MotionPreset.chop] = (new[] { "TreeChopping_Loop" }, null, 3),
            [MotionPreset.die] = (new[] { "Death01", "LayToIdle" }, new[] { "Death", "Standing" }, 0),
            [MotionPreset.surprised] = (new[] { "Hit_Chest" }, null, 0),
            [MotionPreset.knockback] = (new[] { "Hit_Knockback" }, null, 0),
            [MotionPreset.talk] = (new[] { "Idle_Talking_Loop" }, null, 3),
            [MotionPreset.interact] = (new[] { "Interact" }, null, 0),
        };
        static readonly HashSet<string> looping = new HashSet<string> { "Dance" };
        public static bool Loops(string clip) => looping.Contains(clip);
        public static float LoopSeconds(MotionPreset preset) => clips.TryGetValue(preset, out var c) ? Mathf.Max(1.5f, c.loop) : 2;
        public static string[] ClipsFor(MotionPreset preset, ClipAnimator player)
        {
            if (!player || !clips.TryGetValue(preset, out var c)) return null;
            var names = player.Humanoid ? c.human : c.robot;
            return names != null && names.All(player.Has) ? names : null;
        }
    }
}
