using System;
using System.Collections.Generic;

namespace Qandeel.Core
{
    /// <summary>Adaptive airflow over the real season (benefits.adaptive_airflow).</summary>
    public static class SmartAir
    {
        public const double IntakeDrawMps = 0.08;     // ASSUMED flow toward the gap from the intake (planning.INTAKE_DRAW_M_S)
        public const double PublishedMarginMps = 0.175; // results.json adaptive_airflow.margin_m_s (from hold_margin)
        public const double QMin = 1.0, QMax = 4.5, QNormal = 3.0;

        public struct Result
        {
            public int Hours;
            public double ApproachMean, ApproachP95, AirflowMean, ShareAbove3, ShareAboveMax, EnergyVsMaxPct, EnergyVs3Pct;
            public double[] Approach, QSet;
        }

        /// <summary>Approach current toward the mouth in hour h: intake draw plus the onshore part of current + wind drift.</summary>
        public static double ApproachAt(ForcingRecord rec, int h) => IntakeDrawMps + Math.Max(0.0, -rec.TotalV(h));

        public static double AirflowSetting(double approach, double margin = PublishedMarginMps)
            => Math.Min(QMax, Math.Max(QMin, Bulson.AirflowFor(approach + margin)));

        public static Result Season(ForcingRecord rec, double margin = PublishedMarginMps)
        {
            int n = rec.Hours;
            var approach = new double[n]; var qSet = new double[n];
            double sumA = 0, sumQ = 0; int above3 = 0, aboveMax = 0;
            for (int h = 0; h < n; h++)
            {
                approach[h] = ApproachAt(rec, h);
                double need = Bulson.AirflowFor(approach[h] + margin);
                qSet[h] = Math.Min(QMax, Math.Max(QMin, need));
                sumA += approach[h]; sumQ += qSet[h];
                if (need > 3.0) above3++;
                if (need > QMax) aboveMax++;
            }
            double qMean = sumQ / n;
            return new Result
            {
                Hours = n, ApproachMean = sumA / n, ApproachP95 = Stats.Percentile(approach, 95),
                AirflowMean = qMean, ShareAbove3 = (double)above3 / n, ShareAboveMax = (double)aboveMax / n,
                EnergyVsMaxPct = 100 * qMean / QMax, EnergyVs3Pct = 100 * qMean / QNormal,
                Approach = approach, QSet = qSet,
            };
        }

        /// <summary>benefits.hold_margin: fit V_max = U0 - margin, the largest approach held >= 95% for each airflow.</summary>
        public static (double margin, double[] vmax) HoldMargin(double swimMax = 0.10, int n = 250, ulong seed = 0)
        {
            var qs = new[] { 1.5, 3.0, 4.5 };
            var vm = new double[3]; double sum = 0;
            for (int i = 0; i < 3; i++)
            {
                double vmax = 0;
                for (double v = 0.05; v < 0.45 - 1e-9; v += 0.025)
                {
                    if (CurtainHold.HeldShareOf(v, qs[i], hours: 2.0, n: n, swimMax: swimMax, seed: seed) >= 0.95) vmax = v;
                    else break;
                }
                vm[i] = vmax; sum += Bulson.SurfaceCurrent(qs[i]) - vmax;
            }
            return (sum / 3, vm);
        }
    }
}
