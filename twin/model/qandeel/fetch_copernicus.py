"""Download full-precision hourly surface currents directly from Copernicus Marine.

Why: the Open-Meteo route rounds current speed to 0.1 km/h (~0.028 m/s), so the
2025 season has only about 10 distinct speeds, and many hours repeat the previous
one. Copernicus Marine serves the same SMOC currents at full precision.

Needs a free Copernicus Marine account (https://marine.copernicus.eu):

    pip install copernicusmarine
    copernicusmarine login
    python -m qandeel.fetch_copernicus                 # June-September 2025, site point
    python -m qandeel.run_all

Keeps the ERA5 wind already in qandeel/data/gulf_forcing.csv and replaces only the
current columns, writing a backup first. Not tested from the team's build machine
(no access there); check the printed summary before re-running the simulation.
"""
import argparse
import csv
import json
import math
import shutil
from pathlib import Path

HERE = Path(__file__).parent
CSV = HERE / "data" / "gulf_forcing.csv"
# SMOC: Global Ocean Physics Analysis and Forecast, merged surface currents incl. tides and Stokes drift
DATASET = "cmems_mod_glo_phy_anfc_merged-uv_PT1H-i"


def main():
    site = json.loads((HERE / "site.json").read_text())
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--start", default="2025-06-01")
    ap.add_argument("--end", default="2025-09-30")
    ap.add_argument("--lat", type=float, default=site["data_lat"])
    ap.add_argument("--lon", type=float, default=site["data_lon"])
    ap.add_argument("--dataset", default=DATASET)
    a = ap.parse_args()
    try:
        import copernicusmarine
    except ImportError:
        raise SystemExit("pip install copernicusmarine, then: copernicusmarine login")

    ds = copernicusmarine.open_dataset(
        dataset_id=a.dataset, variables=["utotal", "vtotal"],
        minimum_longitude=a.lon - 0.05, maximum_longitude=a.lon + 0.05,
        minimum_latitude=a.lat - 0.05, maximum_latitude=a.lat + 0.05,
        start_datetime=f"{a.start}T00:00:00", end_datetime=f"{a.end}T23:00:00")
    pt = ds.sel(latitude=a.lat, longitude=a.lon, method="nearest")
    if "depth" in pt.dims:
        pt = pt.isel(depth=0)
    times = [str(t)[:13] + ":00" for t in pt["time"].values]
    cur = {t: (float(u), float(v)) for t, u, v in zip(times, pt["utotal"].values, pt["vtotal"].values)
           if not (math.isnan(u) or math.isnan(v))}
    if not cur:
        raise SystemExit("No current values at this point; try --lat/--lon a little further offshore.")

    rows = list(csv.DictReader(CSV.open()))
    shutil.copy(CSV, CSV.with_suffix(".openmeteo.csv"))
    kept = 0
    with CSV.open("w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=["time", "current_u", "current_v", "wind_speed", "wind_from_deg"])
        w.writeheader()
        for r in rows:
            uv = cur.get(r["time"][:16])
            if uv is None:
                continue
            r["current_u"], r["current_v"] = f"{uv[0]:.4f}", f"{uv[1]:.4f}"
            w.writerow(r)
            kept += 1
    spd = [math.hypot(*cur[t]) for t in cur]
    print(f"Wrote {kept} hours to {CSV} (backup: {CSV.with_suffix('.openmeteo.csv').name})")
    print(f"Current speed: mean {sum(spd) / len(spd):.3f} m/s, max {max(spd):.3f} m/s, "
          f"{len({round(s, 4) for s in spd})} distinct values")


if __name__ == "__main__":
    main()
