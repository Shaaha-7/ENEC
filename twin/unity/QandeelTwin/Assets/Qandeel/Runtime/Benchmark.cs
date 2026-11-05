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
