using System;
using System.Collections.Generic;

namespace Qandeel.Core
{
    /// <summary>Monte Carlo drift planner after an alert (planning.py simulate_cloud / forecast_arrival / switch_on_time),
    /// in season-record mode: each run starts at a random hour of the real 2025 season.
    /// Frame: x = east, y = north, coast on y = 0, the mouth at the origin.</summary>
    public static class Planner
    {
        public const double DtS = 600.0;
        public const double EddyDiffusivity = 20.0;   // m^2/s, ASSUMED
        public const double ArriveRadiusM = 2000.0;
        public const double LeadShare = 0.05;
        public const double SwitchOnBufferH = 3.0;
        public const double MinArrivalProb = 0.10;

        public sealed class Cloud
        {
            public int Runs, Particles;
            public double[,] ArriveH, ArriveX, BeachX;   // NaN = never
            public float[][,] Hourly;                     // [hour] (keepRuns*particles, 2) positions for the forecast map
        }

        public sealed class Forecast
        {
            public int Runs;
            public double PArrive, ShareReaching;
            public double? P10H, P50H, P90H;
            public string Side;
            public Cloud Cloud;
            public double? SwitchOnH => (P10H == null || PArrive < MinArrivalProb) ? (double?)null : Math.Max(0.0, P10H.Value - SwitchOnBufferH);
        }

        public static (double x, double y) BearingToXY(double km, double bearingDeg)
        {
            double b = bearingDeg * Math.PI / 180.0;
            return (1000 * km * Math.Sin(b), 1000 * km * Math.Cos(b));
        }

        public static Cloud SimulateCloud(double sx, double sy, double hours, Rng rng, ForcingRecord rec, int runs = 400,
                                          int nParticles = 60, double spreadM = 1500, double swimMax = 0.10,
                                          int keepRuns = 0, double arriveRadiusM = ArriveRadiusM)
        {
            int steps = (int)(hours * 3600 / DtS);
            // velocity_series, season mode: random start hour per run, interpolated, +-20% strength and a small bias
            int nh = rec.Hours;
            var start = new int[runs]; var gain = new double[runs]; var bu = new double[runs]; var bv = new double[runs];
            for (int r = 0; r < runs; r++) start[r] = rng.Integer(0, nh - (int)Math.Ceiling(hours) - 1);
            for (int r = 0; r < runs; r++) gain[r] = rng.Normal(1.0, 0.2);
            for (int r = 0; r < runs; r++) { bu[r] = rng.Normal(0, 0.02); bv[r] = rng.Normal(0, 0.02); }

            int n = runs * nParticles;
            var px = new double[n]; var py = new double[n]; var hd = new double[n]; var sp = new double[n];
            var arrH = new double[runs, nParticles]; var arrX = new double[runs, nParticles]; var beach = new double[runs, nParticles];
            for (int i = 0; i < n; i++) { px[i] = sx + rng.Normal(0, spreadM); py[i] = Math.Max(sy + rng.Normal(0, spreadM), 50.0); }
            for (int i = 0; i < n; i++) hd[i] = rng.Uniform(0, 2 * Math.PI);
            for (int i = 0; i < n; i++) sp[i] = rng.Uniform(0.02, swimMax);
            for (int r = 0; r < runs; r++) for (int j = 0; j < nParticles; j++) { arrH[r, j] = double.NaN; arrX[r, j] = double.NaN; beach[r, j] = double.NaN; }

            var hourly = new List<float[,]>();
            int perH = (int)(3600 / DtS);
            void Snapshot()
            {
                if (keepRuns <= 0) return;
                int k = Math.Min(keepRuns, runs) * nParticles;
                var a = new float[k, 2];
                for (int i = 0; i < k; i++) { a[i, 0] = (float)px[i]; a[i, 1] = (float)py[i]; }
                hourly.Add(a);
            }
            Snapshot();
            double stepSd = Math.Sqrt(2 * EddyDiffusivity * DtS), turnSd = Math.Sqrt(DtS / 1800.0);
            double r2 = arriveRadiusM * arriveRadiusM;
            for (int k = 0; k < steps; k++)
            {
                double tH = k * DtS / 3600.0;
                for (int r = 0; r < runs; r++)
                {
                    double idx = start[r] + tH; int lo = (int)Math.Floor(idx); double fr = idx - lo;
                    double vu = (rec.TotalU(lo) * (1 - fr) + rec.TotalU(lo + 1) * fr) * gain[r] + bu[r];
                    double vv = (rec.TotalV(lo) * (1 - fr) + rec.TotalV(lo + 1) * fr) * gain[r] + bv[r];
                    for (int j = 0; j < nParticles; j++)
                    {
                        int i = r * nParticles + j;
                        hd[i] += rng.Normal(0, turnSd);
                        bool active = double.IsNaN(arrH[r, j]) && double.IsNaN(beach[r, j]);
                        double dx = (vu + sp[i] * Math.Sin(hd[i])) * DtS + rng.Normal(0, stepSd);
                        double dy = (vv + sp[i] * Math.Cos(hd[i])) * DtS + rng.Normal(0, stepSd);
                        if (!active) continue;
                        px[i] += dx; py[i] = Math.Max(py[i] + dy, 0.0);
                        if (px[i] * px[i] + py[i] * py[i] <= r2) { arrH[r, j] = (k + 1) * DtS / 3600.0; arrX[r, j] = px[i]; }
                        else if (py[i] <= 0) beach[r, j] = px[i]; // stranded away from the gap
                    }
                }
                if ((k + 1) % perH == 0) Snapshot();
            }
            return new Cloud { Runs = runs, Particles = nParticles, ArriveH = arrH, ArriveX = arrX, BeachX = beach, Hourly = hourly.ToArray() };
        }

        public static Forecast ForecastArrival(double distanceKm, double bearingDeg, ForcingRecord rec, int runs = 400,
                                               double horizonH = 96.0, ulong seed = 0, int nParticles = 60,
                                               double swimMax = 0.10, int keepRuns = 0, double arriveRadiusM = ArriveRadiusM)
        {
            var rng = new Rng(seed);
            var (sx, sy) = BearingToXY(distanceKm, bearingDeg);
            var c = SimulateCloud(sx, sy, horizonH, rng, rec, runs, nParticles, swimMax: swimMax, keepRuns: keepRuns, arriveRadiusM: arriveRadiusM);
            int kLead = Math.Max(1, (int)Math.Ceiling(LeadShare * nParticles));
            var leads = new List<double>(); var bulks = new List<double>(); var xs = new List<double>();
            int reachedTotal = 0;
            for (int r = 0; r < runs; r++)
            {
                var arr = new List<double>();
                for (int j = 0; j < nParticles; j++)
                {
                    if (!double.IsNaN(c.ArriveH[r, j])) { arr.Add(c.ArriveH[r, j]); reachedTotal++; }
                    if (!double.IsNaN(c.ArriveX[r, j])) xs.Add(c.ArriveX[r, j]);
                }
                if (arr.Count >= kLead) { arr.Sort(); leads.Add(arr[kLead - 1]); bulks.Add(Stats.Median(arr)); }
            }
            var f = new Forecast { Runs = runs, Cloud = c, PArrive = (double)leads.Count / runs, ShareReaching = (double)reachedTotal / (runs * nParticles) };
            if (leads.Count > 0) { f.P10H = Stats.Percentile(leads, 10); f.P50H = Stats.Median(bulks); f.P90H = Stats.Percentile(bulks, 90); }
            if (xs.Count > 0) { double m = Stats.Median(xs); f.Side = m < -500 ? "west" : m > 500 ? "east" : "centre"; }
            return f;
        }
    }
}
