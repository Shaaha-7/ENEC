"""Operator view in the terminal: turn one early-warning alert into a response plan.

    python -m qandeel.plan_alert --distance 28 --bearing 285
    python -m qandeel.plan_alert --distance 28 --bearing 0

Uses the real Gulf record (qandeel/data/gulf_forcing.csv) when present.
For the visual version run: streamlit run qandeel/dashboard.py
"""
import argparse

from qandeel.sim.env import Conditions
from qandeel.sim.forcing import load_forecast, load_record, source_label
from qandeel.sim.planning import forecast_arrival, plan_release, switch_on_time


def compass(bearing):
    names = ["N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW"]
    return names[round(bearing % 360 / 22.5) % 16]


def plan(distance_km, bearing_deg, cond=None, record=None, swim_max=0.10, runs=400, seed=0) -> str:
    fc = forecast_arrival(distance_km, bearing_deg, cond=cond, record=record, runs=runs, seed=seed,
                          swim_max=swim_max)
    head = f"ALERT  Swarm {distance_km:g} km {compass(bearing_deg)} of the intake gap"
    lines = [head, "-" * len(head), f"Forcing: {source_label(record)}"]
    t_on = switch_on_time(fc)
    if t_on is None:
        lines += [f"Chance of reaching the gap in 96 h: {fc.p_arrive:.0%}",
                  "Action: curtain stays OFF; keep tracking, re-plan on the next detection update."]
        return "\n".join(lines)
    best = plan_release(cond=cond, record=record)[0]
    lines += [
        f"Chance of reaching the gap in 96 h: {fc.p_arrive:.0%} (about {fc.share_reaching:.0%} of the swarm)",
        f"Arrival window:                     {fc.p10_h:.0f}-{fc.p90_h:.0f} h after alert (bulk at {fc.p50_h:.0f} h)",
        f"Curtain switch-on:                  in {t_on:.0f} h (earliest arrival minus 3 h buffer)",
        f"Swarm approaches from the:          {fc.side} side; stage the boom crew there",
        f"Release point:                      {best.distance_km:g} km {compass(best.bearing_deg)} of the gap, "
        f"~{best.tow_hours:.0f} h tow, boom + retention bag at <=0.2 m/s through the water",
        f"Simulated return within 72 h:       {best.p_return:.0%} (curtain handles them again: "
        f"{best.load_factor:.2f}x load); strandings near the plant: {best.p_beach_near:.0%}",
        "Backup: gap camera switches the curtain on at once if jellyfish arrive early.",
    ]
    return "\n".join(lines)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--distance", type=float, required=True, help="km from the gap")
    ap.add_argument("--bearing", type=float, required=True, help="compass bearing from the gap, degrees")
    ap.add_argument("--swim", type=float, default=0.10, help="top jellyfish swim speed, m/s")
    ap.add_argument("--assumed", action="store_true", help="ignore the real data files, use assumed conditions")
    ap.add_argument("--forecast", action="store_true", help="use the live forecast (data/forecast.csv), alert = now")
    ap.add_argument("--wind", type=float, default=6.0, help="(assumed mode) wind speed m/s")
    ap.add_argument("--wind-from", type=float, default=315.0, help="(assumed mode) wind from, degrees")
    a = ap.parse_args()
    record = None if a.assumed else (load_forecast() if a.forecast else load_record())
    if a.forecast and record is None:
        raise SystemExit("No forecast file: run python -m qandeel.data_fetch --forecast")
    cond = Conditions(wind_speed=a.wind, wind_from_deg=a.wind_from)
    print(plan(a.distance, a.bearing, cond=cond, record=record, swim_max=a.swim))


if __name__ == "__main__":
    main()
