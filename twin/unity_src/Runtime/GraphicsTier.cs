using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Qandeel
{
    /// <summary>
    /// High / Medium / Low graphics. Starts on High, measures FPS for a few seconds and steps down while it is below
    /// 55 FPS (prompt: >= 60 High, >= 30 Low). Key G or the UI button cycles it by hand; -tier 0|1|2 forces it.
    /// Low turns off volumetric fog, caustics, water tessellation, SSR/SSAO/contact shadows and halves particles.
    /// </summary>
    public sealed class GraphicsTier : MonoBehaviour
    {
        public static readonly string[] Names = { "High", "Medium", "Low" };
        public int Tier { get; private set; }
        public bool Auto { get; private set; } = true;

        readonly Dictionary<ParticleSystem, float> baseRates = new Dictionary<ParticleSystem, float>();

        IEnumerator Start()
        {
            foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None)) baseRates[ps] = ps.emission.rateOverTimeMultiplier;
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-tier");
            if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int forced)) { Auto = false; Apply(forced); yield break; }
            Apply(0);
            yield return new WaitForSecondsRealtime(4f); // shader warm-up
            while (Auto && Tier < 2)
            {
                int frames = 0; float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 3f) { frames++; yield return null; }
                if (frames / 3f >= 55f) break;
                Apply(Tier + 1);
                Debug.Log($"[Qandeel] graphics auto-switched to {Names[Tier]} ({frames / 3f:F0} FPS)");
                yield return new WaitForSecondsRealtime(2f);
            }
        }

        void Update() { if (Input.GetKeyDown(KeyCode.G)) Cycle(); }

        public void Cycle() { Auto = false; Apply((Tier + 1) % 3); }

        public void Apply(int tier)
        {
            Tier = Mathf.Clamp(tier, 0, 2);
            bool high = Tier == 0, low = Tier == 2;

            foreach (var w in FindObjectsByType<WaterSurface>(FindObjectsSortMode.None))
            {
                w.caustics = !low; w.tessellation = !low; w.maxTessellationFactor = high ? 3f : 1.5f;
            }
            foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
                if (v.sharedProfile != null && v.sharedProfile.TryGet(out Fog fog)) fog.enableVolumetricFog.value = high;

            var cam = Camera.main != null ? Camera.main.GetComponent<HDAdditionalCameraData>() : null;
            if (cam != null)
            {
                cam.customRenderingSettings = !high;
                var fs = cam.renderingPathCustomFrameSettings; var mask = cam.renderingPathCustomFrameSettingsOverrideMask;
                void Set(FrameSettingsField f, bool on) { mask.mask[(uint)f] = true; fs.SetEnabled(f, on); }
                Set(FrameSettingsField.SSR, !low); Set(FrameSettingsField.SSAO, !low);
                Set(FrameSettingsField.ContactShadows, high); Set(FrameSettingsField.Volumetrics, high);
                cam.renderingPathCustomFrameSettings = fs; cam.renderingPathCustomFrameSettingsOverrideMask = mask;
            }

            foreach (var kv in baseRates)
            {
                if (kv.Key == null) continue;
                var em = kv.Key.emission; em.rateOverTimeMultiplier = kv.Value * (low ? 0.5f : 1f);
            }
        }
    }
}
