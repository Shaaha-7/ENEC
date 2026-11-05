using System.Collections.Generic;
using UnityEngine;

namespace Qandeel
{
    /// <summary>
    /// Procedural Blue Blubber (Catostylus mosaicus): smoothly domed bell 0.38 m across, NO marginal tentacles, and eight
    /// thick frilly oral arms ending in club-like tips (PROMPT_3D_MODELS.md 4.6). Origin at the bell apex, y up, metres.
    /// </summary>
    public static class JellyfishMesh
    {
        public const int Frames = 6;

        /// <summary>One frame of the swim cycle: phase 0..1 drives the bell pulse (contract/relax) and the arm sway.</summary>
        public static Mesh Build(float bellDiameter = 0.38f, float phase = 0f)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            float w = Mathf.Sin(phase * Mathf.PI * 2f);
            float R = bellDiameter * 0.5f * (1f - 0.08f * w), H = R * 0.7f * (1f + 0.12f * w);
            int seg = 32, rings = 10;

            // Bell: domed outer surface, slight flare at the rim, and a shallow inner (subumbrella) surface
            int outerStart = v.Count;
            for (int r = 0; r <= rings; r++)
            {
                float a = r / (float)rings;                      // 0 apex .. 1 rim
                float rad = R * Mathf.Sin(a * Mathf.PI * 0.5f) * (1f + 0.06f * a * a);
                float y = -H * (1f - Mathf.Cos(a * Mathf.PI * 0.5f)) - 0.02f * a * a * a;
                for (int s = 0; s < seg; s++)
                {
                    float ang = s * Mathf.PI * 2f / seg;
                    float lobe = 1f + 0.025f * a * Mathf.Cos(ang * 16f); // fine marginal lappets, not tentacles
                    v.Add(new Vector3(Mathf.Cos(ang) * rad * lobe, y, Mathf.Sin(ang) * rad * lobe));
                }
            }
            Grid(t, outerStart, rings, seg, false);
            int innerStart = v.Count;
            for (int r = 0; r <= rings; r++)
            {
                float a = r / (float)rings;
                float rad = R * 0.97f * a;
                float y = -H * 0.55f - (H * 0.45f + 0.02f) * a * a;
                for (int s = 0; s < seg; s++)
                {
                    float ang = s * Mathf.PI * 2f / seg;
                    v.Add(new Vector3(Mathf.Cos(ang) * rad, y, Mathf.Sin(ang) * rad));
                }
            }
            Grid(t, innerStart, rings, seg, true);

            // Eight oral arms: thick, frilly (radius ripples along the length), club-like tips
            float armLen = bellDiameter * 0.85f;
            for (int k = 0; k < 8; k++)
            {
                float ang = k * Mathf.PI * 2f / 8f + 0.2f;
                var root = new Vector3(Mathf.Cos(ang) * R * 0.18f, -H * 0.6f, Mathf.Sin(ang) * R * 0.18f);
                var outDir = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
                int ringN = 14, sides = 8, start = v.Count;
                for (int i = 0; i <= ringN; i++)
                {
                    float u = i / (float)ringN;
                    var side0 = Vector3.Cross(outDir, Vector3.up).normalized;
                    float sway = Mathf.Sin(phase * Mathf.PI * 2f - u * 3f + k) * 0.08f * u * bellDiameter; // wave travels down the arm
                    var c = root + Vector3.down * armLen * u + outDir * (0.06f * Mathf.Sin(u * Mathf.PI) * bellDiameter - 0.05f * w * u * bellDiameter)
                            + side0 * sway;
                    float rr = bellDiameter * (0.045f - 0.02f * u) * (1f + 0.35f * Mathf.Sin(u * 40f + k)); // frills
                    if (u > 0.82f) rr *= 1.6f;                                                            // club tip
                    for (int s = 0; s < sides; s++)
                    {
                        float b = s * Mathf.PI * 2f / sides;
                        var side = Vector3.Cross(outDir, Vector3.up).normalized;
                        v.Add(c + (outDir * Mathf.Cos(b) + side * Mathf.Sin(b)) * rr);
                    }
                }
                Grid(t, start, ringN, sides, false);
            }

            var m = new Mesh { name = "Blue Blubber (Catostylus mosaicus)" };
            m.SetVertices(v); m.SetTriangles(t, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static void Grid(List<int> t, int start, int rings, int seg, bool flip)
        {
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = start + r * seg + s, b = start + r * seg + (s + 1) % seg;
                    int c = start + (r + 1) * seg + s, d = start + (r + 1) * seg + (s + 1) % seg;
                    if (!flip) { t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d); }
                    else { t.Add(a); t.Add(b); t.Add(c); t.Add(b); t.Add(d); t.Add(c); }
                }
        }
    }
}
