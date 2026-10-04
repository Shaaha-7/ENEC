"""Bubble-curtain hold test at the breakwater gap.

Local frame in metres: x = along the coast, y = offshore. The breakwater lies
on y = 0 with a gap of width GAP_W centred on x = 0; the intake is behind it.
The curtain spans the gap between the two breakwater heads, CURTAIN_Y metres
seaward of the gap line.

Each jellyfish is an individual that drifts with the water and also swims on
a slowly wandering heading. The water is the sum of:
  * an approach flow toward the gap centre (intake draw plus tide), speed V;
  * a weak along-shore tidal current that slides held jellyfish sideways;
  * the curtain's surface outflow, pointing away from the bubble line on each
    side, from Bulson's empirical relation for pneumatic barriers:
        U0 = 1.46 * (g * q) ** (1/3),  q = free-air flow per metre (m^2/s),
    decaying with distance d from the line as exp(-d / (2 * depth)).
A jellyfish is "held" while it stays seaward of the curtain; once it crosses
the bubble line it is carried into the gap.
"""
from dataclasses import dataclass

import numpy as np

G = 9.81
GAP_W = 300.0  # m, assumed gap width
DEPTH = 8.0  # m, assumed water depth at the gap
CURTAIN_Y = 5.0  # m seaward of the gap line


def bulson_surface_current(q_l_per_s_per_m: float) -> float:
    """Peak surface current (m/s) induced by a bubble curtain."""
    q = q_l_per_s_per_m / 1000.0
    return 1.46 * (G * q) ** (1.0 / 3.0)


@dataclass
class HoldResult:
    approach_speed: float
    airflow: float  # L/s per metre, 0 = curtain off
    n: int
    entered: int
    final_xy: np.ndarray
    entered_mask: np.ndarray
    start_xy: np.ndarray

    @property
    def held_share(self) -> float:
        return 1.0 - self.entered / self.n


def run_hold(approach_speed=0.15, airflow=3.0, hours=3.0, n=500, swim_min=0.02,
             swim_max=0.10, alongshore_amp=0.08, seed=0, dt=2.0) -> HoldResult:
    rng = np.random.default_rng(seed)
    pos = np.column_stack([rng.uniform(-400, 400, n), rng.uniform(150, 500, n)])
    start = pos.copy()
    swim = rng.uniform(swim_min, swim_max, n)
    heading = rng.uniform(0, 2 * np.pi, n)
    u0 = bulson_surface_current(airflow) if airflow > 0 else 0.0
    decay = 2 * DEPTH
    half = GAP_W / 2
    alive = np.ones(n, bool)  # still outside the gap
    turn_sd = np.sqrt(dt / 120.0)  # heading wanders over ~2 minutes
    diff_sd = np.sqrt(2 * 0.05 * dt)
    steps = int(hours * 3600 / dt)
    for k in range(steps):
        t_h = k * dt / 3600
        p = pos[alive]
        # approach flow toward the gap centre
        r = np.hypot(p[:, 0], p[:, 1]) + 1e-9
        v = -approach_speed * p / r[:, None]
        # along-shore tide, semi-diurnal
        v[:, 0] += alongshore_amp * np.sin(2 * np.pi * t_h / 12.42)
        # curtain outflow, away from the line on each side, strongest over the gap
        if u0 > 0:
            dy = p[:, 1] - CURTAIN_Y
            dx_out = np.maximum(np.abs(p[:, 0]) - half, 0.0)  # beyond the heads
            dist = np.hypot(dy, dx_out)
            v[:, 1] += np.sign(dy) * u0 * np.exp(-dist / decay)
        # swimming on a wandering heading
        heading[alive] += rng.normal(0, turn_sd, alive.sum())
        h = heading[alive]
        v += swim[alive, None] * np.column_stack([np.cos(h), np.sin(h)])
        nxt = p + v * dt + rng.normal(0, diff_sd, p.shape)
        # breakwater: solid outside the gap
        wall = (nxt[:, 1] < 0) & (np.abs(nxt[:, 0]) > half)
        nxt[wall, 1] = 0.0
        pos[alive] = nxt
        # crossing the curtain line over the gap = carried in
        gone = (nxt[:, 1] < CURTAIN_Y) & (np.abs(nxt[:, 0]) <= half)
        idx = np.flatnonzero(alive)
        alive[idx[gone]] = False
    entered = int((~alive).sum())
    return HoldResult(approach_speed, airflow, n, entered, pos.copy(), ~alive, start)


def hold_grid(speeds=(0.05, 0.10, 0.15, 0.20, 0.30, 0.40), airflows=(0.0, 1.5, 3.0, 4.5),
              n=400, hours=3.0, seed=0):
    """Share of jellyfish held outside the gap for each approach speed and airflow."""
    rows = []
    for q in airflows:
        for s in speeds:
            res = run_hold(s, q, hours=hours, n=n, seed=seed)
            rows.append({"airflow_l_s_m": q, "approach_m_s": s,
                         "surface_current_m_s": round(bulson_surface_current(q), 3) if q else 0.0,
                         "held_share": round(res.held_share, 3)})
    return rows
