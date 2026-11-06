using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Qandeel
{
    /// <summary>
    /// Two art-directed looks (PROMPT.md 7: "golden hour or bright Gulf midday"). Bright day (default, for a lit event
    /// hall): high white sun, brighter exposure, turquoise water, light vignette. Evening: low warm sun, warm grade, soft
    /// bloom and haze. Key L switches.
    /// </summary>
    public sealed class LightingMood : MonoBehaviour
    {
        public Light sun;
        public Volume volume;
        public bool golden = false;   // bright day by default
        WaterSurface sea;

        void Start() => Apply(golden);
        void Update() { if (Input.GetKeyDown(KeyCode.L)) Apply(!golden); }

        public void Apply(bool goldenHour)
        {
            golden = goldenHour;
            if (sea == null) sea = FindFirstObjectByType<WaterSurface>();
            if (sea != null)
            {
                sea.scatteringColor = goldenHour ? new Color(0.05f, 0.62f, 0.56f) : new Color(0.08f, 0.78f, 0.72f);
                sea.refractionColor = goldenHour ? new Color(0.16f, 0.72f, 0.66f) : new Color(0.24f, 0.86f, 0.8f);
                sea.ambientScattering = goldenHour ? 0.7f : 1.0f;
            }
            if (sun != null)
            {
                // light travels toward azimuth + 180: golden = sun 11 deg up in the WSW (~250 deg); midday = 50 deg up, SSW
                // warm late afternoon, not sunset: sun 24 deg up keeps the water turquoise
                sun.transform.rotation = goldenHour ? Quaternion.Euler(24f, 70f, 0f) : Quaternion.Euler(62f, 30f, 0f);
                sun.colorTemperature = goldenHour ? 4700f : 6000f;
                sun.intensity = goldenHour ? 85000f : 100000f;
            }
            var p = volume != null ? volume.sharedProfile : null;
            if (p == null) return;
            if (p.TryGet(out Exposure e)) e.fixedExposure.value = goldenHour ? 13.4f : 13.0f;   // lower EV = brighter picture
            if (p.TryGet(out ColorAdjustments c)) { c.contrast.value = goldenHour ? 12f : 16f; c.saturation.value = goldenHour ? 8f : 18f; }
            if (p.TryGet(out Vignette v)) v.intensity.value = goldenHour ? 0.22f : 0.1f;
            if (p.TryGet(out WhiteBalance w)) w.temperature.value = goldenHour ? 5f : 0f;
            if (p.TryGet(out Bloom b)) b.intensity.value = goldenHour ? 0.15f : 0.1f;
            if (p.TryGet(out Fog f)) f.meanFreePath.value = goldenHour ? 12000f : 20000f;
            Debug.Log("[Qandeel] lighting: " + (goldenHour ? "evening" : "bright day"));
        }
    }
}
