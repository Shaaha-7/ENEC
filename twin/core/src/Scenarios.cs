namespace Qandeel.Core
{
    /// <summary>Free-play presets near the mouth (PROMPT.md section 7). Every value comes from the kit; assumptions say so.
    /// Rough sea and the 200 t bloom are not here: wave weakening is not quantified (not modelled), and the bloom is a
    /// gathering/boat scenario for a later milestone.</summary>
    public sealed class Scenario
    {
        public string Name, Source;
        public double AirflowLps, ApproachMps, MeanDepthM, OutflowLayerM;
        public int[] SectionsOff = new int[0]; // 0-based
        public bool Replay2025, Bloom;

        public CurtainHold Create(ulong seed = 0, int n = 500)
        {
            var sim = new CurtainHold(ApproachMps, AirflowLps, n, seed: seed);
            if (MeanDepthM > 0) sim.WithSwarmDepth(MeanDepthM, OutflowLayerM);
            foreach (var s in SectionsOff) sim.SectionOn[s] = false;
            sim.PocketsOn = true;
            return sim;
        }

        public static readonly Scenario[] All =
        {
            new Scenario { Name = "Typical 2025 day", AirflowLps = 3.0, ApproachMps = 0.14,
                Source = "Approach 0.14 m/s = 2025 mean (Copernicus + ERA5); normal air 3 L/s/m" },
            new Scenario { Name = "Worst 5% current", AirflowLps = 3.0, ApproachMps = 0.23,
                Source = "Approach 0.23 m/s = worst 5% of 2025 hours" },
            new Scenario { Name = "Too little air", AirflowLps = 1.0, ApproachMps = 0.23,
                Source = "1 L/s/m against the worst 5% current: the curtain is overpowered" },
            new Scenario { Name = "Deep swarm (2 m)", AirflowLps = 3.0, ApproachMps = 0.14, MeanDepthM = 2.0, OutflowLayerM = 1.0,
                Source = "Swarm mean depth 2 m; outflow layer 1 m ASSUMED (not measured): pessimistic case" },
            new Scenario { Name = "Section 5 fails", AirflowLps = 3.0, ApproachMps = 0.14, SectionsOff = new[] { 4 },
                Source = "One central 96.7 m section off; the approach flow funnels toward the centre" },
            new Scenario { Name = "Real 2025 replay", AirflowLps = 3.0, ApproachMps = 0.14, Replay2025 = true,
                Source = "Hourly approach current from real 2025 Copernicus currents + ERA5 wind (1 Jun-30 Sep); Auto air sets the airflow each hour" },
            new Scenario { Name = "200 t/day bloom", AirflowLps = 3.0, ApproachMps = 0.14, Bloom = true,
                Source = "200 t/day = 8.3 t/h. E.g. 30 t bags + 3 shuttle tugs: 15 boats (3 km release) or 10 boats (5 km). The curtain has no tonnage limit in the model" },
        };
    }
}
