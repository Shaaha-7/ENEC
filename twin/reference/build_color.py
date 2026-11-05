"""Re-make the site colour image from Sentinel-2 L2A surface reflectance (B04, B03, B02 at 10 m) with highlight
compression, so the bright desert and the plant keep their detail instead of clipping to white like the TCI product."""
import json, numpy as np, rasterio
from rasterio.windows import from_bounds
from rasterio.warp import transform
from PIL import Image, ImageFilter
SCENE = "https://sentinel-cogs.s3.us-west-2.amazonaws.com/sentinel-s2-l2a-cogs/39/Q/XG/2025/11/S2C_39QXG_20251101_0_L2A/"
m = json.load(open('site/meta.json')); H = 10000; N = 2000
def band(b):
    with rasterio.open(SCENE + b + '.tif') as s:
        xs, ys = transform("EPSG:4326", s.crs, [m['lon0']], [m['lat0']])
        w = from_bounds(xs[0] - H, ys[0] - H, xs[0] + H, ys[0] + H, s.transform)
        return s.read(1, window=w, boundless=True, out_shape=(N, N)).astype(np.float32)
r, g, b = (band(x) for x in ('B04', 'B03', 'B02'))
off = 0.0   # earthsearch:boa_offset_applied = True for this scene (DN = reflectance*10000)
rgb = np.stack([(c - off) / 10000.0 for c in (r, g, b)], -1).clip(0, None)
x = rgb * 3.7                                   # exposure for dark water
y = x / (1 + 0.55 * x)                          # Reinhard-style highlight roll-off: desert keeps texture
y = y ** (1 / 1.18)
lum = y.mean(-1, keepdims=True); y = lum + (y - lum) * 1.18   # a little saturation back
img = Image.fromarray((y.clip(0, 1) * 255).astype(np.uint8))
img = img.filter(ImageFilter.UnsharpMask(radius=1.2, percent=60, threshold=2))
img.save('site/tci.jpg', quality=88, optimize=True)
img.crop((700, 640, 1480, 1300)).save('/tmp/claude-0/-home-user-bess-degradation-analytics/78f97ac8-8231-58f9-92dd-4407d24e43fa/scratchpad/color_chk.jpg')
print('ok', img.size)
