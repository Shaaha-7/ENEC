"""Operator view: turn one early-warning alert into a response plan.

    python -m qandeel.plan_alert --distance 28 --bearing 285
    python -m qandeel.plan_alert --distance 28 --bearing 0 --wind 4 --wind-from 180
"""
import argparse

from qandeel.sim.env import Conditions
from qandeel.sim.planning import approach_section, forecast_arrival, plan_release, switch_on_time

SIDES = {"A": "west", "B": "centre", "C": "east"}


def compass(bearing):
    names = ["N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW"]
    return names[round(bearing % 360 / 22.5) % 16]


def plan(distance_km, bearing_deg, cond: Conditions, runs=400, seed=0) -> str:
    fc = forecast_arrival(distance_km, bearing_deg, cond, runs=runs, seed=seed)
    head = f"ALERT  Swarm {distance_km:g} km {compass(bearing_deg)} of the intake gap"
    lines = [head, "-" * len(head)]
    if fc.p_arrive < 0.10:
        lines += [f"Chance of reaching the gap in 96 h: {fc.p_arrive:.0%}",
                  "Action: curtain stays OFF; keep tracking, re-plan on the next detection update."]
        return "\n".join(lines)
    t_on = switch_on_time(fc)
    best = plan_release(cond)[0]
    lines += [
        f"Chance of reaching the gap in 96 h: {fc.p_arrive:.0%}",
        f"Arrival window (10-90%):            {fc.p10_h:.0f}-{fc.p90_h:.0f} h after alert (median {fc.p50_h:.0f} h)",
        f"Curtain switch-on:                  in {t_on:.0f} h (earliest likely arrival minus 3 h buffer)",
        f"Swarm approaches from the:          {SIDES[approach_section(fc)]} side; stage the boom crew there",
        f"Release point:                      {best.distance_km:g} km {compass(best.bearing_deg)} of the gap, "
        f"{best.tow_hours:.0f} h tow at 0.3 m/s",
        f"Simulated return within 72 h:       {best.p_return:.0%}; strandings near the plant: {best.p_beach_near:.0%}",
        "Backup: gap camera switches the curtain on at once if jellyfish arrive early.",
    ]
    return "\n".join(lines)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--distance", type=float, required=True, help="km from the gap")
    ap.add_argument("--bearing", type=float, required=True, help="compass bearing from the gap, degrees")
    ap.add_argument("--wind", type=float, default=6.0, help="wind speed m/s")
    ap.add_argument("--wind-from", type=float, default=315.0, help="direction the wind blows from, degrees")
    ap.add_argument("--residual", type=float, default=0.08, help="eastward residual current m/s")
    a = ap.parse_args()
    cond = Conditions(wind_speed=a.wind, wind_from_deg=a.wind_from, residual_u=a.residual)
    print(plan(a.distance, a.bearing, cond))


if __name__ == "__main__":
    main()
