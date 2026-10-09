import csv
import math

import numpy as np
import pytest

from qandeel.sim.boom import run_boom
from qandeel.sim.curtain import bulson_surface_current, run_hold
from qandeel.sim.env import WIND_DRIFT_FACTOR, Conditions, bearing_to_xy
from qandeel.sim.forcing import load_record, velocity_series
from qandeel.sim.planning import forecast_arrival, plan_release, switch_on_time
from qandeel.sim.sizing import boom_throughput, compressor, herding_logistics, jellyfish_mass_kg


def test_bearing_to_xy_compass_convention():
    assert np.allclose(bearing_to_xy(1, 0), [0, 1000], atol=1e-9)
    assert np.allclose(bearing_to_xy(1, 90), [1000, 0], atol=1e-9)


def test_wind_drift_blows_downwind():
    drift = Conditions(wind_speed=10, wind_from_deg=0).wind_drift()  # northerly wind
    assert drift[1] == pytest.approx(-10 * WIND_DRIFT_FACTOR) and abs(drift[0]) < 1e-9


def test_upwind_alert_arrives_and_offshore_alert_does_not():
    near = forecast_arrival(28, 285, runs=150, n_particles=30)
    far = forecast_arrival(28, 0, runs=150, n_particles=30)
    assert near.p_arrive > 0.5 and 0 < near.share_reaching <= 1
    assert near.p10_h <= near.p50_h <= near.p90_h
    assert switch_on_time(far) is None


def test_switch_on_respects_buffer():
    fc = forecast_arrival(28, 285, runs=150, n_particles=30)
    assert switch_on_time(fc) == pytest.approx(max(0.0, fc.p10_h - 3.0))


