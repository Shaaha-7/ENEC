using System;
using System.Collections.Generic;
using System.IO;

namespace Qandeel.Core
{
    /// <summary>
    /// Water-only routes for boats (and later far-field drift): A* on the 10 m Sentinel-2 water mask, kept a set clearance
    /// from land, then shortened by line of sight. Boats go round the real breakwaters, never through land.
    /// World frame as Unity: metres, origin at the mouth centre, x = east, z = north.
    /// </summary>
    public sealed class WaterGrid
    {
        public readonly int Size;
        public readonly double CellM, CenterPx, CenterPy;
        readonly bool[] water; // row 0 = north

        public WaterGrid(byte[] cells, int size, double cellM, double centerPx, double centerPy, int clearanceCells)
        {
            Size = size; CellM = cellM; CenterPx = centerPx; CenterPy = centerPy;
            water = new bool[size * size];
            for (int i = 0; i < water.Length; i++) water[i] = cells[i] != 0;
            if (clearanceCells > 0) Erode(clearanceCells);
        }

        public static WaterGrid Load(string binPath, double centerPx, double centerPy, int clearanceCells = 3)
            => new WaterGrid(File.ReadAllBytes(binPath), 2000, 10.0, centerPx, centerPy, clearanceCells);

        void Erode(int r)
        {
            var src = (bool[])water.Clone();
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    if (!src[y * Size + x]) continue;
                    for (int dy = -r; dy <= r && water[y * Size + x]; dy++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx < 0 || yy < 0 || xx >= Size || yy >= Size || !src[yy * Size + xx]) { water[y * Size + x] = false; break; }
                        }
                }
        }

        public bool IsWater(int px, int py) => px >= 0 && py >= 0 && px < Size && py < Size && water[py * Size + px];
        public (int px, int py) ToCell(double x, double z) => ((int)Math.Round(CenterPx + x / CellM), (int)Math.Round(CenterPy - z / CellM));
        public (double x, double z) ToWorld(int px, int py) => ((px - CenterPx) * CellM, (CenterPy - py) * CellM);
        public bool IsWaterAt(double x, double z) { var (px, py) = ToCell(x, z); return IsWater(px, py); }

        public (int, int) NearestWater(int px, int py)
        {
            for (int r = 0; r < 200; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                        if ((Math.Abs(dx) == r || Math.Abs(dy) == r) && IsWater(px + dx, py + dy)) return (px + dx, py + dy);
            throw new Exception("no water near point");
        }

        bool LineIsWater(int x0, int y0, int x1, int y1)
        {
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
            while (true)
            {
                if (!IsWater(x0, y0)) return false;
                if (x0 == x1 && y0 == y1) return true;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>Shortest water route (world waypoints) from (x0, z0) to (x1, z1); null if unreachable.</summary>
        public List<(double x, double z)> Route(double x0, double z0, double x1, double z1)
        {
            var (sx, sy) = NearestWater(ToCell(x0, z0).px, ToCell(x0, z0).py);
            var (gx, gy) = NearestWater(ToCell(x1, z1).px, ToCell(x1, z1).py);
            int n = Size * Size, start = sy * Size + sx, goal = gy * Size + gx;
            var g = new float[n]; for (int i = 0; i < n; i++) g[i] = float.MaxValue;
            var from = new int[n];
            var open = new PriorityQueue<int, float>();
            g[start] = 0; open.Enqueue(start, H(sx, sy, gx, gy));
            int[] ddx = { 1, -1, 0, 0, 1, 1, -1, -1 }, ddy = { 0, 0, 1, -1, 1, -1, 1, -1 };
            float[] cost = { 1, 1, 1, 1, 1.4142f, 1.4142f, 1.4142f, 1.4142f };
            while (open.Count > 0)
            {
                int cur = open.Dequeue();
                if (cur == goal) break;
                int cx = cur % Size, cy = cur / Size;
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + ddx[k], ny = cy + ddy[k];
                    if (!IsWater(nx, ny)) continue;
                    int ni = ny * Size + nx; float ng = g[cur] + cost[k];
                    if (ng < g[ni]) { g[ni] = ng; from[ni] = cur; open.Enqueue(ni, ng + H(nx, ny, gx, gy)); }
                }
            }
            if (g[goal] == float.MaxValue) return null;
            var cells = new List<int>();
            for (int c = goal; c != start; c = from[c]) cells.Add(c);
            cells.Add(start); cells.Reverse();
            // String-pull: keep only the waypoints needed for straight water legs
            var pts = new List<(double, double)>();
            int a = 0;
            pts.Add(ToWorld(cells[0] % Size, cells[0] / Size));
            while (a < cells.Count - 1)
            {
                int b = cells.Count - 1;
                while (b > a + 1 && !LineIsWater(cells[a] % Size, cells[a] / Size, cells[b] % Size, cells[b] / Size)) b--;
                pts.Add(ToWorld(cells[b] % Size, cells[b] / Size));
                a = b;
            }
            return pts;
        }

        /// <summary>Minimal binary min-heap (Unity's .NET has no PriorityQueue).</summary>
        sealed class PriorityQueue<TItem, TPrio> where TPrio : IComparable<TPrio>
        {
            readonly List<(TItem item, TPrio p)> h = new List<(TItem, TPrio)>();
            public int Count => h.Count;
            public void Enqueue(TItem item, TPrio p)
            {
                h.Add((item, p)); int i = h.Count - 1;
                while (i > 0) { int up = (i - 1) / 2; if (h[up].p.CompareTo(h[i].p) <= 0) break; (h[up], h[i]) = (h[i], h[up]); i = up; }
            }
            public TItem Dequeue()
            {
                var top = h[0].item; h[0] = h[h.Count - 1]; h.RemoveAt(h.Count - 1);
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < h.Count && h[l].p.CompareTo(h[m].p) < 0) m = l;
                    if (r < h.Count && h[r].p.CompareTo(h[m].p) < 0) m = r;
                    if (m == i) break;
                    (h[m], h[i]) = (h[i], h[m]); i = m;
                }
                return top;
            }
        }

        static float H(int x, int y, int gx, int gy) { float dx = x - gx, dy = y - gy; return (float)Math.Sqrt(dx * dx + dy * dy); }

        public static double Length(List<(double x, double z)> p)
        {
            double s = 0; for (int i = 1; i < p.Count; i++) s += Math.Sqrt(Math.Pow(p[i].x - p[i - 1].x, 2) + Math.Pow(p[i].z - p[i - 1].z, 2));
            return s;
        }
    }
}
