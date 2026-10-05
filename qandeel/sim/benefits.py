"""Quantified benefits: smart switching, adaptive airflow, cost, survival, emissions.

Smart switching (hindcast). Alerts are placed at random times in the real 2025
season and random positions offshore. For each, the planner sees a forecast
(the real record from that hour plus forecast error) and decides whether and
when to run the curtain. The "truth" is the same real record with no error.
We count curtain hours and whether every swarm that truly arrived was covered.

Adaptive airflow. The curtain only needs a surface current that beats the
approach current plus a margin. Using the real hourly currents and wind, the
airflow is set hour by hour instead of being fixed for the worst case.
"""
from dataclasses import replace

import numpy as np

from .curtain import bulson_surface_current, run_hold
from .env import bearing_to_xy
from .forcing import Record
from .planning import DT_S, LEAD_SHARE, forecast_arrival, simulate_cloud, switch_on_time

HORIZON_H = 96.0
RUN_ON_AFTER_H = 12.0  # keep running this long after the late edge of the window
INTAKE_DRAW_M_S = 0.08  # assumed approach speed created by the intake itself at the gap


def _truth_lead_hour(distance_km, bearing_deg, rec_fc: Record, start_hour, rng, n_particles=60):
    c = simulate_cloud(bearing_to_xy(distance_km, bearing_deg), HORIZON_H, rng, runs=1, n_particles=n_particles,
                       record=rec_fc, keep_runs=0, start_hour=start_hour, forecast_error=False)
    k = max(1, int(np.ceil(LEAD_SHARE * n_particles)))
    return float(np.sort(c.arrive_h[0])[k - 1])  # nan if fewer than 5% arrive


def smart_switching(record: Record, n_alerts=100, seed=7, runs=150, n_particles=30):
    rng = np.random.default_rng(seed)
    rec_fc = replace(record, kind="forecast")
    n_h = len(record.time)
    rows = []
    for _ in range(n_alerts):
        s = float(rng.integers(0, n_h - int(HORIZON_H) - 2))
        d = float(rng.uniform(10, 35))
        b = float(rng.uniform(-90, 90)) % 360
        fc = forecast_arrival(d, b, record=rec_fc, start_hour=s, runs=runs, n_particles=n_particles,
                              seed=int(rng.integers(1e9)))
        t_on = switch_on_time(fc)
        t_off = None if t_on is None else min(HORIZON_H, fc.p90_h + RUN_ON_AFTER_H)
        truth = _truth_lead_hour(d, b, rec_fc, s, rng)
        arrived = not np.isnan(truth)
        planned = arrived and t_on is not None and t_on <= truth <= t_off
        hours = 0.0 if t_on is None else t_off - t_on
        backup = arrived and not planned  # gap camera switches it on when jellyfish appear
        if backup:
            hours += max(0.0, HORIZON_H - truth) if t_on is None or truth > (t_off or 0) else 0.0
        rows.append({"start_hour": s, "distance_km": round(d, 1), "bearing_deg": round(b),
                     "p_arrive": round(fc.p_arrive, 2), "curtain_on": t_on is not None,
                     "truth_arrived": arrived, "covered_by_plan": planned, "needed_backup": backup,
                     "curtain_hours": round(hours, 1)})
    arrived = [r for r in rows if r["truth_arrived"]]
    summary = {
        "alerts": len(rows),
        "swarms_that_arrived": len(arrived),
        "covered_by_plan": sum(r["covered_by_plan"] for r in arrived),
        "caught_by_camera_backup": sum(r["needed_backup"] for r in arrived),
        "curtain_hours_qandeel": round(sum(r["curtain_hours"] for r in rows), 0),
        "curtain_hours_on_every_alert": len(rows) * HORIZON_H,
        "curtain_off_for_non_arriving": sum(1 for r in rows if not r["truth_arrived"] and not r["curtain_on"]),
        "non_arriving": sum(1 for r in rows if not r["truth_arrived"]),
    }
    summary["hours_saved_vs_every_alert_pct"] = round(
        100 * (1 - summary["curtain_hours_qandeel"] / summary["curtain_hours_on_every_alert"]), 0)
    return summary, rows


