"""U-boom collection: how many jellyfish escape under the skirt while towing.

Vertical slice through the boom pocket, in the boom's frame. Water enters the
mouth (x = 0) and flows toward the apex (x = POCKET_M) at the tow speed U.
Near the apex the flow has nowhere to go but down and under the skirt, so it
turns downward with speed DOWNFLOW_FRACTION * U. This is the same mechanism that
makes oil booms lose oil above ~0.35 m/s; jellyfish are neutrally buoyant, so
unlike oil they do not float back up.

Each jellyfish starts in the mouth at a depth drawn from the field observation
that 87-100% sit in the top metre (exponential, mean 0.35 m: 94% above 1 m),
and swims at its own speed. Its vertical swimming is mostly depth-keeping
(DEPTH_KEEPING = 70% of effort steers back toward its preferred depth, which is
why the field finds them near the surface) and partly a wandering heading.
It escapes if it reaches the apex deeper than the skirt, or is pushed below
the skirt while held at the apex.

DOWNFLOW_FRACTION is not known for jellyfish: it is a key quantity for the tank
test. Results are shown for two values.

Design option, closed-bottom retention bag: a soft fabric pocket hung at the
apex (no mesh, so nothing tangles), BAG_DEPTH_M deep with a floor. Water still
leaks through the fabric, so the apex downflow is cut to BAG_LEAK of its open
value, and a jellyfish only escapes by going below the bag floor. Both numbers
are design hypotheses for the tank test, not measurements.
"""
from dataclasses import dataclass

import numpy as np

POCKET_M = 30.0
APEX_ZONE_M = 3.0
DOWNFLOW_FRACTION = 0.5
DEPTH_MEAN_M = 0.35
DEPTH_KEEPING = 0.7
BAG_DEPTH_M = 3.0
BAG_LEAK = 0.2


@dataclass
class BoomResult:
    tow_speed: float
    skirt_m: float
    swim_max: float
    downflow_fraction: float
    retained_share: float
    bag: bool = False


def run_boom(tow_speed=0.3, skirt_m=1.5, swim_max=0.10, downflow_fraction=DOWNFLOW_FRACTION, minutes=60,
             n=600, seed=0, dt=1.0, bag=False) -> BoomResult:
    rng = np.random.default_rng(seed)
    x = rng.uniform(0, 5, n)
    z = np.minimum(rng.exponential(DEPTH_MEAN_M, n), 6.0)  # depth, m (positive down)
    s = rng.uniform(0.02, swim_max, n)
    th = rng.uniform(0, 2 * np.pi, n)
    escaped = z > skirt_m + 5  # nobody yet
    turn_sd = np.sqrt(dt / 60.0)  # heading wanders over ~1 minute
    kz = 2e-4 * (tow_speed / 0.3)  # turbulent vertical mixing, m^2/s (assumed, grows with flow)
    mix_sd = np.sqrt(2 * kz * dt)
    for _ in range(int(minutes * 60 / dt)):
        live = ~escaped
        th[live] += rng.normal(0, turn_sd, live.sum())
        u = tow_speed + s * np.cos(th)
        keep = -np.tanh((z - DEPTH_MEAN_M) / 0.5)  # swim up when too deep
        w = s * ((1 - DEPTH_KEEPING) * np.sin(th) + DEPTH_KEEPING * keep)  # + = downward
        apex_frac = np.clip((x - (POCKET_M - APEX_ZONE_M)) / APEX_ZONE_M, 0, 1)
        k = downflow_fraction * (BAG_LEAK if bag else 1.0)
        w = w + k * tow_speed * apex_frac
        x = np.where(live, np.clip(x + u * dt, 0, POCKET_M), x)
        z = np.where(live, np.maximum(z + w * dt + rng.normal(0, mix_sd, n), 0.0), z)
        floor = BAG_DEPTH_M if bag else skirt_m
        out = live & (x >= POCKET_M - 0.5) & (z > floor)
        escaped |= out
    return BoomResult(tow_speed, skirt_m, swim_max, downflow_fraction, float(1 - escaped.mean()), bag)


def boom_grid(speeds=(0.1, 0.15, 0.2, 0.25, 0.3, 0.35, 0.45), skirts=(1.0, 1.5, 2.0), swims=(0.10, 0.20),
              downflows=(0.25, 0.5), n=400, minutes=30):
    rows = []
    for k in downflows:
        for sw in swims:
            for d in skirts:
                for u in speeds:
                    r = run_boom(u, d, sw, k, minutes=minutes, n=n)
                    rows.append({"downflow_fraction": k, "swim_max_m_s": sw, "skirt_m": d, "tow_m_s": u,
                                 "bag": False, "retained_share": round(r.retained_share, 3)})
            for u in speeds:  # 2 m skirt with the closed-bottom retention bag
                r = run_boom(u, 2.0, sw, k, minutes=minutes, n=n, bag=True)
                rows.append({"downflow_fraction": k, "swim_max_m_s": sw, "skirt_m": 2.0, "tow_m_s": u,
                             "bag": True, "retained_share": round(r.retained_share, 3)})
    return rows
