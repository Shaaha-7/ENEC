"""Surface-drift forcing for the planner: real Gulf record if present, else assumed.

Velocity series have shape (steps, runs, 2) in m/s (east, north), one series per
Monte Carlo run. With a real record, each run starts at a random hour of the
bloom-season record (an "analogue ensemble" of real past conditions) plus a
small forecast error. Without it, runs use the assumed tidal model in env.py.
"""
import csv
import json
from dataclasses import dataclass
from pathlib import Path

import numpy as np

from .env import TIDAL_PERIOD_H, WIND_DRIFT_FACTOR, Conditions, perturb

ROOT = Path(__file__).resolve().parent.parent
DATA_CSV = ROOT / "data" / "gulf_forcing.csv"


def load_site() -> dict:
    return json.loads((ROOT / "site.json").read_text())


@dataclass
class Record:
    time: list
    current: np.ndarray  # (hours, 2) m/s
    wind_drift: np.ndarray  # (hours, 2) m/s, 3% of wind, blowing downwind

    @property
    def label(self) -> str:
        return f"Copernicus SMOC currents + ERA5 wind, {self.time[0][:10]} to {self.time[-1][:10]}"


def load_record(path=DATA_CSV) -> Record | None:
    if not Path(path).exists():
        return None
    t, cur, wd = [], [], []
    with open(path, newline="") as f:
        for r in csv.DictReader(f):
            t.append(r["time"])
            cur.append((float(r["current_u"]), float(r["current_v"])))
            to = np.deg2rad(float(r["wind_from_deg"]) + 180.0)
            s = WIND_DRIFT_FACTOR * float(r["wind_speed"])
            wd.append((s * np.sin(to), s * np.cos(to)))
    return Record(t, np.array(cur), np.array(wd))


def source_label(record: Record | None) -> str:
    return record.label if record is not None else "assumed conditions (no real data file yet)"


def velocity_series(hours: float, dt_s: float, runs: int, rng: np.random.Generator,
                    cond: Conditions | None = None, record: Record | None = None) -> np.ndarray:
    steps = int(hours * 3600 / dt_s)
    t_h = np.arange(steps) * dt_s / 3600
    if record is not None:
        total = record.current + record.wind_drift  # (H, 2)
        n_h = len(total)
        if n_h < hours + 2:
            raise ValueError("record shorter than the simulation horizon")
        starts = rng.integers(0, n_h - int(np.ceil(hours)) - 1, runs)
        idx = starts[None, :] + t_h[:, None]  # fractional hour index (steps, runs)
        lo = np.floor(idx).astype(int)
        frac = (idx - lo)[..., None]
        v = total[lo] * (1 - frac) + total[lo + 1] * frac
        # forecast error: +-20% strength, small bias
        v = v * rng.normal(1.0, 0.2, (1, runs, 1)) + rng.normal(0, 0.02, (1, runs, 2))
        return v
    cond = cond or Conditions()
    variants = [perturb(cond, rng) for _ in range(runs)]
    amp = np.array([c.tide_amp for c in variants])
    phase = np.array([c.tide_phase_h for c in variants])
    base = np.array([[c.residual_u, c.residual_v] for c in variants]) + np.array(
        [c.wind_drift() for c in variants])
    v = np.repeat(base[None], steps, axis=0)
    v[..., 0] += amp[None] * np.sin(2 * np.pi * (t_h[:, None] + phase[None]) / TIDAL_PERIOD_H)
    return v
