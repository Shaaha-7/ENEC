"""Download real Gulf currents and wind for the site, run on your own computer.

    python -m qandeel.data_fetch                         # bloom season 2025 (1 Jun - 30 Sep)
    python -m qandeel.data_fetch --start 2024-06-01 --end 2024-09-30
    python -m qandeel.data_fetch --forecast              # live 7-day forecast from now

Sources (free, no account):
  * Ocean surface currents: Open-Meteo Marine API, which serves the Copernicus
    Marine global ocean model (SMOC) currents.
  * 10 m wind: Open-Meteo historical weather archive (ERA5 reanalysis).

Writes qandeel/data/gulf_forcing.csv with hourly rows:
  time, current_u, current_v, wind_speed, wind_from_deg
current_u/v are east/north components in m/s. The planner uses this file
automatically when it exists.

Check the README for how to confirm the current direction convention.
"""
import argparse
import csv
import json
import math
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

HERE = Path(__file__).parent
OUT = HERE / "data" / "gulf_forcing.csv"
OUT_FORECAST = HERE / "data" / "forecast.csv"


def _get(url, params):
    full = url + "?" + urllib.parse.urlencode(params)
    with urllib.request.urlopen(full, timeout=60) as r:
        return json.loads(r.read().decode())


def fetch_currents(lat, lon, start, end, forecast_days=None):
    url = "https://marine-api.open-meteo.com/v1/marine"
    base = {"latitude": lat, "longitude": lon, "timezone": "GMT", "cell_selection": "sea",
            "hourly": "ocean_current_velocity,ocean_current_direction"}
    if forecast_days:
        data = _get(url, base | {"forecast_days": forecast_days})
    else:
        try:
            data = _get(url, base | {"start_date": start, "end_date": end})
        except urllib.error.HTTPError as e:
            print(f"Historical marine request refused ({e.code}); falling back to the last 92 days.")
            data = _get(url, base | {"past_days": 92, "forecast_days": 1})
    h = data["hourly"]
    speeds_kmh, dirs = h["ocean_current_velocity"], h["ocean_current_direction"]
    unit = data.get("hourly_units", {}).get("ocean_current_velocity", "km/h")
    out = {}
    for t, s, d in zip(h["time"], speeds_kmh, dirs):
        if s is None or d is None:
            continue
        ms = s / 3.6 if "km" in unit else s
        rad = math.radians(d)  # direction the current flows TOWARD
        out[t] = (ms * math.sin(rad), ms * math.cos(rad))
    return out


def fetch_wind(lat, lon, start, end, forecast_days=None):
    common = {"latitude": lat, "longitude": lon, "timezone": "GMT",
              "hourly": "wind_speed_10m,wind_direction_10m", "wind_speed_unit": "ms"}
    if forecast_days:
        data = _get("https://api.open-meteo.com/v1/forecast", common | {"forecast_days": forecast_days})
    else:
        data = _get("https://archive-api.open-meteo.com/v1/archive",
                    common | {"start_date": start, "end_date": end})
    h = data["hourly"]
    return {t: (s, d) for t, s, d in zip(h["time"], h["wind_speed_10m"], h["wind_direction_10m"])
            if s is not None and d is not None}


def main():
    site = json.loads((HERE / "site.json").read_text())
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--start", default="2025-06-01")
    ap.add_argument("--end", default="2025-09-30")
    ap.add_argument("--lat", type=float, default=site["data_lat"])
    ap.add_argument("--lon", type=float, default=site["data_lon"])
    ap.add_argument("--forecast", action="store_true", help="download the live 7-day forecast instead")
    a = ap.parse_args()

    days = 7 if a.forecast else None
    out = OUT_FORECAST if a.forecast else OUT
    cur = fetch_currents(a.lat, a.lon, a.start, a.end, forecast_days=days)
    wind = fetch_wind(a.lat, a.lon, min(cur)[:10], max(cur)[:10], forecast_days=days)
    times = sorted(set(cur) & set(wind))
    if len(times) < 72:
        raise SystemExit(f"Only {len(times)} matching hours; try another date range.")
    out.parent.mkdir(exist_ok=True)
    with out.open("w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["time", "current_u", "current_v", "wind_speed", "wind_from_deg"])
        for t in times:
            u, v = cur[t]
            s, d = wind[t]
            w.writerow([t, f"{u:.4f}", f"{v:.4f}", f"{s:.2f}", f"{d:.0f}"])
    spd = [math.hypot(*cur[t]) for t in times]
    print(f"Wrote {len(times)} hours ({times[0]} to {times[-1]}) to {out}")
    print(f"Current speed: mean {sum(spd) / len(spd):.2f} m/s, max {max(spd):.2f} m/s")
    print("Re-run: python -m qandeel.run_all")


if __name__ == "__main__":
    main()
