using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Qandeel.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Qandeel
{
    /// <summary>
    /// Stand game layer on top of the twin (the physics is unchanged):
    ///  - Attract loop after 60 s idle: a 20 s swarm attack (close-up, curtain holds, curtain fails, alarm, recovery)
    ///    under "CAN YOU DEFEND THE POWER PLANT? · TOUCH TO START".
    ///  - Operator challenge: a 2,500-jellyfish swarm, 60 s, six random incidents (fix fast or make the right call).
    ///    Score = average plant output kept (MW, illustrative plant rule in Operations) + response bonus.
    ///  - Top-10 leaderboard with 3-letter initials, saved next to the user's data (Shift+F9 clears it).
    /// </summary>
    public sealed class Challenge : MonoBehaviour
    {
        public CurtainView view;
        public float idleToAttractS = 60f, gameS = 60f;
        public static Challenge I { get; private set; }

        public enum Phase { Free, Attract, Intro, Countdown, Playing, Result, Board }
        public Phase State { get; private set; } = Phase.Free;
        public bool AutoPlay;                    // test mode: answers every incident correctly after 1.5 s
        public string BoardFile = "leaderboard.json";

        // live game numbers (read by the test)
        public float TimeLeft { get; private set; }
        public int Score { get; private set; }
        public double AvgMW { get; private set; }
        public int Bonus { get; private set; }
        public int Handled { get; private set; }
        public int Incidents { get; private set; }
        public string CurrentIncident => cur?.title;

        VisualElement root, hud, card, cardBtns, attract, board, boardList, nameRow, startBtnBox;
        Label hudTime, hudScore, hudUnits, cardTitle, cardText, attractTitle, attractSub, countdown, boardTitle, boardSub;
        ControlRoom room;
        StandAudio au => StandAudio.I;
        float lastInput; Vector3 lastMouse;
        Coroutine run;

        class Incident { public string key, title, text; public string[] choices; public int correct = -1; public Func<bool> fixedNow; public float t0; public bool done; }
        Incident cur;
        int choice = -1;

        [Serializable] class Entry { public string name; public int score; public int mw; public string when; }
        [Serializable] class Board { public List<Entry> top = new List<Entry>(); }
        Board scores;

        void OnEnable()
        {
            I = this;
            room = GetComponent<ControlRoom>();
            if (room != null) room.idleToAttractS = 1e9f; // the attract loop replaces the idle tour
            foreach (var a in Environment.GetCommandLineArgs())   // automated tests drive the scene themselves: no attract loop
                if (a == "-scenariotest" || a == "-optest" || a == "-standtest" || a == "-benchmark" || a == "-tourtest") idleToAttractS = 1e9f;
            root = GetComponent<UIDocument>().rootVisualElement.Q("root");
            Build();
            LoadBoard();
            lastInput = Time.unscaledTime;
        }

        // ---------- UI
        VisualElement El(string cls, VisualElement parent = null, bool pick = false)
        {
            var e = new VisualElement(); foreach (var c in cls.Split(' ')) e.AddToClassList(c);
            if (!pick) e.pickingMode = PickingMode.Ignore; (parent ?? root).Add(e); return e;
        }
        Label L(string cls, VisualElement parent, string text = "")
        {
            var l = new Label(text); foreach (var c in cls.Split(' ')) l.AddToClassList(c); l.pickingMode = PickingMode.Ignore; parent.Add(l); return l;
        }
        Button B(string text, string cls, VisualElement parent, Action a)
        {
            var b = new Button(a) { text = text }; foreach (var c in cls.Split(' ')) b.AddToClassList(c); parent.Add(b); return b;
        }

        void Build()
        {
            // "Play" button next to the stand actions
            var actions = root.Q("actions");
            if (actions != null) B("PLAY: DEFEND THE PLANT", "btn act act--play", actions, ShowIntro);

            hud = El("ch-hud");
            hudTime = L("ch-hud__time", hud); hudScore = L("ch-hud__score", hud); hudUnits = L("ch-hud__units", hud);
            L("ch-hud__note", hud, "Plant response is illustrative, not modelled");

            card = El("ch-card", null, true);
            cardTitle = L("ch-card__title", card); cardText = L("ch-card__text", card);
            cardBtns = El("ch-card__btns", card, true);

            attract = El("ch-attract", null, true);
            attract.RegisterCallback<PointerDownEvent>(_ => { if (State == Phase.Attract) ShowMenu(); });
            attractTitle = L("ch-attract__title", attract, "CAN YOU DEFEND THE POWER PLANT?");
            attractSub = L("ch-attract__sub", attract, "TOUCH TO START");

            countdown = L("ch-count", root);

            board = El("ch-board", null, true);
            boardTitle = L("ch-board__title", board); boardSub = L("ch-board__sub", board);
            nameRow = El("ch-name", board, true);
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                var b = B("A", "ch-letter", nameRow, () => CycleLetter(k, 1));
                nameBtns[i] = b;
            }
            startBtnBox = El("ch-board__btns", board, true);
            boardList = El("ch-board__list", board);
            SetVisible(hud, false); SetVisible(card, false); SetVisible(attract, false); SetVisible(board, false); SetVisible(countdown, false);
        }
        readonly Button[] nameBtns = new Button[3];
        readonly char[] initials = { 'A', 'A', 'A' };
        int nameCursor;
        void CycleLetter(int k, int d) { initials[k] = (char)('A' + ((initials[k] - 'A' + d + 26) % 26)); nameCursor = k; RefreshName(); au?.Beep(); }
        void RefreshName() { for (int i = 0; i < 3; i++) { nameBtns[i].text = initials[i].ToString(); nameBtns[i].EnableInClassList("ch-letter--cur", i == nameCursor); } }

        static void SetVisible(VisualElement e, bool on) => e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        void Mode(string m)
        {
            foreach (var c in new[] { "root--challenge", "root--attract" }) root.RemoveFromClassList(c);
            if (m != null) root.AddToClassList(m);
        }

        // ---------- idle -> attract
        void Update()
        {
            bool input = Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.touchCount > 0 || (Input.mousePosition - lastMouse).sqrMagnitude > 4f;
            lastMouse = Input.mousePosition;
            if (input) lastInput = Time.unscaledTime;

            if (Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.F9)) { scores = new Board(); SaveBoard(); view.Alert("LEADERBOARD CLEARED", 0, 3f); }
            if (Input.GetKeyDown(KeyCode.Escape) && State != Phase.Free) { ToFree(); return; }
            if (Input.GetKeyDown(KeyCode.P) && State == Phase.Free) ShowIntro();

            switch (State)
            {
                case Phase.Free:
                    if (Time.unscaledTime - lastInput > idleToAttractS && (room == null || room.TourStep < 0) && !Operation.Running) StartAttract();
                    break;
                case Phase.Attract:
                    if (Input.GetMouseButtonDown(0) || Input.touchCount > 0 || Input.anyKeyDown) ShowMenu();
                    attractSub.style.opacity = 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.4f));
                    break;
                case Phase.Intro:
                case Phase.Board:
                    if (Time.unscaledTime - lastInput > 45f) StartAttract();
                    break;
                case Phase.Result:
                    foreach (char ch in Input.inputString)
                        if (char.IsLetter(ch)) { initials[nameCursor] = char.ToUpperInvariant(ch); nameCursor = (nameCursor + 1) % 3; RefreshName(); }
                    if (Input.GetKeyDown(KeyCode.Return)) SaveName();
                    if (Time.unscaledTime - lastInput > 40f) SaveName();
                    break;
            }
        }

        public void ToFree()
        {
            if (run != null) StopCoroutine(run); run = null;
            State = Phase.Free; Mode(null); au?.StopVoice(); if (au != null) au.master = 1f;
            foreach (var e in new[] { hud, card, attract, board, countdown }) SetVisible(e, false);
            CalmPlant();
            lastInput = Time.unscaledTime;
        }

        void CalmPlant()
        {
            if (view.BlowersTripped > 0) view.ResetBlowers();
            if (view.WaveHs > 1) view.SetWaves(0.5);
            if (view.DeepSwarm) view.SetDeepSwarm(false);
            if (view.HighAir) view.SetHighAir(false);
            if (view.CameraOff) view.SetCameraOff(false);
            for (int i = 0; i < view.Sim.SectionOn.Length; i++) if (!view.Sim.SectionOn[i]) { view.SetCurtain(true); break; }
            view.Alert("", 0, 0f);
        }

        // ---------- attract loop
        public void StartAttract()
        {
            if (run != null) StopCoroutine(run);
            room?.StopTourIfRunning();
            State = Phase.Attract; Mode("root--attract");
            foreach (var e in new[] { hud, card, board, countdown }) SetVisible(e, false);
            SetVisible(attract, true);
            if (au != null) au.master = 0.55f;
            run = StartCoroutine(AttractLoop());
        }

        IEnumerator AttractLoop()
        {
            for (int loop = 0; ; loop++)
            {
                CalmPlant();
                view.SwarmIncoming(); view.speed = 240f; view.Alert("", 0, 0f);
                if (loop % 3 == 0) au?.Say("ch_attract");
                // close-up of the swarm under water, then up to the curtain
                if (view.views != null && view.views.Length > 2) view.FlyTo(view.views[2].position, view.views[2].rotation, 0.01f);
                attractTitle.text = "A JELLYFISH SWARM IS COMING";
                yield return new WaitForSecondsRealtime(6f);
                view.FlyToView(1); au?.Whoosh();
                attractTitle.text = "THE BUBBLE CURTAIN HOLDS IT BACK";
                yield return new WaitForSecondsRealtime(5f);
                view.speed = 600f; view.SetCurtain(false);
                attractTitle.text = "WITHOUT IT, THE PLANT GOES DOWN";
                yield return new WaitForSecondsRealtime(5f);
                view.SetCurtain(true); view.speed = 240f;
                attractTitle.text = "CAN YOU DEFEND THE POWER PLANT?";
                yield return new WaitForSecondsRealtime(6f);
            }
        }

        // ---------- menu (after a touch on the attract loop)
        public void ShowMenu()
        {
            if (run != null) StopCoroutine(run); run = null;
            CalmPlant(); view.Load(0); view.speed = 120f; view.FlyToView(1); view.Alert("", 0, 0f);
            State = Phase.Intro; Mode("root--challenge"); if (au != null) au.master = 1f;
            foreach (var e in new[] { hud, attract, board, countdown }) SetVisible(e, false);
            lastInput = Time.unscaledTime;
            ShowCard("PROTECTING THE POWER PLANT FROM JELLYFISH",
                "See the real operation from the first warning to the release at sea, or take the controls yourself.",
                new[] { "SEE HOW IT WORKS", "PLAY THE CHALLENGE", "Explore freely" },
                i => { if (i == 0) { ToFree(); Operation.I?.StartOperation(); } else if (i == 1) ShowIntro(); else ToFree(); }, "ch-btn--go");
        }

        // ---------- intro
        public void ShowIntro()
        {
            if (run != null) StopCoroutine(run); run = null;
            room?.StopTourIfRunning();
            CalmPlant();
            view.Load(0); view.speed = 120f; view.FlyToView(1); view.Alert("", 0, 0f); // fresh start: all 4 units online, no alarm left over
            State = Phase.Intro; Mode("root--challenge"); if (au != null) au.master = 1f;
            foreach (var e in new[] { hud, attract, board, countdown }) SetVisible(e, false);
            lastInput = Time.unscaledTime;
            ShowCard("OPERATOR CHALLENGE",
                "A swarm of 2,500 jellyfish is heading for the power plant's cooling-water intake.\nYou have 60 seconds. Things will go wrong: fix them fast and make the right calls.\nKeep all 4 units online.",
                new[] { "START", "Explore freely" }, i => { if (i == 0) StartGame(); else ToFree(); }, "ch-btn--go");
            au?.Say("ch_intro");
        }

        void ShowCard(string title, string text, string[] buttons, Action<int> onPick, string firstCls = null, int level = 1)
        {
            SetVisible(card, true);
            card.EnableInClassList("ch-card--alarm", level == 2);
            cardTitle.text = title; cardText.text = text;
            cardBtns.Clear();
            for (int i = 0; i < buttons.Length; i++)
            {
                int k = i;
                var b = B(buttons[i], "ch-btn", cardBtns, () => onPick(k));
                if (i == 0 && firstCls != null) b.AddToClassList(firstCls);
            }
        }

        // ---------- the game
        public void StartGame()
        {
            if (run != null) StopCoroutine(run);
            run = StartCoroutine(Game());
        }

        IEnumerator Game()
        {
            SetVisible(card, false);
            State = Phase.Countdown; lastInput = Time.unscaledTime;
            CalmPlant();
            view.SwarmIncoming(); view.Alert("", 0, 0f);
            SetVisible(countdown, true);
            for (int n = 3; n >= 1; n--) { countdown.text = n.ToString(); au?.Beep(); yield return new WaitForSecondsRealtime(0.9f); }
            countdown.text = "GO"; au?.Go(); au?.Say("ch_go");
            yield return new WaitForSecondsRealtime(0.6f);
            SetVisible(countdown, false);

            State = Phase.Playing;
            view.speed = 240f;
            SetVisible(hud, true);
            double mwSeconds = 0; Bonus = 0; Handled = 0; Incidents = 0; Score = 0; AvgMW = Operations.PlantUnits * Operations.UnitMW;
            var pool = new List<Func<Incident>> { Section, Blowers, CurtainTrip, RoughSea, CameraOff, DeepSwarm };
            var rng = new System.Random(Environment.TickCount);
            var order = pool.OrderBy(_ => rng.Next()).ToList();
            // never open with the curtain trip (the swarm has to arrive first)
            if (order[0] == CurtainTrip) { order.RemoveAt(0); order.Insert(2, CurtainTrip); }
            float[] at = { 6f, 15f, 24f, 33f, 42f, 50f };
            int next = 0; float t0 = Time.unscaledTime, lastAlarmVoice = -99f; int lastUnits = Operations.PlantUnits;
            cur = null;
            while (true)
            {
                float t = Time.unscaledTime - t0;
                TimeLeft = Mathf.Max(0f, gameS - t);
                if (TimeLeft <= 0f) break;
                mwSeconds += view.PlantMW * Time.unscaledDeltaTime;
                AvgMW = t > 0.5f ? mwSeconds / t : view.PlantMW;
                Score = (int)Math.Round(AvgMW) + Bonus;

                if (cur == null && next < order.Count && t >= at[next])
                {
                    cur = order[next++](); cur.t0 = Time.unscaledTime; choice = -1; Incidents++;
                    au?.Bad(); au?.Say(cur.key);
                    if (cur.choices != null) ShowCard(cur.title, cur.text, cur.choices, i => choice = i, null, 2);
                    else ShowCard(cur.title, cur.text, new[] { FixLabel(cur) }, _ => DoFix(cur), "ch-btn--fix", 2);
                }
                if (cur != null)
                {
                    float rt = Time.unscaledTime - cur.t0;
                    if (AutoPlay && rt > 1.5f) { if (cur.choices != null) choice = cur.correct; else DoFix(cur); }
                    if (cur.choices != null && choice >= 0)
                    {
                        bool right = choice == cur.correct;
                        Resolve(cur, right, rt);
                        if (!right && cur.key == "ch_camera") { cur = CurtainTripAfterWrongCall(); cur.t0 = Time.unscaledTime; Incidents++; ShowCard(cur.title, cur.text, new[] { FixLabel(cur) }, _ => DoFix(cur), "ch-btn--fix", 2); }
                        else cur = null;
                    }
                    else if (cur != null && cur.choices == null && cur.fixedNow()) { Resolve(cur, true, rt); cur = null; }
                }
                int u = view.UnitsOnline;
                if (u < lastUnits && Time.unscaledTime - lastAlarmVoice > 8f) { au?.Say("ch_alarm", false); lastAlarmVoice = Time.unscaledTime; }
                lastUnits = u;
                Hud();
                yield return null;
            }
            // end
            SetVisible(card, false); cur = null;
            Score = (int)Math.Round(AvgMW) + Bonus;
            Hud();
            au?.Go(); au?.Say("ch_end");
            yield return new WaitForSecondsRealtime(1.2f);
            ShowResult();
        }

        string FixLabel(Incident c) => c.key switch
        {
            "ch_section" => "CLOSE THE GAP", "ch_blower" => "RESTART BLOWERS", "ch_deep" => "BOOST AIR TO 4.5 L/s/m", _ => "RESTORE THE CURTAIN"
        };

        void DoFix(Incident c)
        {
            switch (c.key)
            {
                case "ch_section": for (int i = 0; i < view.Sim.SectionOn.Length; i++) if (!view.Sim.SectionOn[i]) view.ToggleSection(i, false); break;
                case "ch_blower": view.ResetBlowers(); break;
                case "ch_deep": view.SetHighAir(true); break;
                default: view.SetCurtain(true); view.speed = 240f; break;
            }
        }

        void Resolve(Incident c, bool right, float rt)
        {
            c.done = true;
            int pts = right ? Mathf.Max(100, Mathf.RoundToInt(600f - 60f * rt)) : 0;
            Bonus += pts; if (right) Handled++;
            if (right) { au?.Ok(); au?.Say(c.choices != null ? "ch_good" : "ch_fixed"); }
            else { au?.Bad(); au?.Say("ch_wrong"); }
            view.Alert(right ? $"+{pts}  ·  {(c.choices != null ? "GOOD CALL" : "FIXED")} IN {rt:F1} s" : "WRONG CALL", right ? 0 : 2, 3f);
            SetVisible(card, false);
            view.speed = 240f;
        }

        void Hud()
        {
            hudTime.text = $"{Mathf.CeilToInt(TimeLeft)} s";
            hudTime.EnableInClassList("ch-hud__time--low", TimeLeft < 10f);
            hudScore.text = $"SCORE  {Score:N0}";
            int u = view.UnitsOnline;
            hudUnits.text = $"UNITS  {u}/4   ·   {view.PlantMW:N0} MW";
            hudUnits.EnableInClassList("ch-hud__units--bad", u < Operations.PlantUnits);
        }

        // ---------- incidents
        Incident Section()
        {
            int k = UnityEngine.Random.Range(2, 8);
            view.ToggleSection(k, true);
            return new Incident { key = "ch_section", title = $"SECTION {k + 1} FAILED", text = "A valve has closed: there is a gap in the curtain and jellyfish are slipping through to the intake.", fixedNow = () => view.Sim.SectionOn.All(s => s) };
        }
        Incident Blowers()
        {
            view.TripBlower(); view.TripBlower();
            return new Incident { key = "ch_blower", title = "TWO BLOWERS TRIPPED", text = "Only one blower is running. The air is down to half and the curtain is weak.", fixedNow = () => view.BlowersTripped == 0 };
        }
        Incident CurtainTrip()
        {
            view.SetCurtain(false); view.speed = 240f;
            return new Incident { key = "ch_curtain", title = "POWER DIP: CURTAIN DOWN", text = "All 10 sections have stopped. Nothing is holding the swarm.", fixedNow = () => view.CurtainOn && view.Sim.SectionOn.All(s => s) };
        }
        Incident CurtainTripAfterWrongCall()
        {
            return new Incident { key = "ch_curtain", title = "CURTAIN SWITCHED OFF", text = "With the curtain off, the swarm heads straight for the intake. Switch it back on!", fixedNow = () => view.Sim.SectionOn.All(s => s) };
        }
        Incident DeepSwarm()
        {
            if (view.HighAir) view.SetHighAir(false);
            view.SetDeepSwarm(true);
            return new Incident { key = "ch_deep", title = "DEEP SWARM", text = "Sonar shows the jellyfish swimming at 2 m. Normal air only pushes back the top metre.", fixedNow = () => view.HighAir };
        }
        Incident RoughSea()
        {
            view.SetWaves(2.5); view.speed = 240f;
            bool flip = UnityEngine.Random.value < 0.5f;
            var a = "Boats back to port.\nThe curtain keeps running"; var b = "Keep the boats sweeping";
            return new Incident { key = "ch_sea", title = "WAVES ABOVE 2 m", text = "The collection boats are out at the curtain. What do they do?", choices = flip ? new[] { b, a } : new[] { a, b }, correct = flip ? 1 : 0 };
        }
        Incident CameraOff()
        {
            view.SetCameraOff(true);
            bool flip = UnityEngine.Random.value < 0.5f;
            var a = "Keep the curtain on.\nThe forecast still covers it"; var b = "Switch the curtain off\nuntil the camera is fixed";
            return new Incident { key = "ch_camera", title = "AI CAMERA OFFLINE", text = "The swarm-spotting camera has gone dark. Do you keep the curtain running?", choices = flip ? new[] { b, a } : new[] { a, b }, correct = flip ? 1 : 0 };
        }

        // ---------- result and leaderboard
        int pendingRank = -1;

        void ShowResult()
        {
            State = Phase.Result; lastInput = Time.unscaledTime;
            SetVisible(hud, false); SetVisible(board, true);
            int rank = scores.top.Count(e => e.score >= Score);
            bool qualifies = rank < 10;
            pendingRank = qualifies ? rank : -1;
            boardTitle.text = $"SCORE  {Score:N0}";
            boardSub.text = $"Plant output kept: {AvgMW:N0} of 5,600 MW on average   ·   incidents handled: {Handled} of {Incidents}   ·   bonus +{Bonus:N0}\n" +
                            (qualifies ? (rank == 0 ? "NEW TOP SCORE! Enter your initials:" : $"You are #{rank + 1} today. Enter your initials:") : "Not in today's top 10. Try again!");
            if (qualifies && rank == 0) au?.Say("ch_top", false);
            SetVisible(nameRow, qualifies);
            initials[0] = initials[1] = initials[2] = 'A'; nameCursor = 0; RefreshName();
            startBtnBox.Clear();
            if (qualifies) B("SAVE", "ch-btn ch-btn--go", startBtnBox, SaveName);
            else { B("PLAY AGAIN", "ch-btn ch-btn--go", startBtnBox, ShowIntro); B("Done", "ch-btn", startBtnBox, ToFree); State = Phase.Board; }
            FillBoard(-1);
        }

        public void SaveName()
        {
            if (State != Phase.Result) return;
            string n = new string(initials);
            var e = new Entry { name = n, score = Score, mw = (int)Math.Round(AvgMW), when = DateTime.Now.ToString("HH:mm") };
            scores.top.Add(e);
            scores.top = scores.top.OrderByDescending(x => x.score).Take(10).ToList();
            SaveBoard();
            State = Phase.Board; lastInput = Time.unscaledTime;
            SetVisible(nameRow, false);
            boardSub.text = $"Saved. Plant output kept: {AvgMW:N0} MW on average   ·   incidents handled: {Handled} of {Incidents}";
            startBtnBox.Clear();
            B("PLAY AGAIN", "ch-btn ch-btn--go", startBtnBox, ShowIntro); B("Done", "ch-btn", startBtnBox, ToFree);
            FillBoard(scores.top.IndexOf(e));
            au?.Ok();
        }

        void FillBoard(int highlight)
        {
            boardList.Clear();
            L("ch-row ch-row--head", boardList, "TODAY'S TOP OPERATORS");
            if (scores.top.Count == 0) L("ch-row", boardList, "No scores yet. Be the first!");
            for (int i = 0; i < scores.top.Count; i++)
            {
                var e = scores.top[i];
                var l = L("ch-row", boardList, $"{i + 1,2}.   {e.name}    {e.score,6:N0}    {e.mw:N0} MW    {e.when}");
                l.EnableInClassList("ch-row--me", i == highlight);
            }
        }

        string BoardPath => Path.Combine(Application.persistentDataPath, BoardFile);
        void LoadBoard()
        {
            try { scores = File.Exists(BoardPath) ? JsonUtility.FromJson<Board>(File.ReadAllText(BoardPath)) : new Board(); }
            catch { scores = new Board(); }
            if (scores == null || scores.top == null) scores = new Board();
        }
        void SaveBoard() { try { File.WriteAllText(BoardPath, JsonUtility.ToJson(scores, true)); } catch (Exception ex) { Debug.LogWarning("[Qandeel] leaderboard not saved: " + ex.Message); } }
        public void UseBoardFile(string name) { BoardFile = name; LoadBoard(); }
    }
}