def test_real_record_is_used(tmp_path):
    path = tmp_path / "forcing.csv"
    with path.open("w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["time", "current_u", "current_v", "wind_speed", "wind_from_deg"])
        for h in range(200):
            w.writerow([f"t{h}", "0.20", "0.0", "0", "0"])  # steady 0.2 m/s east current, no wind
    rec = load_record(path)
    v = velocity_series(24, 600, 50, np.random.default_rng(0), record=rec)
    assert v.shape == (144, 50, 2)
    assert np.mean(v[..., 0]) == pytest.approx(0.20, abs=0.03)


def test_bulson_current_matches_formula():
    assert bulson_surface_current(3.0) == pytest.approx(1.46 * (9.81 * 0.003) ** (1 / 3))


def test_curtain_holds_moderate_flow_and_fails_strong_flow():
    assert run_hold(0.15, 0.0, n=150).held_share < 0.1
    assert run_hold(0.15, 3.0, n=150).held_share > 0.95
    assert run_hold(0.15, 3.0, n=150, swim_max=0.20).held_share > 0.9
    assert run_hold(0.40, 3.0, n=150).held_share < 0.1


def test_boom_loses_more_when_towed_faster():
    slow = run_boom(0.10, 2.0, 0.10, 0.25, minutes=30, n=300).retained_share
    fast = run_boom(0.35, 2.0, 0.10, 0.25, minutes=30, n=300).retained_share
    assert slow > fast


def test_assumed_conditions_release_point_has_few_returns():
    best = plan_release(runs=30, n_particles=20)[0]
    assert best.p_return < 0.02
    assert best.bearing_deg <= 90  # down-current (east) of the gap


def test_release_ranking_prefers_net_removal():
    opts = plan_release(runs=30, n_particles=20)
    assert opts[0].net_rate * (1 - opts[0].p_beach_near) >= max(o.net_rate * (1 - o.p_beach_near) for o in opts) - 1e-3
    assert opts[0].round_trip_h > opts[0].tow_hours


def test_herding_is_limited_by_the_tow_not_gathering():
    opts = {o.distance_km: o for o in reversed(plan_release(runs=30, n_particles=20))}
    rows = herding_logistics([opts[d] for d in sorted(opts)], gather_t_h=10.0)["rows"]
    for r in rows:
        assert r["net_t_h_single_unit"] < 10.0  # far below the gathering rate
        assert r["net_t_h_with_3_tugs"] >= r["net_t_h_single_unit"]


def test_smaller_arrival_radius_lowers_arrival_chance():
    wide = forecast_arrival(10, 330, runs=60, n_particles=20, seed=2).p_arrive
    tight = forecast_arrival(10, 330, runs=60, n_particles=20, seed=2, arrive_radius_m=300).p_arrive
    assert tight <= wide


def test_sizing_numbers():
    c = compressor(curtain_m=300, airflow_l_s_m=3.0)
    assert c["air_m3_s"] == pytest.approx(0.9)
    assert 80 < c["power_kw"] < 200
    rows = boom_throughput()["rows"]
    assert rows[0]["jellyfish_per_h"] < rows[-1]["jellyfish_per_h"]
    assert jellyfish_mass_kg(30) == pytest.approx(0.99, abs=0.02)
    assert jellyfish_mass_kg(45) == pytest.approx(3.04, abs=0.03)
    assert math.isfinite(c["energy_mwh_per_event"])


def test_gap_tool_distance_and_parsing():
    from qandeel.measure_gap import haversine_m, parse_point
    a, b = parse_point("23.9612 N, 52.2301 E"), parse_point("23.9612, 52.2330")
    assert a == (23.9612, 52.2301)
    assert haversine_m(a, b) == pytest.approx(295, abs=3)


def test_forecast_mode_starts_at_alert_hour(tmp_path):
    from qandeel.sim.forcing import load_forecast
    path = tmp_path / "forecast.csv"
    with path.open("w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["time", "current_u", "current_v", "wind_speed", "wind_from_deg"])
        for h in range(120):
            w.writerow([f"t{h}", "0.30" if h >= 24 else "0.0", "0.0", "0", "0"])
    rec = load_forecast(path)
    assert rec.kind == "forecast"
    v = velocity_series(12, 600, 200, np.random.default_rng(0), record=rec, start_hour=30)
    assert np.mean(v[..., 0]) == pytest.approx(0.30, abs=0.05)  # starts inside the 0.3 m/s part


def test_retention_bag_beats_open_boom():
    open_ = run_boom(0.20, 2.0, 0.10, 0.25, minutes=30, n=300).retained_share
    bag = run_boom(0.20, 2.0, 0.10, 0.25, minutes=30, n=300, bag=True).retained_share
    assert bag > open_ + 0.3


def test_airflow_inverse_of_bulson():
    from qandeel.sim.benefits import airflow_for
    assert airflow_for(bulson_surface_current(3.0)) == pytest.approx(3.0, rel=1e-6)


def test_cost_and_emissions_tables():
    from qandeel.sim.benefits import cost_summary, emissions
    c = cost_summary(11.0)
    assert c["capex_low_usd"] < c["capex_high_usd"]
    assert emissions(10.0)["tco2_grid"] == pytest.approx(4.0)


# ---- checks on the real 2025 data and the numbers reported in results.json ----
import json  # noqa: E402
from pathlib import Path  # noqa: E402


from qandeel.sim.benefits import adaptive_airflow, smart_switching  # noqa: E402
from qandeel.sim.forcing import Record  # noqa: E402
from qandeel.sim.planning import simulate_cloud, tow_over_ground  # noqa: E402

RESULTS = Path(__file__).resolve().parent.parent / "outputs" / "results.json"
REAL = load_record()
needs_real = pytest.mark.skipif(REAL is None or not RESULTS.exists(), reason="real data or results.json missing")


@needs_real
def test_reported_alert_reproduces_on_real_data():
    rep = json.loads(RESULTS.read_text())["alerts"][0]
    fc = forecast_arrival(rep["distance_km"], rep["bearing_deg"], record=REAL, seed=0)
    assert round(fc.p_arrive, 3) == rep["p_arrive"]
    assert round(fc.p90_h, 1) == rep["window_end_h"]


@needs_real
def test_reported_release_point_reproduces_on_real_data():
    rep = json.loads(RESULTS.read_text())["release_options"][0]
    best = plan_release(record=REAL)[0]
    assert (best.distance_km, best.bearing_deg) == (rep["distance_km"], rep["bearing_deg"])
    assert best.p_return == pytest.approx(rep["p_return"])


@needs_real
def test_hindcast_planner_lets_fewer_through_than_camera():
    summary, rows = smart_switching(REAL, n_alerts=12, runs=40, n_particles=20)
    assert len(rows) == 12
    if summary["swarms_that_arrived"]:
        assert summary["unprotected_pct_qandeel"] <= summary["unprotected_pct_camera_only"]
    assert summary["hours_qandeel"] <= summary["hours_every_alert"]


@needs_real
def test_adaptive_airflow_on_real_data():
    ad, approach, q_set = adaptive_airflow(REAL, 0.175)
    assert ad["hours"] == len(REAL.time)
    assert 0 <= ad["share_hours_above_max"] < 0.5
    assert ad["energy_vs_constant_max_pct"] <= 100
    assert approach.min() >= 0.08 - 1e-9  # intake draw is always there


def test_stranded_jellyfish_do_not_later_arrive():
    rng = np.random.default_rng(0)
    c = simulate_cloud((-8000.0, 1500.0), 48, rng, runs=20, n_particles=30, keep_runs=0)
    assert not np.any(~np.isnan(c.beach_x) & ~np.isnan(c.arrive_h))


def test_tow_is_slower_against_the_current():
    hours = 100
    calm = Record([f"2025-07-01T{h % 24:02d}:00" for h in range(hours)], np.zeros((hours, 2)), np.zeros((hours, 2)),
                  kind="forecast")
    onshore = Record(calm.time, np.tile([0.0, -0.06], (hours, 1)), np.zeros((hours, 2)), kind="forecast")
    strong = Record(calm.time, np.tile([0.0, -0.15], (hours, 1)), np.zeros((hours, 2)), kind="forecast")
    t_calm = np.nanmedian(tow_over_ground(3, 0, record=calm, runs=4))
    t_against = np.nanmedian(tow_over_ground(3, 0, record=onshore, runs=4))
    assert t_against > 1.3 * t_calm
    assert np.isnan(tow_over_ground(3, 0, record=strong, runs=4)).all()  # boom cannot leave the gap


def test_dashboard_runs_with_a_distant_alert():
    from streamlit.testing.v1 import AppTest
    at = AppTest.from_file(str(Path(__file__).resolve().parent.parent / "dashboard.py"), default_timeout=300)
    at.query_params["d"] = "28"
    at.query_params["b"] = "0"
    at.run()
    assert not at.exception
