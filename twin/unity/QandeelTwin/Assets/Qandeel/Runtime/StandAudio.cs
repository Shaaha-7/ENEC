using System.Collections.Generic;
using UnityEngine;

namespace Qandeel
{
    /// <summary>
    /// Stand sound, synthesised in code (no sound files): a sea bed, the curtain's bubbling air (follows airflow and the
    /// sections that are on), the intake alarm siren (follows the plant alarm), a turbine wind-down when a unit drops,
    /// and short UI cues. Voice lines (ElevenLabs recordings, tools/make_voice_unity.py) play through Say().
    /// </summary>
    public sealed class StandAudio : MonoBehaviour
    {
        public CurtainView view;
        public string[] voiceKeys;        // e.g. "ch_intro"; filled by SiteBuilder
        public AudioClip[] voiceClips;
                public float master = 1f;

        const int Rate = 44100;
        AudioSource sea, bubbles, siren, sfx, voice;
        AudioClip winddown, ok, bad, beep, go, whoosh;
        readonly Dictionary<string, AudioClip> lines = new Dictionary<string, AudioClip>();
        int lastUnits = 4;

        public static StandAudio I { get; private set; }

        bool sirenLogged;

        void Awake()
        {
            // safety net: the scene must have exactly one AudioListener or nothing is heard
            if (FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length == 0)
            {
                var cam = Camera.main != null ? Camera.main.gameObject : gameObject;
                cam.AddComponent<AudioListener>();
                Debug.Log("[Qandeel] audio: no AudioListener in the scene, added one to " + cam.name);
            }
            Debug.Log($"[Qandeel] audio: {AudioSettings.outputSampleRate} Hz, speaker mode {AudioSettings.speakerMode}, listeners {FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length}, volume {AudioListener.volume:F2}");
            I = this;
            if (voiceKeys != null) for (int i = 0; i < voiceKeys.Length && i < voiceClips.Length; i++) if (voiceClips[i] != null) lines[voiceKeys[i]] = voiceClips[i];
            var rnd = new System.Random(7);
            sea = Loop("sea", Noise(6f, rnd, 0.015f, 0.35f, 0.08f), 0.22f);
            bubbles = Loop("bubbles", Bubbles(4f, rnd), 0f);
            siren = Loop("siren", Siren(2f), 0f);
            sfx = Src("sfx"); voice = Src("voice"); voice.volume = 1f;
            winddown = Winddown(3.5f); ok = Chime(new[] { 660f, 990f }, 0.12f); bad = Buzz(0.45f);
            beep = Chime(new[] { 880f }, 0.12f); go = Chime(new[] { 880f, 1320f }, 0.18f); whoosh = Noise(1.2f, rnd, 0.3f, 1f, 0.5f, true);
        }

        AudioSource Src(string n) { var s = gameObject.AddComponent<AudioSource>(); s.playOnAwake = false; s.spatialBlend = 0f; return s; }
        AudioSource Loop(string n, AudioClip c, float vol) { var s = Src(n); s.clip = c; s.loop = true; s.volume = vol; s.Play(); return s; }

        void Update()
        {
            if (view == null || view.Sim == null) return;
            float dt = Time.unscaledDeltaTime;
            // Bubbles: share of sections on x airflow; silent while the blowers are down (fx down) or the curtain is off
            int on = 0; foreach (var s in view.Sim.SectionOn) if (s) on++;
            float air = Mathf.Clamp01((float)view.Sim.AirflowLps / 3f) * on / view.Sim.SectionOn.Length;
            float near = Mathf.Clamp01(1.2f - Vector3.Distance(Camera.main.transform.position, view.tipA * 0.5f + view.tipB * 0.5f) / 2500f);
            bubbles.volume = Mathf.MoveTowards(bubbles.volume, master * air * (0.18f + 0.35f * near), dt * 0.6f);
            sea.volume = master * 0.2f;
            if (siren.volume > 0.05f && !sirenLogged) { sirenLogged = true; Played("siren"); Debug.Log("[Qandeel] siren sounding"); }
            if (siren.volume < 0.01f) sirenLogged = false;
            siren.volume = Mathf.MoveTowards(siren.volume, view.UnitsOnline < Qandeel.Core.Operations.PlantUnits ? master * 0.32f : 0f, dt * 1.5f);
            int u = view.UnitsOnline;
            if (u < lastUnits) sfx.PlayOneShot(winddown, master * 0.9f);
            lastUnits = u;
        }

        public void Ok() { sfx.PlayOneShot(ok, master * 0.6f); Played("sfx ok"); }
        public void Bad() { sfx.PlayOneShot(bad, master * 0.5f); Played("sfx alert"); }
        public void Beep() { sfx.PlayOneShot(beep, master * 0.5f); Played("sfx beep"); }
        public void Go() { sfx.PlayOneShot(go, master * 0.6f); Played("sfx go"); }
        public void Whoosh() { sfx.PlayOneShot(whoosh, master * 0.35f); Played("sfx whoosh"); }

        // audio log for the automated run-through: what played, and the totals
        public static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
        public static int VoiceLines, VoiceMissing;
        static void Played(string what) { Counts[what] = Counts.TryGetValue(what, out int n) ? n + 1 : 1; }
        public bool Speaking => voice.isPlaying;

