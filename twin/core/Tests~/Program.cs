using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Qandeel.Core;

// Acceptance tests for the physics core (PROMPT.md section 9), checked against the Python model outputs.
// Usage (from core/Tests~): dotnet run -c Release [-- --quick]
static class Program
{
    static int failed, passed;
    static string Root;

    static void Check(string name, bool ok, string detail)
    {
        if (ok) passed++; else failed++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}: {detail}");
    }

    static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "model", "qandeel", "site.json"))) d = d.Parent;
        if (d == null) throw new Exception("model/qandeel/site.json not found above " + AppContext.BaseDirectory);
        return d.FullName;
    }

    static JsonElement Json(string rel) => JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, rel))).RootElement;

    static double MeanHeld(double v, double q, int seeds, int n = 500, double swimMax = 0.10, double hours = 3.0)
    {
        double s = 0;
        for (int k = 0; k < seeds; k++) s += CurtainHold.HeldShareOf(v, q, hours, n, swimMax, (ulong)k);
        return s / seeds;
    }

    static int Main(string[] args)
    {
        Root = FindRoot();
        bool quick = Array.IndexOf(args, "--quick") >= 0;
        var results = Json("model/qandeel/outputs/results.json");
        var t0 = DateTime.Now;

        // Bulson outflow
        foreach (var (q, want) in new[] { (1.5, 0.36), (3.0, 0.45), (4.5, 0.52) })
            Check($"Bulson q={q}", Math.Round(Bulson.SurfaceCurrent(q), 2) == want, $"{Bulson.SurfaceCurrent(q):F3} m/s (want {want})");

        // Curtain acceptance cases, 3 seeds each, every seed must pass
        void Case(string name, double v, double q, Func<double, bool> ok, string rule)
        {
            var vals = new List<string>(); bool all = true;
            for (ulong s = 0; s < 3; s++) { double h = CurtainHold.HeldShareOf(v, q, seed: s); all &= ok(h); vals.Add($"{h:P1}"); }
            Check(name, all, $"held {string.Join(" / ", vals)} ({rule})");
        }
        Case("Typical day q=3 V=0.14", 0.14, 3.0, h => h >= 0.97, ">= 97%");
        Case("Worst 5% q=3 V=0.23", 0.23, 3.0, h => h >= 0.95, ">= 95%");
        Case("Too little air q=1 V=0.23", 0.23, 1.0, h => h <= 0.20, "<= 20%, Python 15%");

        // Whole published hold grid: mean of 4 seeds within 0.08 of Python
        double worst = 0; string worstAt = "";
        foreach (var row in results.GetProperty("curtain_hold").EnumerateArray())
        {
            double sw = row.GetProperty("swim_max_m_s").GetDouble(), q = row.GetProperty("airflow_l_s_m").GetDouble();
            double v = row.GetProperty("approach_m_s").GetDouble(), py = row.GetProperty("held_share").GetDouble();
            double cs = MeanHeld(v, q, quick ? 1 : 4, 400, sw);
            if (Math.Abs(cs - py) > worst) { worst = Math.Abs(cs - py); worstAt = $"swim {sw} q {q} V {v}: C# {cs:F3} vs Python {py:F3}"; }
        }
        Check("Hold grid vs Python (56 cases)", worst <= 0.08, $"largest gap {worst:F3} at {worstAt}");

        // Curtain capacity (approach held >= 95%) and the margin used by smart air
        var (margin, vmax) = SmartAir.HoldMargin();
        Check("Capacity 1.5/3/4.5 L/s/m", Math.Abs(vmax[0] - 0.20) <= 0.026 && Math.Abs(vmax[1] - 0.275) <= 0.026 && Math.Abs(vmax[2] - 0.325) <= 0.026,
              $"{vmax[0]:F3} / {vmax[1]:F3} / {vmax[2]:F3} m/s (Python 0.200 / 0.275 / 0.325); margin {margin:F3} (Python 0.175)");

        // Depth risk table, all 8 rows
        var depth = Json("model/qandeel/outputs/depth_sensitivity.json");
        bool dOk = true; string dBad = "";
        foreach (var row in depth.GetProperty("rows").EnumerateArray())
        {
            double m = row.GetProperty("mean_depth_m").GetDouble(), L = row.GetProperty("outflow_layer_m").GetDouble();
            double w = Math.Round(DepthRisk.HeldWorst(m, L), 2), b = Math.Round(DepthRisk.HeldBest(m, L), 2);
            if (w != row.GetProperty("held_worst").GetDouble() || b != row.GetProperty("held_best").GetDouble()) { dOk = false; dBad += $" [{m},{L}: {w},{b}]"; }
        }
        Check("Depth table (8 rows)", dOk, dOk ? "all match" : "mismatch" + dBad);
        Check("Deep swarm mean 2 m", Math.Abs(DepthRisk.HeldWorst(2, 1) - 0.39) < 0.006 && Math.Abs(DepthRisk.HeldBest(2, 2) - 0.82) < 0.006,
              $"{DepthRisk.HeldWorst(2, 1):P0}-{DepthRisk.HeldBest(2, 2):P0} held (want 39-82%)");

        // Live deep-swarm simulation must agree with the published depth table (worst case = share in layer)
        foreach (var (mean, layer) in new[] { (2.0, 1.0), (1.0, 1.0), (3.0, 2.0) })
        {
            double s = 0;
            for (ulong k = 0; k < 3; k++) s += new CurtainHold(0.14, 3.0, seed: k).WithSwarmDepth(mean, layer).Run(3).HeldShare;
            s /= 3;
            double w = DepthRisk.HeldWorst(mean, layer), b = DepthRisk.HeldBest(mean, layer);
            Check($"Deep swarm sim mean {mean} m, layer {layer} m", s >= w - 0.06 && s <= b + 0.02,
                  $"held {s:P1} (table {w:P0}-{b:P0})");
        }

        // Section failure: one section off must let jellyfish through that section only
        {
            var sim = new CurtainHold(0.14, 3.0, seed: 0); sim.SectionOn[4] = false; sim.Run(3);
            double half = Site.GapWidthM / 2, w = Site.GapWidthM / Site.Sections; int inOff = 0, inOther = 0;
            for (int i = 0; i < sim.N; i++)
                if (sim.Inside[i]) { if (sim.X[i] >= -half + 4 * w - 30 && sim.X[i] < -half + 5 * w + 30) inOff++; else inOther++; }
            Check("Section 5 off", sim.HeldShare < 0.97 && inOff > 0 && inOther <= inOff / 10,
                  $"held {sim.HeldShare:P1}; {inOff} entered at section 5, {inOther} elsewhere");
        }

        // Catch pockets: only jellyfish held >= 30 min; report what the physics delivers in 6 sea-hours
        foreach (var sc in Scenario.All)
        {
            var sim = sc.Create(seed: 0).Run(6);
            bool rule = true; for (int i = 0; i < sim.N; i++) if (sim.Pocketed[i] && sim.HeldS[i] < CurtainHold.MinHeldS) rule = false;
            Check($"Pockets, {sc.Name}", rule, $"6 h: {sim.PocketedCount} pocketed (west {sim.PocketedA}, east {sim.PocketedB}), {sim.Entered} passed, of {sim.N}");
        }

        // Gathering at the centre: after 2 h the boom swept at the held swarm's centroid takes only jellyfish held >= 30 min
        {
            var sim = Scenario.All[0].Create(seed: 0).Run(2);
            bool any = sim.HeldCentroid(out double gx, out double gy, out int cnt);
            int before = 0; for (int i = 0; i < sim.N; i++) if (!sim.Inside[i] && sim.HeldS[i] < CurtainHold.MinHeldS) before++;
            int got = any ? sim.Gather(gx, gy) : 0;
            bool rule = true; for (int i = 0; i < sim.N; i++) if (sim.Pocketed[i] && sim.HeldS[i] < CurtainHold.MinHeldS) rule = false;
            Check("Boom gathers held swarm at centre", any && got > 0 && rule,
                  $"held >= 30 min: {cnt} around ({gx:F0}, {gy:F0}) m; one 66 m sweep took {got}; {before} not yet eligible");
        }

        // Boat route: pocket at the western tip to the release point 3 km at 330 deg, water only
        {
            var site = Json("unity_site/site_unity.json");
            var meta = Json("data/meta.json");
            var c = meta.GetProperty("center_px");
            var grid = WaterGrid.Load(Path.Combine(Root, "unity_site/water_2000.bin"), c[0].GetDouble(), c[1].GetDouble());
            var ta = site.GetProperty("tip_a_xz");
            double ax = ta[0].GetDouble(), az = ta[1].GetDouble();
            var (rx, rz) = Planner.BearingToXY(3, 330);
            var route = grid.Route(ax, az + 40, rx, rz);
            var land = WaterGrid.Load(Path.Combine(Root, "unity_site/water_2000.bin"), c[0].GetDouble(), c[1].GetDouble(), 0); // true coastline
            grid = land;
            bool allWater = route != null; double len = 0;
            if (route != null)
            {
                for (int i = 1; i < route.Count; i++)
                    for (int s = 0; s <= 20; s++)
                    {
                        double t = s / 20.0, x = route[i - 1].x + (route[i].x - route[i - 1].x) * t, z = route[i - 1].z + (route[i].z - route[i - 1].z) * t;
                        if (!grid.IsWaterAt(x, z)) allWater = false;
                    }
                len = WaterGrid.Length(route);
            }
            double straight = Math.Sqrt((rx - ax) * (rx - ax) + (rz - az) * (rz - az));
            var end = route?[route.Count - 1] ?? (0, 0);
            Check("Boat route to release (3 km, 330 deg)", allWater && len >= straight - 1 && Math.Abs(end.x - rx) < 40 && Math.Abs(end.z - rz) < 40,
                  $"{route?.Count} waypoints, {len / 1000:F2} km through water (straight {straight / 1000:F2} km); still-water tow at 0.2 m/s {len / 0.2 / 3600:F1} h (model median with currents 7.1 h)");
        }

        // ---- Demo controls (operating rules on top of the physics)
        // (1) Deep swarm 2 m, normal air vs high air: live held share inside the published table ranges
        foreach (bool high in new[] { false, true })
        {
            double s = 0;
            for (ulong k = 0; k < 3; k++)
                s += new CurtainHold(0.14, high ? Operations.HighAirLps : 3.0, seed: k)
                    .WithSwarmDepth(Operations.DeepSwarmMeanM, high ? Operations.LayerHighAirM : Operations.LayerNormalM).Run(3).HeldShare;
            s /= 3; var (lo, hi) = Operations.ExpectedHeld(high);
            Check($"Demo: deep swarm, {(high ? "high" : "normal")} air", s >= lo - 0.06 && s <= hi + 0.02,
                  $"held {s:P1} (expected {lo:P0}-{hi:P0})");
        }
        // (2) Sea state: boats run below 1 m, stand by 1-2 m, in port above 2 m; the curtain physics does not change
        {
            bool rules = Operations.ForWaves(0.5) == Operations.Boats.Run && Operations.ForWaves(1.0) == Operations.Boats.StandBy
                         && Operations.ForWaves(2.0) == Operations.Boats.StandBy && Operations.ForWaves(2.5) == Operations.Boats.InPort;
            double held = CurtainHold.HeldShareOf(0.14, 3.0, seed: 0);
            Check("Demo: sea state rules", rules && held >= 0.97, $"<1 m run, 1-2 m stand by, >2 m in port; curtain still holds {held:P0}");
        }
        // (3) Failures
        {
            double q1 = Operations.AirflowAfterTrips(3.0, 1), q2 = Operations.AirflowAfterTrips(3.0, 2);
            double h0 = CurtainHold.HeldShareOf(0.14, 3.0, seed: 0), h1 = CurtainHold.HeldShareOf(0.14, q1, seed: 0);
            Check("Demo failure: blower trip", Math.Abs(q1 - 3.0) < 1e-9 && h1 == h0 && Math.Abs(q2 - 1.5) < 0.01,
                  $"1 trip: standby takes over, {q1:F2} L/s/m, held {h1:P0} = {h0:P0}; 2 trips: {q2:F2} L/s/m");
            double qh = Operations.AirflowAfterTrips(Operations.HighAirLps, 0), qh1 = Operations.AirflowAfterTrips(Operations.HighAirLps, 1);
            Check("Demo: high air uses all 3 blowers", Math.Abs(qh - 4.5) < 0.01 && Math.Abs(qh1 - 3.0) < 0.01,
                  $"high air {qh:F2} L/s/m with 3 blowers; after one trip {qh1:F2} L/s/m");
            var sim = Scenario.All[0].Create(seed: 0); sim.SectionOn[4] = false; sim.Run(3);
            Check("Demo failure: one section off", sim.Entered > 0 && sim.HeldShare < h0,
                  $"{sim.Entered} pass through the gap to the existing screens; held {sim.HeldShare:P0}");
            var fc = Planner.ForecastArrival(15, 330, ForcingRecord.Load(Path.Combine(Root, "model/qandeel/data/gulf_forcing.csv")), runs: 100, seed: 0);
            Check("Demo failure: camera off", fc.SwitchOnH != null,
                  $"forecast alone still switches the curtain on at +{fc.SwitchOnH:F0} h (camera is only the backup)");
        }
        // Stand: curtain OFF vs ON and the (illustrative) plant response
        {
            var on = Scenario.All[0].Create(seed: 0).Run(2);
            var off = Scenario.All[0].Create(seed: 0); for (int i = 0; i < off.SectionOn.Length; i++) off.SectionOn[i] = false; off.Run(2);
            int uOn = Operations.UnitsOnline((double)on.Entered / on.N / 2), uOff = Operations.UnitsOnline((double)off.Entered / off.N / 2);
            Check("Stand: curtain OFF vs ON", uOn == 4 && uOff < 4 && off.Entered > 5 * Math.Max(1, on.Entered),
                  $"2 h: ON {on.Entered} passed, {uOn}/4 units; OFF {off.Entered} passed, {uOff}/4 units (illustrative plant rule)");
            var big = Scenario.All[0].Create(seed: 0, n: 2500).Run(2);
            Check("Stand: swarm incoming (2,500)", big.HeldShare >= 0.9, $"held {big.HeldShare:P0} of {big.N}");
        }
        // Real-site boundary from the Sentinel-2 mask: no jellyfish on land; report the effect on the hold
        {
            var site = Json("unity_site/site_unity.json"); var c0 = Json("data/meta.json").GetProperty("center_px");
            var grid = WaterGrid.Load(Path.Combine(Root, "unity_site/water_2000.bin"), c0[0].GetDouble(), c0[1].GetDouble(), 0);
            var ta = site.GetProperty("tip_a_xz"); var tb = site.GetProperty("tip_b_xz");
            double ax = ta[0].GetDouble(), az = ta[1].GetDouble(), bx = tb[0].GetDouble(), bz = tb[1].GetDouble();
            double mx = (ax + bx) / 2, mz = (az + bz) / 2, len = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
            double ux = (bx - ax) / len, uz = (bz - az) / len, sx = -uz, sz = ux; if (sz < 0) { sx = -sx; sz = -sz; }
            double xs = len / Site.GapWidthM;
            bool Water(double x, double y) => grid.IsWaterAt(mx + ux * x * xs + sx * y, mz + uz * x * xs + sz * y);
            var free = new CurtainHold(0.14, 3.0, seed: 0).Run(3);
            var bounded = new CurtainHold(0.14, 3.0, seed: 0).ConstrainToWater(Water).Run(3);
            int onLand = 0; for (int i = 0; i < bounded.N; i++) if (!bounded.Inside[i] && !Water(bounded.X[i], bounded.Y[i])) onLand++;
            int startLandFree = 0, startLandBounded = 0;
            for (int i = 0; i < free.N; i++) { if (!Water(free.StartX[i], free.StartY[i])) startLandFree++; if (!Water(bounded.StartX[i], bounded.StartY[i])) startLandBounded++; }
            Check("Real moles: no jellyfish on land", onLand == 0 && startLandBounded == 0,
                  $"start on land: curtain.py layout {startLandFree}, bounded {startLandBounded}; after 3 h on land: {onLand}; held {bounded.HeldShare:P1} (curtain.py layout {free.HeldShare:P1})");
        }

        // Season check on the real 2025 record
        var rec = ForcingRecord.Load(Path.Combine(Root, "model/qandeel/data/gulf_forcing.csv"));
        var sa = SmartAir.Season(rec);
        Check("Season check", sa.Hours == 2928 && Math.Round(sa.AirflowMean, 2) == 1.29 && Math.Round(sa.EnergyVs3Pct) == 43
                              && Math.Round(sa.EnergyVsMaxPct) == 29 && Math.Round(sa.ShareAbove3, 3) == 0.004,
              $"{sa.Hours} h, mean {sa.AirflowMean:F2} L/s/m, {sa.EnergyVs3Pct:F0}% of fixed 3, {sa.EnergyVsMaxPct:F0}% of max, above 3 in {sa.ShareAbove3:P1} of hours");
        Check("2025 approach current", Math.Round(sa.ApproachMean, 2) == 0.14 && Math.Abs(sa.ApproachP95 - 0.234) < 0.0015,
              $"mean {sa.ApproachMean:F3}, worst 5% {sa.ApproachP95:F3} m/s (Python 0.140 / 0.234)");

        // Forecast, 15 km NNW (bearing 330), 400 runs x 60 jellyfish
        if (!quick)
        {
            var f = Planner.ForecastArrival(15, 330, rec, seed: 0);
            bool ok = Math.Abs(f.PArrive - 0.44) <= 0.05 && Math.Abs(f.P10H.Value - 15.9) <= 3 && Math.Abs(f.P50H.Value - 42.1) <= 4
                      && Math.Abs(f.P90H.Value - 72.7) <= 5 && Math.Abs(f.SwitchOnH.Value - 12.9) <= 3 && f.Side == "west";
            Check("Forecast 15 km NNW", ok,
                  $"{f.PArrive:P0}, window {f.P10H:F1}-{f.P90H:F1} h, bulk {f.P50H:F1} h, curtain on +{f.SwitchOnH:F1} h, {f.Side} (Python 44%, 15.9-72.7, 42.1, +12.9, west)");
            var fn = Planner.ForecastArrival(28, 0, rec, seed: 2);
            var fw = Planner.ForecastArrival(28, 285, rec, seed: 1);
            Check("Forecast 28 km N / WNW", Math.Abs(fn.PArrive - 0.117) <= 0.05 && fw.PArrive <= 0.04,
                  $"N {fn.PArrive:P0} (Python 12%), WNW {fw.PArrive:P1} (Python 1%)");
        }

        Console.WriteLine($"\n{passed} passed, {failed} failed in {(DateTime.Now - t0).TotalSeconds:F1} s");
        return failed == 0 ? 0 : 1;
    }
}
