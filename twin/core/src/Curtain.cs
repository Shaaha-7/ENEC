using System;

namespace Qandeel.Core
{
    /// <summary>Bubble-curtain physics. Port of model/qandeel/sim/curtain.py.</summary>
    public static class Bulson
    {
        public const double G = 9.81;

        /// <summary>Peak surface current (m/s) for free-air flow q in L/s per metre: U0 = 1.46 (g q)^(1/3).</summary>
        public static double SurfaceCurrent(double qLps) => qLps <= 0 ? 0.0 : 1.46 * Math.Pow(G * qLps / 1000.0, 1.0 / 3.0);

        /// <summary>Inverse of SurfaceCurrent: airflow (L/s per m) giving surface current u0 (benefits.airflow_for).</summary>
        public static double AirflowFor(double u0) => Math.Pow(u0 / 1.46, 3) / G * 1000.0;
    }

    /// <summary>
    /// Near-mouth hold simulation (curtain.py run_hold), step by step so a renderer can draw every jellyfish.
    /// Frame: x along the breakwater (gap centred on x = 0), y offshore. Breakwater on y = 0, bubble line at y = CurtainY.
    /// </summary>
    public sealed class CurtainHold
    {
        public const double CurtainY = 5.0;

        public readonly int N;
        public readonly double[] X, Y, StartX, StartY, Swim, Heading;
        public readonly bool[] Inside; // crossed the bubble line over the gap
        public double ApproachSpeed, AirflowLps, Depth, GapWidth, AlongshoreAmp;
        public readonly double Dt;
        public int StepIndex { get; private set; }
        public double TimeHours => StepIndex * Dt / 3600.0;
        public int Entered { get; private set; }
        public double HeldShare => 1.0 - (double)Entered / N;

        /// <summary>Optional per-section switch: section i covers x in [-gap/2 + i w, -gap/2 + (i+1) w).
        /// Python has no sections; with all on the result is identical.</summary>
        public bool[] SectionOn;

        /// <summary>Per-jellyfish depth (m) and the outflow-layer thickness (ASSUMED, not measured). Python's run_hold is
        /// surface-only: all depths 0 and an infinite layer, which is the default here. Jellyfish below the layer get no push
        /// (DepthRisk's worst case).</summary>
        public readonly double[] DepthM;
        public double OutflowLayerM = double.PositiveInfinity;

        readonly Rng rng;
        readonly double turnSd, diffSd;

        public CurtainHold(double approachSpeed = 0.15, double airflowLps = 3.0, int n = 500, double swimMin = 0.02,
                           double swimMax = 0.10, double depth = Site.GapDepthM, double gapWidth = Site.GapWidthM,
                           double alongshoreAmp = 0.08, ulong seed = 0, double dt = 2.0, int sections = Site.Sections)
        {
            N = n; ApproachSpeed = approachSpeed; AirflowLps = airflowLps; Depth = depth; GapWidth = gapWidth;
            AlongshoreAmp = alongshoreAmp; Dt = dt;
            rng = new Rng(seed);
            X = new double[n]; Y = new double[n]; Swim = new double[n]; Heading = new double[n]; Inside = new bool[n];
            double spread = gapWidth / 2 + 100;
            for (int i = 0; i < n; i++) { X[i] = rng.Uniform(-spread, spread); Y[i] = rng.Uniform(150, 500); }
            StartX = (double[])X.Clone(); StartY = (double[])Y.Clone();
            for (int i = 0; i < n; i++) Swim[i] = rng.Uniform(swimMin, swimMax);
            for (int i = 0; i < n; i++) Heading[i] = rng.Uniform(0, 2 * Math.PI);
            turnSd = Math.Sqrt(dt / 120.0);       // heading wanders over ~2 minutes
            diffSd = Math.Sqrt(2 * 0.05 * dt);    // small-scale mixing
            SectionOn = new bool[sections];
            for (int i = 0; i < sections; i++) SectionOn[i] = true;
            DepthM = new double[n];
            HeldS = new double[n]; Pocketed = new bool[n];
            this.seed = seed;
        }

        readonly ulong seed;

