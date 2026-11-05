using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Qandeel.EditorTools
{
    /// <summary>Renders stills of the Barakah scene to unity/previews/ without building a player.
    /// Render: the stand view. Diagnose: exposure sweep, test sphere and an HDRP-made sun, to find lighting problems.
    /// Unity.exe -batchmode -quit -projectPath ... -executeMethod Qandeel.EditorTools.PreviewRenderer.Render</summary>
    public static class PreviewRenderer
    {
        static string Dir => Path.GetFullPath(Path.Combine(Application.dataPath, "../../previews"));

        public static void Render()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Barakah.unity");
            Directory.CreateDirectory(Dir);
            Shot(Camera.main, "stand_view.png");
            var cam = Camera.main;
            cam.transform.SetPositionAndRotation(new Vector3(0, 6000, 0), Quaternion.Euler(90, 0, 0));
            Shot(cam, "top_down.png");
        }

        public static void Diagnose()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Barakah.unity");
            Directory.CreateDirectory(Dir);
            var cam = Camera.main;
            var vol = Object.FindFirstObjectByType<Volume>();
            vol.sharedProfile.TryGet(out Exposure exp);
            var sun = Object.FindFirstObjectByType<Light>();
            Debug.Log($"[Qandeel] sun active={sun.isActiveAndEnabled} {sun.intensity} {sun.lightUnit} fwd={sun.transform.forward} layers={sun.renderingLayerMask}");

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.position = cam.transform.position + cam.transform.forward * 60f;
            sphere.transform.localScale = Vector3.one * 20f;
            var lit = new Material(Shader.Find("HDRP/Lit"));
            HDMaterial.ValidateMaterial(lit);
            sphere.GetComponent<Renderer>().sharedMaterial = lit;

            foreach (float ev in new[] { 15f, 14f, 13f, 12f })
            {
                exp.fixedExposure.value = ev;
                Shot(cam, $"diag_ev{ev}.png");
            }
            exp.fixedExposure.value = 14.5f;

            Object.DestroyImmediate(sun.gameObject);
            var go = new GameObject("HDRP sun");
            var hd = go.AddHDLight(LightType.Directional);
            go.transform.rotation = Quaternion.Euler(38f, 35f, 0f);
            var l = go.GetComponent<Light>(); l.lightUnit = LightUnit.Lux; l.intensity = 100000f;
            Shot(cam, "diag_hdrp_sun_ev14.5.png");
        }

        static void Shot(Camera cam, string name)
        {
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            cam.targetTexture = rt;
            for (int i = 0; i < 12; i++) cam.Render(); // let sky, exposure and water settle
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            var px = tex.GetPixels(); float sum = 0; foreach (var c in px) sum += c.grayscale;
            File.WriteAllBytes(Path.Combine(Dir, name), tex.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.active = null;
            Debug.Log($"[Qandeel] wrote {name}, mean brightness {sum / px.Length:F3}");
        }
    }
}
