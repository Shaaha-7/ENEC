using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Qandeel
{
    /// <summary>On-screen FPS counter. With the command-line flag -benchmark the player waits for warm-up,
    /// averages FPS for 20 s, saves a screenshot and a JSON report next to the executable, then quits.</summary>
    public sealed class Benchmark : MonoBehaviour
    {
        const float WarmupS = 8f, MeasureS = 20f;
        float smoothed = 60f;
        GUIStyle style;

        float startupS = -1f;
        int errors;

        void Start()
        {
            Application.runInBackground = true; // the stand app must not pause when another window takes focus
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            Application.logMessageReceived += (msg, st, type) => { if (type == LogType.Exception || type == LogType.Error) errors++; };
            var args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-benchmark") >= 0) StartCoroutine(Run());
            if (Array.IndexOf(args, "-tourtest") >= 0) StartCoroutine(TourTest());
            if (Array.IndexOf(args, "-standtest") >= 0) StartCoroutine(StandTest());
            if (Array.IndexOf(args, "-challengetest") >= 0) StartCoroutine(ChallengeTest());
            if (Array.IndexOf(args, "-optest") >= 0) StartCoroutine(OperationTest());
            if (Array.IndexOf(args, "-scenariotest") >= 0) StartCoroutine(ScenarioTest());
            int s = Array.IndexOf(args, "-soak");
            if (s >= 0 && s + 1 < args.Length && int.TryParse(args[s + 1], out int minutes)) StartCoroutine(Soak(minutes));
        }

        void Update()
        {
            if (startupS < 0f) startupS = Time.realtimeSinceStartup; // first frame on screen
            smoothed = Mathf.Lerp(smoothed, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);
        }

        /// <summary>Plays the guided tour once from start to finish, saves tour_stepN.png midway through each step, then quits.</summary>
        IEnumerator TourTest()
        {
            string dir = Path.GetDirectoryName(Application.dataPath);
            var room = FindFirstObjectByType<ControlRoom>();
            yield return new WaitForSecondsRealtime(4f);
            room.StartTour();
            int last = -1;
            while (room.TourStep >= 0)
            {
                if (room.TourStep != last && room.TourStepProgress > 0.5f)
                {
                    last = room.TourStep;
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"tour_step{last + 1}.png"));
                }
                yield return null;
            }
            Application.Quit();
        }

        /// <summary>Plays the stand moments once: swarm incoming, curtain OFF (plant alarm), curtain ON (recovery),
        /// section 5 off, blower trip, rough sea. Saves stand_*.png and stand_test.json next to the executable, then quits.</summary>
        IEnumerator StandTest()
        {
            string dir = Path.GetDirectoryName(Application.dataPath);
            var cv = FindFirstObjectByType<CurtainView>();
            var log = new System.Text.StringBuilder("{");
            void Shot(string n) => ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"stand_{n}.png"));
            yield return new WaitForSecondsRealtime(5f);
            cv.SwarmIncoming();
            yield return new WaitForSecondsRealtime(6f); Shot("1_swarm"); yield return null;
            cv.speed = 600f; cv.SetCurtain(false);
            float t0 = Time.realtimeSinceStartup;
            while (cv.UnitsOnline >= 4 && Time.realtimeSinceStartup - t0 < 60f) yield return null;
            yield return new WaitForSecondsRealtime(4f);
            log.Append(string.Format(CultureInfo.InvariantCulture, "\"off_units\": {0}, \"off_alarm_after_s\": {1:F1}, \"off_passed\": {2}, ", cv.UnitsOnline, Time.realtimeSinceStartup - t0, cv.Sim.Entered));
            Shot("2_curtain_off"); yield return null;
            cv.SetCurtain(true); t0 = Time.realtimeSinceStartup;
            while (cv.UnitsOnline < 4 && Time.realtimeSinceStartup - t0 < 90f) yield return null;
            log.Append(string.Format(CultureInfo.InvariantCulture, "\"on_units\": {0}, \"recovered_after_s\": {1:F1}, ", cv.UnitsOnline, Time.realtimeSinceStartup - t0));
            yield return new WaitForSecondsRealtime(2f); Shot("3_curtain_on"); yield return null;
            cv.SwarmIncoming(); cv.speed = 240f; yield return new WaitForSecondsRealtime(3f);
            cv.ToggleSection(4, true);
            yield return new WaitForSecondsRealtime(10f); Shot("4_section_off"); yield return null;
            cv.ToggleSection(4, true); yield return new WaitForSecondsRealtime(3f);
            cv.TripBlower(); yield return new WaitForSecondsRealtime(2.2f); Shot("5_blower_trip"); yield return null;
            yield return new WaitForSecondsRealtime(3f); Shot("6_standby_on"); yield return null;
            cv.ResetBlowers(); cv.SetWaves(2.5);
            yield return new WaitForSecondsRealtime(8f); Shot("7_rough_sea"); yield return null;
            log.Append($"\"errors\": {errors}}}");
            File.WriteAllText(Path.Combine(dir, "stand_test.json"), log.ToString());
            yield return null; yield return null;
            Application.Quit();
        }

        /// <summary>Attract loop, then one full operator challenge played by the auto-operator (every incident answered
        /// correctly after 1.5 s), result and leaderboard. Uses a separate test leaderboard file. Saves challenge_*.png
        /// and challenge_test.json next to the executable, then quits.</summary>
        IEnumerator ChallengeTest()
        {
            string dir = Path.GetDirectoryName(Application.dataPath);
            void Shot(string n) => ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"challenge_{n}.png"));
            yield return new WaitForSecondsRealtime(4f);
            var ch = Challenge.I; ch.UseBoardFile("leaderboard_test.json");
            ch.StartAttract();
            yield return new WaitForSecondsRealtime(3f); Shot("1_attract_closeup"); yield return null;
            yield return new WaitForSecondsRealtime(10f); Shot("2_attract_alarm"); yield return null;
            ch.ShowIntro(); yield return new WaitForSecondsRealtime(1.5f); Shot("3_intro"); yield return null;
            ch.AutoPlay = true; ch.StartGame();
            while (ch.State != Challenge.Phase.Playing) yield return null;
            int shots = 0; string seen = null;
            while (ch.State == Challenge.Phase.Playing)
            {
                if (ch.CurrentIncident != null && ch.CurrentIncident != seen) { seen = ch.CurrentIncident; yield return new WaitForSecondsRealtime(0.8f); if (shots < 3) Shot($"4_incident_{++shots}"); }
                yield return null;
            }
            while (ch.State != Challenge.Phase.Result && ch.State != Challenge.Phase.Board) yield return null;
            yield return new WaitForSecondsRealtime(1f); Shot("5_result"); yield return null;
            ch.SaveName(); yield return new WaitForSecondsRealtime(1f); Shot("6_board"); yield return null;
            File.WriteAllText(Path.Combine(dir, "challenge_test.json"), string.Format(CultureInfo.InvariantCulture,
                "{{\"score\": {0}, \"avg_mw\": {1:F0}, \"bonus\": {2}, \"handled\": {3}, \"incidents\": {4}, \"avg_fps_note\": \"see bench\", \"errors\": {5}}}",
                ch.Score, ch.AvgMW, ch.Bonus, ch.Handled, ch.Incidents, errors));
            yield return null; yield return null;
            Application.Quit();
        }

        /// <summary>Plays the live operation end to end (phases advance on their own), then "what if there was no curtain?".
        /// Saves op_*.png during each phase and op_test.json next to the executable, then quits.</summary>
        IEnumerator OperationTest()
        {
            string dir = Path.GetDirectoryName(Application.dataPath);
            void Shot(string n) => ScreenCapture.CaptureScreenshot(Path.Combine(dir, (Array.IndexOf(Environment.GetCommandLineArgs(), "-early") >= 0 ? "opE_" : "op_") + n + ".png"));
            yield return new WaitForSecondsRealtime(4f);
            var op = Operation.I; float t0 = Time.realtimeSinceStartup;
            op.Begin(Array.IndexOf(Environment.GetCommandLineArgs(), "-early") >= 0);
            int last = -1; float phaseStart = 0;
            var times = new System.Text.StringBuilder(); var fps = new System.Text.StringBuilder();
            int frames = 0; float fpsT0 = Time.realtimeSinceStartup;
            while (op.Phase < 6 && Time.realtimeSinceStartup - t0 < 400f)
            {
                frames++;
                if (op.Phase != last)
                {
                    if (last >= 0) fps.Append($"{last + 1}:{frames / Mathf.Max(0.01f, Time.realtimeSinceStartup - fpsT0):F0} ");
                    frames = 0; fpsT0 = Time.realtimeSinceStartup;
                    last = op.Phase; phaseStart = Time.realtimeSinceStartup; times.Append($"{op.Phase + 1}:{phaseStart - t0:F0} ");
                }
                else if (phaseStart > 0 && Time.realtimeSinceStartup - phaseStart > (op.Phase == 2 ? 9f : 6f)) { Shot($"{op.Phase + 1}"); phaseStart = -1; }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(3f); Shot("7"); yield return null;
            var cv = FindFirstObjectByType<CurtainView>();
            string clear = string.Format(CultureInfo.InvariantCulture, "\"held\": {0:F3}, \"released\": {1}, \"units_with_curtain\": {2}, ", cv.Sim.HeldShare, cv.Released, cv.UnitsOnline);
            op.SendMessage("StartWithoutForTest");
            yield return new WaitForSecondsRealtime(20f); Shot("8_without"); yield return null;
            while (!op.PhaseDone && Time.realtimeSinceStartup - t0 < 500f) yield return null;
            yield return new WaitForSecondsRealtime(1f); Shot("9_without_end"); yield return null;
            File.WriteAllText(Path.Combine(dir, op.Early ? "op_test_early.json" : "op_test.json"), "{" + clear + string.Format(CultureInfo.InvariantCulture,
                "\"units_without\": {0}, \"passed_without\": {1}, \"phase_start_s\": \"{2}\", \"phase_avg_fps\": \"{5}\", \"total_s\": {3:F0}, \"voice_lines\": {6}, \"voice_missing\": {7}, \"sounds\": \"{8}\", \"errors\": {4}}}",
                cv.UnitsOnline, cv.Sim.Entered, times.ToString().Trim(), Time.realtimeSinceStartup - t0, errors, fps.ToString().Trim(),
                StandAudio.VoiceLines, StandAudio.VoiceMissing, string.Join(", ", System.Linq.Enumerable.Select(StandAudio.Counts, kv => kv.Key + " x" + kv.Value))));
            yield return null; yield return null;
            Application.Quit();
        }

        /// <summary>Clicks every scenario button and both sliders the way a visitor would (same methods the UI calls),
        /// lets each run 15 s, and records what changed: air, approach, bubbles on, held share, passed. Saves sc_*.png and
        /// scenario_test.json next to the executable, then quits.</summary>
        IEnumerator ScenarioTest()
        {
            string dir = Path.GetDirectoryName(Application.dataPath);
            var cv = FindFirstObjectByType<CurtainView>();
            var sb = new System.Text.StringBuilder("{\"scenarios\": [");
            yield return new WaitForSecondsRealtime(4f);
            string Row(string name, string banner = null)
            {
                int on = 0; foreach (var s in cv.Sim.SectionOn) if (s) on++;
                return string.Format(CultureInfo.InvariantCulture,
                    "{{\"name\": \"{0}\", \"air\": {1:F2}, \"approach\": {2:F3}, \"sections_on\": {3}, \"held\": {4:F3}, \"passed\": {5}, \"units\": {6}, \"banner\": \"{7}\"}}",
                    name, cv.Sim.AirflowLps, cv.Sim.ApproachSpeed, on, cv.Sim.HeldShare, cv.Sim.Entered, cv.UnitsOnline, (banner ?? cv.AlertText).Replace("\"", "'"));
            }
            for (int k = 0; k < Qandeel.Core.Scenario.All.Length; k++)
            {
                cv.LoadScenario(k);
                yield return new WaitForSecondsRealtime(15f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"sc_{k}.png")); yield return null;
                sb.Append(Row(Qandeel.Core.Scenario.All[k].Name)).Append(", ");
            }
            cv.LoadScenario(0); yield return new WaitForSecondsRealtime(3f);
            cv.SetAirflow(1.0); yield return new WaitForSecondsRealtime(3f); string bannerAir = cv.AlertText; yield return new WaitForSecondsRealtime(12f);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "sc_air_1.png")); yield return null;
            sb.Append(Row("slider: air 1.0", bannerAir)).Append(", ");
            cv.LoadScenario(0); yield return new WaitForSecondsRealtime(3f);
            cv.SetApproach(0.35); yield return new WaitForSecondsRealtime(3f); string bannerAppr = cv.AlertText; yield return new WaitForSecondsRealtime(12f);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "sc_approach_035.png")); yield return null;
            sb.Append(Row("slider: approach 0.35", bannerAppr)).Append(", ");
            cv.LoadScenario(0); cv.SetHighAir(true); yield return new WaitForSecondsRealtime(3f);
            sb.Append(Row("high air"));
            sb.Append($"], \"errors\": {errors}}}");
            File.WriteAllText(Path.Combine(dir, "scenario_test.json"), sb.ToString());
            yield return null; yield return null;
            Application.Quit();
        }

        /// <summary>Stand soak test: runs `minutes`, attract-mode tour loops on its own; every minute logs FPS,
        /// memory and error count to soak.csv next to the executable; changes scenario every 10 minutes.</summary>
        IEnumerator Soak(int minutes)
        {
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "soak.csv");
            File.WriteAllText(path, "minute,avg_fps,min_fps,mono_mb,total_alloc_mb,errors,startup_s\n");
            var cv = FindFirstObjectByType<CurtainView>();
            for (int m = 1; m <= minutes; m++)
            {
                int frames = 0; float worst = 0f, t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 60f) { frames++; worst = Mathf.Max(worst, Time.unscaledDeltaTime); yield return null; }
                File.AppendAllText(path, string.Format(CultureInfo.InvariantCulture, "{0},{1:F1},{2:F1},{3:F0},{4:F0},{5},{6:F1}\n", m, frames / 60f,
                    1f / Mathf.Max(worst, 1e-4f), UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576.0,
                    UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576.0, errors, startupS));
                if (cv != null && m % 10 == 0) cv.Load((m / 10) % Qandeel.Core.Scenario.All.Length);
            }
            Application.Quit();
        }

        IEnumerator Run()
        {
            // Views first (swarm still approaching the curtain), then the FPS measurement
            string dir0 = Path.GetDirectoryName(Application.dataPath);
            var cv = FindFirstObjectByType<CurtainView>();
            if (cv != null && cv.views != null)
            {
                cv.speed = 120f;
                for (int v = 0; v < cv.views.Length; v++)
                {
                    cv.SetView(v);
                    yield return new WaitForSecondsRealtime(v == 0 ? WarmupS : 3f);
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir0, $"view{v + 1}.png"));
                    yield return null; yield return null;
                }
                var room = FindFirstObjectByType<ControlRoom>();
                if (room != null)
                {
                    room.StartTour();
                    yield return new WaitForSecondsRealtime(9f); // step 2 (forecast) after its camera move
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir0, "tour.png"));
                    yield return null; yield return null;
                    room.StopTour();
                    foreach (var p in new[] { "Forecast", "AI camera", "Cost", "Equipment", "Evidence" })
                    {
                        room.ShowPanel(p);
                        yield return new WaitForSecondsRealtime(1f);
                        ScreenCapture.CaptureScreenshot(Path.Combine(dir0, $"panel_{p.Replace(' ', '_')}.png"));
                        yield return null; yield return null;
                    }
                    room.ShowPanel("");
                }
                cv.Load(0); cv.speed = 600f;
                float tw = Time.realtimeSinceStartup;
                while (!cv.BoatStatus.StartsWith("Sweeping") && Time.realtimeSinceStartup - tw < 30f) yield return null;
                cv.speed = 60f; cv.FollowBoat = true;
                yield return new WaitForSecondsRealtime(4f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir0, "sweep.png"));
                yield return null; yield return null;
                cv.speed = 600f; tw = Time.realtimeSinceStartup;
                while (!cv.BoatTowing && Time.realtimeSinceStartup - tw < 30f) yield return null;
                yield return new WaitForSecondsRealtime(3f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir0, "tow.png"));
                yield return null; yield return null;
                cv.FollowBoat = false; cv.SetView(1); cv.speed = 600f;
            }
            yield return new WaitForSecondsRealtime(2f);
            int frames = 0; float worst = 0f, t0 = Time.realtimeSinceStartup;
            var dts = new System.Collections.Generic.List<float>();
            while (Time.realtimeSinceStartup - t0 < MeasureS)
            {
                yield return null;
                frames++; worst = Mathf.Max(worst, Time.unscaledDeltaTime); dts.Add(Time.unscaledDeltaTime);
            }
            float total = 0f; foreach (var d in dts) total += d;
            float avg = frames / total; // from frame times, so a paused window cannot skew it
            dts.Sort();
            float median = 1f / dts[dts.Count / 2], p1low = 1f / dts[(int)(dts.Count * 0.99f)];
            int hitches = dts.FindAll(d => d > 0.05f).Count; // frames over 50 ms
            string dir = Path.GetDirectoryName(Application.dataPath);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "benchmark.png"));
            yield return null; yield return null;
            var inv = CultureInfo.InvariantCulture;
            File.WriteAllText(Path.Combine(dir, "benchmark.json"),
                "{" + string.Format(inv, "\"avg_fps\": {0:F1}, \"median_fps\": {6:F1}, \"1pct_low_fps\": {7:F1}, \"hitches_over_50ms\": {8}, \"startup_s\": {9:F1}, \"errors\": {10}, \"tier\": \"{11}\",\"worst_frame_ms\": {1:F1}, \"width\": {2}, \"height\": {3}, \"gpu\": \"{4}\", \"quality\": \"{5}\"",
                    avg, worst * 1000f, Screen.width, Screen.height, SystemInfo.graphicsDeviceName, QualitySettings.names[QualitySettings.GetQualityLevel()],
                    median, p1low, hitches, startupS, errors,
                    FindFirstObjectByType<GraphicsTier>() is GraphicsTier gt ? GraphicsTier.Names[gt.Tier] : "?") + "}");
            Application.Quit();
        }

        void OnGUI()
        {
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(Screen.width / 2f - 60f, Screen.height - 40f, 160, 36), $"{smoothed:F0} FPS", style);
        }
    }
}
