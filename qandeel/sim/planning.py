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
from .forcing import Record, load_site, velocity_series

DT_S = 600.0  # 10-minute steps
EDDY_DIFFUSIVITY = 20.0  # m^2/s, horizontal mixing (assumed)
ARRIVE_RADIUS_M = 2000.0  # a jellyfish is "at the gap" within this distance
LEAD_SHARE = 0.05  # the swarm "arrives" when 5% of it is at the gap
SWITCH_ON_BUFFER_H = 3.0
MIN_ARRIVAL_PROB = 0.10  # below this, the curtain stays off
NEAR_COAST_KM = 10.0
TOW_THROUGH_WATER = 0.2  # m/s: the most the boom + bag can move through the water without losing jellyfish
INTAKE_DRAW_M_S = 0.08  # assumed flow toward the gap created by the intake itself
INTAKE_ZONE_M = 500.0  # where that draw is felt
MAX_TOW_H = 24.0  # longer tows are impractical
EMPTY_RETURN_M_S = 1.5  # boom unit coming back empty
RELEASE_H = 0.5
FILL_H = 1.0  # time to fill a bag at a typical gathering rate (see sizing.herding_logistics)


@dataclass
class Cloud:
    arrive_h: np.ndarray  # (runs, particles), nan = never arrived
    arrive_x: np.ndarray  # (runs, particles) east position at arrival
    beach_x: np.ndarray  # (runs, particles) east position of first coast contact, nan = none
    hourly: np.ndarray  # (hours+1, keep_runs, particles, 2) positions for maps
    centroid: np.ndarray  # (hours+1, runs, 2)


def simulate_cloud(start_xy, hours, rng, runs=400, n_particles=60, spread_m=1500.0, swim_max=0.10,
                   cond: Conditions | None = None, record: Record | None = None, keep_runs=20,
                   start_hour: float = 0.0, forecast_error: bool = True, arrive_radius_m=None) -> Cloud:
    radius = ARRIVE_RADIUS_M if arrive_radius_m is None else arrive_radius_m
    vel = velocity_series(hours, DT_S, runs, rng, cond=cond, record=record, start_hour=start_hour,
                          forecast_error=forecast_error)
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
        active = np.isnan(arrive_h) & np.isnan(beach_x)  # stranded jellyfish stay on the beach
        heading += rng.normal(0, turn_sd, shape)
        v = vel[k][:, None, :] + speed[..., None] * np.stack([np.sin(heading), np.cos(heading)], -1)
        step = v * DT_S + rng.normal(0, step_sd, shape + (2,))
        pos = np.where(active[..., None], pos + step, pos)
        pos[..., 1] = np.maximum(pos[..., 1], 0.0)
        arrived = active & (np.hypot(pos[..., 0], pos[..., 1]) <= radius)
        hit_coast = active & ~arrived & (pos[..., 1] <= 0)  # strands away from the gap: ends there
        beach_x[hit_coast] = pos[..., 0][hit_coast]
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
                     runs=400, horizon_h=96.0, seed=0, n_particles=60, swim_max=0.10,
                     start_hour: float = 0.0, arrive_radius_m=None) -> ArrivalForecast:
    """start_hour: hours from the start of a forecast file to the alert (forecast mode only)."""
    rng = np.random.default_rng(seed)
    start = bearing_to_xy(distance_km, bearing_deg)
    c = simulate_cloud(start, horizon_h, rng, runs, n_particles, swim_max=swim_max, cond=cond, record=record,
                       start_hour=start_hour, arrive_radius_m=arrive_radius_m)
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


