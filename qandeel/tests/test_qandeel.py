import csv
import math

import numpy as np
import pytest

from qandeel.sim.boom import run_boom
from qandeel.sim.curtain import bulson_surface_current, run_hold
from qandeel.sim.env import Conditions, bearing_to_xy
from qandeel.sim.forcing import load_record, velocity_series
from qandeel.sim.planning import forecast_arrival, plan_release, switch_on_time
from qandeel.sim.sizing import boom_throughput, compressor, jellyfish_mass_kg


def test_bearing_to_xy_compass_convention():
    assert np.allclose(bearing_to_xy(1, 0), [0, 1000], atol=1e-9)
    assert np.allclose(bearing_to_xy(1, 90), [1000, 0], atol=1e-9)


def test_wind_drift_blows_downwind():
    drift = Conditions(wind_speed=10, wind_from_deg=0).wind_drift()  # northerly wind
    assert drift[1] == pytest.approx(-0.3) and abs(drift[0]) < 1e-9


def test_upwind_alert_arrives_and_offshore_alert_does_not():
    near = forecast_arrival(28, 285, runs=150, n_particles=30)
    far = forecast_arrival(28, 0, runs=150, n_particles=30)
    assert near.p_arrive > 0.7 and 0 < near.share_reaching <= 1
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


def test_best_release_point_has_no_returns():
    best = plan_release(runs=30, n_particles=20)[0]
    assert best.p_return < 0.02
    assert best.bearing_deg <= 90  # down-current (east) of the gap


def test_sizing_numbers():
    c = compressor(curtain_m=300, airflow_l_s_m=3.0)
    assert c["air_m3_s"] == pytest.approx(0.9)
    assert 80 < c["power_kw"] < 200
    rows = boom_throughput()["rows"]
    assert rows[0]["jellyfish_per_h"] < rows[-1]["jellyfish_per_h"]
    assert jellyfish_mass_kg(30) == pytest.approx(0.99, abs=0.02)
    assert jellyfish_mass_kg(45) == pytest.approx(3.04, abs=0.03)
    assert math.isfinite(c["energy_mwh_per_event"])
