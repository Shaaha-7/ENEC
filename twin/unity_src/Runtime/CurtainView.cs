using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Qandeel.Core;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Qandeel
{
    /// <summary>
    /// Live curtain hold at the mouth: runs Qandeel.Core.CurtainHold (tested against curtain.py) and draws every modelled
    /// jellyfish as an instanced marker. Physics frame: x along the mouth from tip a to tip b, y offshore; mapped onto the
    /// measured tips. Sea time runs faster than real time; when a frame's physics budget is used up, sea time slows
    /// instead of the picture stuttering.
    /// </summary>
    public sealed class CurtainView : MonoBehaviour
    {
        [Header("Site (filled by SiteBuilder)")]
        public Vector3 tipA, tipB;
        public Renderer[] sectionRenderers;
        public Material sectionOn, sectionOff;
        [Header("Markers")]
        public Mesh markerMesh;
        public Material heldMat, passedMat, deepMat;
        public float markerSizeM = 8f;

        [Header("Art (filled by SiteBuilder)")]
        public GameObject[] sectionFx;     // surface foam per section
        public Transform[] views;          // camera presets: overview, mouth, underwater
        public int currentView = 1;

        [Header("Gathering (filled by SiteBuilder)")]
        public Transform boat, boat2;
        public LineRenderer boom;
        public Vector3[] towRoute;          // water-only route, mouth -> release point (core WaterGrid)
        public Vector3 standby;
        public float boatDrawScale = 3f;   // boats drawn 3x real size so they read from the air (labelled)

        [Header("2025 replay (filled by SiteBuilder)")]
        public TextAsset forcingCsv;       // model/qandeel/data/gulf_forcing.csv
        public int replayStartHour = 1464; // 1 Aug 2025 00:00 UTC
        public bool autoAir = true;
        ForcingRecord rec;
        public SmartAir.Result Season { get; private set; }
        public bool HasRecord => rec != null;
        public string ReplayTime => rec == null ? "" : rec.Time[Mathf.Clamp(replayStartHour + (int)sim.TimeHours, 0, rec.Hours - 1)].Replace("T", " ") + " UTC";
        public double EnergyMWh { get; private set; }
        public readonly System.Collections.Generic.List<string> Events = new System.Collections.Generic.List<string>();
        double lastLoggedQ; bool loggedPass;

        [Header("Real-site boundary and demo controls")]
        public TextAsset waterMask;        // unity_site/water_2000.bin as .bytes (Sentinel-2 mask, 10 m)
        public Vector2 maskCenterPx = new Vector2(1000f, 1000f);
        public WaterSurface sea;
        public Vector3 shelter;            // boats shelter here above 2 m waves (inside the harbour)
        WaterGrid grid;
        public bool DeepSwarm { get; private set; }
        public bool HighAir { get; private set; }
        public double WaveHs { get; private set; } = 0.5;
        public int BlowersTripped { get; private set; }
        public bool CameraOff { get; private set; }
        public Operations.Boats SeaRule => Operations.ForWaves(WaveHs);

        public void SetDeepSwarm(bool on) { DeepSwarm = on; ApplyDemo(); Log(on ? "Deep swarm: mean depth 2 m" : "Swarm back at the surface"); }
        public void SetHighAir(bool on) { HighAir = on; ApplyDemo(); Log(on ? "High air: 4.5 L/s/m" : "Normal air: 3 L/s/m"); }
        public void SetWaves(double hs)
        {
            WaveHs = hs;
            if (sea != null) sea.largeWindSpeed = hs < 1 ? 14f : hs <= 2 ? 40f : 80f; // look only; waves do not change the physics
            var rule = SeaRule;
            Log($"Sea state Hs ~{hs:F1} m: boats {rule switch { Operations.Boats.Run => "run", Operations.Boats.StandBy => "stand by", _ => "shelter in port" }}; curtain keeps running");
            if (rule == Operations.Boats.InPort) { Alert("ROUGH SEA  ·  Hs > 2 m  ·  boats return to port  ·  curtain keeps running", 1, 10f); FlyToView(0); speed = 600; }
            else if (rule == Operations.Boats.StandBy) Alert("SEA 1–2 m  ·  boats stand by  ·  the held swarm waits at the curtain", 1, 8f);
            else Alert("CALM SEA  ·  boats back to work", 0, 5f);
        }
        public void TripBlower()
        {
            BlowersTripped = Mathf.Min(BlowersTripped + 1, Operations.Blowers);
            Log(BlowersTripped == 1 ? "Blower trip: standby blower takes over, no loss of air"
                                    : $"Blower trip: {Operations.RunningBlowers(BlowersTripped)} blower(s) running");
            StopCoroutine(nameof(BlowerTripFx)); StartCoroutine(nameof(BlowerTripFx));
        }
        /// <summary>The bubbles visibly die along the whole curtain, then the standby blower restores them (after the
        /// first trip the air is back to full; after a second trip half the air, so half the boil).</summary>
        System.Collections.IEnumerator BlowerTripFx()
        {
            FlyToView(1);
            Alert($"BLOWER TRIP  ·  duty blower {BlowersTripped} down  ·  air pressure falling", 2, 3f);
            fxDown = true; RefreshSections();
            yield return new WaitForSecondsRealtime(4f);
            fxDown = false; ApplyDemo();
            Alert(BlowersTripped == 1 ? "STANDBY BLOWER ONLINE  ·  full air restored  ·  no loss"
                                      : $"ONLY {Operations.RunningBlowers(BlowersTripped)} BLOWER(S)  ·  air {sim.AirflowLps:F1} L/s/m  ·  curtain weaker",
                  BlowersTripped == 1 ? 0 : 2, 8f);
        }
        bool fxDown;
        public void ResetBlowers() { BlowersTripped = 0; ApplyDemo(); Log("Blowers reset: 2 duty + 1 standby"); Alert("BLOWERS RESET  ·  2 duty + 1 standby", 0, 4f); }
        public void SetCameraOff(bool off)
        {
            CameraOff = off; Log(off ? "AI camera off: the forecast still switches the curtain on" : "AI camera back on");
            Alert(off ? "AI CAMERA OFFLINE  ·  the forecast still switches the curtain on (+13 h warning)" : "AI CAMERA BACK ONLINE", off ? 1 : 0, 8f);
        }

        // ---- stand moments: swarm incoming, curtain master switch, on-screen alerts, camera moves
        public bool SwarmMode { get; private set; }
        public string AlertText { get; private set; } = "";
        public int AlertLevel { get; private set; }   // 0 info, 1 warning, 2 alarm
        public float AlertUntil { get; private set; }
        public void Alert(string text, int level, float seconds) { AlertText = text; AlertLevel = level; AlertUntil = Time.unscaledTime + seconds; }

        /// <summary>A 2,500-jellyfish swarm arrives at the mouth (5x the normal model); the curtain runs at full air.</summary>
        public void SwarmIncoming()
        {
            Load(0, 2500);
            SwarmMode = true; speed = 240;
            Log("Swarm detected 15 km NNW: 2,500 modelled jellyfish (5x normal)");
            Alert("SWARM INCOMING  ·  2,500 jellyfish heading for the intake  ·  curtain at full air", 1, 14f);
            FlyToView(1);
        }

        public bool CurtainOn { get { foreach (var s in sim.SectionOn) if (s) return true; return false; } }

        /// <summary>Master switch: all ten sections off (what happens without Qandeel) or all on.</summary>
        public void SetCurtain(bool on)
        {
            for (int i = 0; i < sim.SectionOn.Length; i++) sim.SectionOn[i] = on;
            RefreshSections();
            Log(on ? "Curtain ON: all 10 sections" : "Curtain OFF: no protection at the intake");
            Alert(on ? "CURTAIN ON  ·  the swarm is pushed back to sea" : "CURTAIN OFF  ·  nothing stops the swarm reaching the intake", on ? 0 : 2, 6f);
            FlyToView(1);
        }

        // Rolling count of jellyfish reaching the intake in the last sea hour (one sample per sea minute)
        readonly int[] enteredAt = new int[61]; int samples; double nextSample;
        public double PassedLastHourShare
        {
            get
            {
                if (samples == 0) return 0;
                int old = samples <= enteredAt.Length ? enteredAt[0] : enteredAt[samples % enteredAt.Length]; // oldest = ~60 sea minutes ago
                return (double)(sim.Entered - old) / sim.N;
            }
        }
        void SamplePassed()
        {
            while (sim.TimeHours * 60.0 >= nextSample) { enteredAt[samples % enteredAt.Length] = sim.Entered; samples++; nextSample += 1.0; }
        }
        public int UnitsOnline => Operations.UnitsOnline(PassedLastHourShare);
        public double PlantMW => Operations.PlantMW(PassedLastHourShare);

        public void FlyToView(int i) { if (views != null && i < views.Length) FlyTo(views[i].position, views[i].rotation, 2.5f); currentView = i; }

        /// <summary>Camera to a curtain section, low and from the sea side, so its jellyfish are easy to see.</summary>
        public void FocusSection(int i)
        {
            double w = Site.GapWidthM / sim.SectionOn.Length, x = -Site.GapWidthM / 2 + (i + 0.5) * w;
            var c = ToWorld(x, 0);
            var pos = c + seaward * 260f + Vector3.up * 120f;
            FlyTo(pos, Quaternion.LookRotation(c - seaward * 40f - pos), 2.5f);
        }

        public void FlyTo(Vector3 pos, Quaternion rot, float seconds)
        {
            followBoat = false;
            if (fly != null) StopCoroutine(fly);
            fly = StartCoroutine(FlyRoutine(pos, rot, seconds));
        }
        Coroutine fly;
        System.Collections.IEnumerator FlyRoutine(Vector3 pos, Quaternion rot, float seconds)
        {
            var cam = Camera.main.transform; Vector3 p0 = cam.position; Quaternion r0 = cam.rotation;
            for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
            {
                float u = Mathf.SmoothStep(0f, 1f, t / seconds);
                cam.SetPositionAndRotation(Vector3.Lerp(p0, pos, u), Quaternion.Slerp(r0, rot, u));
                yield return null;
            }
            cam.SetPositionAndRotation(pos, rot); fly = null;
        }

        /// <summary>Applies the demo controls on top of the scenario; the physics core is unchanged.</summary>
        void ApplyDemo()
        {
            double demand = HighAir ? Operations.HighAirLps : scenario.AirflowLps;
            sim.AirflowLps = Operations.AirflowAfterTrips(demand, BlowersTripped);
            sim.WithSwarmDepth(DeepSwarm ? Operations.DeepSwarmMeanM : 0.0, HighAir ? Operations.LayerHighAirM : Operations.LayerNormalM);
            RefreshSections();
        }

        public int scenarioIndex;
        public float speed = 120f;           // sea seconds per real second
        public float physicsBudgetMs = 4f;
        public bool paused;

        CurtainHold sim;
        Scenario scenario;
        double simAccum;
        float effectiveSpeed;
        Vector3 mid, along, seaward;
        float xScale;
        [Header("Translucent bells for the underwater close-up (filled by SiteBuilder)")]
        public Material heldClear, deepClear, passedClear;
        [Header("Water effects (filled by SiteBuilder)")]
        public ParticleSystem[] wakes;     // one per boat, at the stern
        public ParticleSystem bagFoam;     // where the net and bag drag
        public Transform[] boomPieces;      // 16 x 12.5 m RO-BOOM 2000 class pieces (Blender), strung along the U
        public Transform bagModel;         // closed-bottom bag (Blender) at the bottom of the U
        int nHeld, nPassed, nDeep;
        // Boat cycle (sea-time speeds from planning.py: tow 0.2 m/s through water, empty 1.5 m/s, fill 1 h, release 0.5 h)
        enum BoatState { Standby, ToSwarm, Sweeping, Towing, Releasing, Returning }
        BoatState state;
        float stateTime, routePos; int inBag, released, trips;
        const float TowMps = 0.2f, EmptyMps = 1.5f, SweepS = 3600f, ReleaseS = 1800f; const int LaunchAt = 50;
        bool followBoat;
        float drawScale = 1f;
        GUIStyle title, label, small, btn, btnOn;
        bool benchmark; bool reported;
        readonly Stopwatch sw = new Stopwatch();

        void Start()
        {
            mid = (tipA + tipB) * 0.5f;
            along = (tipB - tipA).normalized;
            seaward = Vector3.Cross(along, Vector3.up).normalized;
            if (Vector3.Dot(seaward, Vector3.forward) < 0) seaward = -seaward; // offshore is north
            xScale = Vector3.Distance(tipA, tipB) / (float)Site.GapWidthM;

            var args = Environment.GetCommandLineArgs();
            benchmark = Array.IndexOf(args, "-benchmark") >= 0;
            int si = Array.IndexOf(args, "-scenario");
            if (si >= 0 && si + 1 < args.Length && int.TryParse(args[si + 1], out int s)) scenarioIndex = s;
            if (benchmark) speed = 600f;
            jellyFrames = new Mesh[JellyfishMesh.Frames];
            for (int f = 0; f < jellyFrames.Length; f++) jellyFrames[f] = JellyfishMesh.Build(0.38f, f / (float)JellyfishMesh.Frames);
            markerMesh = jellyFrames[0];
            if (forcingCsv != null) { rec = ForcingRecord.Parse(forcingCsv.text); Season = SmartAir.Season(rec); }
            Load(scenarioIndex);
            SetView(currentView);
        }

        public void SetView(int i)
        {
            if (views == null || views.Length == 0) return;
            currentView = (i + views.Length) % views.Length;
            followBoat = false;
            Camera.main.transform.SetPositionAndRotation(views[currentView].position, views[currentView].rotation);
        }

        public void Load(int i) => Load(i, 500);

        public void Load(int i, int n)
        {
            scenarioIndex = Mathf.Clamp(i, 0, Scenario.All.Length - 1);
            scenario = Scenario.All[scenarioIndex];
            sim = scenario.Create(seed: 0, n: n);
            SwarmMode = false; samples = 0; nextSample = 0; fxDown = false; lastUnits = Operations.PlantUnits;
            if (grid == null && waterMask != null)
                grid = new WaterGrid(waterMask.bytes, 2000, 10.0, maskCenterPx.x, maskCenterPx.y, 0);
            if (grid != null) sim.ConstrainToWater((x, y) => { var w = ToWorld(x, y); return grid.IsWaterAt(w.x, w.z); });
            if (DeepSwarm || HighAir || BlowersTripped > 0) ApplyDemo();
            buckets = new Matrix4x4[3, JellyfishMesh.Frames][]; counts = new int[3, JellyfishMesh.Frames];
            for (int b = 0; b < 3; b++) for (int f = 0; f < JellyfishMesh.Frames; f++) buckets[b, f] = new Matrix4x4[sim.N];
            simAccum = 0; reported = false;
            state = BoatState.Standby; stateTime = 0; inBag = released = trips = 0;
            EnergyMWh = 0; Events.Clear(); loggedPass = false; lastLoggedQ = sim.AirflowLps;
            Log($"Scenario: {scenario.Name}");
            if (boat != null)
            {
                unitPos = standby; unitDir = seaward;
                boat.localScale = Vector3.one * boatDrawScale;
                if (boat2 != null) boat2.localScale = Vector3.one * boatDrawScale;
                PlaceUnit();
            }
            RefreshSections();
        }

        public void ToggleSection(int i) => ToggleSection(i, false);

        /// <summary>Switches one section; with focus the camera flies to it so the gap and the jellyfish going through show.</summary>
        public void ToggleSection(int i, bool focus)
        {
            sim.SectionOn[i] = !sim.SectionOn[i];
            Log($"Section {i + 1} {(sim.SectionOn[i] ? "on" : "off")}");
            RefreshSections();
            if (!sim.SectionOn[i]) Alert($"SECTION {i + 1} OFF  ·  gap in the curtain  ·  jellyfish (red) slip through to the screens", 2, 8f);
            else Alert($"SECTION {i + 1} BACK ON", 0, 4f);
            if (focus) FocusSection(i);
        }

        void RefreshSections()
        {
            float airShare = Mathf.Clamp((float)sim.AirflowLps / 3f, 0f, 1.5f);
            for (int i = 0; i < sectionRenderers.Length && i < sim.SectionOn.Length; i++)
            {
                bool on = sim.SectionOn[i] && sim.AirflowLps > 0 && !fxDown;
                sectionRenderers[i].sharedMaterial = on ? sectionOn : sectionOff;
                if (sectionFx == null || i >= sectionFx.Length || sectionFx[i] == null) continue;
                if (fxDown) foreach (var ps in sectionFx[i].GetComponentsInChildren<ParticleSystem>(true)) ps.Clear(true);
                sectionFx[i].SetActive(on);
                // boil strength follows the air actually delivered (half air after two blower trips = half the boil)
                foreach (var ps in sectionFx[i].GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (!fxBase.TryGetValue(ps, out float b0)) fxBase[ps] = b0 = ps.emission.rateOverTimeMultiplier;
                    var em = ps.emission; em.rateOverTimeMultiplier = b0 * airShare;
                }
            }
        }

        readonly System.Collections.Generic.Dictionary<ParticleSystem, float> fxBase = new System.Collections.Generic.Dictionary<ParticleSystem, float>();

        public Vector3 World(double x, double y) => ToWorld(x, y);
        public ForcingRecord Record => rec;
        public bool HideSwarm;
        public bool IsWater(Vector3 w) => grid == null || grid.IsWaterAt(w.x, w.z);

        /// <summary>Switches one section without an alert or camera move (start-up / shut-down sequences).</summary>
        public void SetSectionQuiet(int i, bool on) { sim.SectionOn[i] = on; RefreshSections(); }

        /// <summary>Moves the modelled swarm into an offshore patch (sim frame: x along the mouth, y seaward, metres),
        /// on water only, e.g. as it arrives from the forecast track.</summary>
        public void PlaceSwarm(double cx, double cy, double rx, double ry, ulong seed = 3)
        {
            var r = new Rng(seed);
            for (int i = 0; i < sim.N; i++)
            {
                for (int tries = 0; tries < 30; tries++)
                {
                    double a = r.Uniform(0, 2 * System.Math.PI), d = System.Math.Sqrt(r.Uniform(0, 1));
                    double x = cx + System.Math.Cos(a) * rx * d, y = cy + System.Math.Sin(a) * ry * d;
                    var w = ToWorld(x, y);
                    if (grid == null || grid.IsWaterAt(w.x, w.z)) { sim.X[i] = x; sim.Y[i] = y; sim.StartX[i] = x; sim.StartY[i] = y; break; }
                }
            }
            samples = 0; nextSample = 0; lastUnits = Operations.PlantUnits;
        }

        Vector3 ToWorld(double x, double y) => mid + along * ((float)x * xScale) + seaward * (float)y;

        double fxAir = -1;

        /// <summary>Scenario button: loads it, says on screen what it changes and what to watch, and runs fast enough
        /// for the effect to show within seconds.</summary>
        public void LoadScenario(int k)
        {
            Load(k);
            string watch = k switch
            {
                0 => "normal air 3 L/s/m against the 2025 mean current: watch the swarm held at sea",
                1 => "stronger current 0.23 m/s pushes the swarm harder against the curtain",
                2 => "only 1 L/s/m of air: weak bubbles, watch jellyfish (red) slip through",
                3 => "swarm 2 m deep: grey bells swim under the outflow layer and some pass",
                4 => "section 5 off: a 97 m gap, watch the jellyfish funnel through it",
                5 => "real hourly 2025 currents; auto air sets the airflow each hour",
                _ => "a 200 t/day bloom: the curtain holds it, the boats have to shuttle",
            };
            Alert($"{scenario.Name.ToUpperInvariant()}  ·  {watch}", k == 2 || k == 3 || k == 4 ? 1 : 0, 10f);
            speed = 300f;
            if (k == 4) FocusSection(4); else FlyToView(1);
        }

        /// <summary>Air slider: sets the airflow, the bubbles follow it, and the banner gives the outflow it makes.</summary>
        public void SetAirflow(double q)
        {
            autoAir = false; sim.AirflowLps = q; RefreshSections(); fxAir = q;
            Alert($"AIR {q:F1} L/s/m  ·  surface outflow {Bulson.SurfaceCurrent(q):F2} m/s  ·  checking the model...", 0, 5f);
            Predict();
        }

        /// <summary>Approach slider: the current carrying the swarm toward the intake.</summary>
        public void SetApproach(double v)
        {
            sim.ApproachSpeed = v;
            Alert($"APPROACH CURRENT {v:F2} m/s  ·  checking the model...", 0, 5f);
            Predict();
        }

        // Model prediction for the slider banner: the same CurtainHold physics, 300 jellyfish over 2 sea hours,
        // run off the main thread after the slider has been still for 0.4 s.
        float predictAt = -1; System.Threading.Tasks.Task<double> predictTask; double pQ, pV;
        void Predict() { predictAt = Time.unscaledTime + 0.4f; }
        void PredictTick()
        {
            if (predictAt > 0 && Time.unscaledTime >= predictAt && predictTask == null)
            {
                predictAt = -1; pQ = sim.AirflowLps; pV = sim.ApproachSpeed;
                double q = pQ, v = pV;
                predictTask = System.Threading.Tasks.Task.Run(() => CurtainHold.HeldShareOf(v, q, hours: 2.0, n: 300));
            }
            if (predictTask != null && predictTask.IsCompleted)
            {
                if (predictTask.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
                {
                    double h = predictTask.Result;
                    Alert($"AIR {pQ:F1} L/s/m (outflow {Bulson.SurfaceCurrent(pQ):F2} m/s) vs APPROACH {pV:F2} m/s  ·  model: {h:P0} held over 2 h  ·  " +
                          (h >= 0.97 ? "the curtain holds" : h >= 0.8 ? "some get through" : "the current overpowers the curtain"), h >= 0.97 ? 0 : h >= 0.8 ? 1 : 2, 8f);
                }
                predictTask = null;
            }
        }

        void Update()
        {
            PredictTick();
            if (sim != null && System.Math.Abs(sim.AirflowLps - fxAir) > 0.05) { fxAir = sim.AirflowLps; RefreshSections(); }
            for (int k = 0; k < 10; k++)
                if (Input.GetKeyDown(k == 9 ? KeyCode.Alpha0 : KeyCode.Alpha1 + k)) ToggleSection(k, true);
            if (Input.GetKeyDown(KeyCode.S)) SwarmIncoming();
            if (Input.GetKeyDown(KeyCode.C)) SetCurtain(!CurtainOn);
            if (Input.GetKeyDown(KeyCode.Space)) paused = !paused;
            if (Input.GetKeyDown(KeyCode.R)) Load(scenarioIndex);
            if (Input.GetKeyDown(KeyCode.F)) followBoat = !followBoat;
            if (Input.GetKeyDown(KeyCode.V)) SetView(currentView + 1);

            // Physics with a per-frame time budget
            if (!paused)
            {
                simAccum += Time.unscaledDeltaTime * speed;
                sw.Restart();
                int steps = 0;
                while (simAccum >= sim.Dt && sw.Elapsed.TotalMilliseconds < physicsBudgetMs)
                {
                    sim.Step(); simAccum -= sim.Dt; steps++;
                }
                if (boat != null) BoatStep(steps * (float)sim.Dt);
                AfterPhysics(steps * sim.Dt);
                if (simAccum > sim.Dt * 4) simAccum = sim.Dt * 4; // budget hit: drop the backlog, sea time slows
                effectiveSpeed = Mathf.Lerp(effectiveSpeed, steps * (float)sim.Dt / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);
            }

            if (benchmark && !reported && sim.TimeHours >= 4.0) ReportResult();
            if (HideSwarm) return;   // live operation: the swarm is still offshore (drawn by Operation)

            // Draw Blue Blubber: real size up close, enlarged from the air so the swarm reads (factor shown on screen).
            // Each swims through a 6-frame cycle (~1 Hz bell pulse, arms swaying) with its own phase.
            float camDist = Vector3.Distance(Camera.main.transform.position, mid);
            drawScale = Mathf.Clamp(camDist / 25f, 1f, markerSizeM / 0.38f);
            bool underwater = Camera.main.transform.position.y < 0f;
            float tt = Time.time;
            for (int b = 0; b < 3; b++) for (int f = 0; f < JellyfishMesh.Frames; f++) counts[b, f] = 0;
            for (int i = 0; i < sim.N; i++)
            {
                float ph = (i * 0.6180339f % 1f) * 6.2831f;
                float cyc = tt * (0.85f + 0.3f * ((i * 0.3819f) % 1f)) + ph / 6.2831f;   // each pulses at its own rate
                int frame = (int)(cyc * JellyfishMesh.Frames) % JellyfishMesh.Frames;
                var sc = Vector3.one * drawScale * (0.8f + 0.45f * ((i * 0.7548f) % 1f));  // mixed sizes
                // gentle tilt and rock as it swims (bell leans into each stroke)
                var rot = Quaternion.Euler(14f * Mathf.Sin(tt * 0.7f + ph), ph * 57.3f + 8f * Mathf.Sin(tt * 0.3f + ph * 2f), 14f * Mathf.Cos(tt * 0.55f + ph * 1.3f));
                float bob = 0.06f * Mathf.Sin(cyc * 6.2831f);                                        // rises on each contraction
                if (sim.Pocketed[i])
                {
                    // Caught: packed in the bag at the bottom of the boom U while it is out; afterwards released alive at 3 km, 330°
                    bool inBagNow = inBag > 0 && (state == BoatState.Sweeping || state == BoatState.Towing || state == BoatState.Releasing);
                    float a = i * 2.39996f, r = Mathf.Sqrt((i * 0.618034f) % 1f);
                    Vector3 c, off;
                    if (inBagNow) { c = unitPos - unitDir * (4.5f * boatDrawScale + BoomDepth); off = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r * 12f; }
                    else if (released > 0 && towRoute != null && towRoute.Length > 0) { c = towRoute[towRoute.Length - 1]; off = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r * 120f; }
                    else continue;
                    var bm = Matrix4x4.TRS(c + off + Vector3.up * 0.02f * drawScale, rot, sc);
                    Put(0, frame, bm);
                    continue;
                }
                float depth = (float)sim.DepthM[i] + 0.15f;
                var pos = ToWorld(sim.X[i], sim.Y[i]) + Vector3.up * (drawScale > 1.5f ? 0.02f * drawScale : -depth + bob);
                if (grid != null && !grid.IsWaterAt(pos.x, pos.z)) continue;   // never drawn on a breakwater or the beach
                var m = Matrix4x4.TRS(pos, drawScale > 1.5f ? Quaternion.Euler(0, ph * 57.3f, 0) : rot, sc);
                Put(sim.Inside[i] ? 2 : !sim.InLayer(i) ? 1 : 0, frame, m);
            }
            var mats = underwater && heldClear != null ? new[] { heldClear, deepClear, passedClear } : new[] { heldMat, deepMat, passedMat };
            for (int b = 0; b < 3; b++)
                for (int f = 0; f < JellyfishMesh.Frames; f++)
                    if (counts[b, f] > 0) Graphics.RenderMeshInstanced(new RenderParams(mats[b]), jellyFrames[f], 0, buckets[b, f], counts[b, f]);
        }

        // instanced draw buckets: [held / deep / passed, swim frame]
        Mesh[] jellyFrames;
        Matrix4x4[,][] buckets;
        int[,] counts;
        void Put(int b, int f, Matrix4x4 m) { if (counts[b, f] < buckets[b, f].Length) buckets[b, f][counts[b, f]++] = m; }

        // ---- air supply gauges (design.json / results.json "compressor": 174 m3/min, 436 kW, 1.06-1.22 bar(g) at 3 L/s/m)
        public double AirM3PerMin => sim.AirflowLps * Site.GapWidthM * 60.0 / 1000.0;
        public double PowerKW => 436.0 * sim.AirflowLps / 3.0;   // ASSUMED proportional to airflow
        public (double lo, double hi) PressureBarG
        {
            get { double d = (sim.AirflowLps - 3.0) / 1.5 * 0.06; return (1.06 + d, 1.22 + d); } // linear between normal and max design points
        }

        void AfterPhysics(double seaS)
        {
            if (seaS <= 0) return;
            if (scenario.Replay2025 && rec != null)
            {
                int h = Mathf.Clamp(replayStartHour + (int)sim.TimeHours, 0, rec.Hours - 1);
                sim.ApproachSpeed = SmartAir.ApproachAt(rec, h);
            }
            if (autoAir && scenario.Replay2025) sim.AirflowLps = SmartAir.AirflowSetting(sim.ApproachSpeed);
            if (System.Math.Abs(sim.AirflowLps - lastLoggedQ) >= 0.4)
            {
                lastLoggedQ = sim.AirflowLps;
                Log($"Air set to {sim.AirflowLps:F1} L/s/m (approach {sim.ApproachSpeed:F2} m/s)");
            }
            EnergyMWh += PowerKW * seaS / 3600.0 / 1000.0;
            SamplePassed();
            int units = UnitsOnline;
            if (units != lastUnits) { if (units < lastUnits) Log($"Screens clogging: {units}/4 units online*"); else Log($"Screens clearing: {units}/4 units online*"); lastUnits = units; }
            if (!loggedPass && sim.Entered > 0) { loggedPass = true; Log("First jellyfish passed the curtain"); }
            if (state != lastBoatState) { lastBoatState = state; Log("Boat: " + BoatStatus + (state == BoatState.Towing ? $" ({inBag} in bag)" : "")); }
        }

        BoatState lastBoatState;
        int lastUnits = Operations.PlantUnits;

        public void Log(string msg)
        {
            double t = sim != null ? sim.TimeHours : 0;
            Events.Insert(0, $"T+{(int)t}:{(int)(t * 60 % 60):00}  {msg}");
            if (Events.Count > 4) Events.RemoveAt(Events.Count - 1);
        }

        Vector3 ToPhysics(Vector3 w) { var d = w - mid; return new Vector3(Vector3.Dot(d, along) / xScale, 0, Vector3.Dot(d, seaward)); }

        float RouteLength() { float s = 0; for (int i = 1; i < towRoute.Length; i++) s += Vector3.Distance(towRoute[i - 1], towRoute[i]); return s; }

        Vector3 RoutePoint(float dist, out Vector3 dir)
        {
            for (int i = 1; i < towRoute.Length; i++)
            {
                float seg = Vector3.Distance(towRoute[i - 1], towRoute[i]);
                if (dist <= seg || i == towRoute.Length - 1)
                {
                    dir = (towRoute[i] - towRoute[i - 1]).normalized;
                    return Vector3.Lerp(towRoute[i - 1], towRoute[i], Mathf.Clamp01(dist / seg));
                }
                dist -= seg;
            }
            dir = Vector3.forward; return towRoute[towRoute.Length - 1];
        }

        // Two-boat gathering unit (design.json: tow force per boat, two boats): the boats run ~66 m apart with the 200 m
        // boom slung between their sterns as a U and the closed bag at its bottom. unitPos = midpoint between the boats.
        const float HalfOpening = 33f, BoomDepth = 70f;
        Vector3 unitPos, unitDir = Vector3.forward;
        bool atRouteStart, wasInPort;

        bool MoveTo(Vector3 target, float metres)
        {
            var d = target - unitPos; d.y = 0;
            if (d.magnitude > 1f) unitDir = Vector3.Slerp(unitDir, d.normalized, 0.15f).normalized; // turn only while moving
            if (d.magnitude <= metres) { unitPos = new Vector3(target.x, unitPos.y, target.z); return true; }
            unitPos += d.normalized * metres; return false;
        }

        void Follow(float dist, bool backwards)
        {
            unitPos = RoutePoint(Mathf.Clamp(dist, 0, RouteLength()), out var dir);
            unitDir = Vector3.Slerp(unitDir, backwards ? -dir : dir, 0.2f).normalized;
        }

        /// <summary>One gathering cycle: sweep along the curtain through the held swarm, tow the closed bag round the breakwater
        /// to the release point at 0.2 m/s, release alive, return empty. Advanced in sea seconds with the physics.</summary>
        void BoatStep(float seaS)
        {
            if (seaS <= 0) return;
            // Sea state (narration topic.boats): above 2 m the boats shelter in port, 1-2 m they stand by where they are,
            // below 1 m they work. The curtain keeps running in every case.
            var rule = SeaRule;
            if (rule == Operations.Boats.InPort) { MoveTo(shelter, EmptyMps * seaS); wasInPort = true; PlaceUnit(); return; }
            if (wasInPort)
            {
                wasInPort = false;
                if (state == BoatState.Towing) { routePos = 0; atRouteStart = false; }
                if (state == BoatState.Returning) state = BoatState.Standby;
            }
            if (rule == Operations.Boats.StandBy) { PlaceUnit(); return; }
            stateTime += seaS;
            bool held = sim.HeldCentroid(out double hx, out double hy, out int count);
            Vector3 swarm = held ? ToWorld(hx, hy) : mid + seaward * 30f;
            Vector3 sweepStart = swarm - along * 150f, sweepEnd = swarm + along * 120f;
            switch (state)
            {
                case BoatState.Standby:
                    if (held && count >= LaunchAt) { state = BoatState.ToSwarm; stateTime = 0; }
                    break;
                case BoatState.ToSwarm:
                    if (MoveTo(sweepStart, EmptyMps * seaS)) { state = BoatState.Sweeping; stateTime = 0; }
                    break;
                case BoatState.Sweeping:
                    bool done = MoveTo(sweepEnd, TowMps * seaS);
                    var p = ToPhysics(unitPos - unitDir * (BoomDepth * 0.5f)); // inside the U
                    inBag += sim.Gather(p.x, p.z, HalfOpening);
                    if (done || stateTime >= SweepS) { state = BoatState.Towing; stateTime = 0; routePos = 0; atRouteStart = false; }
                    break;
                case BoatState.Towing:
                    if (!atRouteStart) { atRouteStart = MoveTo(towRoute[0], TowMps * seaS); break; }
                    routePos += TowMps * seaS; Follow(routePos, false);
                    if (routePos >= RouteLength()) { state = BoatState.Releasing; stateTime = 0; }
                    break;
                case BoatState.Releasing:
                    if (stateTime >= ReleaseS) { released += inBag; inBag = 0; trips++; state = BoatState.Returning; routePos = RouteLength(); }
                    break;
                case BoatState.Returning:
                    if (routePos > 0) { routePos -= EmptyMps * seaS; Follow(routePos, true); }
                    else if (MoveTo(standby, EmptyMps * seaS)) { state = BoatState.Standby; stateTime = 0; }
                    break;
            }
            PlaceUnit();
        }

        /// <summary>Puts both boats either side of the unit centre and draws the boom U between their sterns.</summary>
        void PlaceUnit()
        {
            var right = Vector3.Cross(Vector3.up, unitDir).normalized;
            var rot = Quaternion.LookRotation(unitDir);
            float gap = state == BoatState.Standby || state == BoatState.Returning || state == BoatState.ToSwarm ? 12f : HalfOpening;
            boat.SetPositionAndRotation(unitPos + right * gap, rot);
            if (boat2 != null) boat2.SetPositionAndRotation(unitPos - right * gap, rot);
            bool moving = state == BoatState.ToSwarm || state == BoatState.Sweeping || state == BoatState.Towing || state == BoatState.Returning;
            bool dragging = state == BoatState.Sweeping || state == BoatState.Towing;
            if (wakes != null) foreach (var w in wakes) if (w != null) { var em = w.emission; em.enabled = moving; }
            if (bagFoam != null)
            {
                var em = bagFoam.emission; em.enabled = dragging;
                bagFoam.transform.position = unitPos - unitDir * (4.5f * boatDrawScale + BoomDepth) + Vector3.up * 0.2f;
            }
            bool show = state == BoatState.Sweeping || state == BoatState.Towing || state == BoatState.Releasing;
            float stern = 4.5f * boatDrawScale;
            Vector3 U(float t) { var pt = unitPos + right * (HalfOpening * Mathf.Cos(Mathf.PI * t)) - unitDir * (stern + BoomDepth * Mathf.Sin(Mathf.PI * t)); pt.y = 0.6f; return pt; }
            if (boomPieces != null && boomPieces.Length > 0)
            {
                for (int i = 0; i < boomPieces.Length; i++)
                {
                    var p0 = U(i / (float)boomPieces.Length); var p1 = U((i + 1) / (float)boomPieces.Length);
                    boomPieces[i].gameObject.SetActive(show);
                    if (!show) continue;
                    boomPieces[i].SetPositionAndRotation((p0 + p1) * 0.5f, Quaternion.LookRotation((p1 - p0).normalized));
                    boomPieces[i].localScale = new Vector3(boatDrawScale, boatDrawScale, Vector3.Distance(p0, p1) / 12.5f);
                }
                if (boom != null) boom.enabled = false;
            }
            if (bagModel != null)
            {
                bagModel.gameObject.SetActive(show && inBag > 0);
                bagModel.SetPositionAndRotation(U(0.5f), Quaternion.LookRotation(unitDir));
                bagModel.localScale = Vector3.one * boatDrawScale;
            }
            if (boom == null || (boomPieces != null && boomPieces.Length > 0)) return;
            boom.enabled = show;
            if (!show) return;
            int n = boom.positionCount = 25;
            for (int i = 0; i < n; i++) boom.SetPosition(i, U(i / (float)(n - 1)) + Vector3.up * 0.6f);
        }

        void LateUpdate()
        {
            if (!followBoat || boat == null) return;
            var cam = Camera.main.transform;
            var target = unitPos - unitDir * 450f + Vector3.up * 280f;
            cam.position = Vector3.Lerp(cam.position, target, 1f - Mathf.Exp(-2f * Time.unscaledDeltaTime));
            cam.LookAt(unitPos - unitDir * 40f);
        }

        void Draw(Material mat, Matrix4x4[] m, int n)
        {
            if (n == 0 || mat == null || markerMesh == null) return;
            Graphics.RenderMeshInstanced(new RenderParams(mat), markerMesh, 0, m, n);
        }

        void ReportResult()
        {
            reported = true;
            string dir = Path.GetDirectoryName(Application.dataPath);
            File.WriteAllText(Path.Combine(dir, $"scenario_{scenarioIndex}.json"), string.Format(CultureInfo.InvariantCulture,
                "{{\"scenario\": \"{0}\", \"sea_hours\": {1:F2}, \"held_share\": {2:F3}, \"entered\": {3}, \"n\": {4}, \"boat\": \"{5}\", \"in_bag\": {6}, \"tow_route_m\": {7:F0} }}",
                scenario.Name, sim.TimeHours, sim.HeldShare, sim.Entered, sim.N, state, inBag, towRoute != null ? RouteLength() : 0));
        }

        // ---- read-only state for the control-room UI and the tour
        public CurtainHold Sim => sim;
        public Scenario CurrentScenario => scenario;
        public string BoatStatus => state switch
        {
            BoatState.Standby => "Standby at the western tip",
            BoatState.ToSwarm => "Heading to the held swarm",
            BoatState.Sweeping => "Sweeping the held swarm with the boom",
            BoatState.Towing => "Towing the closed bag at 0.2 m/s",
            BoatState.Releasing => "Releasing alive at 3 km, 330°",
            _ => "Returning empty",
        };
        public bool BoatTowing => state == BoatState.Towing;
        public int InBag => inBag;
        public int Released => released;
        public float EffectiveSpeed => effectiveSpeed;
        public float DrawScale => drawScale;
        public bool FollowBoat { get => followBoat; set => followBoat = value; }
        public bool showDebugGui;

        /// <summary>Runs the physics and boat ahead by `hours` of sea time at once (used by the tour).</summary>
        public void FastForward(double hours)
        {
            int steps = (int)(hours * 3600 / sim.Dt);
            for (int k = 0; k < steps; k++) { sim.Step(); if (boat != null && k % 30 == 0) BoatStep(30f * (float)sim.Dt); }
            SamplePassed();
        }

        void Styles()
        {
            if (title != null) return;
            title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            label = new GUIStyle(GUI.skin.label) { fontSize = 18, normal = { textColor = new Color(0.9f, 0.95f, 1f) } };
            small = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, normal = { textColor = new Color(0.75f, 0.82f, 0.9f) } };
            btn = new GUIStyle(GUI.skin.button) { fontSize = 16 };
            btnOn = new GUIStyle(btn) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.7f, 0.3f) } };
        }

        void OnGUI()
        {
            if (!showDebugGui) return;
            Styles();
            float w = 420, x = Screen.width - w - 16, y = 16;
            GUI.Box(new Rect(x - 8, y - 8, w + 16, 790), GUIContent.none);
            GUI.Label(new Rect(x, y, w, 34), "Bubble curtain at the mouth", title); y += 40;
            for (int i = 0; i < Scenario.All.Length; i++)
            {
                if (GUI.Button(new Rect(x + (i % 2) * (w / 2), y, w / 2 - 4, 30), Scenario.All[i].Name, i == scenarioIndex ? btnOn : btn)) Load(i);
                if (i % 2 == 1) y += 34;
            }
            y += 40;
            GUI.Label(new Rect(x, y, w, 40), scenario.Source, small); y += 44;
            double u0 = Bulson.SurfaceCurrent(sim.AirflowLps);
            int below = 0; for (int i = 0; i < sim.N; i++) if (!sim.Inside[i] && !sim.InLayer(i)) below++;
            GUI.Label(new Rect(x, y, w, 24), $"Sea time  T+{(int)sim.TimeHours}:{(int)(sim.TimeHours * 60 % 60):00}   (x{effectiveSpeed:F0})", label); y += 26;
            GUI.Label(new Rect(x, y, w, 24), $"Air {sim.AirflowLps:F1} L/s/m  →  outflow {u0:F2} m/s", label); y += 26;
            GUI.Label(new Rect(x, y, w, 24), $"Approach current {sim.ApproachSpeed:F2} m/s", label); y += 26;
            GUI.Label(new Rect(x, y, w, 24), $"Held at sea  {sim.HeldShare:P0}   ({sim.N - sim.Entered} of {sim.N})", label); y += 26;
            GUI.Label(new Rect(x, y, w, 24), $"Passed the curtain  {sim.Entered}" + (below > 0 ? $"   ·  below the layer {below}" : ""), label); y += 26;
            GUI.Label(new Rect(x, y, w, 24), $"Boat: {state}   in bag {inBag}   released alive {released}   trips {trips}", label); y += 26;
            GUI.Label(new Rect(x, y, w, 24), $"Tip pockets {sim.PocketedA + sim.PocketedB} (the model keeps the swarm at the centre)", small); y += 30;
            GUI.Label(new Rect(x, y, w, 22), "Sections (keys 1-0):", small); y += 24;
            for (int i = 0; i < sim.SectionOn.Length; i++)
                if (GUI.Button(new Rect(x + i * (w / 10), y, w / 10 - 3, 30), (i + 1).ToString(), sim.SectionOn[i] ? btnOn : btn)) ToggleSection(i);
            y += 40;
            if (GUI.Button(new Rect(x, y, w / 3 - 4, 30), paused ? "Resume" : "Pause", btn)) paused = !paused;
            if (GUI.Button(new Rect(x + w / 3, y, w / 3 - 4, 30), "Restart", btn)) Load(scenarioIndex);
            if (GUI.Button(new Rect(x + 2 * w / 3, y, w / 3 - 4, 30), speed >= 600 ? "Speed x120" : "Speed x600", btn)) speed = speed >= 600 ? 120 : 600;
            y += 34;
            if (GUI.Button(new Rect(x, y, w, 30), followBoat ? "Camera: following boat (F)" : "Camera: follow boat (F)", followBoat ? btnOn : btn)) followBoat = !followBoat;
            y += 40;
            GUI.Label(new Rect(x, y, w, 70),
                "Crewed, chartered 8-10 m workboat; boom 200 m (RO-BOOM 2000 class), ~66 m opening; closed bag towed at 0.2 m/s round the " +
                "breakwater to the release point 3 km at 330°. In the model ~63% drift back and are held again. Boat drawn 3x size.", small);
            y += 74;
            GUI.Label(new Rect(x, y, w, 90),
                "Purple: held at sea · Red: passed into the intake channel · Grey: below the outflow layer (assumed thickness). " +
                $"{sim.N} modelled Blue Blubber, drawn {drawScale:F0}x real size in this view (38 cm bell). Physics: port of curtain.py, tested against the Python model. Views: V.",
                small);
        }
    }
}