def tow_over_ground(distance_km, bearing_deg, record: Record | None = None, cond: Conditions | None = None,
                    start_hour: float = 0.0, runs=40, max_h=MAX_TOW_H * 2, seed=3):
    """Hours to tow a full bag from the gap to a release point, per run (nan = not reached in max_h).

    The boom moves at TOW_THROUGH_WATER through the water; over the ground the water's own
    motion along the route (current + wind drift, plus the intake draw near the gap) adds or
    subtracts. When the water runs toward the gap faster than the tow, the boom waits.
    """
    rng = np.random.default_rng(seed)
    vel = velocity_series(max_h, DT_S, runs, rng, cond=cond, record=record, start_hour=start_hour,
                          forecast_error=False)
    u = bearing_to_xy(1.0, bearing_deg) / 1000.0  # unit vector, east/north
    dist = distance_km * 1000.0
    s = np.zeros(runs)
    done = np.full(runs, np.nan)
    for k in range(vel.shape[0]):
        along = vel[k] @ u - np.where(s < INTAKE_ZONE_M, INTAKE_DRAW_M_S, 0.0)
        s = np.where(np.isnan(done), s + np.maximum(0.0, TOW_THROUGH_WATER + along) * DT_S, s)
        newly = np.isnan(done) & (s >= dist)
        done[newly] = (k + 1) * DT_S / 3600
    return done


@dataclass
class ReleaseOption:
    distance_km: float
    bearing_deg: float
    p_return: float  # share of released jellyfish drifting back to the gap within the horizon
    p_beach: float  # share stranding anywhere on the coast
    p_beach_near: float  # share stranding within NEAR_COAST_KM of the plant
    tow_hours: float  # median over-ground tow time
    tow_p90_h: float = float("nan")  # slow case

    @property
    def round_trip_h(self) -> float:
        return self.tow_hours + RELEASE_H + self.distance_km * 1000 / EMPTY_RETURN_M_S / 3600

    @property
    def net_rate(self) -> float:
        """Bag loads per hour that stay away from the gap (returners are held by the curtain again)."""
        return (1.0 - self.p_return) / (FILL_H + self.round_trip_h)

    @property
    def score(self) -> float:
        """Lower is better: net removal rate, discounted for strandings near the plant."""
        return -self.net_rate * (1.0 - self.p_beach_near)

    @property
    def load_factor(self) -> float:
        """Extra handling caused by returns: each returner meets the curtain again."""
        return 1.0 / max(1e-9, 1.0 - self.p_return)


def _site_bearings():
    lo, hi = load_site().get("release_bearings_deg", [-75, 75])
    return range(int(lo), int(hi) + 1, 15)


def plan_release(cond: Conditions | None = None, record: Record | None = None, distances_km=(3, 5, 8, 10, 15),
                 bearings_deg=None, runs=60, n_particles=30, horizon_h=72.0,
                 min_coast_km=2.0, seed=1, start_hour: float = 0.0):
    """Score candidate release points by simulating released jellyfish for 72 h.

    The best point removes the most jellyfish per hour of boom time: a far point has
    few returns but a long tow; a near point is quick but more drift back to the
    curtain. Strandings near the plant are penalised; strandings far away are the
    bloom's natural end (most of an untouched summer swarm strands anyway).
    Tow time is over the ground (see tow_over_ground); points the boom cannot reach within
    MAX_TOW_H in most runs are skipped.
    """
    rng = np.random.default_rng(seed)
    bearings_deg = _site_bearings() if bearings_deg is None else bearings_deg
    options = []
    for dist in distances_km:
        for b in bearings_deg:
            start = bearing_to_xy(dist, b % 360)
            if start[1] < min_coast_km * 1000:
                continue
            tows = tow_over_ground(dist, b % 360, record=record, cond=cond, start_hour=start_hour, seed=seed)
            if np.isnan(tows).mean() > 0.1:
                continue  # too often the boom cannot get there
            tow_h, tow_p90 = float(np.nanmedian(tows)), float(np.nanpercentile(tows, 90))
            if tow_h > MAX_TOW_H:
                continue
            c = simulate_cloud(start, horizon_h, rng, runs, n_particles, spread_m=200.0,
                               cond=cond, record=record, keep_runs=0, start_hour=start_hour + tow_h)
            returned = ~np.isnan(c.arrive_h)
            beached = ~np.isnan(c.beach_x) & ~returned
            near = beached & (np.abs(np.nan_to_num(c.beach_x, nan=1e9)) <= NEAR_COAST_KM * 1000)
            options.append(ReleaseOption(dist, b % 360, float(returned.mean()), float(beached.mean()),
                                         float(near.mean()), tow_h, tow_p90))
    options.sort(key=lambda o: (round(o.score, 3), o.tow_hours))  # ties: shortest tow
    return options
