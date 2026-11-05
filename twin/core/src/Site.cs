using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Qandeel.Core
{
    /// <summary>Site constants from model/qandeel/site.json and outputs/design.json.</summary>
    public static class Site
    {
        public const double GapWidthM = 967.0;   // measured tip to tip
        public const double GapDepthM = 9.5;     // NOT surveyed
        public const int Sections = 10;
        public const double SectionLengthM = 96.7;
    }

    /// <summary>Hourly 2025 bloom-season forcing (model/qandeel/data/gulf_forcing.csv), as forcing.load_record.</summary>
    public sealed class ForcingRecord
    {
        public const double WindDriftFactor = 0.01; // env.WIND_DRIFT_FACTOR: 1% of wind speed

        public readonly string[] Time;
        public readonly double[] CurU, CurV, WindU, WindV; // m/s, east / north

        public int Hours => Time.Length;

        ForcingRecord(string[] t, double[] cu, double[] cv, double[] wu, double[] wv)
        { Time = t; CurU = cu; CurV = cv; WindU = wu; WindV = wv; }

        public static ForcingRecord Load(string csvPath, double windFactor = WindDriftFactor)
            => Parse(File.ReadAllLines(csvPath), windFactor);

        public static ForcingRecord Parse(string csvText, double windFactor = WindDriftFactor)
            => Parse(csvText.Split('\n'), windFactor);

        static ForcingRecord Parse(IEnumerable<string> lines, double windFactor)
        {
            var t = new List<string>(); var cu = new List<double>(); var cv = new List<double>();
            var wu = new List<double>(); var wv = new List<double>();
            var inv = CultureInfo.InvariantCulture;
            bool header = true;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (header) { header = false; continue; }
                if (line.Length == 0) continue;
                var f = line.Split(',');
                t.Add(f[0]);
                cu.Add(double.Parse(f[1], inv)); cv.Add(double.Parse(f[2], inv));
                double to = (double.Parse(f[4], inv) + 180.0) * Math.PI / 180.0;
                double s = windFactor * double.Parse(f[3], inv);
                wu.Add(s * Math.Sin(to)); wv.Add(s * Math.Cos(to));
            }
            return new ForcingRecord(t.ToArray(), cu.ToArray(), cv.ToArray(), wu.ToArray(), wv.ToArray());
        }

        public double TotalU(int h) => CurU[h] + WindU[h];
        public double TotalV(int h) => CurV[h] + WindV[h];
    }

    public static class Stats
    {
        /// <summary>numpy.percentile with the default linear interpolation.</summary>
        public static double Percentile(IList<double> values, double p)
        {
            var a = new List<double>(values); a.Sort();
            if (a.Count == 0) return double.NaN;
            double idx = p / 100.0 * (a.Count - 1);
            int lo = (int)Math.Floor(idx), hi = Math.Min(lo + 1, a.Count - 1);
            return a[lo] + (a[hi] - a[lo]) * (idx - lo);
        }

        public static double Median(IList<double> values) => Percentile(values, 50);
    }
}
