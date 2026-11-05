"""Build the real-site layers for the simulator from Sentinel-2 L2A (Copernicus open data, AWS sentinel-cogs).
Outputs in sim/site/: tci.jpg (true colour), mask.png (water=255, physics), height.png, meta.json."""
import json, math, numpy as np, rasterio
from rasterio.windows import from_bounds
from rasterio.warp import transform
from PIL import Image, ImageFilter
from scipy import ndimage
SCENE = "https://sentinel-cogs.s3.us-west-2.amazonaws.com/sentinel-s2-l2a-cogs/39/Q/XG/2025/11/S2C_39QXG_20251101_0_L2A/"
site = json.load(open('/home/user/enec/qandeel/site.json'))
(a_lat, a_lon), (b_lat, b_lon) = site['gap_tip_a'], site['gap_tip_b']
LAT0, LON0 = (a_lat + b_lat) / 2, (a_lon + b_lon) / 2
H = 10000; N = 2 * H // 10
def read(band, crs_pt=None):
    with rasterio.open(SCENE + band) as s:
        xs, ys = transform("EPSG:4326", s.crs, [LON0], [LAT0]); cx, cy = xs[0], ys[0]
        w = from_bounds(cx - H, cy - H, cx + H, cy + H, s.transform)
        return s.read(window=w, boundless=True, out_shape=(s.count, N, N)), (cx, cy), s.crs
tci, (cx, cy), crs = read("TCI.tif")
g = read("B03.tif")[0][0].astype(float); nir = read("B08.tif")[0][0].astype(float)
ndwi = (g - nir) / (g + nir + 1e-6)
water = ndwi > 0.0
water = ndimage.binary_opening(water, iterations=1) | (ndwi > 0.15)
rgb0 = np.moveaxis(tci, 0, -1).astype(float)
white = (rgb0.min(axis=2) > 95) & (rgb0[..., 0] > 105)        # rock/concrete crests are grey-white; sea is cyan
water = water & ~white
land = ~water
land = ndimage.binary_closing(land, structure=np.ones((3, 3)), iterations=2)
land = ndimage.binary_fill_holes(land) if False else land
# physics mask: grow land 1 px so thin breakwaters stay closed
phys_land = ndimage.binary_dilation(land, iterations=1)
Image.fromarray(np.where(phys_land, 0, 255).astype(np.uint8)).save('site/mask.png', optimize=True)
# heights: land +3 m, thin structures (breakwaters) +6 m, sea -5 m, dark dredged channel -9.5 m
rgb = np.moveaxis(tci, 0, -1).astype(float)
bright = rgb.mean(axis=2)
land_frac = ndimage.uniform_filter(land.astype(float), 9)
h = np.full(land.shape, -5.0)
h[water & (bright < 32)] = -9.5
h[land] = 3.0
h[land & (land_frac < 0.35)] = 6.0
h = ndimage.gaussian_filter(h, 1.0)
h[land & (land_frac < 0.35)] = np.maximum(h[land & (land_frac < 0.35)], 5.0)
hq = np.clip((h + 12) / 20 * 255, 0, 255).astype(np.uint8)
Image.fromarray(hq).resize((1000, 1000), Image.BILINEAR).save('site/height.png', optimize=True)
# colour: gentle tone curve so desert does not clip
c = rgb / 255.0
c = np.clip(c * 1.35, 0, 1) ** 0.95
c = 1 - (1 - c) ** 1.15
Image.fromarray((c * 255).astype(np.uint8)).save('site/tci.jpg', quality=86, optimize=True)
def px(lat, lon):
    x, y = transform("EPSG:4326", crs, [lon], [lat]); return [(x[0] - (cx - H)) / 10, ((cy + H) - y[0]) / 10]
A, B = px(a_lat, a_lon), px(b_lat, b_lon)
dE, dN = (B[0] - A[0]) * 10, -(B[1] - A[1]) * 10
theta = math.atan2(dN, dE)
meta = dict(scene="S2C_39QXG_20251101 (Sentinel-2 L2A, Copernicus)", px_m=10, size_px=N, center_px=[(A[0] + B[0]) / 2, (A[1] + B[1]) / 2],
            tip_a=A, tip_b=B, gap_m=math.hypot(dE, dN), theta_rad=theta, lat0=LAT0, lon0=LON0)
json.dump(meta, open('site/meta.json', 'w'), indent=1); print(meta)
