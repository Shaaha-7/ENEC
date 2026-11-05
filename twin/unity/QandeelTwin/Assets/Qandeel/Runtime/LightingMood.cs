using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Qandeel
{
    /// <summary>
    /// Two art-directed looks (PROMPT.md 7: "golden hour or bright Gulf midday"). Golden hour: low warm sun from the
    /// west-south-west, warm grade, soft bloom and haze. Midday: high white sun, neutral grade. Key L switches.
    /// </summary>
    public sealed class LightingMood : MonoBehaviour
    {
        public Light sun;
        public Volume volume;
        public bool golden = true;

        void Start() => Apply(golden);
        void Update() { if (Input.GetKeyDown(KeyCode.L)) Apply(!golden); }

        public void Apply(bool goldenHour)
        {
            golden = goldenHour;
            if (sun != null)
            {
                // light travels toward azimuth + 180: golden = sun 11 deg up in the WSW (~250 deg); midday = 50 deg up, SSW
                // warm late afternoon, not sunset: sun 24 deg up keeps the water turquoise
                sun.transform.rotation = goldenHour ? Quaternion.Euler(24f, 70f, 0f) : Quaternion.Euler(50f, 20f, 0f);
                sun.colorTemperature = goldenHour ? 4700f : 5600f;
                sun.intensity = goldenHour ? 85000f : 100000f;
            }
            var p = volume != null ? volume.sharedProfile : null;
            if (p == null) return;
            if (p.TryGet(out Exposure e)) e.fixedExposure.value = goldenHour ? 13.4f : 14f;
            if (p.TryGet(out ColorAdjustments c)) { c.contrast.value = goldenHour ? 12f : 10f; c.saturation.value = goldenHour ? 8f : 6f; }
            if (p.TryGet(out WhiteBalance w)) w.temperature.value = goldenHour ? 5f : 0f;
            if (p.TryGet(out Bloom b)) b.intensity.value = goldenHour ? 0.15f : 0.1f;
            if (p.TryGet(out Fog f)) f.meanFreePath.value = goldenHour ? 12000f : 14000f;
        }
    }
}
