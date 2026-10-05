"""Turn two breakwater-tip coordinates into the gap width and save it to site.json.

In Google Earth (or Google Maps), right-click each breakwater tip at the intake
opening and copy its coordinates, then:

    python -m qandeel.measure_gap --a "23.9612, 52.2301" --b "23.9618, 52.2335"

Formats like "23.9612 N, 52.2301 E" also work. Add --depth 9.5 to set the depth.
Use --dry-run to print the width without saving.
"""
import argparse
import json
import math
import re
from pathlib import Path

SITE = Path(__file__).parent / "site.json"
EARTH_R = 6_371_008.8  # mean Earth radius, m


def parse_point(text: str) -> tuple[float, float]:
    nums = [float(x) for x in re.findall(r"-?\d+(?:\.\d+)?", text)]
    if len(nums) != 2:
        raise SystemExit(f"Could not read coordinates from {text!r}. Paste the numbers Google Maps shows, "
                         'e.g. --a "23.96123, 52.23045"')
    lat, lon = nums
    up = text.upper()
    if "S" in up:
        lat = -abs(lat)
    if "W" in up:
        lon = -abs(lon)
    if not (-90 <= lat <= 90 and -180 <= lon <= 180):
        raise ValueError(f"out of range: {text!r}")
    return lat, lon


def haversine_m(a, b) -> float:
    (la1, lo1), (la2, lo2) = [(math.radians(p[0]), math.radians(p[1])) for p in (a, b)]
    h = math.sin((la2 - la1) / 2) ** 2 + math.cos(la1) * math.cos(la2) * math.sin((lo2 - lo1) / 2) ** 2
    return 2 * EARTH_R * math.asin(math.sqrt(h))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--a", required=True, help="first breakwater tip, 'lat, lon'")
    ap.add_argument("--b", required=True, help="second breakwater tip, 'lat, lon'")
    ap.add_argument("--depth", type=float, help="water depth at the opening, m (optional)")
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()
    pa, pb = parse_point(a.a), parse_point(a.b)
    width = haversine_m(pa, pb)
    print(f"Gap width: {width:.0f} m")
    if width < 20 or width > 3000:
        print("Warning: unusual width; check you clicked the two breakwater tips.")
    if a.dry_run:
        return
    site = json.loads(SITE.read_text())
    site.update({"gap_width_m": round(width), "gap_width_measured": True,
                 "gap_tip_a": list(pa), "gap_tip_b": list(pb)})
    if a.depth is not None:
        site.update({"gap_depth_m": a.depth, "gap_depth_measured": True})
    SITE.write_text(json.dumps(site, indent=2) + "\n")
    print(f"Saved to {SITE}. Re-run: python -m qandeel.run_all")


if __name__ == "__main__":
    main()