        /// <summary>Gives the swarm an exponential depth profile with the given mean (as DepthRisk) and sets the
        /// outflow layer. Uses its own random stream, so the surface results are unchanged.</summary>
        public CurtainHold WithSwarmDepth(double meanDepthM, double outflowLayerM)
        {
            var r = new Rng(seed ^ 0xD3E9_7A11UL);
            for (int i = 0; i < N; i++) DepthM[i] = -meanDepthM * Math.Log(1.0 - r.NextDouble());
            OutflowLayerM = outflowLayerM;
            return this;
        }

        public bool InLayer(int i) => DepthM[i] < OutflowLayerM;

        /// <summary>Optional real-site boundary (physics frame x, y -> true if water). curtain.py models one straight wall
        /// with the gap; the real moles reach out to sea from the tips. When set, no jellyfish starts on land and a step
        /// that would end on land is refused (the jellyfish stays and turns). Null by default: identical to curtain.py.</summary>
        public Func<double, double, bool> IsWater { get; private set; }

        public CurtainHold ConstrainToWater(Func<double, double, bool> isWater)
        {
            IsWater = isWater;
            var r = new Rng(seed ^ 0x5EA_7A7EUL); // own stream: the physics draws are unchanged
            double spread = GapWidth / 2 + 100;
            for (int i = 0; i < N; i++)
                for (int k = 0; k < 200 && !isWater(X[i], Y[i]); k++)
                {
                    X[i] = r.Uniform(-spread, spread); Y[i] = r.Uniform(150, 500);
                    StartX[i] = X[i]; StartY[i] = Y[i];
                }
            return this;
        }

        // Catch pockets at both breakwater tips (PROMPT.md 6: only jellyfish the curtain has held >= 30 min).
        // "Held" time accumulates while a jellyfish is seaward of the bubble line within HoldZoneM of it, over the gap.
        // Pocket position and capture radius are DESIGN ASSUMPTIONS (moored boom pocket at each tip).
        public const double HoldZoneM = 100.0, MinHeldS = 1800.0, PocketRadiusM = 40.0, PocketOffsetM = 15.0;
        public readonly double[] HeldS;
        public readonly bool[] Pocketed;
        public int PocketedCount { get; private set; }
        public int PocketedA { get; private set; }
        public int PocketedB { get; private set; }
        public bool PocketsOn; // off by default so results stay comparable with curtain.py (no pockets there)

        void UpdateHoldAndPockets(int i)
        {
            double half = GapWidth / 2, x = X[i], y = Y[i];
            if (Math.Abs(x) <= half && y >= CurtainY && y - CurtainY <= HoldZoneM) HeldS[i] += Dt;
            if (!PocketsOn || HeldS[i] < MinHeldS) return;
            double py = CurtainY + PocketOffsetM, r2 = PocketRadiusM * PocketRadiusM;
            double da = (x + half) * (x + half) + (y - py) * (y - py), db = (x - half) * (x - half) + (y - py) * (y - py);
            if (da <= r2 || db <= r2) { Pocketed[i] = true; PocketedCount++; if (da <= r2) PocketedA++; else PocketedB++; }
        }

        /// <summary>Gathering unit: a boom (Desmi RO-BOOM 2000 class, ~66 m U-opening) swept through the held swarm in front
        /// of the curtain closes its bag on every jellyfish the curtain has held >= 30 min within `radiusM` of (x, y).
        /// The bag keeps 100% (base case; 84-93% pessimistic, design.json). Returns how many were taken.</summary>
        public int Gather(double x, double y, double radiusM = 33.0)
        {
            int n = 0; double r2 = radiusM * radiusM;
            for (int i = 0; i < N; i++)
            {
                if (Inside[i] || Pocketed[i] || HeldS[i] < MinHeldS) continue;
                double dx = X[i] - x, dy = Y[i] - y;
                if (dx * dx + dy * dy <= r2) { Pocketed[i] = true; n++; }
            }
            Gathered += n;
            return n;
        }

        public int Gathered { get; private set; }

        /// <summary>Centroid of jellyfish held >= 30 min (for the boat to aim at); false if none.</summary>
        public bool HeldCentroid(out double x, out double y, out int count)
        {
            x = y = 0; count = 0;
            for (int i = 0; i < N; i++)
                if (!Inside[i] && !Pocketed[i] && HeldS[i] >= MinHeldS) { x += X[i]; y += Y[i]; count++; }
            if (count == 0) return false;
            x /= count; y /= count; return true;
        }

