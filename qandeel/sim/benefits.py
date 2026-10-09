"""Quantified benefits: smart switching, adaptive airflow, cost, survival, emissions.

Smart switching (hindcast). Alerts are placed at random times in the real 2025
season and random positions offshore. For each, the planner sees a forecast
(the real record from that hour plus forecast error) and decides whether and
when to run the curtain. Two versions of "what really happened" are used:

* self-consistency: the same drift model on the same record with no error.
  This only shows the planner copes with forecast noise; it is not validation.
* model-error stress test: the truth uses physics the planner does not know:
  currents scaled 0.7-1.3x, wind drift 1.5-5% of wind speed instead of 3%,
  and a steady 0-3 cm/s unmodelled drift. Still a simulation, not real drifter
  tracks, but the planner is no longer graded against itself.

Three strategies are compared on the same truth:
* every alert: curtain on for the whole 96 h (upper bound, not a real practice);
* camera only: switched on when the gap camera sees jellyfish. An optical camera
  only works in daylight (06-18 local), so night arrivals wait until morning;
* Qandeel: planned switch-on, with the camera as backup.

Adaptive airflow. The curtain only needs a surface current that beats the
approach current plus a margin. Using the real hourly currents and wind, the
airflow is set hour by hour instead of being fixed for the worst case.
"""
from dataclasses import replace

import numpy as np

from .curtain import bulson_surface_current, run_hold
from .env import bearing_to_xy
from .forcing import Record
from .planning import INTAKE_DRAW_M_S, forecast_arrival, simulate_cloud, switch_on_time

HORIZON_H = 96.0
RUN_ON_AFTER_H = 12.0  # keep running this long after the late edge of the window / last sighting

LOCAL_UTC_OFFSET_H = 4  # UAE
STARTUP_H = 0.25  # compressor start and pipe fill


def _hour_of_day(record: Record, abs_hour: float) -> float:
    i = int(min(len(record.time) - 1, max(0, abs_hour)))
    return (int(record.time[i][11:13]) + LOCAL_UTC_OFFSET_H + (abs_hour - int(abs_hour))) % 24


def _camera_on(record: Record, start_hour: float, first_h: float) -> float:
    """Hours after the alert when a daylight-only camera at the gap sees the first jellyfish."""
    tod = _hour_of_day(record, start_hour + first_h)
    wait = 0.0 if 6 <= tod < 18 else (6 - tod) % 24
    return first_h + wait + STARTUP_H


def _truth(distance_km, bearing_deg, record: Record, start_hour, rng, stress, n_particles=60):
    rec = replace(record, kind="forecast")
    if stress:
        cur_k, wind_k = rng.uniform(0.7, 1.3), rng.uniform(0.5, 1.67)
        ang, spd = rng.uniform(0, 2 * np.pi), rng.uniform(0, 0.03)
        rec = replace(rec, current=record.current * cur_k + spd * np.array([np.sin(ang), np.cos(ang)]),
                      wind_drift=record.wind_drift * wind_k)
    c = simulate_cloud(bearing_to_xy(distance_km, bearing_deg), HORIZON_H, rng, runs=1, n_particles=n_particles,
                       record=rec, keep_runs=0, start_hour=start_hour, forecast_error=False)
    return np.sort(c.arrive_h[0])  # nan last


def smart_switching(record: Record, n_alerts=100, seed=7, runs=150, n_particles=30, stress=True):
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
        arr = _truth(d, b, record, s, rng, stress)
        arr = arr[~np.isnan(arr)]
        arrived = arr.size >= max(1, int(np.ceil(0.05 * 60)))  # same 5% rule as the planner
        row = {"start_hour": s, "distance_km": round(d, 1), "bearing_deg": round(b),
               "p_arrive": round(fc.p_arrive, 2), "curtain_on": t_on is not None, "truth_arrived": bool(arrived)}
        if arrived:
            first, last = float(arr[0]), float(arr[-1])
            cam_on = _camera_on(record, s, first)
            cam_off = min(HORIZON_H, max(cam_on, last) + RUN_ON_AFTER_H)
            covered = t_on is not None and t_on <= first and t_off >= last
            q_on = t_on if (t_on is not None and t_on <= cam_on) else cam_on  # camera backup if plan is late/absent
            q_off = max(t_off or 0.0, cam_off if (t_on is None or t_off < last) else 0.0)
            row |= {"covered_by_plan": bool(covered),
                    "camera_unprotected_share": float((arr < cam_on).mean()),
                    "qandeel_unprotected_share": float((arr < q_on).mean()),
                    "warning_h": round(first, 1) if t_on is not None else 0.0,
                    "camera_hours": cam_off - cam_on,
                    "qandeel_hours": max(0.0, q_off - q_on)}
        else:
            row |= {"camera_hours": 0.0, "qandeel_hours": 0.0 if t_on is None else t_off - t_on}
        rows.append(row)
    arrived = [r for r in rows if r["truth_arrived"]]
    non = [r for r in rows if not r["truth_arrived"]]
    mean = lambda k: float(np.mean([r[k] for r in arrived])) if arrived else 0.0  # noqa: E731
    summary = {
        "test": "model-error stress test" if stress else "self-consistency",
        "alerts": len(rows),
        "swarms_that_arrived": len(arrived),
        "covered_by_plan": sum(r["covered_by_plan"] for r in arrived),
        "curtain_off_for_non_arriving": sum(not r["curtain_on"] for r in non),
        "non_arriving": len(non),
        "hours_every_alert": len(rows) * HORIZON_H,
        "hours_camera_only": round(sum(r["camera_hours"] for r in rows)),
        "hours_qandeel": round(sum(r["qandeel_hours"] for r in rows)),
        "unprotected_pct_camera_only": round(100 * mean("camera_unprotected_share"), 1),
        "unprotected_pct_qandeel": round(100 * mean("qandeel_unprotected_share"), 1),
        "median_warning_h_qandeel": round(float(np.median([r["warning_h"] for r in arrived])), 1) if arrived else 0.0,
    }
    summary["hours_saved_vs_every_alert_pct"] = round(100 * (1 - summary["hours_qandeel"] / summary["hours_every_alert"]))
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