        /// <summary>Plays a voice line by key (e.g. "ch_intro"); returns its length in seconds, 0 if not recorded.</summary>
        public float Say(string key, bool interrupt = true)
        {
            if (!lines.TryGetValue(key, out var c)) { VoiceMissing++; Debug.LogWarning("[Qandeel] voice line missing: " + key); return 0f; }
            if (voice.isPlaying && !interrupt) return 0f;
            voice.Stop(); voice.clip = c; voice.volume = master; voice.Play();
            VoiceLines++; Played("voice " + key); Debug.Log($"[Qandeel] voice {key} ({c.length:F1} s, loaded {c.loadState})");
            return c.length;
        }
        public void StopVoice() => voice.Stop();

        // ---- synthesis
        static AudioClip Make(string n, float[] d) { var c = AudioClip.Create(n, d.Length, 1, Rate, false); c.SetData(d, 0); return c; }

        /// <summary>Low-passed noise (sea wash): a = smoothing, swell modulation depth, gain; fade = one-shot envelope.</summary>
        static AudioClip Noise(float sec, System.Random r, float a, float swell, float gain, bool fade = false)
        {
            int n = (int)(sec * Rate); var d = new float[n]; float y = 0, y2 = 0;
            for (int i = 0; i < n; i++)
            {
                y += a * ((float)r.NextDouble() * 2f - 1f - y); y2 += a * (y - y2);
                float t = i / (float)n;
                float env = fade ? Mathf.Sin(t * Mathf.PI) : 1f - swell * 0.5f * (1f + Mathf.Sin(t * Mathf.PI * 2f));
                d[i] = y2 * env * gain * 12f;
            }
            return Make("noise", fade ? d : Seam(d));
        }

        /// <summary>Rising air: a soft roar plus many short pitched "blups" at random times.</summary>
        static AudioClip Bubbles(float sec, System.Random r)
        {
            int n = (int)(sec * Rate); var d = new float[n]; float y = 0;
            for (int i = 0; i < n; i++) { y += 0.08f * ((float)r.NextDouble() * 2f - 1f - y); d[i] = y * 0.6f; }
            for (int b = 0; b < (int)(sec * 70); b++)
            {
                int s0 = r.Next(n); float f0 = 300f + (float)r.NextDouble() * 900f, len = 0.02f + (float)r.NextDouble() * 0.05f, amp = 0.05f + (float)r.NextDouble() * 0.12f;
                int m = (int)(len * Rate); float ph = 0;
                for (int k = 0; k < m; k++)
                {
                    float t = k / (float)m; ph += 2f * Mathf.PI * f0 * (1f + 0.8f * t) / Rate;
                    d[(s0 + k) % n] += amp * Mathf.Sin(ph) * Mathf.Exp(-5f * t) * Mathf.Min(1f, k / 40f);
                }
            }
            return Make("bubbles", Seam(d));
        }

        /// <summary>Two-tone industrial alarm sweep, 1 s period.</summary>
        static AudioClip Siren(float sec)
        {
            int n = (int)(sec * Rate); var d = new float[n]; float ph = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate; float f = 600f + 350f * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * t));
                ph += 2f * Mathf.PI * f / Rate;
                d[i] = 0.35f * (Mathf.Sin(ph) + 0.3f * Mathf.Sin(ph * 2f) + 0.15f * Mathf.Sin(ph * 3f));
            }
            return Make("siren", d);
        }

        /// <summary>Turbine-generator coasting down: falling harmonic hum plus a low thump.</summary>
        static AudioClip Winddown(float sec)
        {
            int n = (int)(sec * Rate); var d = new float[n]; float ph = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n; float f = 180f * Mathf.Pow(0.18f, t);
                ph += 2f * Mathf.PI * f / Rate;
                float env = Mathf.Min(1f, i / 2000f) * (1f - t);
                d[i] = env * (0.4f * Mathf.Sin(ph) + 0.25f * Mathf.Sin(ph * 2f) + 0.15f * Mathf.Sin(ph * 4.01f))
                     + 0.5f * Mathf.Sin(2f * Mathf.PI * 45f * i / Rate) * Mathf.Exp(-i / (0.15f * Rate));
            }
            return Make("winddown", d);
        }

        static AudioClip Chime(float[] notes, float each)
        {
            int m = (int)(each * Rate), n = m * notes.Length + Rate / 4; var d = new float[n];
            for (int j = 0; j < notes.Length; j++)
                for (int k = 0; k < m + Rate / 4 && j * m + k < n; k++)
                {
                    float t = k / (float)Rate;
                    d[j * m + k] += 0.4f * Mathf.Sin(2f * Mathf.PI * notes[j] * t) * Mathf.Exp(-t * 9f) * Mathf.Min(1f, k / 100f);
                }
            return Make("chime", d);
        }

        static AudioClip Buzz(float sec)
        {
            int n = (int)(sec * Rate); var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, s = Mathf.Sin(2f * Mathf.PI * 140f * t);
                d[i] = 0.35f * Mathf.Sign(s) * (1f - i / (float)n) * Mathf.Min(1f, i / 300f);
            }
            return Make("buzz", d);
        }

        /// <summary>Crossfade the last 0.1 s into the start so the loop has no click.</summary>
        static float[] Seam(float[] d)
        {
            int f = Rate / 10;
            for (int i = 0; i < f; i++) { float w = i / (float)f; d[i] = d[i] * w + d[d.Length - f + i] * (1f - w); }
            System.Array.Resize(ref d, d.Length - f); // the tail now lives in the faded head
            return d;
        }
    }
}
