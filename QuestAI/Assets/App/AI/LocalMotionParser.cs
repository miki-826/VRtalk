using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace QuestAI
{
    // On-device understanding of Japanese movement requests, so the body obeys instantly and without any API.
    // Each clause (split at 「、」「それから」「てから」…) becomes one or more steps; body-part phrases
    // (「右手を上げたまま左足を上げて」) become a custom keyframed pose. Anything not understood is reported so the
    // AI planner can take over when it is available.
    public static class LocalMotionParser
    {
        public sealed class Result
        {
            public MotionPlan plan = new MotionPlan();
            // At least one clause looked like a request, and every such clause was understood.
            public bool anyRequest, complete;
            public readonly List<string> unknown = new List<string>();
        }
        static readonly Regex Splitter = new Regex("[。、,，!！?？\n]|そして|それから|その後|そのあと|次に|つぎに|あとで");
        static readonly Regex Number = new Regex(@"(\d+(?:\.\d+)?|[一二三四五六七八九十百]+|半)\s*(回|かい|歩|ほ|メートル|ｍ|m|度|ど|段|だん|個|こ|秒|びょう)");
        // Split connected imperatives, but leave auxiliary endings (てみて/でください) and simultaneous poses intact.
        static readonly Regex Connected = new Regex(@"(?<=(?:して|んで|いて|いで|って|えて|けて|げて|せて|めて|べて|れて|きて|来て|見て|みて))(?<!急いで)(?<!いそいで)(?=(?:ゆっくり|そっと|急いで|早く|次|その|それ|こっち|あっち|[左右両前後]|\d|[一二三四五六七八九十]|ジャンプ|じゃんぷ|お辞儀|踊|おど|拍手|手を|腕を|頭を|首を|体を|足を|座|すわ|立|たち|しゃが|歩|あるい|走|はし|回|まわ|向|むい|ボール|積み木|クッション|本を|箱を|ぬいぐるみ|窓|暖炉|ソファ|椅子|テーブル|外を|拾|ひろ|投げ|なげ|蹴|転が|ころが|崩|くず|並べ|待|まって|止ま|ストップ))");
        static bool Has(string text, params string[] words) => words.Any(text.Contains);

        public static Result Parse(string text, MotionRequest context)
        {
            var result = new Result(); var steps = new List<MotionStep>(); var summary = new List<string>();
            text = (text ?? "").Normalize(NormalizationForm.FormKC).Replace(" ", "").Replace("　", "");
            // 「積んでから崩して」: keep the て so each half still reads as a request.
            text = Regex.Replace(text, "(て|で)から", "$1、");
            text = Connected.Replace(text, "、");
            if (text.Length == 0) return result;
            string lastToy = null;
            foreach (var raw in Splitter.Split(text))
            {
                var clause = raw.Trim(); if (clause.Length == 0) continue;
                if (Has(clause, "ないで", "なくていい", "しなくて") && !Has(clause, "動かないで", "うごかないで")) continue;
                bool request = IsRequest(clause), seeing = Has(clause, "見える", "みえる", "何が見", "なにが見", "なにがみ", "何見て", "何を見", "これ見て", "あれ見て", "よく見て", "見てみて", "周り見", "まわり見", "観察", "景色", "けしき", "外を見", "そとを見", "様子を見");
                if (!request && !seeing) continue;
                result.anyRequest = true;
                Pace pace = Has(clause, "ゆっくり", "そっと", "のんびり") ? Pace.slow : Has(clause, "急いで", "いそいで", "走って", "はしって", "早く", "はやく", "全速") ? Pace.hurry : Pace.normal;
                Emotion mood = Has(clause, "嬉しそう", "うれしそう", "楽しそう", "たのしそう", "笑顔", "ニコニコ") ? Emotion.happy : Has(clause, "悲しそう", "かなしそう", "しょんぼり") ? Emotion.sad : Has(clause, "元気", "げんき", "ノリノリ") ? Emotion.excited : Emotion.neutral;
                var made = Clause(clause, context, ref lastToy, out string said);
                if (made.Count == 0) { result.unknown.Add(clause); continue; }
                foreach (var step in made) { if (step.pace == Pace.normal) step.pace = pace; if (step.emotion == Emotion.neutral) step.emotion = mood; if (step.action == MotionAction.motion || step.action == MotionAction.gesture) step.duration = Amount(clause, "秒", "びょう"); }
                steps.AddRange(made); summary.Add(said);
            }
            result.plan = MotionPlans.Validate(new MotionPlan { steps = steps.ToArray(), summary = string.Join("、", summary) }, context);
            if (steps.Count > MotionPlans.MaxSteps) result.unknown.Add("動作が一度に実行できる数を超えています");
            result.complete = result.anyRequest && result.unknown.Count == 0 && !result.plan.Empty;
            return result;
        }

        // Imperatives, invitations and "can you…?" all count; plain statements and negatives do not.
        public static bool IsRequest(string clause)
        {
            if (Has(clause, "ないで", "なくていい", "しなくて")) return Has(clause, "動かないで", "うごかないで");
            if (Regex.IsMatch(clause, "(て|で)(ください|下さい|くれ|くれる|くれない|ほしい|欲しい|みて|みせて|見せて|ちょうだい|ごらん|よ|ね|な|ね〜|〜)*[〜ー!！?？。…♪]*$")) return true;
            if (Has(clause, "して", "しよう", "しよっか", "しようか", "やって", "やろう", "おいで", "ちょうだい", "しろ", "てほしい", "て欲しい", "てくれ", "てみて", "見せて", "みせて", "できる", "できます", "お願い", "おねがい", "頼む", "たのむ")) return true;
            if (Regex.IsMatch(clause, "(ろう|よう|こう|そう|ぼう|もう|ごう|とう|ねう)[よかね!！?？〜ー]*$")) return true;
            // A bare action word: 「ジャンプ！」「バンザイ」「ストップ」.
            return clause.Length <= 8 && Has(clause, "ジャンプ", "じゃんぷ", "バンザイ", "ばんざい", "万歳", "ストップ", "すとっぷ", "ダンス", "お辞儀", "おじぎ", "拍手", "スピン", "ターン", "キック", "シュート", "パス", "回転", "一回転");
        }
        static float Amount(string clause, params string[] units)
        {
            foreach (Match m in Number.Matches(clause))
            {
                if (units.Length > 0 && !units.Contains(m.Groups[2].Value)) continue;
                string n = m.Groups[1].Value;
                if (float.TryParse(n, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value)) return value;
                return Kanji(n);
            }
            return 0;
        }
        static float Kanji(string n)
        {
            if (n == "半") return .5f;
            const string digits = "一二三四五六七八九"; float total = 0, current = 0;
            foreach (char c in n)
            {
                int d = digits.IndexOf(c);
                if (d >= 0) current = d + 1;
                else if (c == '十') { total += (current == 0 ? 1 : current) * 10; current = 0; }
                else if (c == '百') { total += (current == 0 ? 1 : current) * 100; current = 0; }
            }
            return total + current;
        }
        static readonly string[] Seats = { "ソファ", "ひじ掛け椅子", "椅子", "ベッド" };
        static readonly string[] Me = { "こっち", "私", "わたし", "僕", "ぼく", "俺", "おれ", "自分", "あたし", "うち" };
        static MotionDirection Direction(string clause, out string toward, MotionRequest context)
        {
            toward = Mentioned(clause, context, includeToys: false);
            if (toward != null) return MotionDirection.to_object;
            toward = "";
            if (Has(clause, Me)) return MotionDirection.to_user;
            if (Has(clause, "向こう", "むこう", "あっち", "遠く", "とおく")) return MotionDirection.away_from_user;
            if (Has(clause, "右", "みぎ")) return MotionDirection.right;
            if (Has(clause, "左", "ひだり")) return MotionDirection.left;
            if (Has(clause, "後ろ", "うしろ", "後へ", "バック")) return MotionDirection.back;
            if (Has(clause, "前", "まえ")) return MotionDirection.forward;
            return MotionDirection.none;
        }
        // The longest room object or toy name in the clause (ids like 「椅子2」 also match by their label 「椅子」).
        static string Mentioned(string clause, MotionRequest context, bool includeToys = true)
        {
            var names = (context?.places ?? Array.Empty<string>()).Concat(includeToys ? context?.toys ?? Array.Empty<string>() : Array.Empty<string>());
            string best = null; int length = 0;
            foreach (var id in names)
            {
                string label = id.TrimEnd("0123456789".ToCharArray());
                string hit = clause.Contains(id) ? id : label.Length > 0 && clause.Contains(label) ? label : null;
                if (hit != null && hit.Length > length) { best = id; length = hit.Length; }
            }
            return best;
        }
        static string ToyIn(string clause, MotionRequest context, ref string lastToy)
        {
            var named = (context?.toys ?? Array.Empty<string>()).FirstOrDefault(clause.Contains);
            if (named != null) return lastToy = named;
            var kind = CompanionPlayground.KindOf(clause);
            if (kind.HasValue) return lastToy = CompanionPlayground.Label(kind.Value);
            if (Has(clause, "それ", "これ", "あれ", "そいつ")) return lastToy ?? "";
            return lastToy ?? "";
        }
        static MotionStep Step(MotionAction action, string target = "", MotionDirection direction = MotionDirection.none, float amount = 0, string toward = "") => new MotionStep { action = action, target = target ?? "", direction = direction, amount = amount, toward = toward ?? "" };

        static List<MotionStep> Clause(string c, MotionRequest context, ref string lastToy, out string said)
        {
            var steps = new List<MotionStep>(); said = c;
            int heldAt = c.IndexOf("まま", StringComparison.Ordinal);
            if (heldAt >= 0)
            {
                var held = BodyPose(c.Substring(0, heldAt)); string rest = c.Substring(heldAt + 2);
                if (held != null && held.Length == 4 && MotionPlans.ArmsOnly(held[1]) && rest.Length > 0)
                {
                    var following = Clause(rest, context, ref lastToy, out string description);
                    if (following.Count > 0 && following.All(s => MotionPlans.SupportsHeldArms(s.action)))
                    { foreach (var step in following) step.keys = new[] { Copy(held[1]) }; said = c.Substring(0, heldAt + 2) + description; return following; }
                    // Body-only combinations are handled below. Other simultaneous actions need the AI planner.
                    if (BodyPose(rest) == null) return steps;
                }
            }
            float count = Amount(c, "回", "かい");
            if (Has(c, "止ま", "とまって", "とまれ", "ストップ", "すとっぷ", "やめ", "止めて", "じっとして", "動かないで", "うごかないで") && c.Length <= 12) { steps.Add(Step(MotionAction.stop)); said = "止まる"; return steps; }
            if (Has(c, "待って", "まって", "待機", "そのままでいて")) { steps.Add(Step(MotionAction.wait, amount: Amount(c, "秒", "びょう"))); said = "待つ"; return steps; }
            if (Has(c, "見える", "みえる", "何が見", "なにが見", "なにがみ", "何見て", "何を見", "これ見て", "あれ見て", "よく見て", "見てみて", "周り見", "まわり見", "観察", "景色", "けしき", "外を見", "そとを見", "様子を見"))
            { steps.Add(Step(MotionAction.look_closely, Mentioned(c, context))); said = "よく見る"; return steps; }
            if (Has(c, "片づけ", "片付け", "かたづけ", "しまって", "お片付け")) { steps.Add(Step(MotionAction.put_away)); said = "おもちゃを片づける"; return steps; }

            // Toys.
            var kind = CompanionPlayground.KindOf(c); string toy = ToyIn(c, context, ref lastToy);
            bool spinnerWord = kind == CompanionPlayground.ToyKind.Spinner;
            if (kind.HasValue && Has(c, "出し", "だし", "出て", "用意", "作って", "つくって") && !Has(c, "投げ", "なげ", "蹴", "転が", "ころが", "積", "つん", "並べ", "崩"))
            { steps.Add(Step(MotionAction.spawn_toy, CompanionPlayground.Label(kind.Value))); said = CompanionPlayground.Label(kind.Value) + "を出す"; return steps; }
            if (kind.HasValue && Has(c, "出し", "だし", "用意")) steps.Add(Step(MotionAction.spawn_toy, CompanionPlayground.Label(kind.Value)));
            {
                var dir = Direction(c, out string toward, context);
                if (Has(c, "投げ", "なげ", "放っ", "ほうっ", "放り", "ほうり", "パス", "トス"))
                { steps.Add(Step(MotionAction.throw_ball, toy == "" ? "ボール" : toy, dir, 0, toward)); said = "投げる"; return steps; }
                if (Has(c, "蹴", "けとば", "キック", "シュート") || kind.HasValue && Has(c, "けっ", "ける", "けて"))
                { steps.Add(Step(MotionAction.kick, toy == "" ? "ボール" : toy, dir, 0, toward)); said = "蹴る"; return steps; }
                if (Has(c, "転が", "ころが") && !Has(c, "前転", "寝転", "ねころ", "ごろごろ", "ゴロゴロ", "転がって", "ころがって") || Has(c, "転がして", "ころがして"))
                { steps.Add(Step(MotionAction.roll, toy == "" ? "ボール" : toy, dir, 0, toward)); said = "転がす"; return steps; }
                if (Has(c, "弾ま", "はずま", "バウンド", "ドリブル", "まりつき", "つい") && kind == CompanionPlayground.ToyKind.Ball)
                { steps.Add(Step(MotionAction.bounce, toy, MotionDirection.none, count > 0 ? count : 3)); said = "弾ませる"; return steps; }
            }
            if (Has(c, "持ってき", "もってき", "取ってき", "とってき", "拾ってき", "ひろってき", "ちょうだい", "渡して", "わたして", "届けて", "とどけて"))
            { steps.Add(Step(MotionAction.fetch, toy == "" ? "ボール" : toy)); said = "持ってくる"; return steps; }
            if (Has(c, "積ん", "積み上", "積も", "積む", "つんで", "つみあ", "重ね", "かさね", "タワー", "塔"))
            { steps.Add(Step(MotionAction.stack, toy == "" ? "積み木" : toy, MotionDirection.none, Amount(c, "段", "だん", "個", "こ"))); said = "積み木を積む"; return steps; }
            if (Has(c, "崩", "くず", "倒し", "たおし", "壊", "こわ") && !Has(c, "体を倒", "からだを倒", "上半身"))
            { var dir = Direction(c, out string toward, context); steps.Add(Step(MotionAction.knock_down, toy == "" ? "積み木" : toy, dir, 0, toward)); said = "崩す"; return steps; }
            if (Has(c, "並べ", "ならべ", "一列"))
            { steps.Add(Step(MotionAction.line_up, toy == "" ? "積み木" : toy)); said = "並べる"; return steps; }
            if (spinnerWord && Has(c, "回し", "まわし", "回転", "回す", "まわす"))
            { steps.Add(Step(MotionAction.spin_toy, toy)); said = "こまを回す"; return steps; }
            string onto = Mentioned(c, context, includeToys: false);
            if (onto != null && Has(c, "の上", "にのせ", "に乗せ", "にのっけ", "に置いて", "におい", "に置こう", "に置く") && (kind.HasValue || Has(c, "それ", "これ", "あれ") || lastToy != null))
            { steps.Add(Step(MotionAction.put_on, toy == "" ? "ボール" : toy, MotionDirection.to_object, 0, onto)); said = onto + "の上に置く"; return steps; }
            if (Has(c, "握手", "あくしゅ", "ハイタッチ", "タッチして", "触って", "さわって", "触れて", "ふれて", "ノック", "なでて", "撫で", "つついて"))
            {
                string touched = kind.HasValue ? toy : Mentioned(c, context) ?? (Has(c, Me) || Has(c, "握手", "あくしゅ", "ハイタッチ") ? "user" : "");
                if (touched != "") { steps.Add(Step(MotionAction.touch, touched)); said = "触る"; return steps; }
            }
            if (Has(c, "持って", "もって", "拾", "ひろ", "つかん", "掴", "取って", "とって", "抱え", "かかえ", "持ち上げ", "もちあげ", "つまん") && (kind.HasValue || Has(c, "それ", "これ", "あれ")))
            { steps.Add(Step(MotionAction.pick_up, toy)); said = "持つ"; return steps; }
            if (Has(c, "置いて", "おいて", "下ろして", "おろして", "離して", "はなして", "置こう"))
            { steps.Add(Step(MotionAction.put_down, toy)); said = "置く"; return steps; }

            // Moving around.
            if (Has(c, "ついてき", "ついて来", "ついてこ", "付いて", "ついて"))
            { steps.Add(Step(MotionAction.follow_user, "", MotionDirection.none, Amount(c, "秒", "びょう") is float s && s > 0 ? s : 20)); said = "ついて行く"; return steps; }
            string place = Mentioned(c, context);
            if (place != null && Has(c, "行って", "いって", "行こう", "いこう", "行け", "まで", "に向か", "のところ", "のそば", "の前", "へ行", "に行", "歩いて", "あるいて", "近づ", "ちかづ") && !Has(c, "座", "すわ", "指", "見て", "みて"))
            { steps.Add(Step(MotionAction.walk_to, place)); said = place + "へ行く"; return steps; }
            if (Has(c, "来て", "こっち", "おいで", "近づ", "ちかづ", "そばに", "寄って", "よって") && !Has(c, "向いて", "むいて", "見て"))
            { steps.Add(Step(MotionAction.approach_user)); said = "そばに来る"; return steps; }
            if (Has(c, "離れ", "はなれ", "下がって", "さがって", "あっち行", "あっちへ行"))
            { steps.Add(Step(MotionAction.move, "", MotionDirection.away_from_user, Amount(c, "メートル", "m", "ｍ") is float m && m > 0 ? m : 1)); said = "離れる"; return steps; }
            if (Has(c, "進ん", "すすん", "進め", "歩いて", "あるいて", "歩こう", "走って", "はしって", "移動", "動いて", "うごいて", "前に出", "まえにで", "前へ", "下がって", "バック") && !Has(c, "手", "腕", "足", "脚"))
            {
                var dir = Direction(c, out string toward, context); if (dir == MotionDirection.none) dir = MotionDirection.forward;
                float meters = Amount(c, "メートル", "m", "ｍ"); if (meters <= 0) meters = Amount(c, "歩", "ほ") * .4f; if (meters <= 0) meters = 1;
                steps.Add(Step(MotionAction.move, "", dir, meters, toward)); said = "歩く"; return steps;
            }
            if (Has(c, "回って", "まわって", "回転", "ターン") && (Has(c, "右", "みぎ", "左", "ひだり") || Amount(c, "度", "ど") > 0))
            { var dir = Has(c, "左", "ひだり") ? MotionDirection.left : MotionDirection.right; steps.Add(Step(MotionAction.turn, "", dir, Amount(c, "度", "ど") is float angle && angle > 0 ? angle : count > 0 ? count * 360 : 90)); said = "向きを変える"; return steps; }
            if (Has(c, "一回転", "回って", "まわって", "くるっと", "くるくる", "くるりと", "回転して", "スピン", "ターン", "回ろう"))
            { var spin = Step(MotionAction.motion, "", MotionDirection.none, count > 0 ? count : 1); spin.motion = MotionPreset.spin; steps.Add(spin); said = "くるっと回る"; return steps; }
            if (Has(c, "向いて", "むいて", "向け", "向こう向", "振り向", "ふりむ", "見て", "みて") && !Has(c, "手", "腕", "足", "脚", "頭", "首"))
            {
                var dir = Direction(c, out string toward, context);
                if (Has(c, "見て", "みて") && !Has(c, "向")) { float seconds=Amount(c,"秒","びょう"); steps.Add(Step(MotionAction.look_at, place ?? (dir == MotionDirection.to_object ? toward : "user"), MotionDirection.none, seconds > 0 ? seconds : 3)); said = "見る"; return steps; }
                if (Has(c, "振り向", "ふりむ")) dir = MotionDirection.to_user;
                if (dir != MotionDirection.none) { steps.Add(Step(MotionAction.turn, "", dir, Amount(c, "度", "ど"), toward)); said = "向きを変える"; return steps; }
            }
            if (Has(c, "座って", "すわって", "座ろう", "すわろう", "座る", "腰掛", "こしか", "腰かけ", "お座り", "おすわり"))
            {
                // Without a named seat the comfiest one in the room is used; 「床に」 sits on the floor.
                string seat = Has(c, "床", "ゆか", "地面", "その場") ? "" : place ?? Seats.Select(label => (context?.places ?? Array.Empty<string>()).FirstOrDefault(id => id.TrimEnd("0123456789".ToCharArray()) == label)).FirstOrDefault(id => id != null) ?? "";
                steps.Add(Step(MotionAction.sit, seat, amount: Amount(c, "秒", "びょう"))); said = "座る"; return steps;
            }
            if (Has(c, "しゃが", "かが", "屈", "しゃがみ")) { steps.Add(Step(MotionAction.crouch, place ?? "", MotionDirection.none, Amount(c, "秒", "びょう"))); said = "しゃがむ"; return steps; }
            if (Has(c, "立って", "たって", "立ち上が", "たちあが", "起きて", "おきて", "立とう") && !Has(c, "片足", "かたあし")) { steps.Add(Step(MotionAction.stand)); said = "立つ"; return steps; }
            if (Has(c, "指さ", "指差", "ゆびさ", "指して", "さして")) { steps.Add(Step(MotionAction.point, place ?? "user")); said = "指さす"; return steps; }

            // Ready-made whole-body motions and small gestures.
            var preset = Preset(c);
            if (preset.HasValue) { var m = Step(MotionAction.motion, "", MotionDirection.none, count > 0 ? count : 1); m.motion = preset.Value; steps.Add(m); said = c; return steps; }
            var gesture = Has(c, "左手", "右手", "左腕", "右腕") ? (Gesture?)null : GestureIn(c);
            if (gesture.HasValue) { var g = Step(MotionAction.gesture, "", MotionDirection.none, count > 0 ? count : 1); g.gesture = gesture.Value; steps.Add(g); said = c; return steps; }

            // Free body-part poses: 「右手を上げて」「左足を上げたまま」「頭を右に傾けて」「両手を広げて」…
            var keys = BodyPose(c);
            if (keys != null) { var custom = Step(MotionAction.motion, "", MotionDirection.none, count > 0 ? count : 1); custom.motion = MotionPreset.custom; custom.keys = keys; steps.Add(custom); said = c; return steps; }
            return steps;
        }
        static MotionPreset? Preset(string c)
        {
            if (Has(c, "両手を上げ", "両手をあげ", "両腕を上げ", "りょうてをあげ")) return null;
            if (Has(c, "ジャンプ", "じゃんぷ", "跳ん", "とんで", "飛んで", "跳ね", "はねて", "飛び跳", "とびは")) return MotionPreset.jump;
            if (Has(c, "お辞儀", "おじぎ", "礼して", "ぺこり", "ペコリ")) return MotionPreset.bow;
            if (Has(c, "バンザイ", "ばんざい", "万歳")) return MotionPreset.banzai;
            if (Has(c, "拍手", "手をたた", "手を叩", "てをたた", "ぱちぱち", "パチパチ")) return MotionPreset.clap;
            if (Has(c, "踊", "おど", "ダンス", "だんす")) return MotionPreset.dance;
            if (Has(c, "伸び", "のびを", "のびし", "ストレッチ")) return MotionPreset.stretch;
            if (Has(c, "首を振", "首をふ", "くびをふ", "いやいや", "イヤイヤ", "首を横")) return MotionPreset.shake_head;
            if (Has(c, "うなず", "頷", "首を縦")) return MotionPreset.nod;
            if (Has(c, "両手を振", "両手をふ", "りょうてをふ")) return MotionPreset.wave_both;
            if (Has(c, "床に座", "床にすわ", "体育座", "あぐら", "地面に座")) return MotionPreset.sit_floor;
            if (Has(c, "考え", "かんがえ", "悩", "なやん")) return MotionPreset.think;
            if (Has(c, "応援", "おうえん", "喜ん", "よろこん", "やったー", "ガッツ", "はしゃ")) return MotionPreset.cheer;
            if (Has(c, "パンチ", "殴", "なぐ", "ボクシング")) return MotionPreset.punch;
            if (Has(c, "前転", "でんぐり", "ごろごろ", "ゴロゴロ", "転がって", "ころがって")) return MotionPreset.forward_roll;
            if (Has(c, "押して", "押す", "おして", "押そう")) return MotionPreset.push;
            if (Has(c, "腕を組", "腕組", "うでぐみ", "うでをく")) return MotionPreset.fold_arms;
            if (Has(c, "電話", "でんわ")) return MotionPreset.phone;
            if (Has(c, "ゾンビ")) return MotionPreset.zombie;
            if (Has(c, "泳", "およ", "クロール")) return MotionPreset.swim;
            if (Has(c, "修理", "直して", "なおして", "なおそう", "直そう")) return MotionPreset.fix;
            if (Has(c, "食べ", "たべ", "飲ん", "のんで", "飲み", "飲も", "もぐもぐ")) return MotionPreset.eat;
            if (Has(c, "収穫", "しゅうかく", "野菜を採", "引っこ抜", "ひっこぬ")) return MotionPreset.harvest;
            if (Has(c, "種", "植え", "うえて")) return MotionPreset.plant;
            if (Has(c, "水やり", "水をあげ", "水を撒", "水をま")) return MotionPreset.water;
            if (Has(c, "薪割", "まきわり", "木を切", "斧")) return MotionPreset.chop;
            if (Has(c, "死んだふり", "死んだ振り", "倒れて", "たおれて", "気絶", "きぜつ", "やられたふり")) return MotionPreset.die;
            if (Has(c, "驚", "おどろ", "びっくり")) return MotionPreset.surprised;
            if (Has(c, "吹っ飛", "ふっとば", "ふっとん", "やられて")) return MotionPreset.knockback;
            if (Has(c, "身振り", "みぶり", "ジェスチャー", "手振り")) return MotionPreset.talk;
            return null;
        }
        static Gesture? GestureIn(string c)
        {
            if (Has(c, "手を振", "手をふ", "てをふ", "バイバイ", "ばいばい", "さようなら", "またね")) return Gesture.wave;
            if (Has(c, "笑って", "わらって", "笑お")) return Gesture.laugh;
            if (Has(c, "肩をすく", "かたをすく")) return Gesture.shrug;
            if (Has(c, "首をかし", "かしげ", "首を傾", "小首")) return Gesture.head_tilt;
            if (Has(c, "目をそら", "そっぽ")) return Gesture.look_away;
            return null;
        }
        // Builds a hold pose from body-part phrases; 「〜たまま」 combines several into one.
        public static MotionKey[] BodyPose(string c)
        {
            var pose = new MotionKey(); bool any = false, swing = false, circle = false;
            var swung = new HashSet<string>();
            foreach (var part in Regex.Split(c, "まま|ながら"))
            {
                if (part.Length == 0) continue;
                var before = Copy(pose); bool partSwing = false, partCircle = false;
                any |= Part(part, pose, ref partSwing, ref partCircle);
                if (partSwing) foreach (var field in MotionKey.Fields)
                    if (field.Name != "time" && (float)field.GetValue(before) != (float)field.GetValue(pose)) swung.Add(field.Name);
                swing |= partSwing; circle |= partCircle;
            }
            if (!any) return null;
            float hold = 2.5f; var rest = new MotionKey();
            if (swing)
            {
                var a = Copy(pose); var b = Copy(pose);
                foreach (var field in MotionKey.Fields) if (swung.Contains(field.Name))
                {
                    float value = (float)field.GetValue(b);
                    // A wave keeps the upper arm raised and bends the elbow, rather than flinging the whole arm down.
                    field.SetValue(b, field.Name.EndsWith("_arm_out") ? value * .82f : field.Name.EndsWith("_elbow") ? Math.Min(120, value + 40) : value * -.35f);
                }
                return new[] { new MotionKey(), At(a, .35f), At(b, .7f), At(Copy(a), 1.05f), At(Copy(b), 1.4f), At(Copy(a), 1.75f), At(rest, 2.15f) };
            }
            if (circle)
            {
                // Arm circles: forward, up, out and back down.
                var up = Copy(pose); var side = Copy(pose); side.left_arm_out = pose.left_arm_up != 0 ? 150 : 0; side.right_arm_out = pose.right_arm_up != 0 ? 150 : 0; side.left_arm_up = side.right_arm_up = 0;
                var forward = Copy(pose); forward.left_arm_up = pose.left_arm_up != 0 ? 90 : 0; forward.right_arm_up = pose.right_arm_up != 0 ? 90 : 0;
                return new[] { new MotionKey(), At(forward, .3f), At(up, .6f), At(side, .9f), At(new MotionKey(), 1.2f), At(Copy(forward), 1.5f), At(Copy(up), 1.8f), At(Copy(side), 2.1f), At(rest, 2.5f) };
            }
            return new[] { new MotionKey(), At(Copy(pose), .5f), At(Copy(pose), .5f + hold), At(rest, 1 + hold) };
        }
        static MotionKey At(MotionKey k, float time) { k.time = time; return k; }
        static MotionKey Copy(MotionKey k) { var c = new MotionKey(); foreach (var f in MotionKey.Fields) f.SetValue(c, f.GetValue(k)); return c; }
        // One phrase such as 「右手を上げ」: finds the body part, the side and the verb and writes it into the pose.
        static bool Part(string p, MotionKey k, ref bool swing, ref bool circle)
        {
            float degrees = Amount(p, "度", "ど");
            bool right = Has(p, "右", "みぎ"), left = Has(p, "左", "ひだり"), both = Has(p, "両", "りょう");
            bool arm = Has(p, "手", "腕", "うで", "てを", "ひじ", "肘"), leg = Has(p, "足", "脚", "あし", "ひざ", "膝", "もも"), head = Has(p, "頭", "首", "あたま", "くび", "顔", "かお"), body = Has(p, "体", "からだ", "腰", "こし", "上半身", "背中", "せなか", "胸");
            bool up = Has(p, "上げ", "あげ", "挙げ", "上に", "うえに", "高く"), down = Has(p, "下げ", "さげ", "下ろ", "おろ", "下に"), wide = Has(p, "横", "よこ", "広げ", "ひろげ", "開い", "ひらい");
            bool forward = Has(p, "前に", "まえに", "前へ", "伸ば", "のば", "突き出", "つきだ", "まっすぐ"), bend = Has(p, "曲げ", "まげ", "折", "おり", "かがめ");
            bool shake = Has(p, "振", "ふって", "ふる", "ぶらぶら", "パタパタ"), turn = Has(p, "回し", "まわし", "ぐるぐる", "回す");
            bool tilt = Has(p, "傾け", "かたむけ", "かしげ", "倒し", "たおし"), twist = Has(p, "ひね", "ねじ", "捻"), back = Has(p, "反ら", "そら", "後ろに", "うしろに");
            bool look = Has(p, "向", "むけ", "むい", "見上げ", "みあげ", "うつむ", "見下ろ", "みおろ");
            if (Has(p, "片足で立", "片足立", "かたあしで立", "かたあしだち")) { if (left) { k.right_leg = 55; k.right_knee = 70; } else { k.left_leg = 55; k.left_knee = 70; } return true; }
            if (!arm && !leg && !head && !body) return false;
            bool r = right || both || !left, l = left || both;
            if (arm)
            {
                if (shake) { if (r) { k.right_arm_out = 140; k.right_elbow = 40; } if (l) { k.left_arm_out = 140; k.left_elbow = 40; } swing = true; return true; }
                if (turn) { if (r) k.right_arm_up = 180; if (l) k.left_arm_up = 180; circle = true; return true; }
                float lift = up ? (wide ? 0 : degrees > 0 ? Math.Min(180, degrees) : 170) : forward ? 90 : down ? 0 : bend ? 60 : wide ? 0 : -1;
                float outward = wide ? (degrees > 0 ? Math.Min(180, degrees) : up ? 150 : 90) : 0, elbow = bend ? (degrees > 0 ? Math.Min(150, degrees) : 120) : 0;
                if (lift < 0 && outward == 0 && elbow == 0) return false;
                if (r) { k.right_arm_up = Math.Max(0, lift); k.right_arm_out = outward; k.right_elbow = elbow; }
                if (l) { k.left_arm_up = Math.Max(0, lift); k.left_arm_out = outward; k.left_elbow = elbow; }
                return true;
            }
            if (leg)
            {
                if (shake) { if (r) k.right_leg = 50; if (l && !r) k.left_leg = 50; swing = true; return true; }
                float lift = up ? (degrees > 0 ? Math.Min(100, degrees) : 70) : forward ? 45 : bend ? 30 : back ? -35 : -1; float knee = bend ? (degrees > 0 ? Math.Min(140, degrees) : 90) : up ? 25 : 0;
                if (lift < -30 && !back) return false;
                // Both legs cannot leave the floor: 「両足を曲げて」 is a squat.
                if (both) { k.hips_drop = Math.Max(k.hips_drop, bend || up ? .6f : 0); k.left_knee = k.right_knee = knee; return true; }
                if (r) { k.right_leg = lift; k.right_knee = knee; } else { k.left_leg = lift; k.left_knee = knee; }
                return true;
            }
            if (head)
            {
                if (shake) { k.head_yaw = 30; swing = true; return true; }
                if (turn) { k.head_roll = 20; swing = true; return true; }
                if (tilt) { k.head_roll = (left ? -1 : 1) * (degrees > 0 ? Math.Min(45, degrees) : 25); return true; }
                if (Has(p, "下", "した", "うつむ", "見下ろ", "みおろ")) { k.head_pitch = 35; return true; }
                if (Has(p, "上", "うえ", "見上げ", "みあげ")) { k.head_pitch = -30; return true; }
                if (right || left) { k.head_yaw = (right ? 1 : -1) * (degrees > 0 ? Math.Min(75, degrees) : 45); return true; }
                return look;
            }
            if (body)
            {
                if (shake) { k.chest_roll = 15; swing = true; return true; }
                if (twist || turn) { k.chest_yaw = (left ? -1 : 1) * (degrees > 0 ? Math.Min(70, degrees) : 40); return true; }
                if (tilt && (right || left)) { k.chest_roll = (left ? -1 : 1) * (degrees > 0 ? Math.Min(45, degrees) : 22); return true; }
                if (back) { k.chest_pitch = -22; k.head_pitch = -15; return true; }
                if (bend || tilt || forward || Has(p, "前屈", "ぜんくつ")) { k.chest_pitch = 50; k.head_pitch = 15; return true; }
                if (Has(p, "低く", "ひくく", "沈め", "しずめ")) { k.hips_drop = .7f; k.left_knee = k.right_knee = 60; return true; }
            }
            return false;
        }
    }
}