# Indicative capital costs in USD (low, high), to confirm with suppliers. The curtain scales with
# its length and the compressor with its power; the rest does not.
CURTAIN_USD_PER_M = (500, 1500)  # diffuser pipe, flotation hose, anchors, installed
COMPRESSOR_USD_PER_KW = (1000, 2300)  # oil-free variable-speed unit, standby unit, air line
CAPEX_FIXED_USD = [
    ("Ocean containment boom 300 m with 2 m skirt", 40_000, 120_000),
    ("Closed-bottom retention bags (2) and quick-release ends", 10_000, 30_000),
    ("Two uncrewed surface vessels (USVs) with tow gear, shore-supervised", 200_000, 800_000),
    ("Gap camera, pressure sensors, control PLC, software", 30_000, 80_000),
]


def capex_items(curtain_m=300.0, power_kw=135.0):
    return [(f"Bubble curtain: diffuser pipe, flotation hose, anchors ({curtain_m:.0f} m, installed)",
             curtain_m * CURTAIN_USD_PER_M[0], curtain_m * CURTAIN_USD_PER_M[1]),
            (f"Oil-free variable-speed compressor ~{power_kw:.0f} kW, standby unit, air line",
             power_kw * COMPRESSOR_USD_PER_KW[0], power_kw * COMPRESSOR_USD_PER_KW[1])] + CAPEX_FIXED_USD


# Indicative yearly running costs (USD) beyond electricity: to confirm with operators and suppliers.
OPEX_USD_PER_YEAR = [
    ("Two shore-supervised operators on call through the bloom season (share of existing staff)", 60_000, 150_000),
    ("Uncrewed boats: maintenance, insurance, communications", 40_000, 120_000),
    ("Diver/ROV inspection and diffuser cleaning (warm-water fouling), 4-6 visits", 30_000, 90_000),
    ("Compressor service, boom and bag repair, spares", 20_000, 60_000),
]
OUTAGE_COST_USD_PER_12H = 500_000  # industry figure for a 12-hour unit outage, as cited by EPRI


def cost_summary(energy_mwh_per_event, events_per_season=10, usd_per_mwh=100.0, curtain_m=300.0, power_kw=135.0):
    items = capex_items(curtain_m, power_kw)
    lo = sum(r[1] for r in items)
    hi = sum(r[2] for r in items)
    run = energy_mwh_per_event * usd_per_mwh
    op_lo = sum(r[1] for r in OPEX_USD_PER_YEAR) + run * events_per_season
    op_hi = sum(r[2] for r in OPEX_USD_PER_YEAR) + run * events_per_season
    return {"capex_items": [{"item": a, "low_usd": round(b, -3), "high_usd": round(c, -3)} for a, b, c in items],
            "capex_low_usd": lo, "capex_high_usd": hi,
            "energy_cost_per_event_usd": round(run, 0),
            "energy_cost_per_season_usd": round(run * events_per_season, 0),
            "opex_items": [{"item": a, "low_usd": b, "high_usd": c} for a, b, c in OPEX_USD_PER_YEAR],
            "opex_low_usd_per_year": round(op_lo, -3), "opex_high_usd_per_year": round(op_hi, -3),
            "outage_cost_12h_usd": OUTAGE_COST_USD_PER_12H,
            "outages_avoided_to_pay_back": f"{lo / OUTAGE_COST_USD_PER_12H:.1f}-{hi / OUTAGE_COST_USD_PER_12H:.1f}",
            "outages_avoided_per_year_to_cover_running": f"{op_lo / OUTAGE_COST_USD_PER_12H:.1f}-"
                                                         f"{op_hi / OUTAGE_COST_USD_PER_12H:.1f}"}


def emissions(energy_mwh_per_event, grid_t_per_mwh=0.4):
    """tCO2 per event if the compressor runs on a gas-heavy grid vs on the plant's own low-carbon power."""
    return {"energy_mwh_per_event": energy_mwh_per_event,
            "tco2_grid": round(energy_mwh_per_event * grid_t_per_mwh, 1),
            "tco2_nuclear_supply": round(energy_mwh_per_event * 0.012, 2)}
