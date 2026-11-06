using System;
using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Qandeel.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Qandeel
{
    /// <summary>
    /// LIVE OPERATION: the real sequence from warning to release, in seven phases.
    ///  1 Normal day: plant at full power, curtain on standby (no air).
    ///  2 Warning: offshore monitoring reports a swarm 15 km NNW (the kit's forecast case, data/forecast_meta.json).
    ///  3 Forecast: the drift planner (core Planner.SimulateCloud, real 2025 Copernicus currents + ERA5 wind) runs live
    ///    and its cloud of possible tracks is animated; the numbers shown are the kit's 400-run forecast.
    ///  4 Curtain on: 3 h before the earliest arrival, blowers start, sections come on one by one.
    ///  5 Arrival: the modelled swarm comes in from offshore and is held by the curtain (core CurtainHold physics).
    ///  6 Collect: the boat closes the boom, tows the closed bag 3 km and releases the jellyfish alive.
    ///  7 All clear: summary, then "what if there was no curtain?" (same swarm, curtain off, illustrative plant rule).
    /// Presenter keys: Right = next phase, Left = repeat phase, Esc = leave.
    /// </summary>
    public sealed class Operation : MonoBehaviour
    {
        public CurtainView view;
        public static Operation I { get; private set; }
        public static bool Running => I != null && I.run != null;
        public int Phase { get; private set; } = -1;
        public bool PhaseDone { get; private set; }

        static readonly string[] StepsForecast = { "NORMAL DAY", "WARNING", "FORECAST", "CURTAIN ON", "ARRIVAL", "COLLECT", "ALL CLEAR" };
        static readonly string[] StepsEarly = { "NORMAL DAY", "WARNING", "FORECAST", "AI CAMERA ALARM", "EMERGENCY START", "COLLECT", "ALL CLEAR" };
        string[] Steps => early ? StepsEarly : StepsForecast;
        public bool Early => early;
        bool early;
        const double EarlyH = 9.0;   // demo case: the swarm reaches the mouth 9 h after the warning, before the planned switch-on

        // AI camera feed (picture in picture): a frame from the detector's held-out test set with its real boxes
        VisualElement overlay;
        Vector2[] dotPos; bool[] dotReach;
        VisualElement feed, feedImg; Label feedHead, feedNote;

        // forecast case (kit numbers, read from forecast_meta.json; defaults are the same values)
        double distKm = 15, bearing = 330, pArrive = 0.44, firstH = 15.9, bulkH = 42.1, switchH = 12.9, shareReach = 0.13;
        string side = "west";

        VisualElement root, bar, card, chips, labelSwarm, labelIntake, labelZone, labelCam;
        Label title, text, clock, sub, lSwarm, lIntake, lZone;
        VisualElement btnRow, fill;
        Label[] stepLabels;
        Coroutine run;
        bool skip, again;
        StandAudio au => StandAudio.I;

        // forecast cloud
        Planner.Cloud cloud; Task<Planner.Cloud> cloudTask;
        float cloudHour = -1f; bool cloudOn, swarmMarkerOn;
        Matrix4x4[] cloudReach, cloudMiss; int nReach, nMiss;
        LineRenderer ringSwarm, ringZone;
        Vector3 swarmW, mouthW;
        int minUnits; double heldAtEnd; int releasedAtEnd;

        void OnEnable()
        {
            I = this;
            var doc = GetComponent<UIDocument>();
            root = doc.rootVisualElement.Q("root");
            ReadForecast(GetComponent<ControlRoom>()?.forecastMeta);
            Build();
        }

        void ReadForecast(TextAsset meta)
        {
            if (meta == null) return;
            string t = meta.text;
            double N(string k, double d) { var m = Regex.Match(t, "\"" + k + "\":\\s*(-?[0-9.]+)"); return m.Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : d; }
            distKm = N("distance_km", distKm); bearing = N("bearing_deg", bearing); pArrive = N("p_arrive", pArrive);
            firstH = N("window_start_h", firstH); bulkH = N("bulk_arrival_h", bulkH); switchH = N("switch_on_h", switchH);
            shareReach = N("share_reaching", shareReach);
            var s = Regex.Match(t, "\"approach_side\":\\s*\"([a-z]+)\""); if (s.Success) side = s.Groups[1].Value;
        }

        // ---------- UI
        Label L(string cls, VisualElement p, string tx = "") { var l = new Label(tx); foreach (var c in cls.Split(' ')) l.AddToClassList(c); l.pickingMode = PickingMode.Ignore; p.Add(l); return l; }
        VisualElement E(string cls, VisualElement p, bool pick = false) { var e = new VisualElement(); foreach (var c in cls.Split(' ')) e.AddToClassList(c); if (!pick) e.pickingMode = PickingMode.Ignore; p.Add(e); return e; }
        Button B(string tx, string cls, VisualElement p, Action a) { var b = new Button(a) { text = tx }; foreach (var c in cls.Split(' ')) b.AddToClassList(c); p.Add(b); return b; }

        void Build()
        {
            var actions = root.Q("actions");
            if (actions != null) { var b = B("LIVE OPERATION", "btn act act--op", actions, () => StartOperation()); b.SendToBack(); }

            overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            overlay.style.position = Position.Absolute; overlay.style.left = 0; overlay.style.top = 0; overlay.style.right = 0; overlay.style.bottom = 0;
            overlay.generateVisualContent += DrawOverlay;
            root.Insert(0, overlay);
            bar = E("op-bar", root);
            stepLabels = new Label[Steps.Length];
            for (int i = 0; i < Steps.Length; i++) stepLabels[i] = L("op-step", bar, "");
            clock = L("op-clock", bar);

            card = E("op-card", root, true);
            title = L("op-card__title", card); text = L("op-card__text", card);
            chips = E("op-chips", card);
            var barBg = E("op-progress", card); fill = E("op-progress__fill", barBg);
            btnRow = E("op-btns", card, true);

            labelSwarm = E("op-tag op-tag--swarm", root); lSwarm = L("op-tag__t", labelSwarm);
            labelIntake = E("op-tag", root); lIntake = L("op-tag__t", labelIntake, "INTAKE");
            labelZone = E("op-tag op-tag--zone", root); lZone = L("op-tag__t", labelZone, "2 km ALERT ZONE");
            labelCam = E("op-tag op-tag--cam", root); L("op-tag__t", labelCam, "AI CAMERA (drawn 3x)");
            feed = E("op-feed", root);
            feedHead = L("op-feed__head", feed, "AI CAMERA  ·  BREAKWATER");
            feedImg = E("op-feed__img", feed);
            feedNote = L("op-feed__note", feed);
            feed.style.display = DisplayStyle.None;
            Show(false);
        }

        void Show(bool on)
        {
            foreach (var e in new[] { bar, card }) e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) foreach (var e in new[] { labelSwarm, labelIntake, labelZone, labelCam }) e.style.display = DisplayStyle.None;
            if (on) root.AddToClassList("root--op"); else root.RemoveFromClassList("root--op");
        }

        void Card(string t, string body, string[] chipTexts = null)
        {
            title.text = t; text.text = body;
            chips.Clear();
            if (chipTexts != null) foreach (var c in chipTexts) L("op-chip", chips, c);
        }

        void Buttons(params (string label, string cls, Action a)[] bs)
        {
            btnRow.Clear();
            foreach (var b in bs) B(b.label, "op-btn " + b.cls, btnRow, b.a);
        }

        void StepBar(int p)
        {
            for (int i = 0; i < stepLabels.Length; i++)
            {
                stepLabels[i].EnableInClassList("op-step--on", i == p);
                stepLabels[i].EnableInClassList("op-step--done", i < p);
            }
        }

        void Clock(double tH, string extra = "")
        {
            // alert at 06:00 on day 1
            double mins = 6 * 60 + tH * 60; int day = 1 + (int)(mins / 1440); mins %= 1440; if (mins < 0) { mins += 1440; day--; }
            string tt = tH < 0 ? $"T{tH:+0.0;-0.0} h" : $"T+{tH:0.0} h";
            clock.text = $"DAY {day}  {(int)(mins / 60):00}:{(int)(mins % 60):00}   ·   {tt} from warning{extra}";
        }

        // ---------- control
        /// <summary>Asks which case to show, then starts it.</summary>
        public void StartOperation()
        {
            Challenge.I?.ToFree();
            GetComponent<ControlRoom>()?.StopTourIfRunning();
            if (run != null) StopCoroutine(run);
            run = null;
            Show(true); bar.style.display = DisplayStyle.None;
            Card("LIVE OPERATION: CHOOSE A CASE",
                "Follow a real operation from the first warning to the release at sea.",
                new[] { "AS FORECAST: the curtain is on 3 h before the swarm", "ARRIVES EARLY: the AI camera spots it before the curtain is on" });
            fill.style.width = 0;
            Buttons(("AS FORECAST", "op-btn--go", () => Begin(false)), ("ARRIVES EARLY", "op-btn--bad", () => Begin(true)), ("Leave", "op-btn--quiet", Stop));
            au?.Say("ch_op_choose");
            run = StartCoroutine(Idle());
        }
        IEnumerator Idle() { while (true) yield return null; }

        public void Begin(bool earlyCase)
        {
            early = earlyCase;
            for (int i = 0; i < stepLabels.Length; i++) stepLabels[i].text = $"{i + 1}  {Steps[i]}";
            Challenge.I?.ToFree();
            GetComponent<ControlRoom>()?.StopTourIfRunning();
            if (run != null) StopCoroutine(run);
            run = StartCoroutine(Run());
        }

        public void Stop()
        {
            if (run != null) StopCoroutine(run);
            run = null; Phase = -1; cloudOn = false; swarmMarkerOn = false;
            if (ringSwarm) ringSwarm.enabled = false; if (ringZone) ringZone.enabled = false;
            view.HideSwarm = false; view.paused = false; view.FollowBoat = false; Feed(-1, null);
            if (AiCameraMast.I != null) AiCameraMast.I.Alarm = false;
            Camera.main.farClipPlane = 40000f;
            au?.StopVoice();
            Show(false);
            view.Load(0); view.speed = 120f; view.FlyToView(1);
        }

        void Update()
        {
            if (run == null) return;
            if (Phase < 0) return;
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.PageDown)) skip = true;
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.PageUp)) again = true;
            if (Input.GetKeyDown(KeyCode.Escape)) { Stop(); return; }
            Tags();
            overlay?.MarkDirtyRepaint();
        }

        /// <summary>Waits for the voice line and a minimum time; Right skips, Left repeats the phase.</summary>
        IEnumerator Hold(float minS, Func<bool> until = null, float maxS = 120f, Action tick = null)
        {
            float t0 = Time.unscaledTime; skip = false;
            while (true)
            {
                float t = Time.unscaledTime - t0;
                fill.style.width = Length.Percent(Mathf.Clamp01(t / Mathf.Max(minS, 0.1f)) * 100f);
                tick?.Invoke();
                if (skip || again) break;
                bool voiceDone = au == null || !au.Speaking;
                if (t >= minS && voiceDone && (until == null || until())) break;
                if (t >= maxS) break;
                yield return null;
            }
            skip = false;
        }

        IEnumerator Run()
        {
            Show(true);
            Buttons(("Next", "", () => skip = true), ("Leave", "op-btn--quiet", Stop));
            Feed(-1, null);
            Camera.main.farClipPlane = 60000f;
            mouthW = view.World(0, 0);
            var (sx, sy) = Planner.BearingToXY(distKm, bearing);
            swarmW = view.World(sx, sy);
            // start the forecast cloud now in the background (it is ready long before phase 3)
            var rec = view.Record;
            if (rec != null && cloud == null && cloudTask == null)
                cloudTask = Task.Run(() => Planner.SimulateCloud(sx, sy, Math.Ceiling(firstH) + 4, new Rng(11), rec, runs: 80, nParticles: 30, keepRuns: 80));

            for (Phase = 0; Phase < 7; Phase++)
            {
                again = false; PhaseDone = false;
                StepBar(Phase);
                Debug.Log($"[Qandeel] operation phase {Phase + 1}/7 {Steps[Phase]} at {Time.realtimeSinceStartup:F1} s");
                IEnumerator body = Phase switch { 0 => Normal(), 1 => Warning(), 2 => Forecast(), 3 => early ? AiCamEarly() : CurtainOn(), 4 => early ? EmergencyStart() : Arrival(), 5 => Collect(), _ => AllClear() };
                yield return body;
                PhaseDone = true;
                if (again) Phase--;
            }
        }

        // ---------- phases
        IEnumerator Normal()
        {
            cloudOn = false; swarmMarkerOn = false; Rings(false);
            view.Load(0); view.HideSwarm = true; view.paused = true; view.speed = 120f;
            for (int i = 0; i < Site.Sections; i++) view.SetSectionQuiet(i, false);
            if (view.BlowersTripped > 0) view.ResetBlowers();
            if (view.WaveHs > 1) view.SetWaves(0.5);
            view.Alert("", 0, 0f);
            view.FlyToView(0);
            Clock(-2);
            Card("A NORMAL DAY AT THE PLANT",
                "Seawater flows in through this channel to cool the plant's 4 units. The bubble curtain is installed across the mouth but on standby: no air, no cost.",
                new[] { "PLANT  5,600 MW  ·  4/4 units", "CURTAIN  standby", "AI CAMERA  watching the mouth", "BOATS  moored", "SEA  calm, Hs ~0.5 m" });
            au?.Say("ch_op_normal");
            yield return Hold(9f);
        }

        IEnumerator Warning()
        {
            Clock(0);
            RegionalShot(0.35f);
            swarmMarkerOn = true; Rings(true);
            au?.Bad();
            view.Alert($"SWARM WARNING  ·  {distKm:0} km NNW of the intake  ·  offshore monitoring", 1, 10f);
            Card("WARNING: SWARM SPOTTED OFFSHORE",
                $"Offshore monitoring reports a Blue Blubber swarm {distKm:0} km north-north-west of the intake. Qandeel starts here, the moment the warning comes in.",
                new[] { "SOURCE  offshore monitoring", $"POSITION  {distKm:0} km, bearing {bearing:0}°", "NEXT  run the drift forecast" });
            au?.Say("ch_op_detect");
            yield return Hold(9f);
        }

        IEnumerator Forecast()
        {
            Clock(0.2);
            while (cloudTask != null && !cloudTask.IsCompleted) yield return null;
            if (cloudTask != null && cloudTask.Status == TaskStatus.RanToCompletion) cloud = cloudTask.Result;
            RegionalShot(0.5f);
            Card("DRIFT FORECAST",
                $"The planner runs 400 forecasts with the real 2025 currents and wind. Orange: tracks that reach the 2 km zone at the intake. Grey: tracks that miss.",
                new[] { $"CHANCE IT ARRIVES  {pArrive * 100:0}%", $"EARLIEST  ~{firstH:0} h", $"MOST OF IT  ~{bulkH:0} h", $"SIDE  {side}", $"SWITCH CURTAIN ON  +{switchH:0.0} h" });
            au?.Say("ch_op_forecast");
            cloudOn = cloud != null;
            float t0 = Time.unscaledTime, dur = 14f;
            float endH = (float)Math.Min(firstH + 2, cloud != null ? cloud.Hourly.Length - 1 : 0);
            skip = false;
            while (!skip && !again)
            {
                float u = Mathf.Clamp01((Time.unscaledTime - t0) / dur);
                cloudHour = Mathf.Lerp(0f, endH, Mathf.SmoothStep(0, 1, u));
                Clock(cloudHour, "   ·   FORECAST VIEW");
                fill.style.width = Length.Percent(u * 100f);
                if (u >= 1f && (au == null || !au.Speaking)) break;
                yield return null;
            }
            skip = false;
        }

        IEnumerator CurtainOn()
        {
            cloudOn = false; swarmMarkerOn = false; Rings(false);
            Clock(switchH);
            view.FlyToView(1);
            Card("CURTAIN ON: 3 h BEFORE THE EARLIEST ARRIVAL",
                "The blowers start (2 duty + 1 standby, 174 m³/min) and air reaches all ten sections. A wall of bubbles rises across the 967 m mouth.",
                new[] { $"TIME  +{switchH:0.0} h", "AIR  3 L/s per m", "SECTIONS  0/10" });
            au?.Say("ch_op_curtain");
            yield return new WaitForSecondsRealtime(2.5f);
            au?.Whoosh();
            for (int i = 0; i < Site.Sections; i++)
            {
                view.SetSectionQuiet(i, true); au?.Beep();
                ((Label)chips[2]).text = $"SECTIONS  {i + 1}/10";
                yield return new WaitForSecondsRealtime(0.55f);
                if (skip || again) break;
            }
            for (int i = 0; i < Site.Sections; i++) view.SetSectionQuiet(i, true);
            ((Label)chips[2]).text = "SECTIONS  10/10 ON";
            view.Alert("CURTAIN ON  ·  all 10 sections  ·  ready for the swarm", 0, 6f);
            yield return Hold(5f);
        }

        IEnumerator Arrival()
        {
            swarmMarkerOn = false; Rings(false);
            Clock(firstH);
            view.Load(0, 900);
            double cx = side == "west" ? -110 : side == "east" ? 110 : 0;
            view.PlaceSwarm(cx, 1000, 320, 380);
            view.HideSwarm = false; view.paused = false; view.speed = 300f;
            SeawardShot();
            minUnits = Operations.PlantUnits;
            Card("THE SWARM ARRIVES, AND IS HELD",
                "The rising bubbles drive a surface current back out to sea. Jellyfish swimming in the top layer are held outside the mouth. Each bell is one modelled jellyfish.",
                new[] { "HELD AT SEA  100%", "PLANT  5,600 MW", "BOATS  standing by" });
            au?.Say("ch_op_aicam_confirm");
            CameraShot(2f);
            Feed(0, "Blue Blubber at the mouth: on time, curtain already on");
            yield return new WaitForSecondsRealtime(6f);
            Feed(-1, null);
            SeawardShot();
            au?.Say("ch_op_arrive");
            float t0 = Time.unscaledTime;
            yield return Hold(16f, () => view.Sim.HeldCentroid(out _, out _, out int c) && c > 400, 120f, () =>
            {
                ((Label)chips[0]).text = $"HELD AT SEA  {view.Sim.HeldShare * 100:0}%";
                ((Label)chips[1]).text = $"PLANT  {view.PlantMW:N0} MW";
                ((Label)chips[2]).text = view.BoatStatus.Length > 0 ? "BOAT  " + view.BoatStatus : "BOATS  standing by";
                minUnits = Mathf.Min(minUnits, view.UnitsOnline);
            });
            heldAtEnd = view.Sim.HeldShare;
        }

        // ---------- early case: the AI camera sees the swarm before the planned switch-on
        int slippedEarly;

        IEnumerator AiCamEarly()
        {
            cloudOn = false; swarmMarkerOn = false; Rings(false);
            Clock(EarlyH);
            view.Load(0, 900);
            for (int i = 0; i < Site.Sections; i++) view.SetSectionQuiet(i, false);   // still on standby
            double cx = side == "west" ? -110 : side == "east" ? 110 : 0;
            view.PlaceSwarm(cx, 260, 320, 220);
            view.HideSwarm = false; view.paused = false; view.speed = 60f;
            if (AiCameraMast.I != null) AiCameraMast.I.Alarm = true;
            CameraShot();
            minUnits = Operations.PlantUnits;
            au?.Bad();
            view.Alert($"AI CAMERA ALARM  ·  jellyfish at the mouth  ·  {switchH - EarlyH:0} h EARLIER than forecast  ·  curtain still on standby", 2, 12f);
            Card("AI CAMERA ALARM: THE SWARM IS EARLY",
                $"{EarlyH:0} h after the warning, the AI camera on the breakwater spots Blue Blubber at the mouth. The curtain was not due on until +{switchH:0.0} h.",
                new[] { $"TIME  +{EarlyH:0.0} h", $"PLANNED SWITCH-ON  +{switchH:0.0} h", "CURTAIN  standby", "PASSED  0" });
            au?.Say("ch_op_aicam_early");
            Feed(0, "Blue Blubber at the mouth: ALARM");
            // the leading edge is already at the mouth: let the first few drift in, then the alarm starts the curtain
            yield return Hold(7f, () => view.Sim.Entered >= 12, 30f, () =>
            {
                ((Label)chips[3]).text = $"PASSED  {view.Sim.Entered:N0}";
                if (view.Sim.Entered >= 12) view.speed = 10f;
            });
        }

        IEnumerator EmergencyStart()
        {
            Clock(EarlyH + 0.05);
            Feed(-1, null);
            if (AiCameraMast.I != null) AiCameraMast.I.Alarm = false;
            SeawardShot();
            Card("EMERGENCY START: CURTAIN ON NOW",
                "The camera alarm starts the curtain at once: all ten sections open together and the blowers run up to full air. A few jellyfish slip in before the bubble wall is complete; the intake screens catch them.",
                new[] { "SECTIONS  0/10", "PASSED  0", "HELD AT SEA  100%", "PLANT  5,600 MW" });
            au?.Say("ch_op_emergency"); au?.Whoosh();
            // all sections at once, air ramping up over the first moments (the bubble wall builds section by section on screen)
            for (int i = 0; i < Site.Sections; i++) { view.SetSectionQuiet(i, true); ((Label)chips[0]).text = $"SECTIONS  {i + 1}/10"; yield return new WaitForSecondsRealtime(0.12f); }
            ((Label)chips[0]).text = "SECTIONS  10/10 ON";
            view.Alert("CURTAIN ON  ·  emergency start from the AI camera alarm", 1, 6f);
            slippedEarly = view.Sim.Entered;
            view.speed = 300f;
            yield return Hold(16f, () => view.Sim.HeldCentroid(out _, out _, out int c) && c > 300, 120f, () =>
            {
                ((Label)chips[1]).text = $"PASSED  {view.Sim.Entered:N0}";
                ((Label)chips[2]).text = $"HELD AT SEA  {view.Sim.HeldShare * 100:0}%";
                ((Label)chips[3]).text = $"PLANT  {view.PlantMW:N0} MW";
                minUnits = Mathf.Min(minUnits, view.UnitsOnline);
            });
            slippedEarly = view.Sim.Entered;
            heldAtEnd = view.Sim.HeldShare;
        }

        /// <summary>Shows the AI camera feed (frame k of the detector test set, with its predicted boxes), or hides it (k &lt; 0).</summary>
        void Feed(int k, string note)
        {
            if (feed == null) return;
            var room = GetComponent<ControlRoom>();
            if (k < 0 || room == null || room.aiImages == null || room.aiImages.Length <= k || room.aiImages[k] == null) { feed.style.display = DisplayStyle.None; return; }
            feed.style.display = DisplayStyle.Flex;
            feedImg.style.backgroundImage = new StyleBackground(room.aiImages[k]);
            feedImg.Clear();
            if (room.detections != null)
            {
                // the k-th entry's predicted boxes (normalised xyxy) and confidences
                var entries = Regex.Split(room.detections.text, "\\{\\s*\"file\"");
                if (entries.Length > k + 1)
                    foreach (Match m in Regex.Matches(entries[k + 1], "\"conf\":\\s*([0-9.]+),\\s*\"xyxy\":\\s*\\[([0-9.]+),\\s*([0-9.]+),\\s*([0-9.]+),\\s*([0-9.]+)\\]"))
                    {
                        float F(int g) => float.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
                        if (F(1) < 0.5f) continue;
                        var box = new VisualElement(); box.AddToClassList("op-feed__box"); box.pickingMode = PickingMode.Ignore;
                        box.style.left = Length.Percent(F(2) * 100); box.style.top = Length.Percent(F(3) * 100);
                        box.style.width = Length.Percent((F(4) - F(2)) * 100); box.style.height = Length.Percent((F(5) - F(3)) * 100);
                        var tag = new Label($"jellyfish {F(1):0.00}"); tag.AddToClassList("op-feed__tag"); box.Add(tag);
                        feedImg.Add(box);
                    }
            }
            feedNote.text = note + "\nSample frame from the detector's test set (not this site). Detector: 80% precision, 91% recall on jellyfish.";
        }

        IEnumerator Collect()
        {
            Clock(firstH + 1.5);
            view.speed = 600f;
            Card("COLLECT AND RELEASE ALIVE",
                "A workboat closes a 200 m floating boom around the held swarm, gathers it into a closed bag and tows it 3 km out, where the jellyfish are released alive.",
                new[] { "IN BAG  0", "RELEASED ALIVE  0", "PLANT  5,600 MW" });
            au?.Say("ch_op_collect");
            float t0 = Time.unscaledTime;
            while (!view.BoatTowing && Time.unscaledTime - t0 < 30f && !skip && !again) { ChipsLive(); yield return null; }
            view.FollowBoat = true;
            yield return Hold(8f, () => view.Released > 0, 45f, ChipsLive);
            view.FollowBoat = false;
            releasedAtEnd = view.Released;
            heldAtEnd = view.Sim.HeldShare;
        }

        void ChipsLive()
        {
            if (chips.childCount < 3) return;
            ((Label)chips[0]).text = $"IN BAG  {view.InBag:N0}";
            ((Label)chips[1]).text = $"RELEASED ALIVE  {view.Released:N0}";
            ((Label)chips[2]).text = $"PLANT  {view.PlantMW:N0} MW";
            minUnits = Mathf.Min(minUnits, view.UnitsOnline);
        }

        IEnumerator AllClear()
        {
            Clock(firstH + 4);
            view.paused = true; view.FlyToView(0);
            yield return null;
            if (early)
            {
                Card("ALL CLEAR: EARLY SWARM HANDLED",
                    $"The swarm came {switchH - EarlyH:0} h before the curtain was due on. The AI camera caught it at the mouth and started the curtain at once. A few jellyfish slipped in during start-up; the rest were held at sea and the first bag was released alive.",
                    new[] { $"SLIPPED IN DURING START-UP  {slippedEarly:N0} of {view.Sim.N:N0}", $"HELD AT SEA  {heldAtEnd * 100:0}%", $"RELEASED ALIVE  {Mathf.Max(releasedAtEnd, view.Released):N0} (first trip)", $"PLANT  {(minUnits >= 4 ? "5,600 MW all the time" : $"lowest {minUnits}/4 units")}" });
                au?.Say("ch_op_early_clear"); au?.Ok();
                Buttons(("WHAT IF THERE WAS NO CURTAIN?", "op-btn--bad", StartWithoutForTest),
                        ("Other case", "", StartOperation),
                        ("PLAY THE CHALLENGE", "op-btn--go", () => { Stop(); Challenge.I?.ShowIntro(); }),
                        ("Leave", "op-btn--quiet", Stop));
                fill.style.width = Length.Percent(100);
                while (true) yield return null;
            }
            Card("ALL CLEAR",
                "The swarm was held at sea and the first bag was released alive. The boats keep shuttling while the curtain stays on; when the swarm has gone, the curtain returns to standby. The plant never lost power.",
                new[] { $"HELD AT SEA  {heldAtEnd * 100:0}%", $"RELEASED ALIVE  {Mathf.Max(releasedAtEnd, view.Released):N0} (first trip)", $"PLANT  {(minUnits >= 4 ? "5,600 MW all the time" : $"lowest {minUnits}/4 units")}", $"WARNING TO CURTAIN ON  {switchH:0.0} h" });
            au?.Say("ch_op_clear"); au?.Ok();
            Buttons(("WHAT IF THERE WAS NO CURTAIN?", "op-btn--bad", StartWithoutForTest),
                    ("Other case", "", StartOperation),
                    ("PLAY THE CHALLENGE", "op-btn--go", () => { Stop(); Challenge.I?.ShowIntro(); }),
                    ("Leave", "op-btn--quiet", Stop));
            fill.style.width = Length.Percent(100);
            while (true) yield return null; // wait for a choice
        }

        void StartWithoutForTest() { PhaseDone = false; if (run != null) StopCoroutine(run); run = StartCoroutine(Without()); }

        IEnumerator Without()
        {
            StepBar(4);
            Clock(firstH);
            view.Load(0, 900);
            for (int i = 0; i < Site.Sections; i++) view.SetSectionQuiet(i, false);
            double cx = side == "west" ? -110 : side == "east" ? 110 : 0;
            view.PlaceSwarm(cx, 1000, 320, 380);
            view.HideSwarm = false; view.paused = false; view.speed = 480f;
            SeawardShot();
            Card("THE SAME SWARM, WITHOUT THE CURTAIN",
                "Nothing holds the swarm at the mouth. Jellyfish are drawn into the channel and onto the cooling-water screens (plant response illustrative, not modelled).",
                new[] { "PASSED  0", "PLANT  5,600 MW" });
            Buttons(("Leave", "op-btn--quiet", Stop));
            au?.Say("ch_op_without");
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 40f && view.UnitsOnline > 0)
            {
                ((Label)chips[0]).text = $"PASSED  {view.Sim.Entered:N0}";
                ((Label)chips[1]).text = $"PLANT  {view.PlantMW:N0} MW  ·  {view.UnitsOnline}/4 units";
                fill.style.width = Length.Percent((Time.unscaledTime - t0) / 40f * 100f);
                yield return null;
            }
            yield return new WaitForSecondsRealtime(3f);
            ((Label)chips[1]).text = $"PLANT  {view.PlantMW:N0} MW  ·  {view.UnitsOnline}/4 units";
            Card("WITHOUT QANDEEL", $"{view.Sim.Entered:N0} jellyfish reached the screens and {4 - view.UnitsOnline} of 4 units went offline. With the curtain: 5,600 MW all the time.",
                new[] { $"PASSED  {view.Sim.Entered:N0}", $"PLANT  {view.PlantMW:N0} MW" });
            Buttons(("Replay the operation", "", StartOperation), ("PLAY THE CHALLENGE", "op-btn--go", () => { Stop(); Challenge.I?.ShowIntro(); }), ("Leave", "op-btn--quiet", Stop));
            PhaseDone = true;
            while (true) yield return null;
        }

        /// <summary>From above the channel, behind the curtain, looking out to sea: the curtain line in front and the swarm coming in.</summary>
        /// <summary>Over the shoulder of the AI camera mast, looking out through its field of view.</summary>
        void CameraShot(float seconds = 2.5f)
        {
            var m = AiCameraMast.I; if (m == null) { SeawardShot(); return; }
            Vector3 side = Vector3.Cross(Vector3.up, m.seaward).normalized;
            // camera back and to the side of the mast, a little above its head: the mast stands on the left, the wedge and the sea ahead
            Vector3 pos = m.transform.position - m.seaward * 55f + side * 95f + Vector3.up * 32f;
            Vector3 look = m.transform.position + m.seaward * 170f - side * 40f + Vector3.up * 12f;
            view.FlyTo(pos, Quaternion.LookRotation(look - pos), seconds);
        }

        void SeawardShot()
        {
            Vector3 pos = view.World(0, -650) + Vector3.up * 420f, look = view.World(0, 700);
            view.FlyTo(pos, Quaternion.LookRotation(look - pos), 2.5f);
        }

        // ---------- regional view, forecast cloud and labels
        void RegionalShot(float seconds)
        {
            Vector3 c = Vector3.Lerp(mouthW, swarmW, 0.5f);
            Vector3 back = (mouthW - swarmW); back.y = 0; back.Normalize();
            Vector3 look = c - back * 1200f;                          // a little past the midpoint, toward the swarm
            Vector3 pos = look + back * 6000f + Vector3.up * 24000f;  // high, almost top-down: intake at the bottom, swarm near the top
            view.FlyTo(pos, Quaternion.LookRotation(look - pos, -back), Mathf.Max(seconds * 8f, 2.5f));
        }

        void Rings(bool on)
        {
            if (ringSwarm == null)
            {
                var src = view.boom != null ? view.boom.sharedMaterial : null;
                ringSwarm = Ring("Swarm warning ring", src, new Color(1f, 0.55f, 0.2f), 60f);
                ringZone = Ring("2 km alert zone", src, new Color(0.35f, 0.85f, 0.95f), 40f);
            }
            ringSwarm.enabled = on; ringZone.enabled = on;
        }

        LineRenderer Ring(string n, Material src, Color c, float w)
        {
            var go = new GameObject(n); var lr = go.AddComponent<LineRenderer>();
            if (src != null) { var m = new Material(src); m.SetColor("_UnlitColor", c); m.SetColor("_BaseColor", c); m.SetColor("_EmissiveColor", c * 2f); lr.sharedMaterial = m; }
            lr.loop = true; lr.positionCount = 72; lr.widthMultiplier = w; lr.startColor = lr.endColor = c;
            lr.useWorldSpace = true; lr.enabled = false;
            return lr;
        }

        void DrawRing(LineRenderer lr, Vector3 c, float r)
        {
            for (int i = 0; i < lr.positionCount; i++) { float a = i * Mathf.PI * 2f / lr.positionCount; lr.SetPosition(i, c + new Vector3(Mathf.Cos(a) * r, 8f, Mathf.Sin(a) * r)); }
        }

        void LateUpdate()
        {
            if (run == null) return;
            float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 4f);
            if (ringSwarm != null && ringSwarm.enabled) { DrawRing(ringSwarm, swarmW, 1600f * pulse); DrawRing(ringZone, mouthW, 2000f); }
            // the swarm marker and the forecast cloud are drawn by the map overlay (DrawOverlay), not as 3D meshes
        }

        /// <summary>Forecast map overlay: the reported swarm (pulsing orange patch) and the planner's cloud of possible
        /// positions (orange = tracks that reach the 2 km zone, grey = tracks that miss), projected from the 3D world.</summary>
        void DrawOverlay(MeshGenerationContext ctx)
        {
            var cam = Camera.main; if (cam == null || overlay.panel == null || run == null) return;
            var pz = ctx.painter2D;
            Vector2 P(Vector3 w) => RuntimePanelUtils.CameraTransformWorldToPanel(overlay.panel, w, cam);
            bool Front(Vector3 w) => Vector3.Dot(cam.transform.forward, w - cam.transform.position) > 0;
            if (swarmMarkerOn && !cloudOn && Front(swarmW))
            {
                Vector2 c = P(swarmW); float rpx = Mathf.Max(10f, (P(swarmW + Vector3.right * 1300f) - c).magnitude);
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f);
                pz.fillColor = new Color(1f, 0.55f, 0.15f, 0.25f + 0.2f * pulse);
                pz.BeginPath(); pz.Arc(c, rpx * (1f + 0.15f * pulse), 0f, 360f); pz.Fill();
                var r = new System.Random(5);
                pz.fillColor = new Color(1f, 0.75f, 0.35f, 0.95f);
                pz.BeginPath();
                for (int i = 0; i < 90; i++)
                {
                    float a = (float)r.NextDouble() * 6.283f, d = Mathf.Sqrt((float)r.NextDouble()) * rpx * 0.8f;
                    var q = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
                    pz.MoveTo(q + new Vector2(3f, 0)); pz.Arc(q, 3f, 0f, 360f); pz.ClosePath();
                }
                pz.Fill();
            }
            if (cloudOn && cloud != null && cloud.Hourly.Length > 0)
            {
                int h0 = Mathf.Clamp(Mathf.FloorToInt(cloudHour), 0, cloud.Hourly.Length - 1), h1 = Mathf.Min(h0 + 1, cloud.Hourly.Length - 1);
                float f = cloudHour - h0;
                var a = cloud.Hourly[h0]; var b = cloud.Hourly[h1]; int n = a.GetLength(0);
                // count the dots first, then write them as diamonds (4 vertices, 2 triangles) into one mesh
                int cnt = 0;
                var pts = dotPos ??= new Vector2[n]; var isReach = dotReach ??= new bool[n];
                if (pts.Length < n) { pts = dotPos = new Vector2[n]; isReach = dotReach = new bool[n]; }
                for (int i = 0; i < n; i++)
                {
                    int rr = i / cloud.Particles, j = i % cloud.Particles;
                    double ah = cloud.ArriveH[rr, j];
                    bool reach = !double.IsNaN(ah);
                    if (reach && ah < cloudHour) continue;
                    var w = view.World(a[i, 0] + (b[i, 0] - a[i, 0]) * f, a[i, 1] + (b[i, 1] - a[i, 1]) * f);
                    if (!Front(w)) continue;
                    pts[cnt] = P(w); isReach[cnt] = reach; cnt++;
                }
                if (cnt > 0)
                {
                    var md = ctx.Allocate(cnt * 4, cnt * 6);
                    Color32 grey = new Color(0.75f, 0.82f, 0.88f, 0.6f), orange = new Color(1f, 0.55f, 0.15f, 0.95f);
                    for (int k = 0; k < cnt; k++)
                    {
                        float rad = isReach[k] ? 4f : 3f; var c = pts[k]; var col = isReach[k] ? orange : grey;
                        md.SetNextVertex(new Vertex { position = new Vector3(c.x, c.y - rad, Vertex.nearZ), tint = col });
                        md.SetNextVertex(new Vertex { position = new Vector3(c.x + rad, c.y, Vertex.nearZ), tint = col });
                        md.SetNextVertex(new Vertex { position = new Vector3(c.x, c.y + rad, Vertex.nearZ), tint = col });
                        md.SetNextVertex(new Vertex { position = new Vector3(c.x - rad, c.y, Vertex.nearZ), tint = col });
                        ushort v = (ushort)(k * 4);
                        md.SetNextIndex(v); md.SetNextIndex((ushort)(v + 1)); md.SetNextIndex((ushort)(v + 2));
                        md.SetNextIndex(v); md.SetNextIndex((ushort)(v + 2)); md.SetNextIndex((ushort)(v + 3));
                    }
                }
            }
        }

        void Tags()
        {
            bool regional = swarmMarkerOn || cloudOn;
            Place(labelSwarm, swarmW, regional && !cloudOn);
            Place(labelIntake, mouthW, regional);
            Place(labelZone, mouthW + Vector3.right * 2000f, regional);
            lSwarm.text = $"SWARM  ·  {distKm:0} km NNW";
            var mast = AiCameraMast.I;
            bool camNear = mast != null && !regional && Vector3.Distance(Camera.main.transform.position, mast.Lens) < 1500f;
            Place(labelCam, mast != null ? mast.Lens + Vector3.up * 6f : Vector3.zero, camNear);
        }

        void Place(VisualElement e, Vector3 w, bool on)
        {
            var cam = Camera.main;
            if (!on || cam == null || Vector3.Dot(cam.transform.forward, w - cam.transform.position) <= 0) { e.style.display = DisplayStyle.None; return; }
            var p = RuntimePanelUtils.CameraTransformWorldToPanel(e.panel, w, cam);
            e.style.display = DisplayStyle.Flex;
            e.style.left = p.x; e.style.top = p.y;
        }
    }
}