def hold_margin(swim_max=0.10, n=250):
    """Fit V_max = U0 - margin: the largest approach current held (>=95%) for each airflow."""
    pts = []
    for q in (1.5, 3.0, 4.5):
        u0 = bulson_surface_current(q)
        vmax = 0.0
        for v in np.arange(0.05, 0.45, 0.025):
            if run_hold(float(v), q, n=n, swim_max=swim_max, hours=2.0).held_share >= 0.95:
                vmax = float(v)
            else:
                break
        pts.append((u0, vmax))
    return float(np.mean([u0 - v for u0, v in pts])), pts


def airflow_for(u0_needed):
    """Invert Bulson: q (L/s per m) giving surface current u0."""
    return (u0_needed / 1.46) ** 3 / 9.81 * 1000.0


def adaptive_airflow(record: Record, margin: float, q_min=1.0, q_max=4.5):
    total_v = record.current[:, 1] + record.wind_drift[:, 1]
    onshore = np.maximum(0.0, -total_v)  # north is offshore; negative v pushes toward the gap
    approach = INTAKE_DRAW_M_S + onshore
    q_need = airflow_for(approach + margin)
    q_set = np.clip(q_need, q_min, q_max)
    return {
        "hours": int(len(approach)),
        "approach_mean_m_s": round(float(approach.mean()), 3),
        "approach_p95_m_s": round(float(np.percentile(approach, 95)), 3),
        "margin_m_s": round(margin, 3),
        "airflow_mean_l_s_m": round(float(q_set.mean()), 2),
        "share_hours_above_3": round(float((q_need > 3.0).mean()), 3),
        "share_hours_above_max": round(float((q_need > q_max).mean()), 3),
        "energy_vs_constant_max_pct": round(100 * float(q_set.mean() / q_max), 0),
        "energy_vs_constant_3_pct": round(100 * float(q_set.mean() / 3.0), 0),
    }, approach, q_set


# Indicative capital costs in USD: order of magnitude for a 300 m gap, to confirm with suppliers.
CAPEX_USD = [
    ("Bubble curtain: diffuser pipe, flotation hose, anchors (300 m, installed)", 150_000, 450_000),
    ("Oil-free compressor ~150 kW, standby unit, air line", 120_000, 300_000),
    ("Ocean containment boom 300 m with 2 m skirt", 40_000, 120_000),
    ("Closed-bottom retention bags (2) and quick-release ends", 10_000, 30_000),
    ("Two workboats (or seasonal charter)", 60_000, 300_000),
    ("Gap camera, pressure sensors, control PLC, software", 30_000, 80_000),
]
OUTAGE_COST_USD_PER_12H = 500_000  # industry figure for a 12-hour unit outage, as cited by EPRI


def cost_summary(energy_mwh_per_event, events_per_season=10, usd_per_mwh=100.0):
    lo = sum(r[1] for r in CAPEX_USD)
    hi = sum(r[2] for r in CAPEX_USD)
    run = energy_mwh_per_event * usd_per_mwh
    return {"capex_items": [{"item": a, "low_usd": b, "high_usd": c} for a, b, c in CAPEX_USD],
            "capex_low_usd": lo, "capex_high_usd": hi,
            "energy_cost_per_event_usd": round(run, 0),
            "energy_cost_per_season_usd": round(run * events_per_season, 0),
            "outage_cost_12h_usd": OUTAGE_COST_USD_PER_12H,
            "outages_avoided_to_pay_back": f"{lo / OUTAGE_COST_USD_PER_12H:.1f}-{hi / OUTAGE_COST_USD_PER_12H:.1f}"}


def emissions(energy_mwh_per_event, grid_t_per_mwh=0.4):
    """tCO2 per event if the compressor runs on a gas-heavy grid vs on the plant's own low-carbon power."""
    return {"energy_mwh_per_event": energy_mwh_per_event,
            "tco2_grid": round(energy_mwh_per_event * grid_t_per_mwh, 1),
            "tco2_nuclear_supply": round(energy_mwh_per_event * 0.012, 2)}
