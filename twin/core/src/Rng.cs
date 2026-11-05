using System;

namespace Qandeel.Core
{
    /// <summary>Deterministic seeded generator (xoshiro256**), identical on every platform and in Unity.
    /// numpy's PCG64 stream cannot be reproduced, so results match the Python models statistically, not draw for draw.</summary>
    public sealed class Rng
    {
        ulong s0, s1, s2, s3;
        double spare;
        bool hasSpare;

        public Rng(ulong seed)
        {
            ulong x = seed;
            s0 = SplitMix(ref x); s1 = SplitMix(ref x); s2 = SplitMix(ref x); s3 = SplitMix(ref x);
        }

        static ulong SplitMix(ref ulong x)
        {
            ulong z = x += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

        public ulong NextULong()
        {
            ulong result = Rotl(s1 * 5, 7) * 9;
            ulong t = s1 << 17;
            s2 ^= s0; s3 ^= s1; s1 ^= s2; s0 ^= s3; s2 ^= t; s3 = Rotl(s3, 45);
            return result;
        }

        /// <summary>Uniform in [0, 1).</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

        public double Uniform(double lo, double hi) => lo + (hi - lo) * NextDouble();

        /// <summary>Integer in [lo, hi), like numpy's integers(lo, hi).</summary>
        public int Integer(int lo, int hi) => lo + (int)(NextDouble() * (hi - lo));

        public double Normal(double mean, double sd)
        {
            if (hasSpare) { hasSpare = false; return mean + sd * spare; }
            double u, v, s;
            do { u = 2 * NextDouble() - 1; v = 2 * NextDouble() - 1; s = u * u + v * v; } while (s >= 1 || s == 0);
            double m = Math.Sqrt(-2 * Math.Log(s) / s);
            spare = v * m; hasSpare = true;
            return mean + sd * u * m;
        }
    }
}
