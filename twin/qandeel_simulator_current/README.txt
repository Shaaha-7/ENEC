QANDEEL SIMULATOR - STAND NOTES (finals 11-13 Nov 2026)

Run offline (no internet needed except for "Ask Qandeel"):
  1. Open a terminal in this folder (plan_local/sim).
  2. python -m http.server 8000
  3. Chrome: http://localhost:8000/index.html   (F11 for full screen)
  Browsers block 3D modules from file://, so the small local server is required.

Online version (Ask Qandeel works there): the claude.ai artifact link.

Before the judges arrive:
  - Watch the FPS counter (top centre). Below 40 FPS for 3 s the page switches to Low graphics by itself;
    you can also press "Low graphics". Confirm >= 45 FPS on the stand laptop in Chrome.
  - Voice: Chrome/Edge built-in voices. ElevenLabs: Ask Qandeel tab > Voice > ElevenLabs, paste the key
    (kept in memory only; works in the offline copy, blocked inside claude.ai).
  - Idle for 2 minutes -> the guided mission starts by itself.

What is real data, what is model, what is assumed:
  - Site: Copernicus Sentinel-2 L2A, 1 Nov 2025, tile 39QXG (site/). Water depths approximate, not surveyed.
  - Sea: Copernicus SMOC currents + ERA5 wind, hourly 1 Jun - 30 Sep 2025 ("Real 2025 sea", "Season check").
  - Forecast tab: exported from qandeel/sim/planning.py with the published seeds (tools/export_forecast.py).
  - AI camera tab: real YOLO11n output on held-out test images; metrics re-measured on the 116-image test split.
  - Curtain physics near the mouth mirrors qandeel/sim/curtain.py (Bulson outflow, 2 s steps, swim, mixing).
  - Assumed: 1.5 m outflow layer, wave leakage rate, boat sizes drawn 3x, 1 marker = ~100 jellyfish.

Rebuild after editing qandeel_sim.html:  python tools/make_offline.py
Attribution: Contains modified Copernicus Sentinel data 2025; Copernicus Marine Service; ERA5 (C3S).
Fonts: Barlow, Barlow Condensed, JetBrains Mono (SIL Open Font License, fonts/).
