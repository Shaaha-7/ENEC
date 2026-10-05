"""Scale the full-size design down to a tank model (Froude similarity).

    python -m qandeel.tank_scale --scale 20      # 1:20 model
    python -m qandeel.tank_scale --scale 10

Free-surface flows driven by gravity and buoyancy (bubble plumes, surface
currents, booms) keep the same behaviour when the Froude number U / sqrt(g L)
is the same. With length ratio S (full / model):
    lengths / S,   speeds / sqrt(S),   times / sqrt(S),
    airflow per metre of pipe / S^1.5  (so Bulson's (g q)^(1/3) scales like a speed).

Jellyfish behaviour does not scale: mock jellyfish are passive, so the model
tests the hydrodynamics (curtain current, boom downflow, bag), not swimming.
"""
import argparse
import math

from qandeel.sim.curtain import bulson_surface_current
from qandeel.sim.forcing import load_site


def model(scale: float) -> list[tuple[str, str, str]]:
    site = load_site()
    s, rs = scale, math.sqrt(scale)
    gap, depth = float(site["gap_width_m"]), float(site["gap_depth_m"])
    q_full = 3.0  # L/s per m
    q_model = q_full / s ** 1.5  # L/s per m
    rows = [
        ("Gap width", f"{gap:.0f} m", f"{gap / s * 100:.0f} cm"),
        ("Water depth", f"{depth:.1f} m", f"{depth / s * 100:.0f} cm"),
        ("Curtain height above bed", "0.5-1 m", f"{50 / s:.1f}-{100 / s:.1f} cm"),
        ("Airflow per metre of pipe", f"{q_full:g} L/s", f"{q_model * 60:.2f} L/min per m"),
        ("Bubble surface current (Bulson)", f"{bulson_surface_current(q_full):.2f} m/s",
         f"{bulson_surface_current(q_full) / rs * 100:.1f} cm/s"),
        ("Approach current where curtain holds", "0.20 m/s", f"{20 / rs:.1f} cm/s"),
        ("Approach current where curtain fails", "0.30-0.40 m/s", f"{30 / rs:.1f}-{40 / rs:.1f} cm/s"),
        ("Jellyfish bell diameter", "30-45 cm", f"{30 / s:.1f}-{45 / s:.1f} cm"),
        ("Boom skirt depth", "2.0 m", f"{200 / s:.0f} cm"),
        ("Retention bag depth", "3.0 m", f"{300 / s:.0f} cm"),
        ("Boom pocket length", "30 m", f"{3000 / s:.0f} cm"),
        ("Boom tow speed (with bag)", "0.20 m/s", f"{20 / rs:.1f} cm/s"),
        ("Oil-boom failure speed", "0.35 m/s", f"{35 / rs:.1f} cm/s"),
        ("30 minutes in the boom", "30 min", f"{30 / rs:.1f} min"),
    ]
    return rows


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--scale", type=float, default=20.0, help="full size / model size, e.g. 20 for 1:20")
    a = ap.parse_args()
    rows = model(a.scale)
    w = max(len(r[0]) for r in rows)
    print(f"Froude-scaled tank model, 1:{a.scale:g}")
    print(f"{'Quantity'.ljust(w)}  {'Full size':>14}  {'Model':>18}")
    for name, full, mod in rows:
        print(f"{name.ljust(w)}  {full:>14}  {mod:>18}")


if __name__ == "__main__":
    main()
