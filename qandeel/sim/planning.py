"""Response planning after an ENEC early-warning alert.

Physics-based Monte Carlo drift, not a trained model. Each run moves a swarm of
individual jellyfish (spread over an area, each swimming on a wandering heading)
under one plausible version of the currents and wind: either a slice of the real
bloom-season record or the assumed tidal model. The spread across runs gives the
arrival window; the share of each swarm that reaches the gap gives the load.
"""
from dataclasses import dataclass

import numpy as np

from .env import Conditions, bearing_to_xy
from .forcing import Record, velocity_series

DT_S = 600.0  # 10-minute steps
EDDY_DIFFUSIVITY = 20.0  # m^2/s, horizontal mixing (assumed)
ARRIVE_RADIUS_M = 2000.0  # a jellyfish is "at the gap" within this distance
LEAD_SHARE = 0.05  # the swarm "arrives" when 5% of it is at the gap
SWITCH_ON_BUFFER_H = 3.0
MIN_ARRIVAL_PROB = 0.10  # below this, the curtain stays off
NEAR_COAST_KM = 10.0
TOW_SPEED = 0.3  # m/s, below the ~0.35 m/s at which booms start losing material


@dataclass
class Cloud:
    arrive_h: np.ndarray  # (runs, particles), nan = never arrived
    arrive_x: np.ndarray  # (runs, particles) east position at arrival
    beach_x: np.ndarray  # (runs, particles) east position of first coast contact, nan = none
    hourly: np.ndarray  # (hours+1, keep_runs, particles, 2) positions for maps
    centroid: np.ndarray  # (hours+1, runs, 2)


def simulate_cloud(start_xy, hours, rng, runs=400, n_particles=60, spread_m=1500.0, swim_max=0.10,
                   cond: Conditions | None = None, record: Record | None = None, keep_runs=20) -> Cloud:
    vel = velocity_series(hours, DT_S, runs, rng, cond=cond, record=record)
    steps = vel.shape[0]
    shape = (runs, n_particles)
    pos = np.asarray(start_xy, float) + rng.normal(0, spread_m, shape + (2,))
    pos[..., 1] = np.maximum(pos[..., 1], 50.0)
    heading = rng.uniform(0, 2 * np.pi, shape)
    speed = rng.uniform(0.02, swim_max, shape)
    arrive_h = np.full(shape, np.nan)
    arrive_x = np.full(shape, np.nan)
    beach_x = np.full(shape, np.nan)
    per_h = int(3600 / DT_S)
    hourly = [pos[:keep_runs].copy()]
    centroid = [pos.mean(axis=1)]
    step_sd = np.sqrt(2 * EDDY_DIFFUSIVITY * DT_S)
    turn_sd = np.sqrt(DT_S / 1800.0)  # heading wanders over ~30 min
    for k in range(steps):
        active = np.isnan(arrive_h)
        heading += rng.normal(0, turn_sd, shape)
        v = vel[k][:, None, :] + speed[..., None] * np.stack([np.sin(heading), np.cos(heading)], -1)
        step = v * DT_S + rng.normal(0, step_sd, shape + (2,))
        pos = np.where(active[..., None], pos + step, pos)
        hit_coast = active & (pos[..., 1] <= 0) & np.isnan(beach_x)
        beach_x[hit_coast] = pos[..., 0][hit_coast]
        pos[..., 1] = np.maximum(pos[..., 1], 0.0)
        arrived = active & (np.hypot(pos[..., 0], pos[..., 1]) <= ARRIVE_RADIUS_M)
        arrive_h[arrived] = (k + 1) * DT_S / 3600
        arrive_x[arrived] = pos[..., 0][arrived]
        if (k + 1) % per_h == 0:
            hourly.append(pos[:keep_runs].copy())
            centroid.append(pos.mean(axis=1))
    return Cloud(arrive_h, arrive_x, beach_x, np.array(hourly), np.array(centroid))