        /// <summary>Removes the jellyfish in pocket at tip a (west) or b (east) from the count, as a boat empties it.</summary>
        public int EmptyPocket(bool west) { int n = west ? PocketedA : PocketedB; if (west) PocketedA = 0; else PocketedB = 0; return n; }

        bool CurtainOnAt(double x)
        {
            double half = GapWidth / 2;
            if (x <= -half) return SectionOn[0];
            if (x >= half) return SectionOn[SectionOn.Length - 1];
            int i = (int)((x + half) / (GapWidth / SectionOn.Length));
            return SectionOn[Math.Min(i, SectionOn.Length - 1)];
        }

        public void Step()
        {
            double tH = StepIndex * Dt / 3600.0;
            double u0 = Bulson.SurfaceCurrent(AirflowLps);
            double decay = 2 * Depth, half = GapWidth / 2;
            double tide = AlongshoreAmp * Math.Sin(2 * Math.PI * tH / 12.42);
            for (int i = 0; i < N; i++)
            {
                if (Inside[i] || Pocketed[i]) continue;
                double px = X[i], py = Y[i];
                double r = Math.Sqrt(px * px + py * py) + 1e-9;
                double vx = -ApproachSpeed * px / r + tide, vy = -ApproachSpeed * py / r;
                if (u0 > 0 && CurtainOnAt(px) && DepthM[i] < OutflowLayerM)
                {
                    double dy = py - CurtainY, dxOut = Math.Max(Math.Abs(px) - half, 0.0);
                    vy += Math.Sign(dy) * u0 * Math.Exp(-Math.Sqrt(dy * dy + dxOut * dxOut) / decay);
                }
                Heading[i] += rng.Normal(0, turnSd);
                vx += Swim[i] * Math.Cos(Heading[i]);
                vy += Swim[i] * Math.Sin(Heading[i]);
                double nx = px + vx * Dt + rng.Normal(0, diffSd), ny = py + vy * Dt + rng.Normal(0, diffSd);
                if (ny < 0 && Math.Abs(nx) > half) ny = 0;          // breakwater is solid outside the gap
                if (IsWater != null && !IsWater(nx, ny)) { nx = px; ny = py; Heading[i] += Math.PI; } // real moles: stay in water
                X[i] = nx; Y[i] = ny;
                if (ny < CurtainY && Math.Abs(nx) <= half) { Inside[i] = true; Entered++; }
                else UpdateHoldAndPockets(i);
            }
            StepIndex++;
        }

        public CurtainHold Run(double hours)
        {
            int steps = (int)(hours * 3600 / Dt);
            for (int k = 0; k < steps; k++) Step();
            return this;
        }

        /// <summary>Share held after `hours`, as run_hold(...).held_share.</summary>
        public static double HeldShareOf(double approach, double airflow, double hours = 3.0, int n = 500,
                                         double swimMax = 0.10, ulong seed = 0)
            => new CurtainHold(approach, airflow, n, swimMax: swimMax, seed: seed).Run(hours).HeldShare;
    }

    /// <summary>
    /// Dense-body (depth) risk. Reconstructed from outputs/depth_sensitivity.json; the kit has no Python source for it.
    /// Swarm depth is exponential with the given mean, so the share inside the outflow layer is 1 - exp(-layer / mean).
    /// Worst case: jellyfish below the layer all pass. Best case: half of them are still held.
    /// The outflow-layer thickness is an ASSUMPTION (not measured). Reproduces all 8 published rows to 2 decimals.
    /// </summary>
    public static class DepthRisk
    {
        public static double ShareInLayer(double meanDepthM, double layerM) => 1.0 - Math.Exp(-layerM / meanDepthM);
        public static double HeldWorst(double meanDepthM, double layerM, double surfaceHold = 1.0)
            => surfaceHold * ShareInLayer(meanDepthM, layerM);
        public static double HeldBest(double meanDepthM, double layerM, double surfaceHold = 1.0)
        {
            double s = ShareInLayer(meanDepthM, layerM);
            return surfaceHold * (s + 0.5 * (1 - s));
        }
    }
}
