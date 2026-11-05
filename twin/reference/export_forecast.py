"""Export the planner's 400-drift Monte Carlo for the simulator's forecast timeline.
Uses the same call, seed and data as run_all.py, so the numbers match results.json exactly."""
import base64, json, sys
import numpy as np
sys.path.insert(0, '/home/user/enec')
from qandeel.run_all import ALERTS
from qandeel.sim.env import Conditions, bearing_to_xy
from qandeel.sim.forcing import load_record
from qandeel.sim import planning as P

cond, record = Conditions(), load_record()
out = {"source": "qandeel/sim/planning.py forecast_arrival(): 400 runs x 60 jellyfish, Copernicus SMOC + ERA5 2025, seeds as run_all.py",
       "frame": "km from the intake mouth; x east, y north; the model coast is the straight line y = 0", "alerts": []}
GX0, GX1, GY0, GY1, CELL = -26.0, 14.0, 0.0, 30.0, 0.5
NX, NY = int((GX1 - GX0) / CELL), int((GY1 - GY0) / CELL)
for i, (label, dist, bearing) in enumerate(ALERTS):
    rng = np.random.default_rng(i)                                   # identical to forecast_arrival(seed=i)
    start = bearing_to_xy(dist, bearing)
    c = P.simulate_cloud(start, 96.0, rng, 400, 60, swim_max=0.10, cond=cond, record=record, keep_runs=400)
    fc = P.forecast_arrival(dist, bearing, cond=cond, record=record, seed=i)   # the published numbers
    assert np.allclose(np.nan_to_num(c.arrive_h, nan=-1), np.nan_to_num(fc.cloud.arrive_h, nan=-1)), "RNG mismatch"
    t_on = P.switch_on_time(fc)
    H = c.hourly.shape[0]                                            # hours+1 frames, (400, 60, 2) each
    frames = []
    for h in range(H):
        xy = c.hourly[h].reshape(-1, 2) / 1000.0
        g, _, _ = np.histogram2d(xy[:, 1], xy[:, 0], bins=[NY, NX], range=[[GY0, GY1], [GX0, GX1]])
        share = g / xy.shape[0]
        q = np.clip(np.round(np.sqrt(share / 0.02) * 255), 0, 255).astype(np.uint8)   # sqrt scale, 2% of particles = full
        frames.append(base64.b64encode(q[::-1].tobytes()).decode())   # row 0 = north
    k_lead = max(1, int(np.ceil(P.LEAD_SHARE * 60)))
    lead = np.sort(c.arrive_h, axis=1)[:, k_lead - 1]
    hours = np.arange(H)
    runs_arrived = [float(np.mean(lead <= t)) for t in hours]            # nan compares False
    swarm_arrived = [float(np.mean(c.arrive_h <= t)) for t in hours]
    tracks = [[[round(float(v), 2) for v in c.centroid[h, r] / 1000.0] for h in range(0, H, 2)] for r in range(0, 400, 10)]
    out["alerts"].append({
        "label": label, "distance_km": dist, "bearing_deg": bearing, "start_km": [round(float(v), 2) for v in start / 1000.0],
        "p_arrive": round(fc.p_arrive, 3), "window_start_h": fc.p10_h and round(fc.p10_h, 1),
        "bulk_arrival_h": fc.p50_h and round(fc.p50_h, 1), "window_end_h": fc.p90_h and round(fc.p90_h, 1),
        "switch_on_h": t_on and round(t_on, 1), "approach_side": fc.side if t_on is not None else None,
        "share_reaching": round(fc.share_reaching, 3), "arrive_radius_km": P.ARRIVE_RADIUS_M / 1000.0,
        "runs_arrived_by_h": [round(v, 3) for v in runs_arrived], "swarm_arrived_by_h": [round(v, 4) for v in swarm_arrived],
        "tracks_every_2h": tracks, "grid": {"x0": GX0, "x1": GX1, "y0": GY0, "y1": GY1, "nx": NX, "ny": NY, "scale": "sqrt(share/0.02)*255"},
        "frames_b64": frames})
    print(label, out["alerts"][-1]["p_arrive"], out["alerts"][-1]["window_start_h"], out["alerts"][-1]["bulk_arrival_h"], out["alerts"][-1]["window_end_h"], out["alerts"][-1]["switch_on_h"], out["alerts"][-1]["approach_side"])
json.dump(out, open('/home/user/enec/plan_local/sim/site/forecast.json', 'w'), separators=(',', ':'))