@dataclass
class ArrivalForecast:
    runs: int
    p_arrive: float  # share of runs in which at least 5% of the swarm reaches the gap
    share_reaching: float  # average share of the swarm reaching the gap within the horizon
    p10_h: float | None  # early edge of the window (10th percentile of lead arrival)
    p50_h: float | None  # median arrival of the swarm's bulk
    p90_h: float | None  # late edge (90th percentile of bulk arrival)
    lead_h: np.ndarray  # per run
    side: str | None  # west / centre / east approach
    cloud: Cloud
    start_xy: np.ndarray


def forecast_arrival(distance_km, bearing_deg, cond: Conditions | None = None, record: Record | None = None,
                     runs=400, horizon_h=96.0, seed=0, n_particles=60, swim_max=0.10) -> ArrivalForecast:
    rng = np.random.default_rng(seed)
    start = bearing_to_xy(distance_km, bearing_deg)
    c = simulate_cloud(start, horizon_h, rng, runs, n_particles, swim_max=swim_max, cond=cond, record=record)
    k_lead = max(1, int(np.ceil(LEAD_SHARE * n_particles)))
    lead = np.sort(c.arrive_h, axis=1)[:, k_lead - 1]  # nan sorts last
    ok = ~np.isnan(lead)
    bulk = np.full(runs, np.nan)
    if ok.any():
        bulk[ok] = np.nanmedian(c.arrive_h[ok], axis=1)
    p10 = float(np.percentile(lead[ok], 10)) if ok.any() else None
    p50 = float(np.median(bulk[ok])) if ok.any() else None
    p90 = float(np.percentile(bulk[ok], 90)) if ok.any() else None
    xs = c.arrive_x[~np.isnan(c.arrive_x)]
    side = None
    if xs.size:
        m = float(np.median(xs))
        side = "west" if m < -500 else "east" if m > 500 else "centre"
    return ArrivalForecast(runs, float(ok.mean()), float((~np.isnan(c.arrive_h)).mean()), p10, p50, p90,
                           lead, side, c, start)


def switch_on_time(fc: ArrivalForecast, buffer_h=SWITCH_ON_BUFFER_H):
    """Hours after the alert to start the curtain, or None if arrival is unlikely."""
    if fc.p10_h is None or fc.p_arrive < MIN_ARRIVAL_PROB:
        return None
    return max(0.0, fc.p10_h - buffer_h)


@dataclass
class ReleaseOption:
    distance_km: float
    bearing_deg: float
    p_return: float  # share of released jellyfish drifting back to the gap within the horizon
    p_beach: float  # share stranding anywhere on the coast
    p_beach_near: float  # share stranding within NEAR_COAST_KM of the plant
    tow_hours: float

    @property
    def score(self) -> float:
        return self.p_return + 0.5 * self.p_beach_near


def plan_release(cond: Conditions | None = None, record: Record | None = None, distances_km=(6, 10, 15),
                 bearings_deg=range(-75, 76, 15), runs=60, n_particles=30, horizon_h=72.0,
                 min_coast_km=3.0, seed=1):
    """Score candidate release points by simulating released jellyfish for 72 h.

    Strandings far from the plant are reported but treated like a bloom's
    natural end; strandings near the plant and returns to the gap are penalised.
    """
    rng = np.random.default_rng(seed)
    options = []
    for dist in distances_km:
        for b in bearings_deg:
            start = bearing_to_xy(dist, b % 360)
            if start[1] < min_coast_km * 1000:
                continue
            c = simulate_cloud(start, horizon_h, rng, runs, n_particles, spread_m=200.0,
                               cond=cond, record=record, keep_runs=0)
            returned = ~np.isnan(c.arrive_h)
            beached = ~np.isnan(c.beach_x) & ~returned
            near = beached & (np.abs(np.nan_to_num(c.beach_x, nan=1e9)) <= NEAR_COAST_KM * 1000)
            options.append(ReleaseOption(dist, b % 360, float(returned.mean()), float(beached.mean()),
                                         float(near.mean()), dist * 1000 / TOW_SPEED / 3600))
    options.sort(key=lambda o: (round(o.score, 2), o.tow_hours))  # ties within 1%: shortest tow
    return options
