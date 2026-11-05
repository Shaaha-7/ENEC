using System.Collections.Generic;
using UnityEngine;

namespace Qandeel
{
    /// <summary>
    /// Rock armour along the real breakwaters: rocks scattered on every point of data/breakwater.json (Sentinel-2 rock-armour
    /// pixels, 10 m), sitting on the terrain, drawn with GPU instancing. Unit shape and size are a generic quarry-rock
    /// DESIGN ASSUMPTION (Barakah's concrete armour units are not verified), 2-4.5 m across.
    /// </summary>
    public sealed class RockArmour : MonoBehaviour
    {
        public TextAsset breakwaterJson;
        public Terrain terrain;
        public Material rockMat;
        public Vector2 centerPx = new Vector2(1000f, 1000f);
        public int rocksPerPoint = 4;

        Mesh[] meshes;
        readonly List<Matrix4x4[]> batches = new List<Matrix4x4[]>();
        readonly List<int> batchMesh = new List<int>();

        void Start()
        {
            meshes = new[] { RockMesh(1), RockMesh(2), RockMesh(3) };
            var pts = Parse(breakwaterJson.text);
            var rng = new System.Random(7);
            var all = new List<Matrix4x4>[meshes.Length];
            for (int k = 0; k < meshes.Length; k++) all[k] = new List<Matrix4x4>();
            foreach (var p in pts)
            {
                for (int r = 0; r < rocksPerPoint; r++)
                {
                    float x = (p.x - centerPx.x) * 10f + (float)(rng.NextDouble() - 0.5) * 10f;
                    float z = (centerPx.y - p.y) * 10f + (float)(rng.NextDouble() - 0.5) * 10f;
                    float y = terrain.SampleHeight(new Vector3(x, 0, z)) + terrain.transform.position.y;
                    if (y < -1.5f) continue; // keep rocks on the structure, not on the seabed
                    float s = 2f + (float)rng.NextDouble() * 2.5f;
                    var rot = Quaternion.Euler((float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f);
                    all[rng.Next(meshes.Length)].Add(Matrix4x4.TRS(new Vector3(x, y - 0.3f * s, z), rot, new Vector3(s, s * 0.75f, s)));
                }
            }
            for (int k = 0; k < meshes.Length; k++)
                for (int i = 0; i < all[k].Count; i += 1023)
                {
                    batches.Add(all[k].GetRange(i, Mathf.Min(1023, all[k].Count - i)).ToArray());
                    batchMesh.Add(k);
                }
        }

        void Update()
        {
            var rp = new RenderParams(rockMat) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = true };
            for (int i = 0; i < batches.Count; i++) Graphics.RenderMeshInstanced(rp, meshes[batchMesh[i]], 0, batches[i]);
        }

        static List<Vector2> Parse(string json)
        {
            var list = new List<Vector2>();
            var nums = System.Text.RegularExpressions.Regex.Matches(json, "-?\\d+(\\.\\d+)?");
            for (int i = 0; i + 1 < nums.Count; i += 2)
                list.Add(new Vector2(float.Parse(nums[i].Value, System.Globalization.CultureInfo.InvariantCulture),
                                     float.Parse(nums[i + 1].Value, System.Globalization.CultureInfo.InvariantCulture)));
            return list;
        }

        /// <summary>Angular quarry rock: subdivided octahedron with seeded noise, flat-shaded facets.</summary>
        static Mesh RockMesh(int seed)
        {
            var rng = new System.Random(seed * 97);
            var v = new List<Vector3> { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            var t = new List<int> { 0, 4, 3, 0, 3, 5, 0, 5, 2, 0, 2, 4, 1, 3, 4, 1, 5, 3, 1, 2, 5, 1, 4, 2 };
            for (int it = 0; it < 2; it++)
            {
                var nt = new List<int>(); var cache = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (cache.TryGetValue(key, out int m)) return m;
                    v.Add(((v[a] + v[b]) * 0.5f).normalized); cache[key] = v.Count - 1; return v.Count - 1;
                }
                for (int i = 0; i < t.Count; i += 3)
                {
                    int a = t[i], b = t[i + 1], c = t[i + 2], ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    nt.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                t = nt;
            }
            for (int i = 0; i < v.Count; i++) v[i] *= 0.42f + (float)rng.NextDouble() * 0.16f;
            // flat shading: unshare vertices
            var fv = new List<Vector3>(); var ft = new List<int>();
            for (int i = 0; i < t.Count; i++) { fv.Add(v[t[i]]); ft.Add(i); }
            var m = new Mesh { name = "Armour rock " + seed };
            m.SetVertices(fv); m.SetTriangles(ft, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
