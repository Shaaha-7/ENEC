using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Qandeel.EditorTools
{
    /// <summary>Art pass for the Barakah scene: Gulf water, curtain foam, workboat, plant massing, underwater section.
    /// Dimensions from the design files; anything not specified is labelled a design assumption or illustrative.</summary>
    public static class VisualBuilder
    {
        public static Material Lit(string name, Color c, float smooth = 0.5f, float metal = 0f, bool transparent = false)
        {
            var m = new Material(Shader.Find("HDRP/Lit")) { enableInstancing = true };
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            if (transparent) { m.SetFloat("_SurfaceType", 1f); m.SetFloat("_BlendMode", 0f); }
            HDMaterial.ValidateMaterial(m);
            AssetDatabase.CreateAsset(m, $"Assets/Site/{name}.mat");
            return m;
        }

        /// <summary>Clear, shallow Gulf water: turquoise scattering, long absorption, caustics, Hs ~0.5 m sea, foam on.</summary>
        public static void Water(WaterSurface w, Transform decalAnchor)
        {
            w.largeWindSpeed = 14f;           // km/h: light breeze, sea Hs ~0.4-0.8 m (film prompt)
            w.largeChaos = 0.7f;
            w.scatteringColor = new Color(0.05f, 0.62f, 0.56f);
            w.refractionColor = new Color(0.16f, 0.72f, 0.66f);
            w.underWaterScatteringColor = new Color(0.03f, 0.35f, 0.36f);
            w.absorptionDistance = 9f;
            w.maxRefractionDistance = 3f;
            w.ambientScattering = 0.7f; w.heightScattering = 0.3f; w.displacementScattering = 0.45f; // brighter turquoise body
            w.absorptionDistance = 14f;
            w.caustics = true; w.causticsIntensity = 0.9f; w.causticsResolution = WaterSurface.WaterCausticsResolution.Caustics512;
            w.underWater = true;
            w.foam = true; w.foamPersistenceMultiplier = 0.8f; w.foamSmoothness = 0.25f;
            w.decalRegionAnchor = decalAnchor; w.decalRegionSize = new Vector2(1400f, 1400f);
        }

        /// <summary>Soft round sprite (bubble rim highlight or foam puff) generated in code and saved as an asset.</summary>
        public static Texture2D SoftTexture(string name, bool bubble)
        {
            const int n = 64; var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f, r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = bubble
                        ? Mathf.Clamp01(1f - r) * 0.25f + Mathf.Exp(-Mathf.Pow((r - 0.82f) / 0.08f, 2f)) * 0.9f   // thin bright rim
                          + Mathf.Exp(-((dx + 0.35f) * (dx + 0.35f) + (dy - 0.35f) * (dy - 0.35f)) / 0.02f) * 0.8f // specular dot
                        : Mathf.Pow(Mathf.Clamp01(1f - r), 1.6f);
                    if (r > 1f) a = 0f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
                }
            tex.Apply();
            AssetDatabase.CreateAsset(tex, $"Assets/Site/{name}.asset");
            return tex;
        }

        public static Material Sprite(string name, Texture2D tex, Color c)
        {
            var m = new Material(Shader.Find("HDRP/Unlit"));
            m.SetFloat("_SurfaceType", 1f); m.SetFloat("_BlendMode", 0f);
            m.SetTexture("_UnlitColorMap", tex); m.SetColor("_UnlitColor", c);
            HDMaterial.ValidateMaterial(m);
            AssetDatabase.CreateAsset(m, $"Assets/Site/{name}.mat");
            return m;
        }

        /// <summary>Foam trail on the water (boat wake or the drag of the net/bag): flat sprites that spread and fade.</summary>
        public static ParticleSystem FoamTrail(string name, Transform parent, Vector3 localPos, Material foam, float rate, float size, float life)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false); go.transform.localPosition = localPos;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.scalingMode = ParticleSystemScalingMode.Local;
            main.startLifetime = life; main.startSpeed = 0.3f; main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f); main.maxParticles = 4000;
            main.startColor = new Color(1f, 1f, 1f, 0.75f);
            var em = ps.emission; em.rateOverTime = rate;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = size * 0.3f; sh.rotation = new Vector3(90, 0, 0);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                                              new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.5f, 1, 3f));
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = foam; r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            return ps;
        }

        /// <summary>Upward spray where the plume breaks the surface: short droplet bursts above the boil.</summary>
        static void Spray(Transform boil, float lengthM, Material foam)
        {
            var go = new GameObject("Spray");
            go.transform.SetParent(boil, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.2f); main.gravityModifier = 0.6f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.8f); main.maxParticles = 800; main.prewarm = true;
            main.startColor = new Color(1f, 1f, 1f, 0.7f);
            var em = ps.emission; em.rateOverTime = 50f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(3f, lengthM, 0.1f);
            var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = foam; r.renderMode = ParticleSystemRenderMode.Billboard;
        }

        /// <summary>White boil over one curtain section, where the plume reaches the surface (on/off with the section).</summary>
        public static GameObject SectionBoil(Transform section, float lengthM, Material foam)
        {
            var go = new GameObject("Surface boil");
            go.transform.SetParent(section.parent, true);
            go.transform.SetPositionAndRotation(new Vector3(section.position.x, 0.15f, section.position.z),
                                                Quaternion.LookRotation(Vector3.up, section.forward));
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f); main.startSpeed = 0.25f;
            main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 3.5f); main.maxParticles = 1200;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            main.startColor = new Color(1f, 1f, 1f, 0.8f); main.prewarm = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 120f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(4f, lengthM, 0.1f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                                              new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.5f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = foam; r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            Spray(go.transform, lengthM, foam);
            return go;
        }

        // ---------- Workboat: 9.0 m aluminium, beam 3.2 m (design assumption), wheelhouse forward, A-frame and tow bitt aft
        public static GameObject Workboat()
        {
            var white = Lit("Paint_White", new Color(0.9f, 0.92f, 0.94f), 0.65f);
            var orange = Lit("Paint_Orange", new Color(1f, 0.38f, 0.04f), 0.55f);
            var steel = Lit("Steel", new Color(0.55f, 0.57f, 0.6f), 0.5f, 0.9f);
            var glass = Lit("Glass", new Color(0.05f, 0.08f, 0.1f), 0.95f, 0.2f);
            var rubber = Lit("Rubber", new Color(0.05f, 0.05f, 0.05f), 0.3f);
            var deck = Lit("Deck", new Color(0.35f, 0.37f, 0.38f), 0.3f, 0.6f);

            var root = new GameObject("Workboat (crewed, chartered, 9 m; drawn 3x)");
            Mesh(root, "Hull", HullMesh(0f), white);
            Mesh(root, "Band", HullBand(), orange);
            Mesh(root, "Deck", DeckMesh(), deck);
            Box(root, "Wheelhouse", new Vector3(0, 2.15f, 1.5f), new Vector3(2.3f, 1.9f, 2.6f), white);
            Box(root, "Windows front", new Vector3(0, 2.5f, 2.81f), new Vector3(2.1f, 0.75f, 0.03f), glass);
            Box(root, "Windows port", new Vector3(-1.16f, 2.5f, 1.5f), new Vector3(0.03f, 0.75f, 2.2f), glass);
            Box(root, "Windows stbd", new Vector3(1.16f, 2.5f, 1.5f), new Vector3(0.03f, 0.75f, 2.2f), glass);
            Box(root, "Roof", new Vector3(0, 3.15f, 1.6f), new Vector3(2.6f, 0.1f, 3.0f), white);
            Cyl(root, "Mast", new Vector3(0, 3.9f, 1.2f), 0.05f, 1.5f, steel);
            Sphere(root, "Nav light port (red)", new Vector3(-1.2f, 3.25f, 2.6f), 0.12f, Lit("NavRed", Color.red, 0.9f));
            Sphere(root, "Nav light stbd (green)", new Vector3(1.2f, 3.25f, 2.6f), 0.12f, Lit("NavGreen", Color.green, 0.9f));
            // A-frame aft
            var af = new GameObject("A-frame"); af.transform.SetParent(root.transform, false);
            Cyl(af, "Leg port", new Vector3(-1.2f, 2.2f, -4.0f), 0.08f, 2.2f, orange, new Vector3(0, 0, -8));
            Cyl(af, "Leg stbd", new Vector3(1.2f, 2.2f, -4.0f), 0.08f, 2.2f, orange, new Vector3(0, 0, 8));
            Cyl(af, "Beam", new Vector3(0, 3.3f, -4.0f), 0.08f, 2.3f, orange, new Vector3(0, 0, 90));
            Cyl(root, "Tow bitt", new Vector3(0, 1.55f, -2.8f), 0.15f, 0.35f, steel);
            Cyl(root, "Tow bitt cap", new Vector3(0, 1.75f, -2.8f), 0.24f, 0.05f, steel);
            // Railings and fendering
            for (int side = -1; side <= 1; side += 2)
            {
                for (float z = -4.2f; z <= 0.2f; z += 1.1f) Cyl(root, "Stanchion", new Vector3(side * 1.5f, 1.75f, z), 0.025f, 0.5f, steel);
                Cyl(root, "Rail", new Vector3(side * 1.5f, 2.0f, -2.0f), 0.03f, 2.3f, steel, new Vector3(90, 0, 0));
                Cyl(root, "Fender", new Vector3(side * 1.63f, 0.85f, -0.3f), 0.12f, 3.6f, rubber, new Vector3(90, 0, 0));
            }
            Cyl(root, "Liferaft canister", new Vector3(0.9f, 3.35f, 0.6f), 0.25f, 0.35f, white, new Vector3(0, 0, 90));
            Box(root, "Tow point (empty node)", new Vector3(0, 1.6f, -4.4f), Vector3.one * 0.01f, steel);
            return root;
        }

        static float HalfBeam(float z) // z: -4.5 stern .. +4.5 bow
        {
            if (z < 2f) return 1.6f - 0.08f * Mathf.Abs(z) / 4.5f;
            float u = (z - 2f) / 2.5f; return 1.58f * Mathf.Sqrt(Mathf.Max(0f, 1f - u * u));
        }

        static float Sheer(float z) => 1.2f + 0.45f * Mathf.Pow(Mathf.Max(0f, (z + 1f) / 5.5f), 2f);

        static Mesh HullMesh(float _)
        {
            int st = 30; var v = new List<Vector3>(); var t = new List<int>();
            // section points (half): keel, chine, deck edge; mirrored for the other side
            for (int i = 0; i <= st; i++)
            {
                float z = -4.5f + 9f * i / st, b = HalfBeam(z), d = Sheer(z);
                float keel = -0.6f + 0.35f * Mathf.Max(0, (z - 2.5f) / 2f);
                v.Add(new Vector3(0, keel, z));
                v.Add(new Vector3(b * 0.88f, -0.12f, z)); v.Add(new Vector3(b, d, z));
                v.Add(new Vector3(-b * 0.88f, -0.12f, z)); v.Add(new Vector3(-b, d, z));
            }
            for (int i = 0; i < st; i++)
            {
                int a = i * 5, n = (i + 1) * 5;
                Quad(t, a, a + 1, n + 1, n); Quad(t, a + 1, a + 2, n + 2, n + 1);   // starboard bottom, side
                Quad(t, a, n, n + 3, a + 3); Quad(t, a + 3, n + 3, n + 4, a + 4);   // port bottom, side
            }
            // transom
            Quad(t, 0, 3, 4, 2); t.AddRange(new[] { 0, 2, 1 });
            var m = new Mesh { name = "Hull" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); return m;
        }

        static Mesh HullBand()
        {
            int st = 30; var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= st; i++)
            {
                float z = -4.5f + 9f * i / st, b = HalfBeam(z) + 0.012f, d = Sheer(z);
                v.Add(new Vector3(b, d - 0.45f, z)); v.Add(new Vector3(b, d - 0.2f, z));
                v.Add(new Vector3(-b, d - 0.45f, z)); v.Add(new Vector3(-b, d - 0.2f, z));
            }
            for (int i = 0; i < st; i++) { int a = i * 4, n = (i + 1) * 4; Quad(t, a, a + 1, n + 1, n); Quad(t, a + 2, n + 2, n + 3, a + 3); }
            var m = new Mesh { name = "Band" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); return m;
        }

        static Mesh DeckMesh()
        {
            int st = 30; var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= st; i++)
            {
                float z = -4.5f + 9f * i / st, b = HalfBeam(z) - 0.02f, d = Sheer(z) - 0.05f;
                v.Add(new Vector3(-b, d, z)); v.Add(new Vector3(b, d, z));
            }
            for (int i = 0; i < st; i++) { int a = i * 2, n = (i + 1) * 2; Quad(t, a, n, n + 1, a + 1); }
            var m = new Mesh { name = "Deck" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); return m;
        }

        static void Quad(List<int> t, int a, int b, int c, int d) { t.Add(a); t.Add(b); t.Add(c); t.Add(a); t.Add(c); t.Add(d); }

        // ---------- Plant: four APR-1400-class units at their satellite positions (illustrative massing)
        public static GameObject Plant()
        {
            var concrete = Lit("Concrete_Light", new Color(0.86f, 0.84f, 0.8f), 0.25f);
            var hall = Lit("Hall_Cladding", new Color(0.78f, 0.8f, 0.82f), 0.35f, 0.2f);
            var root = new GameObject("Barakah Units 1-4 (illustrative massing at satellite positions)");
            // Image-pixel centres of the four reactor buildings read from the Sentinel-2 scene (x = (px-1000)*10, z = (1000-py)*10)
            float[] px = { 1229f, 1253f, 1294f, 1318f }; const float py = 1254f;
            foreach (var x in px)
            {
                var c = new Vector3((x - 1000f) * 10f, 3f, (1000f - py) * 10f);
                Cyl(root, "Containment", c + Vector3.up * 26f, 23f, 26f, concrete);
                Sphere(root, "Dome", c + Vector3.up * 52f, 23f, concrete, new Vector3(1f, 0.55f, 1f));
                Box(root, "Auxiliary building", c + new Vector3(0, 15f, -45f), new Vector3(70f, 30f, 40f), concrete);
                Box(root, "Turbine hall", c + new Vector3(0, 22f, 62f), new Vector3(60f, 44f, 105f), hall);
            }
            return root;
        }

        // ---------- One curtain section in detail for the underwater cutaway: pipe on precast sleepers + bubble plume
        public static GameObject UnderwaterSection(Vector3 a, Vector3 along, float seabedY, float lengthM)
        {
            var black = Lit("HDPE_Black", new Color(0.03f, 0.03f, 0.035f), 0.45f);
            var conc = Lit("Sleeper_Concrete", new Color(0.55f, 0.54f, 0.5f), 0.15f);
            var root = new GameObject("Curtain section 5 in detail (HDPE OD200 SDR17, 0.75 m above seabed)");
            var rot = Quaternion.LookRotation(along);
            var pipeAxis = a + Vector3.up * (seabedY + 0.75f);
            var pipe = Cyl(root, "Pipe OD200", pipeAxis + along * lengthM * 0.5f, 0.1f, lengthM * 0.5f, black, Vector3.zero);
            pipe.transform.rotation = rot * Quaternion.Euler(90, 0, 0);
            // Sleeper spacing is NOT specified in the design: placed for display only, no value stated anywhere
            for (float s = 2f; s < lengthM; s += 8f)
            {
                var sl = Box(root, "Precast sleeper", a + along * s + Vector3.up * (seabedY + 0.32f), new Vector3(1.6f, 0.64f, 0.5f), conc);
                sl.transform.rotation = rot;
            }
            var feed = Cyl(root, "DN150 feed-line", a + along * 1f + Vector3.up * (seabedY + 0.4f) + Vector3.Cross(along, Vector3.up) * 3f,
                           0.08f, 3f, black, Vector3.zero);
            feed.transform.rotation = rot * Quaternion.Euler(0, 90, 90);

            // Bubble plume: bubble clusters rising ~0.3 m/s (film prompt 0.25-0.35), widening upward
            var pgo = new GameObject("Bubble plume");
            pgo.transform.SetParent(root.transform, false);
            // local z = up (emission direction), local y = along the pipe (box length)
            pgo.transform.SetPositionAndRotation(pipeAxis + Vector3.up * 0.12f + along * lengthM * 0.5f, Quaternion.LookRotation(Vector3.up, along));
            var ps = pgo.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = (-seabedY - 0.75f) / 0.3f; main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.06f); main.maxParticles = 30000;
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = 0f;
            main.startColor = new Color(0.9f, 0.97f, 1f, 0.75f);
            main.prewarm = true;
            var em = ps.emission; em.rateOverTime = 1400f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(0.05f, lengthM, 0.05f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.12f; noise.frequency = 0.4f; noise.scrollSpeed = 0.3f;
            var size = ps.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.7f, 1, 1.4f));
            var r = pgo.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Sprite("Bubble", SoftTexture("BubbleTex", true), new Color(0.9f, 0.97f, 1f, 0.9f));
            r.renderMode = ParticleSystemRenderMode.Billboard;
            return root;
        }

        // ---------- Blender models (assets/src/build_assets.py) with materials remapped to HDRP by slot name
        static Dictionary<string, Material> lib;

        static Material LibMat(string slot)
        {
            lib ??= new Dictionary<string, Material>();
            string key = slot.Split('.')[0];
            if (lib.TryGetValue(key, out var m)) return m;
            m = key switch
            {
                "Paint_White" => Lit("M_Paint_White", new Color(0.9f, 0.92f, 0.94f), 0.65f),
                "Paint_Orange" => Lit("M_Paint_Orange", new Color(1f, 0.33f, 0.03f), 0.6f),
                "Antifoul" => Lit("M_Antifoul", new Color(0.45f, 0.08f, 0.06f), 0.3f),
                "Steel" => Lit("M_Steel", new Color(0.6f, 0.62f, 0.65f), 0.6f, 0.9f),
                "Steel_Dark" => Lit("M_Steel_Dark", new Color(0.15f, 0.16f, 0.17f), 0.5f, 0.8f),
                "Glass" => Lit("M_Glass", new Color(0.03f, 0.05f, 0.07f), 0.95f, 0.3f),
                "Rubber" => Lit("M_Rubber", new Color(0.04f, 0.04f, 0.04f), 0.2f),
                "Deck" => Lit("M_Deck", new Color(0.32f, 0.34f, 0.35f), 0.3f, 0.5f),
                "NavRed" => Lit("M_NavRed", Color.red, 0.8f),
                "NavGreen" => Lit("M_NavGreen", Color.green, 0.8f),
                "Container_Paint" => Lit("M_Container", new Color(0.82f, 0.84f, 0.82f), 0.45f, 0.3f),
                "Concrete_Light" => Lit("M_Concrete", new Color(0.86f, 0.84f, 0.8f), 0.35f),
                "Hall_Cladding" => Lit("M_Hall", new Color(0.78f, 0.8f, 0.82f), 0.55f, 0.2f),
                "Roof_Grey" => Lit("M_Roof", new Color(0.55f, 0.57f, 0.6f), 0.4f),
                "Boom_Yellow" => Lit("M_Boom_Yellow", new Color(1f, 0.78f, 0.05f), 0.55f),
                "Boom_Skirt" => Lit("M_Boom_Skirt", new Color(0.95f, 0.72f, 0.05f), 0.4f),
                "Bag_Net" => Lit("M_Bag_Net", new Color(0.12f, 0.3f, 0.32f), 0.2f),
                "HDPE_Black" => Lit("M_HDPE", new Color(0.03f, 0.03f, 0.035f), 0.45f),
                "Sleeper_Concrete" => Lit("M_Sleeper", new Color(0.55f, 0.54f, 0.5f), 0.15f),
                "Hole_Dark" => Lit("M_Hole", Color.black, 0f),
                "Lantern" => Lit("M_Lantern", new Color(1f, 0.85f, 0.3f), 0.9f),
                _ => Lit("M_" + key, new Color(0.7f, 0.7f, 0.7f), 0.5f),
            };
            lib[key] = m; return m;
        }

        /// <summary>Instantiates an exported model, swaps its materials to HDRP ones by slot name, and turns it so the
        /// direction from `aftPart` to `forePart` points along +Z (bow forward / turbine hall seaward).</summary>
        public static GameObject Model(string fbxPath, string name, string aftPart, string forePart)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            var root = new GameObject(name);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.transform.SetParent(root.transform, false);
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = LibMat(mats[i] != null ? mats[i].name : "Default");
                r.sharedMaterials = mats;
            }
            Transform Find(string n) { foreach (var t in inst.GetComponentsInChildren<Transform>()) if (t.name.StartsWith(n)) return t; return null; }
            var aft = Find(aftPart); var fore = Find(forePart);
            if (aft != null && fore != null)
            {
                Vector3 C(Transform t) => t.GetComponent<Renderer>() ? t.GetComponent<Renderer>().bounds.center : t.position;
                var d = C(fore) - C(aft); d.y = 0;
                inst.transform.rotation = Quaternion.FromToRotation(d.normalized, Vector3.forward) * inst.transform.rotation;
            }
            return root;
        }

        // ---------- helpers
        static int meshCount;
        static GameObject Mesh(GameObject p, string name, Mesh mesh, Material m)
        {
            AssetDatabase.CreateAsset(mesh, $"Assets/Site/Mesh_{name}_{++meshCount}.asset");
            var go = new GameObject(name); go.transform.SetParent(p.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        public static GameObject Box(GameObject p, string name, Vector3 pos, Vector3 size, Material m)
            => Prim(p, PrimitiveType.Cube, name, pos, size, m, Vector3.zero);

        static GameObject Sphere(GameObject p, string name, Vector3 pos, float r, Material m, Vector3? squash = null)
            => Prim(p, PrimitiveType.Sphere, name, pos, Vector3.Scale(Vector3.one * r * 2f, squash ?? Vector3.one), m, Vector3.zero);

        static GameObject Cyl(GameObject p, string name, Vector3 pos, float radius, float halfHeight, Material m, Vector3? euler = null)
            => Prim(p, PrimitiveType.Cylinder, name, pos, new Vector3(radius * 2f, halfHeight, radius * 2f), m, euler ?? Vector3.zero);

        static GameObject Prim(GameObject p, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material m, Vector3 euler)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name; go.transform.SetParent(p.transform, false);
            go.transform.localPosition = pos; go.transform.localScale = scale; go.transform.localEulerAngles = euler;
            go.GetComponent<Renderer>().sharedMaterial = m;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }
    }
}
