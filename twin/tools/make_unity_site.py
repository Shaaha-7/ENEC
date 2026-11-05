"""Convert the real-site layers in data/ into Unity terrain inputs in unity_site/.

World frame for Unity (metres): origin at the mouth centre (data/meta.json center_px), x = east, z = north, y = up.
Outputs:
  height_2049.raw  16-bit little-endian heightmap, row 0 = SOUTH edge (Unity TerrainData.SetHeights order),
                   value 0..65535 = -12..+8 m (height.png: value/255*20 - 12 m; approximate, NOT surveyed)
  tci.jpg, mask.png copied unchanged; site_unity.json with the frame and the gap tips in world metres.
Run: python tools/make_unity_site.py
"""
import json
import shutil
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
DATA, OUT = ROOT / "data", ROOT / "unity_site"
OUT.mkdir(exist_ok=True)
meta = json.loads((DATA / "meta.json").read_text())
SIZE_M = meta["size_px"] * meta["px_m"]  # 20 000 m
RES = 2049
H_MIN, H_RANGE = -12.0, 20.0

h8 = Image.open(DATA / "height.png").convert("L")
h = np.asarray(h8.resize((RES, RES), Image.BILINEAR), dtype=np.float64) / 255.0 * H_RANGE + H_MIN
# height.png is 20 m/px, which smooths 30-50 m breakwaters below sea level. Restore them from the 10 m layers:
# every land pixel of mask.png stays >= +2 m, and the rock-armour points of breakwater.json form a +5 m crest.
# (Design assumption for display only: crest heights are not surveyed.)
LAND_MIN_M, CREST_M = 2.0, 5.0
scale = RES / meta["size_px"]
land = np.asarray(Image.open(DATA / "mask.png").convert("L").resize((RES, RES), Image.NEAREST)) == 0
h[land] = np.maximum(h[land], LAND_MIN_M)
for px, py in json.loads((DATA / "breakwater.json").read_text()):
    cx_, cy_ = int(round(px * scale)), int(round(py * scale))
    h[max(cy_ - 1, 0):cy_ + 2, max(cx_ - 1, 0):cx_ + 2] = np.maximum(h[max(cy_ - 1, 0):cy_ + 2, max(cx_ - 1, 0):cx_ + 2], CREST_M)
h = np.flipud(h)  # image row 0 is north; Unity row 0 is south
raw = np.clip((h - H_MIN) / H_RANGE * 65535.0, 0, 65535).astype("<u2")
raw.tofile(OUT / "height_2049.raw")

# Water grid for boat routing (core Route.cs): 2000 x 2000 bytes, 1 = water, row 0 = NORTH (as the image), 10 m cells
water = (np.asarray(Image.open(DATA / "mask.png").convert("L")) > 127).astype(np.uint8)
water.tofile(OUT / "water_2000.bin")

for f in ("tci.jpg", "mask.png", "breakwater.json"):
    shutil.copy(DATA / f, OUT / f)

cx, cy = meta["center_px"]
to_world = lambda p: [round((p[0] - cx) * meta["px_m"], 2), round((cy - p[1]) * meta["px_m"], 2)]  # noqa: E731
site = {
    "source": meta["scene"] + "; heights approximate (not surveyed)",
    "size_m": SIZE_M, "resolution": RES, "height_min_m": H_MIN, "height_range_m": H_RANGE,
    "terrain_origin_xz": [-(cx * meta["px_m"]), -((meta["size_px"] - cy) * meta["px_m"])],
    "tip_a_xz": to_world(meta["tip_a"]), "tip_b_xz": to_world(meta["tip_b"]),
    "gap_m_measured": 967, "theta_rad": meta["theta_rad"],
}
(OUT / "site_unity.json").write_text(json.dumps(site, indent=1))
print(json.dumps(site, indent=1), "\nheights m: min", round(h.min(), 2), "max", round(h.max(), 2))
