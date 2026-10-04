"""Response planning after an ENEC early-warning alert.

Physics-based Monte Carlo drift, not a trained model: each run moves the swarm
with tide, residual current, wind drift and random eddies under one plausible
variant of the forecast. The spread of runs gives the arrival window.
"""
from dataclasses import dataclass

import numpy as np

from .env import TIDAL_PERIOD_H, Conditions, bearing_to_xy, perturb

DT_S = 600.0  # 10-minute steps
EDDY_DIFFUSIVITY = 20.0  # m^2/s, swarm-scale horizontal mixing (assumed)
ARRIVE_RADIUS_M = 2000.0  # swarm "at the gap" once its centre is this close
SWITCH_ON_BUFFER_H = 3.0
NEAR_COAST_KM = 10.0
TOW_SPEED = 0.3  # m/s, below the ~0.35 m/s at which booms start losing material


def drift(start_xy, cond: Conditions, hours: float, rng: np.random.Generator):
    """Track of one swarm centre (n_steps+1, 2) in metres, hour-0 at start."""
    return drift_many(start_xy, [cond], hours, rng)[:, 0, :]


def drift_many(start_xy, conds: list[Conditions], hours: float, rng: np.random.Generator):
    """Tracks for one start point under several forecast variants.

    Returns an array (n_steps+1, n_runs, 2) in metres.
    """
    n = int(hours * 3600 / DT_S)
    m = len(conds)
    amp = np.array([c.tide_amp for c in conds])
    phase = np.array([c.tide_phase_h for c in conds])
    base = np.array([[c.residual_u, c.residual_v] for c in conds]) + np.array(
        [c.wind_drift() for c in conds])
    pos = np.empty((n + 1, m, 2))
    pos[0] = start_xy
    step_sd = np.sqrt(2 * EDDY_DIFFUSIVITY * DT_S)
    for k in range(n):
        t_h = k * DT_S / 3600
        v = base.copy()
        v[:, 0] += amp * np.sin(2 * np.pi * (t_h + phase) / TIDAL_PERIOD_H)
        nxt = pos[k] + v * DT_S + rng.normal(0, step_sd, (m, 2))
        nxt[:, 1] = np.maximum(nxt[:, 1], 0.0)  # cannot cross the coast; slides along it
        pos[k + 1] = nxt
    return pos


@dataclass
class ArrivalForecast:
    runs: int
    p_arrive: float  # share of runs reaching the gap within the horizon
    p10_h: float | None
    p50_h: float | None
    p90_h: float | None
    arrival_hours: np.ndarray  # per run; nan = never arrived
    tracks: list  # a few tracks for plotting


def forecast_arrival(distance_km, bearing_deg, cond: Conditions, runs=400,
                     horizon_h=96.0, seed=0, keep_tracks=40) -> ArrivalForecast:
    rng = np.random.default_rng(seed)
    start = bearing_to_xy(distance_km, bearing_deg)
    tr = drift_many(start, [perturb(cond, rng) for _ in range(runs)], horizon_h, rng)
    near = np.hypot(tr[..., 0], tr[..., 1]) <= ARRIVE_RADIUS_M  # (steps, runs)
    hit_any = near.any(axis=0)
    hours = np.where(hit_any, near.argmax(axis=0) * DT_S / 3600, np.nan)
    tracks = [tr[:, i, :] for i in range(min(keep_tracks, runs))]
    arrived = hours[~np.isnan(hours)]
    pct = (lambda q: float(np.percentile(arrived, q))) if arrived.size else (lambda q: None)
    return ArrivalForecast(runs, arrived.size / runs, pct(10), pct(50), pct(90), hours, tracks)


def switch_on_time(fc: ArrivalForecast, buffer_h=SWITCH_ON_BUFFER_H):
    """Hours after the alert to start the curtain, or None if no arrival is expected."""
    if fc.p10_h is None:
        return None
    return max(0.0, fc.p10_h - buffer_h)


def approach_section(fc: ArrivalForecast, n_sections=3) -> str:
    """Which curtain section faces the swarm: West (A), Centre (B) or East (C).

    Uses the mean bearing of the last tracked approach before arrival.
    """
    bearings = []
    for tr in fc.tracks:
        d = np.hypot(tr[:, 0], tr[:, 1])
        idx = np.flatnonzero(d <= 3 * ARRIVE_RADIUS_M)
        if idx.size:
            x, y = tr[idx[0]]
            bearings.append(np.degrees(np.arctan2(x, y)))
    if not bearings:
        return "B"
    b = float(np.mean(bearings))  # -90 (west) .. +90 (east)
    edges = np.linspace(-90, 90, n_sections + 1)
    i = int(np.clip(np.searchsorted(edges, b) - 1, 0, n_sections - 1))
    return "ABC"[i]


@dataclass
class ReleaseOption:
    distance_km: float
    bearing_deg: float
    p_return: float
    p_beach: float  # strands anywhere on the coast within the horizon
    p_beach_near: float  # strands within NEAR_COAST_KM of the plant
    tow_hours: float

    @property
    def score(self) -> float:
        return self.p_return + 0.5 * self.p_beach_near


def plan_release(cond: Conditions, distances_km=(6, 10, 15), bearings_deg=range(-75, 76, 15),
                 runs=120, horizon_h=72.0, min_coast_km=3.0, seed=1):
    """Score candidate release points by simulating released jellyfish for 72 h.

    Return = comes back within the arrival radius of the gap.
    Beach = reaches the coastline; strandings within NEAR_COAST_KM of the plant
    are penalised (beaches and facilities near the site), strandings far away
    are reported but treated like a bloom's natural end.
    """
    rng = np.random.default_rng(seed)
    options = []
    for dist in distances_km:
        for b in bearings_deg:
            start = bearing_to_xy(dist, b % 360)
            if start[1] < min_coast_km * 1000:
                continue
            tr = drift_many(start, [perturb(cond, rng) for _ in range(runs)], horizon_h, rng)
            returned = (np.hypot(tr[..., 0], tr[..., 1]) <= ARRIVE_RADIUS_M).any(axis=0)
            on_coast = tr[..., 1] <= 0.0
            beached = on_coast.any(axis=0) & ~returned
            first = on_coast.argmax(axis=0)
            x_strand = tr[first, np.arange(tr.shape[1]), 0]
            near = beached & (np.abs(x_strand) <= NEAR_COAST_KM * 1000)
            options.append(ReleaseOption(dist, b % 360, returned.mean(), beached.mean(), near.mean(),
                                         dist * 1000 / TOW_SPEED / 3600))
    options.sort(key=lambda o: (o.score, o.tow_hours))
    return options
