using System;

namespace Qandeel.Core
{
    /// <summary>
    /// Operating rules for the demo controls. These sit on top of the physics and do not change it.
    /// Sources: narration topic.boats (boats stand by above 1 m waves, shelter above 2 m, the curtain keeps working);
    /// design facts (3 oil-free screw blowers, 2 duty + 1 standby, ~87 m3/min each, 174 m3/min at 3 L/s/m);
    /// depth_sensitivity.json (mean depth 2 m: 39-70% held with a 1 m outflow layer, 63-82% with 2 m).
    /// </summary>
    public static class Operations
    {
        public enum Boats { Run, StandBy, InPort }

        /// <summary>Boat rule by significant wave height. The curtain runs in every case; the wave effect on the
        /// curtain itself is NOT modelled (not quantified in the evidence).</summary>
        public static Boats ForWaves(double hsM) => hsM < 1.0 ? Boats.Run : hsM <= 2.0 ? Boats.StandBy : Boats.InPort;

        public const int Blowers = 3, DutyBlowers = 2;
        /// <summary>Each duty blower supplies half the normal flow (174 m3/min at 3 L/s/m over 967 m): ~87 m3/min.</summary>
        public static readonly double BlowerM3PerMin = 3.0 * Site.GapWidthM * 60.0 / 1000.0 / DutyBlowers;

        /// <summary>Airflow (L/s per m) the blowers can still deliver after `tripped` units trip: the standby unit
        /// takes over the first trip, so the curtain loses nothing until a second unit trips.</summary>
        public static double AirflowAfterTrips(double demandLps, int tripped)
        {
            int running = Math.Min(DutyBlowers, Blowers - Math.Max(0, tripped));
            double capLps = running * BlowerM3PerMin * 1000.0 / 60.0 / Site.GapWidthM;
            return Math.Min(demandLps, capLps);
        }

        public static int RunningBlowers(int tripped) => Math.Max(0, Math.Min(DutyBlowers, Blowers - tripped));

        /// <summary>Deep-swarm demo: swarm mean depth 2 m. The outflow layer is ASSUMED 1 m at normal air and 2 m with
        /// high air (maximum 4.5 L/s/m); neither thickness is measured.</summary>
        public const double DeepSwarmMeanM = 2.0, LayerNormalM = 1.0, LayerHighAirM = 2.0, HighAirLps = 4.5;

        public static (double lo, double hi) ExpectedHeld(bool highAir)
        {
            double layer = highAir ? LayerHighAirM : LayerNormalM;
            return (DepthRisk.HeldWorst(DeepSwarmMeanM, layer), DepthRisk.HeldBest(DeepSwarmMeanM, layer));
        }

        /// <summary>Plant response for the stand: Barakah has 4 units of ~1,400 MWe (5,600 MW). ILLUSTRATIVE, NOT MODELLED:
        /// one unit is taken offline for every 10% of the modelled swarm that reaches the intake screens within an hour.
        /// Shows why holding the swarm matters; the real screen capacity and trip logic are the operator's.</summary>
        public const int PlantUnits = 4; public const double UnitMW = 1400.0, TripSharePerUnit = 0.10;

        public static int UnitsOnline(double passedLastHourShare) =>
            Math.Max(0, PlantUnits - (int)Math.Floor(Math.Max(0, passedLastHourShare) / TripSharePerUnit));

        public static double PlantMW(double passedLastHourShare) => UnitsOnline(passedLastHourShare) * UnitMW;
    }
}
