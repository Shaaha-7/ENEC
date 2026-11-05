using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Qandeel.EditorTools
{
    /// <summary>
    /// Builds Assets/Scenes/Barakah.unity from unity_site/ (made by tools/make_unity_site.py): the real Sentinel-2 site as terrain,
    /// an HDRP ocean at sea level, sun, camera and the 10 curtain sections across the measured mouth.
    /// World frame: metres, origin at the mouth centre, x = east, z = north, y = up (sea level 0).
    /// Unity.exe -batchmode -quit -projectPath ... -executeMethod Qandeel.EditorTools.SiteBuilder.Build
    /// </summary>
    public static class SiteBuilder
    {
        static string SiteDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../../../unity_site"));

        [Serializable] class SiteJson
        {
            public float size_m; public int resolution; public float height_min_m, height_range_m;
            public float[] terrain_origin_xz, tip_a_xz, tip_b_xz;
        }

        [MenuItem("Qandeel/Rebuild Barakah scene")]
        public static void Build()
        {
            var site = JsonUtility.FromJson<SiteJson>(File.ReadAllText(Path.Combine(SiteDir, "site_unity.json")));
            EnsureHdrp();
            Directory.CreateDirectory("Assets/Site");
            Directory.CreateDirectory("Assets/Scenes");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Terrain from the 16-bit heightmap (row 0 = south)
            int res = site.resolution;
            var raw = File.ReadAllBytes(Path.Combine(SiteDir, "height_2049.raw"));
            var h = new float[res, res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    h[z, x] = BitConverter.ToUInt16(raw, 2 * (z * res + x)) / 65535f;
            var td = new TerrainData { heightmapResolution = res };
            td.size = new Vector3(site.size_m, site.height_range_m, site.size_m);
            td.SetHeights(0, 0, h);

            File.Copy(Path.Combine(SiteDir, "tci.jpg"), "Assets/Site/tci.jpg", true);
            AssetDatabase.ImportAsset("Assets/Site/tci.jpg");
            var imp = (TextureImporter)AssetImporter.GetAtPath("Assets/Site/tci.jpg");
            imp.maxTextureSize = 4096; imp.wrapMode = TextureWrapMode.Clamp; imp.sRGBTexture = true; imp.mipmapEnabled = true;
            imp.SaveAndReimport();
            var layer = new TerrainLayer
            {
                diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Site/tci.jpg"),
                tileSize = new Vector2(site.size_m, site.size_m), tileOffset = Vector2.zero, smoothness = 0.15f,
            };
            AssetDatabase.CreateAsset(layer, "Assets/Site/Satellite.terrainlayer");
            td.terrainLayers = new[] { layer };
            AssetDatabase.CreateAsset(td, "Assets/Site/BarakahTerrain.asset");
            var terrainGo = Terrain.CreateTerrainGameObject(td);
            terrainGo.name = "Barakah site (Sentinel-2, 1 Nov 2025; heights approximate)";
            terrainGo.transform.position = new Vector3(site.terrain_origin_xz[0], site.height_min_m, site.terrain_origin_xz[1]);
            var terrain = terrainGo.GetComponent<Terrain>();
            terrain.heightmapPixelError = 3; terrain.basemapDistance = 30000;
            var terrainMat = new Material(Shader.Find("HDRP/TerrainLit"));
            HDMaterial.ValidateMaterial(terrainMat); // script-made HDRP materials render black until their passes and keywords are set
            AssetDatabase.CreateAsset(terrainMat, "Assets/Site/TerrainLit.mat");
            terrain.materialTemplate = terrainMat;

            // HDRP ocean at sea level
            var waterGo = new GameObject("Sea (HDRP water)");
            var water = waterGo.AddComponent<WaterSurface>();
            water.surfaceType = WaterSurfaceType.OceanSeaLake;
            water.geometryType = WaterGeometryType.Infinite;

            // Sun: early afternoon on 1 Nov at 24 N, elevation ~50 deg from the south-south-west (side light for the stand view)
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            if (!sunGo.TryGetComponent<HDAdditionalLightData>(out _)) sunGo.AddComponent<HDAdditionalLightData>();
            sun.lightUnit = UnityEngine.Rendering.LightUnit.Lux;
            sun.intensity = 100000f; // bright Gulf sun
            sun.useColorTemperature = true; sun.colorTemperature = 5600f;
            sunGo.transform.rotation = Quaternion.Euler(50f, 20f, 0f);

            // Sky, exposure and haze
            var volGo = new GameObject("Sky and fog");
            var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true;
            // The profile asset must exist before its components are added, or they are not saved with it
            AssetDatabase.DeleteAsset("Assets/Site/SkyProfile.asset");
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, "Assets/Site/SkyProfile.asset");
            T Add<T>() where T : VolumeComponent
            {
                var c = profile.Add<T>(true);
                c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }
            var sky = Add<VisualEnvironment>();
            sky.skyType.overrideState = true; sky.skyType.value = (int)SkyType.PhysicallyBased;
            Add<PhysicallyBasedSky>();
            var fog = Add<Fog>();
            fog.enabled.overrideState = true; fog.enabled.value = true;
            fog.meanFreePath.overrideState = true; fog.meanFreePath.value = 14000f;
            fog.enableVolumetricFog.overrideState = true; fog.enableVolumetricFog.value = true; // light shafts underwater, haze
            var exp = Add<Exposure>();
            // Fixed exposure so the stand looks the same every time; EV 14 (near real daylight) measured best in the sweep
            exp.mode.overrideState = true; exp.mode.value = ExposureMode.Fixed;
            exp.fixedExposure.overrideState = true; exp.fixedExposure.value = 14f;
            // Cinematic grade: ACES tonemapping, contrast/saturation, warm white balance, soft bloom, light vignette
            var tm = Add<Tonemapping>(); tm.mode.overrideState = true; tm.mode.value = TonemappingMode.ACES;
            var ca = Add<ColorAdjustments>(); ca.contrast.overrideState = true; ca.saturation.overrideState = true;
            var wb = Add<WhiteBalance>(); wb.temperature.overrideState = true;
            var bl = Add<Bloom>(); bl.intensity.overrideState = true; bl.threshold.overrideState = true; bl.threshold.value = 1.2f;
            var vg = Add<Vignette>(); vg.intensity.overrideState = true; vg.intensity.value = 0.22f; vg.smoothness.overrideState = true; vg.smoothness.value = 0.5f;
            fog.albedo.overrideState = true; fog.albedo.value = new Color(1f, 0.93f, 0.82f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            vol.sharedProfile = profile;
            var mood = volGo.AddComponent<LightingMood>(); mood.sun = sun; mood.volume = vol;

            // Curtain: 10 sections between the measured tips, 5 m seaward (marker at the surface for now)
            var a = new Vector3(site.tip_a_xz[0], 0, site.tip_a_xz[1]);
            var b = new Vector3(site.tip_b_xz[0], 0, site.tip_b_xz[1]);
            var along = (b - a).normalized; var seaward = Vector3.Cross(along, Vector3.up).normalized;
            if (Vector3.Dot(seaward, Vector3.forward) < 0) seaward = -seaward; // offshore is north
            var curtain = new GameObject("Bubble curtain (10 x 96.7 m)");
            var mat = new Material(Shader.Find("HDRP/Unlit")) { color = new Color(1f, 0.55f, 0.1f) };
            HDMaterial.ValidateMaterial(mat);
            AssetDatabase.CreateAsset(mat, "Assets/Site/CurtainMarker.mat");
            Material Unlit(string name, Color c)
            {
                var m = new Material(Shader.Find("HDRP/Unlit")) { color = c, enableInstancing = true };
                HDMaterial.ValidateMaterial(m);
                AssetDatabase.CreateAsset(m, $"Assets/Site/{name}.mat");
                return m;
            }
            var offMat = Unlit("CurtainOff", new Color(0.35f, 0.35f, 0.38f));
            float len = Vector3.Distance(a, b) / 10f;
            var sections = new Renderer[10];
            for (int i = 0; i < 10; i++)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
                s.name = $"Section {i + 1}";
                s.transform.SetParent(curtain.transform);
                s.transform.position = a + along * (len * (i + 0.5f)) + seaward * 5f + Vector3.up * 0.4f;
                s.transform.rotation = Quaternion.LookRotation(along);
                s.transform.localScale = new Vector3(3f, 0.6f, len - 4f);
                sections[i] = s.GetComponent<Renderer>();
                sections[i].sharedMaterial = mat;
                UnityEngine.Object.DestroyImmediate(s.GetComponent<Collider>());
            }

            // Live curtain physics and jellyfish markers
            var view = curtain.AddComponent<CurtainView>();
            view.tipA = a; view.tipB = b; view.sectionRenderers = sections; view.sectionOn = mat; view.sectionOff = offMat;
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            view.markerMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            UnityEngine.Object.DestroyImmediate(tmp);

            // Water-only tow route (same core code as the tests): from just seaward of the curtain to 3 km at 330 deg
            float cxPx = -site.terrain_origin_xz[0] / 10f, cyPx = site.size_m / 10f + site.terrain_origin_xz[1] / 10f;
            var grid = Qandeel.Core.WaterGrid.Load(Path.Combine(SiteDir, "water_2000.bin"), cxPx, cyPx);
            var (rx, rz) = Qandeel.Core.Planner.BearingToXY(3, 330);
            var start = (a + b) * 0.5f + seaward * 60f;
            var route = grid.Route(start.x, start.z, rx, rz);
            view.towRoute = route.ConvertAll(p => new Vector3((float)p.x, 0.4f, (float)p.z)).ToArray();
            view.standby = a + seaward * 120f - along * 60f + Vector3.up * 0.4f;
            view.shelter = (a + b) * 0.5f - seaward * 350f + Vector3.up * 0.4f; // inside the intake harbour, behind the curtain
            view.sea = water;
            File.Copy(Path.Combine(SiteDir, "water_2000.bin"), "Assets/Site/water_2000.bytes", true); // .bytes imports as TextAsset
            AssetDatabase.ImportAsset("Assets/Site/water_2000.bytes");
            view.waterMask = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Site/water_2000.bytes");
            view.maskCenterPx = new Vector2(cxPx, cyPx);
            Debug.Log($"[Qandeel] tow route {route.Count} waypoints, {Qandeel.Core.WaterGrid.Length(route):F0} m");

            // Art pass: Gulf water and foam boiling over each curtain section
            VisualBuilder.Water(water, curtain.transform);
            view.sectionFx = new GameObject[10];
            var foamMat = VisualBuilder.Sprite("Foam", VisualBuilder.SoftTexture("FoamTex", false), new Color(1f, 1f, 1f, 0.85f));
            for (int i = 0; i < 10; i++)
            {
                view.sectionFx[i] = VisualBuilder.SectionBoil(sections[i].transform, len - 4f, foamMat);
                sections[i].enabled = false; // the boil shows the curtain now; the orange bars were placeholders
            }
            // Opaque bells: HDRP water refraction only shows opaque objects below the surface
            view.heldMat = VisualBuilder.Lit("JellyBell", new Color(0.78f, 0.86f, 1f), 0.9f);
            view.passedMat = VisualBuilder.Lit("JellyPassedBell", new Color(1f, 0.3f, 0.3f), 0.85f);
            view.deepMat = VisualBuilder.Lit("JellyDeepBell", new Color(0.45f, 0.5f, 0.6f), 0.85f);

            // Workboat with tow bitt and A-frame, plant massing, and section 5 in detail for the underwater view
            var orange = Unlit("BoatBand", new Color(1f, 0.45f, 0.05f));
            // Blender models (assets/out, made by assets/src/build_assets.py)
            Directory.CreateDirectory("Assets/Models");
            foreach (var f in new[] { "workboat", "blower_container", "plant_unit", "curtain_segment", "boom_section", "catch_bag", "marker_buoy" })
            {
                File.Copy(Path.GetFullPath(Path.Combine(Application.dataPath, $"../../../assets/out/{f}.fbx")), $"Assets/Models/{f}.fbx", true);
                AssetDatabase.ImportAsset($"Assets/Models/{f}.fbx");
            }
            var boatGo = VisualBuilder.Model("Assets/Models/workboat.fbx", "Workboat (crewed, chartered, 9 m; drawn 3x)", "Tow_Bitt", "Wheelhouse");
            view.boat = boatGo.transform;
            // Four APR-1400-class units at their satellite positions (illustrative massing), turbine halls seaward (north)
            var plant = new GameObject("Barakah Units 1-4 (illustrative massing at satellite positions)");
            foreach (var px in new[] { 1229f, 1253f, 1294f, 1318f })
            {
                var u = VisualBuilder.Model("Assets/Models/plant_unit.fbx", "Unit", "Auxiliary", "Turbine_Hall");
                var pos = new Vector3((px - 1000f) * 10f, 0f, (1000f - 1254f) * 10f);
                pos.y = terrain.SampleHeight(pos) + terrainGo.transform.position.y;
                u.transform.SetParent(plant.transform); u.transform.position = pos;
            }
            // Three 40 ft blower containers (2 duty + 1 standby) on the western tip, side by side (placement: design assumption)
            for (int i = 0; i < 3; i++)
            {
                var c = VisualBuilder.Model("Assets/Models/blower_container.fbx", $"Blower container {i + 1}", "Cable_Entry", "Outlet_Pipe");
                var pos = a - seaward * (40f + i * 3.4f) - along * 25f;
                pos.y = terrain.SampleHeight(pos) + terrainGo.transform.position.y;
                c.transform.position = pos; c.transform.rotation = Quaternion.LookRotation(-along);
            }

            // Rock armour on the real breakwater points
            File.Copy(Path.Combine(SiteDir, "breakwater.json"), "Assets/Site/breakwater.json", true);
            AssetDatabase.ImportAsset("Assets/Site/breakwater.json");
            var armour = new GameObject("Rock armour (generic quarry rock, design assumption)").AddComponent<RockArmour>();
            armour.breakwaterJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Site/breakwater.json");
            armour.terrain = terrain;
            armour.centerPx = new Vector2(-site.terrain_origin_xz[0] / 10f, site.size_m / 10f + site.terrain_origin_xz[1] / 10f);
            armour.rockMat = VisualBuilder.Lit("ArmourRock", new Color(0.66f, 0.62f, 0.55f), 0.18f);
            var s5a = a + along * (len * 4f) + seaward * 5f;
            float seabed = terrain.SampleHeight(s5a) + terrainGo.transform.position.y;
            seabed = Mathf.Min(seabed, -3f); // keep the cutaway under water where the approximate heights are too shallow
            var detail = VisualBuilder.UnderwaterSection(s5a, along, seabed, len);
            // Replace the simple pipe and sleepers with the Blender curtain segment (8 x 12 m along section 5)
            foreach (Transform t in detail.transform)
                if (t.name.StartsWith("Pipe") || t.name.StartsWith("Precast")) t.gameObject.SetActive(false);
            for (int k = 0; k < 8; k++)
            {
                var seg = VisualBuilder.Model("Assets/Models/curtain_segment.fbx", $"Curtain segment {k + 1}", "Hole_0", "Hole_14");
                seg.transform.SetParent(detail.transform, true);
                seg.transform.SetPositionAndRotation(s5a + along * (6f + k * 12f) + Vector3.up * seabed, Quaternion.LookRotation(along));
            }
            Debug.Log($"[Qandeel] section 5 seabed {seabed:F1} m (approximate, not surveyed)");

            // Camera views: overview, mouth, underwater at section 5
            var mouthMid = (a + b) * 0.5f;
            view.views = new[]
            {
                View("View 1: overview", mouthMid + seaward * 2600f + Vector3.up * 1300f - along * 900f, mouthMid - seaward * 900f),
                View("View 2: mouth", mouthMid + seaward * 520f + Vector3.up * 170f - along * 160f, mouthMid + seaward * 40f),
                View("View 3: underwater, section 5", s5a + along * (len * 0.5f) + seaward * 7f + Vector3.up * (seabed + 1.6f),
                     s5a + along * (len * 0.5f - 6f) + Vector3.up * (seabed + 3.5f)),
            };
            // Second boat of the gathering unit; the boom U between their sterns is drawn each frame by CurtainView
            var boat2Go = UnityEngine.Object.Instantiate(boatGo); // clone: rebuilding would overwrite boat 1's material assets
            boat2Go.name = "Workboat 2 (crewed, chartered, 9 m; drawn 3x)";
            view.boat2 = boat2Go.transform;
            var boomGo = new GameObject("Boom U between the two boats (RO-BOOM 2000 class, 200 m, ~66 m opening)");
            var lr = boomGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = true; lr.widthMultiplier = 4f; lr.sharedMaterial = Unlit("Boom", new Color(1f, 0.85f, 0.1f));
            lr.positionCount = 0;
            view.boom = lr;
            view.wakes = new[]
            {
                VisualBuilder.FoamTrail("Wake", boatGo.transform, new Vector3(0, 0.3f, -4.8f), foamMat, 40f, 4f, 25f),
                VisualBuilder.FoamTrail("Wake", boat2Go.transform, new Vector3(0, 0.3f, -4.8f), foamMat, 40f, 4f, 25f),
            };
            // Real boom pieces and bag (Blender), strung along the U at runtime
            view.boomPieces = Enumerable.Range(0, 16).Select(i =>
            {
                var bp = VisualBuilder.Model("Assets/Models/boom_section.fbx", $"Boom piece {i + 1}", "Float_0", "Float_4");
                bp.transform.SetParent(boomGo.transform, true); bp.SetActive(false); return bp.transform;
            }).ToArray();
            var bagGo = VisualBuilder.Model("Assets/Models/catch_bag.fbx", "Closed-bottom bag (4 x 3 m mouth, ~20 m3)", "Bag", "Rim");
            bagGo.SetActive(false); view.bagModel = bagGo.transform;
            view.bagFoam = VisualBuilder.FoamTrail("Net and bag drag foam", boomGo.transform, Vector3.zero, foamMat, 25f, 6f, 30f);
            view.heldClear = VisualBuilder.Lit("JellyClear", new Color(0.78f, 0.88f, 1f, 0.42f), 0.95f, 0f, true);
            view.deepClear = VisualBuilder.Lit("JellyDeepClear", new Color(0.5f, 0.55f, 0.65f, 0.4f), 0.95f, 0f, true);
            view.passedClear = VisualBuilder.Lit("JellyPassedClear", new Color(1f, 0.35f, 0.35f, 0.45f), 0.95f, 0f, true);

            var buoy = VisualBuilder.Model("Assets/Models/marker_buoy.fbx", "Release point 3 km at 330 deg (lit marker buoy: design assumption)", "Float", "Mast");
            buoy.transform.position = new Vector3((float)rx, 0f, (float)rz); buoy.transform.localScale = Vector3.one * 3f; // drawn 3x

            // Camera: drone view from offshore, close enough to see the swarm at the mouth
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 1f; cam.farClipPlane = 40000f; cam.fieldOfView = 50f;
            camGo.AddComponent<HDAdditionalCameraData>();
            var mid = (a + b) * 0.5f;
            camGo.transform.position = mid + seaward * 1100f + Vector3.up * 420f - along * 250f;
            camGo.transform.LookAt(mid + seaward * 120f);
            camGo.AddComponent<Benchmark>();
            camGo.AddComponent<GraphicsTier>();

            // Control-room interface (UI Toolkit) and the guided tour
            Directory.CreateDirectory("Assets/UI");
            File.WriteAllText("Assets/UI/QandeelTheme.tss", "@import url(\"unity-theme://default\");\n");
            AssetDatabase.ImportAsset("Assets/UI/QandeelTheme.tss");
            AssetDatabase.DeleteAsset("Assets/UI/QandeelPanel.asset");
            var panel = ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.ThemeStyleSheet>("Assets/UI/QandeelTheme.tss");
            panel.scaleMode = UnityEngine.UIElements.PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.screenMatchMode = UnityEngine.UIElements.PanelScreenMatchMode.MatchWidthOrHeight; panel.match = 0.5f;
            AssetDatabase.CreateAsset(panel, "Assets/UI/QandeelPanel.asset");
            var uiGo = new GameObject("Control room UI");
            var doc = uiGo.AddComponent<UnityEngine.UIElements.UIDocument>();
            doc.panelSettings = panel;
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.VisualTreeAsset>("Assets/UI/ControlRoom.uxml");
            var room = uiGo.AddComponent<ControlRoom>();
            room.view = view;
            var narration = File.ReadAllText(Path.Combine(SiteDir, "../data/narration_lines.json"));
            room.tourText = new[] { "mission.0", "mission.1", "mission.2", "mission.3", "mission.4", "mission.5", "topic.idea" }
                .Select(k => Line(narration, k)).ToArray();
            if (doc.visualTreeAsset == null) Debug.LogError("[Qandeel] ControlRoom.uxml not found");
            // Tour narration (ElevenLabs recordings of the same lines), if recorded
            Directory.CreateDirectory("Assets/Audio");
            room.tourVoice = new[] { "mission_0", "mission_1", "mission_2", "mission_3", "mission_4", "mission_5", "topic_idea" }.Select(k =>
            {
                var srcMp3 = Path.Combine(SiteDir, "voice", k + ".mp3");
                if (!File.Exists(srcMp3)) return null;
                File.Copy(srcMp3, $"Assets/Audio/{k}.mp3", true); AssetDatabase.ImportAsset($"Assets/Audio/{k}.mp3");
                return AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Audio/{k}.mp3");
            }).ToArray();

            // Panel data straight from the kit (no copies edited by hand)
            Directory.CreateDirectory("Assets/UI/Data");
            string kit = Path.GetFullPath(Path.Combine(SiteDir, ".."));
            T Copy<T>(string from, string name) where T : UnityEngine.Object
            {
                string dst = "Assets/UI/Data/" + name;
                File.Copy(Path.Combine(kit, from), dst, true);
                AssetDatabase.ImportAsset(dst);
                if (AssetImporter.GetAtPath(dst) is TextureImporter ti)
                {
                    ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.Uncompressed;
                    ti.npotScale = TextureImporterNPOTScale.None; ti.filterMode = FilterMode.Bilinear; ti.wrapMode = TextureWrapMode.Clamp;
                    ti.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<T>(dst);
            }
            room.forecastMeta = Copy<TextAsset>("data/forecast_meta.json", "forecast_meta.json");
            room.detections = Copy<TextAsset>("data/ai/detections.json", "detections.json");
            room.detectorMetrics = Copy<TextAsset>("data/detector_metrics.json", "detector_metrics.json");
            room.costs = Copy<TextAsset>("model/qandeel/outputs/costs.json", "costs.json");
            File.Copy(Path.Combine(kit, "model/qandeel/data/gulf_forcing.csv"), "Assets/UI/Data/gulf_forcing.txt", true); // .txt imports as TextAsset
            AssetDatabase.ImportAsset("Assets/UI/Data/gulf_forcing.txt");
            view.forcingCsv = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/UI/Data/gulf_forcing.txt");
            room.forecastAtlases = Enumerable.Range(0, 3).Select(i => Copy<Texture2D>($"data/fc_atlas_{i}.png", $"fc_atlas_{i}.png")).ToArray();
            var detFiles = System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(Path.Combine(kit, "data/ai/detections.json")), "ai_(\\d+)\\.jpg")
                .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Value).ToArray();
            room.aiImages = detFiles.Select(f => Copy<Texture2D>("data/ai/" + f, f)).ToArray();
            Debug.Log($"[Qandeel] panels: 3 forecast atlases, {room.aiImages.Length} AI images");

            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Barakah.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Barakah.unity", true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[Qandeel] Barakah scene built");
        }

        /// <summary>One value from the flat narration JSON (string values only).</summary>
        static string Line(string json, string key)
        {
            var m = System.Text.RegularExpressions.Regex.Match(json, "\"" + System.Text.RegularExpressions.Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!m.Success) { Debug.LogError($"[Qandeel] narration line {key} missing"); return ""; }
            return System.Text.RegularExpressions.Regex.Unescape(m.Groups[1].Value);
        }

        static Transform View(string name, Vector3 pos, Vector3 lookAt)
        {
            var go = new GameObject(name);
            go.transform.position = pos; go.transform.LookAt(lookAt);
            return go.transform;
        }

        static void Part(GameObject parent, string name, Vector3 pos, Vector3 size, Material m)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            c.name = name; c.transform.SetParent(parent.transform, false);
            c.transform.localPosition = pos; c.transform.localScale = size;
            c.GetComponent<Renderer>().sharedMaterial = m;
            UnityEngine.Object.DestroyImmediate(c.GetComponent<Collider>());
        }

        /// <summary>Makes HDRP the active pipeline with water enabled.</summary>
        static void EnsureHdrp()
        {
            // HDRP only lights correctly in Linear colour space; projects made with -createProject start in Gamma
            if (PlayerSettings.colorSpace != ColorSpace.Linear) PlayerSettings.colorSpace = ColorSpace.Linear;
            var asset = GraphicsSettings.defaultRenderPipeline as HDRenderPipelineAsset;
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
                Directory.CreateDirectory("Assets/Settings");
                AssetDatabase.CreateAsset(asset, "Assets/Settings/QandeelHDRP.asset");
                GraphicsSettings.defaultRenderPipeline = asset;
            }
            for (int i = 0; i < QualitySettings.names.Length; i++) { QualitySettings.SetQualityLevel(i, false); QualitySettings.renderPipeline = asset; }
            var so = new SerializedObject(asset);
            var water = so.FindProperty("m_RenderPipelineSettings.supportWater");
            if (water != null) water.boolValue = true;
            else Debug.LogWarning("[Qandeel] supportWater setting not found; enable Water in the HDRP asset by hand");
            // Adaptive Probe Volumes give zero ambient light until baked; use classic probes so the sky lights shaded sides
            var probes = so.FindProperty("m_RenderPipelineSettings.lightProbeSystem");
            if (probes != null) probes.intValue = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Windows player build for the benchmark: Builds/Qandeel/Qandeel.exe</summary>
        public static void BuildPlayer()
        {
            PlayerSettings.runInBackground = true;
            PlayerSettings.productName = "Qandeel";
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Barakah.unity" },
                locationPathName = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/Qandeel/Qandeel.exe")),
                target = BuildTarget.StandaloneWindows64,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[Qandeel] build {report.summary.result}, {report.summary.totalSize / 1048576} MB");
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(3);
        }
    }
}
