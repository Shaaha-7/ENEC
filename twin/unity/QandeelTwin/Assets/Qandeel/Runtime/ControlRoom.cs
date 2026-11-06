using System;
using System.Collections;
using Qandeel.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Qandeel
{
    /// <summary>
    /// Control-room interface (UI Toolkit) bound live to CurtainView, plus the 7-step guided tour with captions taken
    /// word for word from data/narration_lines.json, and attract mode after 2 minutes idle.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ControlRoom : MonoBehaviour
    {
        public CurtainView view;
        [TextArea] public string[] tourText = new string[7]; // filled by SiteBuilder from narration_lines.json
        public AudioClip[] tourVoice = new AudioClip[7];     // ElevenLabs recordings of the same lines (tools/make_voice_unity.py)
        AudioSource voice;
        public int TourStep { get; private set; } = -1;   // -1 = no tour running
        public float TourStepProgress { get; private set; }
        public float idleToAttractS = 120f;
        [Header("Panel data (filled by SiteBuilder from the kit)")]
        public TextAsset forecastMeta, detections, detectorMetrics, costs;
        public Texture2D[] forecastAtlases, aiImages;
        InfoPanels panels;
        Label gauges, season, eventList;
        Toggle autoAir;
        Slider inAir, inApproach;
        Button demoDeep, demoHigh, failBlower, failSection, failCamera;
        Button[] seaBtns;
        Label deepNote, seaNote, failNote, kPlant, alarmText;
        VisualElement alarm, alarmFrame;
        Button actSwarm, actCurtain;
        float nextUi;
        GraphicsTier tierCtl; Button tierBtn;

        static readonly string[] StepNames = { "ALERT", "FORECAST", "CURTAIN ON", "UNDERWATER", "GATHERING", "BOATS", "RESULT" };

        VisualElement root, caption, captionFill, sectionsBox, scenariosBox;
        Label kTime, kHeld, kPassed, kBag, kReleased, kAir, kApproach, source, boat, legendScale, capStep, capText;
        Button tourBtn, followBtn, pauseBtn, speedBtn;
        Button[] secBtns, viewBtns, scenBtns;
        Coroutine tour;
        float lastInput;
        Vector3 lastMouse;

        void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            Q(out kTime, "kpi-time"); Q(out kHeld, "kpi-held"); Q(out kPassed, "kpi-passed"); Q(out kBag, "kpi-bag");
            Q(out kReleased, "kpi-released"); Q(out kAir, "kpi-air"); Q(out kApproach, "kpi-approach");
            Q(out source, "scenario-source"); Q(out boat, "boat-state"); Q(out legendScale, "legend-scale");
            Q(out capStep, "caption-step"); Q(out capText, "caption-text");
            caption = root.Q("caption"); captionFill = root.Q("caption-progress");
            sectionsBox = root.Q("sections"); scenariosBox = root.Q("scenarios");

            tourBtn = root.Q<Button>("btn-tour"); tourBtn.clicked += ToggleTour;
            followBtn = root.Q<Button>("btn-follow"); followBtn.clicked += () => view.FollowBoat = !view.FollowBoat;
            pauseBtn = root.Q<Button>("btn-pause"); pauseBtn.clicked += () => view.paused = !view.paused;
            root.Q<Button>("btn-restart").clicked += () => view.Load(view.scenarioIndex);
            speedBtn = root.Q<Button>("btn-speed"); speedBtn.clicked += () => view.speed = view.speed >= 600 ? 120 : 600;
            viewBtns = new Button[3];
            for (int i = 0; i < 3; i++) { int k = i; viewBtns[i] = root.Q<Button>($"view-{i}"); viewBtns[i].clicked += () => view.SetView(k); }

            scenBtns = new Button[Scenario.All.Length];
            for (int i = 0; i < scenBtns.Length; i++)
            {
                int k = i;
                var b = new Button(() => view.LoadScenario(k)) { text = Scenario.All[i].Name };
                b.AddToClassList("btn"); b.AddToClassList("btn--scenario");
                scenariosBox.Add(b); scenBtns[i] = b;
            }
            tierCtl = FindFirstObjectByType<GraphicsTier>();
            tierBtn = root.Q<Button>("btn-tier"); tierBtn.clicked += () => tierCtl?.Cycle();
            Q(out gauges, "gauges"); Q(out season, "season"); Q(out eventList, "event-list");
            autoAir = root.Q<Toggle>("auto-air");
            autoAir.RegisterValueChangedCallback(e => view.autoAir = e.newValue);
            inAir = root.Q<Slider>("in-air"); inApproach = root.Q<Slider>("in-approach");
            inAir.RegisterValueChangedCallback(e => view.SetAirflow(e.newValue));
            inApproach.RegisterValueChangedCallback(e => view.SetApproach(e.newValue));

            // Demo controls (operating rules on top of the physics)
            demoDeep = root.Q<Button>("demo-deep"); demoDeep.clicked += () => view.SetDeepSwarm(!view.DeepSwarm);
            demoHigh = root.Q<Button>("demo-high"); demoHigh.clicked += () => view.SetHighAir(!view.HighAir);
            seaBtns = new[] { root.Q<Button>("sea-0"), root.Q<Button>("sea-1"), root.Q<Button>("sea-2") };
            double[] hs = { 0.5, 1.5, 2.5 };
            for (int i = 0; i < 3; i++) { int k = i; seaBtns[i].clicked += () => view.SetWaves(hs[k]); }
            failBlower = root.Q<Button>("fail-blower"); failBlower.clicked += () => { if (view.BlowersTripped > 0) view.ResetBlowers(); else view.TripBlower(); };
            failSection = root.Q<Button>("fail-section"); failSection.clicked += () => view.ToggleSection(4, true);
            failCamera = root.Q<Button>("fail-camera"); failCamera.clicked += () => view.SetCameraOff(!view.CameraOff);
            Q(out kPlant, "kpi-plant"); Q(out alarmText, "alarm-text");
            alarm = root.Q("alarm"); alarmFrame = root.Q("alarm-frame");
            actSwarm = root.Q<Button>("act-swarm"); actSwarm.clicked += () => view.SwarmIncoming();
            actCurtain = root.Q<Button>("act-curtain"); actCurtain.clicked += () => view.SetCurtain(!view.CurtainOn);
            Q(out deepNote, "demo-deep-note"); Q(out seaNote, "sea-note"); Q(out failNote, "fail-note");

            if (forecastMeta != null)
                panels = new InfoPanels(root.Q("root"), root.Q("tabs"), forecastMeta, forecastAtlases, detections, aiImages, detectorMetrics, costs);
            secBtns = new Button[Site.Sections];
            for (int i = 0; i < secBtns.Length; i++)
            {
                int k = i;
                var b = new Button(() => view.ToggleSection(k, true)) { text = (i + 1).ToString() };
                b.AddToClassList("sec");
                sectionsBox.Add(b); secBtns[i] = b;
            }
        }

        void Q(out Label l, string name) => l = root.Q<Label>(name);

        void Update()
        {
            if (view == null || view.Sim == null) return;
            var sim = view.Sim;
            panels?.Update(Time.unscaledDeltaTime);

            // Idle detection for attract mode
            if (Input.anyKeyDown || (Input.mousePosition - lastMouse).sqrMagnitude > 4f) lastInput = Time.unscaledTime;
            lastMouse = Input.mousePosition;
            if (tour == null && Time.unscaledTime - lastInput > idleToAttractS) StartTour();
            if (tour != null && Input.GetKeyDown(KeyCode.Escape)) StopTour();

            // Alarm: the plant alarm (units offline) wins over passing alerts; the red frame pulses while it is on
            int units = view.UnitsOnline;
            bool plantAlarm = units < Operations.PlantUnits;
            bool alertOn = Time.unscaledTime < view.AlertUntil;
            int level = alertOn ? view.AlertLevel : plantAlarm ? 2 : -1;   // a fresh event is shown first; the plant alarm returns after it
            float pulse = level == 2 || plantAlarm ? 0.25f + 0.55f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3.2f)) : 0f;
            alarmFrame.style.opacity = pulse;

            // Readouts at 10 Hz: per-frame text and slider updates force UI layout passes and cost FPS
            if (Time.unscaledTime < nextUi) return;
            nextUi = Time.unscaledTime + 0.1f;

            int h = (int)sim.TimeHours, m = (int)(sim.TimeHours * 60 % 60);
            kTime.text = $"T+{h}:{m:00}";
            kHeld.text = $"{sim.HeldShare:P0}";
            kHeld.EnableInClassList("kpi__v--good", sim.HeldShare >= 0.95);
            kHeld.EnableInClassList("kpi__v--bad", sim.HeldShare < 0.8);
            kPassed.text = sim.Entered.ToString();
            kPassed.EnableInClassList("kpi__v--bad", sim.Entered > 0);
            kBag.text = view.InBag.ToString();
            kReleased.text = view.Released.ToString();
            kAir.text = $"{sim.AirflowLps:F1} L/s/m → {Bulson.SurfaceCurrent(sim.AirflowLps):F2} m/s";
            kApproach.text = $"{sim.ApproachSpeed:F2} m/s";
            kPlant.text = $"{view.PlantMW:N0} MW · {units}/4";
            kPlant.EnableInClassList("kpi__v--good", !plantAlarm);
            kPlant.EnableInClassList("kpi__v--bad", plantAlarm);
            alarm.EnableInClassList("alarm--on", level >= 0);
            for (int i = 0; i < 3; i++) alarm.EnableInClassList($"alarm--{i}", level == i);
            if (alertOn) alarmText.text = view.AlertText;
            else if (plantAlarm)
                alarmText.text = $"INTAKE ALARM  ·  jellyfish on the cooling-water screens  ·  {4 - units} of 4 units offline  ·  {view.PlantMW:N0} MW*";
            bool cOn = view.CurtainOn;
            actCurtain.text = cOn ? "CURTAIN: ON   (switch off)" : "CURTAIN: OFF   (switch on)";
            actCurtain.EnableInClassList("act--off", !cOn);
            actSwarm.EnableInClassList("btn--on", view.SwarmMode);
            source.text = view.CurrentScenario.Source;
            boat.text = view.BoatStatus;
            var (plo, phi) = view.PressureBarG;
            gauges.text = sim.AirflowLps <= 0 ? "Blowers off" :
                $"Air <b>{view.AirM3PerMin:F0} m³/min</b> · {plo:F2}–{phi:F2} bar(g)\nPower <b>~{view.PowerKW:F0} kW</b> · energy <b>{view.EnergyMWh:F2} MWh</b>" +
                (view.CurrentScenario.Replay2025 ? $"\n{view.ReplayTime}" : "");
            var s = view.Season;
            season.text = view.HasRecord
                ? $"Season check 2025 ({s.Hours} h): smart air averages {s.AirflowMean:F2} L/s/m = {s.EnergyVs3Pct:F0}% of fixed 3 L/s/m and {s.EnergyVsMaxPct:F0}% of max; need exceeds 3 L/s/m in {s.ShareAbove3 * 100:F1}% of hours."
                : "";
            eventList.text = string.Join("\n", view.Events);
            // Demo control states and captions
            demoDeep.EnableInClassList("btn--on", view.DeepSwarm); demoHigh.EnableInClassList("btn--on", view.HighAir);
            var (elo, ehi) = Operations.ExpectedHeld(view.HighAir);
            deepNote.text = view.DeepSwarm
                ? $"Held now {sim.HeldShare:P0} · expected {elo:P0}–{ehi:P0} (published table). Outflow layer assumed {(view.HighAir ? 2 : 1)} m, not measured."
                : (view.HighAir ? "High air 4.5 L/s/m (maximum design airflow)." : "");
            int si = view.WaveHs < 1 ? 0 : view.WaveHs <= 2 ? 1 : 2;
            for (int i = 0; i < 3; i++) seaBtns[i].EnableInClassList("btn--on", i == si);
            seaNote.text = (si == 0 ? "Boats run." : si == 1 ? "Boats stand by; the held swarm waits at the curtain." : "Boats in port; the held swarm waits at the curtain.")
                           + " Curtain keeps running. Wave effect on the curtain not modelled.";
            failBlower.text = view.BlowersTripped > 0 ? "Reset air" : "Blower trip";
            failBlower.EnableInClassList("btn--on", view.BlowersTripped > 0);
            failSection.EnableInClassList("btn--on", !sim.SectionOn[4]);
            failCamera.EnableInClassList("btn--on", view.CameraOff);
            var fails = new System.Collections.Generic.List<string>();
            if (view.BlowersTripped > 0) fails.Add($"{3 - view.BlowersTripped} of 3 blowers available · air {sim.AirflowLps:F1} L/s/m" +
                                                   (view.BlowersTripped == 1 ? " (standby took over, no loss)" : ""));
            if (!sim.SectionOn[4]) fails.Add($"Gap at section 5: {sim.Entered} passed to the existing screens (last layer)");
            if (view.CameraOff) fails.Add("Camera off: the forecast still switches the curtain on (+13 h for 15 km NNW)");
            failNote.text = string.Join("\n", fails);
            if (tierCtl != null) tierBtn.text = $"Graphics: {GraphicsTier.Names[tierCtl.Tier]}{(tierCtl.Auto ? " (auto)" : "")}  ·  G";
            autoAir.SetValueWithoutNotify(view.autoAir);
            autoAir.SetEnabled(view.CurrentScenario.Replay2025);
            inAir.SetValueWithoutNotify((float)sim.AirflowLps);
            inApproach.SetValueWithoutNotify((float)sim.ApproachSpeed);
            legendScale.text = $"{sim.N} modelled Blue Blubber · drawn {view.DrawScale:F0}x real size in this view (38 cm bell)";

            for (int i = 0; i < secBtns.Length; i++) secBtns[i].EnableInClassList("sec--off", !sim.SectionOn[i]);
            for (int i = 0; i < scenBtns.Length; i++) scenBtns[i].EnableInClassList("btn--on", i == view.scenarioIndex);
            for (int i = 0; i < viewBtns.Length; i++) viewBtns[i].EnableInClassList("btn--on", !view.FollowBoat && i == view.currentView);
            followBtn.EnableInClassList("btn--on", view.FollowBoat);
            pauseBtn.text = view.paused ? "Resume" : "Pause";
            speedBtn.text = view.speed >= 600 ? "x600" : "x120";
            tourBtn.text = tour != null ? "■  Stop tour" : "▶  Guided tour";
        }

        void ToggleTour() { if (tour != null) StopTour(); else StartTour(); }

        public void StopTourIfRunning() { if (tour != null) StopTour(); }
        public void StartTour() { lastInput = Time.unscaledTime; panels?.Show(""); tour = StartCoroutine(Tour()); }

        /// <summary>Opens a panel by tab name ("" closes); used by the benchmark screenshots.</summary>
        public void ShowPanel(string name) => panels?.Show(name);

        public void StopTour()
        {
            if (tour != null) StopCoroutine(tour);
            if (voice != null) voice.Stop();
            tour = null; lastInput = Time.unscaledTime; TourStep = -1; TourStepProgress = 0;
            caption.RemoveFromClassList("caption--on"); root.Q("root").RemoveFromClassList("root--tour");
            view.FollowBoat = false; view.speed = 120;
        }

        /// <summary>alert → forecast → curtain on → underwater → gathering → boats → result (about 3 minutes).</summary>
        IEnumerator Tour()
        {
            root.Q("root").AddToClassList("root--tour");
            caption.AddToClassList("caption--on");
            view.Load(0); view.speed = 60;
            TourStep = 0;
            for (int s = 0; s < 7; s++)
            {
                switch (s)
                {
                    case 0: yield return Fly(view.views[0], 0.1f); break;
                    case 1: yield return Fly(HighView(), 4f); break;
                    case 2: yield return Fly(view.views[1], 4f); break;
                    case 3: yield return Fly(view.views[2], 3f); break;
                    case 4: view.Load(0); view.FastForward(0.75); view.speed = 60; yield return Fly(view.views[1], 3f); break;
                    case 5: view.speed = 600; while (!view.BoatTowing) yield return null; view.FollowBoat = true; break;
                    case 6: view.FollowBoat = false; view.speed = 120; yield return Fly(view.views[0], 4f); break;
                }
                string text = tourText != null && s < tourText.Length ? tourText[s] : "";
                Debug.Log($"[Qandeel] tour step {s + 1}/7 {StepNames[s]} at {Time.realtimeSinceStartup:F1} s");
                capStep.text = $"{s + 1} / 7   ·   {StepNames[s]}";
                capText.text = text;
                float dur = Mathf.Clamp(text.Split(' ').Length / 2.6f, 14f, 32f), t0 = Time.unscaledTime;
                var clip = tourVoice != null && s < tourVoice.Length ? tourVoice[s] : null;
                if (clip != null)
                {
                    if (voice == null) voice = gameObject.AddComponent<AudioSource>();
                    voice.Stop(); voice.clip = clip; voice.Play();
                    dur = clip.length + 1.5f; // the caption stays until the voice has finished
                }
                while (Time.unscaledTime - t0 < dur)
                {
                    TourStep = s; TourStepProgress = (Time.unscaledTime - t0) / dur;
                    captionFill.style.width = Length.Percent(100f * TourStepProgress);
                    yield return null;
                }
            }
            Debug.Log($"[Qandeel] tour complete at {Time.realtimeSinceStartup:F1} s");
            StopTour();
        }

        Transform high;
        Transform HighView()
        {
            if (high == null)
            {
                high = new GameObject("Tour: forecast view").transform;
                var o = view.views[0];
                high.position = o.position + Vector3.up * 2500f - o.forward * 1500f;
                high.rotation = Quaternion.LookRotation((o.position + o.forward * 2000f) - high.position);
            }
            return high;
        }

        /// <summary>Smooth drone-like move (ease in/out), no instant jumps except the first shot.</summary>
        IEnumerator Fly(Transform target, float seconds)
        {
            view.FollowBoat = false;
            var cam = Camera.main.transform;
            Vector3 p0 = cam.position; Quaternion r0 = cam.rotation;
            for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
            {
                float u = Mathf.SmoothStep(0f, 1f, t / seconds);
                cam.SetPositionAndRotation(Vector3.Lerp(p0, target.position, u), Quaternion.Slerp(r0, target.rotation, u));
                yield return null;
            }
            cam.SetPositionAndRotation(target.position, target.rotation);
        }
    }
}
