# Prompt for the local session (paste everything below the line)

---

# Qandeel Digital Twin: World-Class Interactive Simulation

## 1. Mission
Build the **Qandeel digital twin**: a real-time, interactive 3D simulation of the Qandeel jellyfish-swarm defence at the **Barakah Nuclear Energy Plant seawater intake (UAE)**. It is the centrepiece of Team Qandeel's stand at the **ATRC Advanced Technology Pioneers 2026 finals, ENEC track, 11–13 November 2026**. The judges include engineers from ENEC, a government nuclear operator.

The bar is **world class**: visuals at the level of a professional engineering visualisation or AAA-quality environment, physics that an engineer can verify, and zero failures on the day. When **accuracy** and **beauty** conflict, accuracy wins, but aim for both.

You are on a local machine with a real GPU, a screen and full internet. Use the best free tools available. Do not lower ambition because something is hard; lower it only when a test, a measurement or the deadline proves it is necessary, and tell me when you do.

## 2. Non-negotiable rules
1. **Truth.** Every number shown comes from this kit (`model/qandeel/outputs/*.json`, `model/qandeel/site.json`, `data/*`) or a public source you cite on screen. If a value does not exist, show "not measured" or leave the feature out. Every assumption is labelled "assumed" on screen. No decorative fake data, no fake AI boxes, no invented equipment.
2. **Verified physics.** The simulation core must reproduce the Python models (`model/qandeel/sim/curtain.py`, `planning.py`, `benefits.py`). Build it as a **renderer-independent core** with automated tests against the Python outputs (section 9). A feature that fails its test does not ship.
3. **Free.** Only free software and free tiers: Unreal Engine 5 (free under its revenue threshold), Unity 6 Personal, Blender, three.js, Godot, Cesium (check the free tier's terms for a competition display), Node, Python, open MCP servers. No secrets in code or files; API keys only in environment variables.
4. **Reliability.** It must run **offline** on the stand laptop, start in under 30 s, run 3 hours without a crash, and degrade gracefully on weaker hardware.
5. **Show, don't claim.** After every milestone, run it, record a screenshot or short video, measure FPS on the target laptop, and report honestly what works and what does not.
6. Do not push to GitHub unless I ask.

## 3. Engine decision (day 1, measured, not guessed)
Benchmark the stand laptop first: GPU model, VRAM, CPU, RAM, and a test scene in each candidate. Then choose with this rule and explain the choice:
- **Dedicated GPU (RTX 3060 / RX 6600 class or better): Unreal Engine 5** (Lumen lighting, Water plugin, Niagara particles for bubbles and jellyfish, landscape from the real site data). Physics core in C++ or a Python/TypeScript service, with the same tests.
- **Mid-range GPU: Unity 6 HDRP** with the HDRP Water System and VFX Graph.
- **Integrated graphics only:** three.js with WebGPU (WebGL2 fallback), Vite + TypeScript.

Whatever the engine: a 1920×1080 build at **≥ 60 FPS on High and ≥ 30 FPS on Low** on the stand laptop, and a packaged offline build. Use MCP servers where they help (Blender MCP for models, Unreal/Unity MCP for the editor, Playwright for automated visual and UI tests).

## 4. What is in this kit
- `model/qandeel/`: the validated Python models and published results (`outputs/results.json`, `design.json`, `costs.json`, `depth_sensitivity.json`), real 2025 hourly currents and wind (`data/gulf_forcing.csv`).
- `data/`: real site and exports.
  - `tci.jpg`: 2000×2000 px, 10 m/px, Copernicus Sentinel-2 L2A, 1 Nov 2025, tile 39QXG, natural colour.
  - `mask.png`: water white, land and breakwaters black.
  - `height.png`: approximate heights (value/255·20 − 12 m; not surveyed).
  - `breakwater.json`: rock-armour points.
  - `meta.json`: the frame.
    - Local frame: x runs along the mouth from tip a (west) to tip b (east), y points offshore.
    - Conversion: `E = x·cosθ − y·sinθ`, `N = x·sinθ + y·cosθ`, `px = cx + E/10`, `py = cy − N/10`.
  - `forcing2025.json`: hourly currents and wind, ready for replay.
  - `forecast_meta.json` + `fc_atlas_*.png`: the planner's 400-run forecast, hour by hour, for three alerts.
  - `detector_metrics.json`, `ai/`: real YOLO11n detections; `detector/`: the weights.
  - `narration_lines.json`: 15 checked narration texts.
- `reference/`: the previous web simulator (for its verified logic: physics block, season check, forecast and AI panels) and the data scripts. Learn from it; do not copy its visual quality.
- `qandeel_simulator_current/` (separate zip): the newest offline three.js build of that simulator, with vendored three.js and fonts. Test it first; if three.js is chosen, it is the starting point for logic and data loading.
- Higher-resolution imagery: you may fetch free sources (e.g. more Sentinel-2 dates, Copernicus DEM, GEBCO bathymetry, Cesium/Bing via the free tier if its terms allow) if they improve fidelity. Record the source and licence of every layer, and keep the measured 967 m mouth exactly.

## 5. Verified facts (use exactly these)
- **Mouth:** 967 m wide (measured tip to tip); depth up to ~9.5 m, **not surveyed**.
- **Curtain:**
  - 10 sections × 96.7 m of HDPE PE100 **OD200 SDR17** pipe.
  - **2 mm** holes, ~1,133 per section at normal air (8.5 cm apart).
  - Pipe **0.75 m above the seabed** on precast concrete sleepers. Sleeper spacing is **not specified**: do not state one.
  - ~4.7 t submerged ballast per section; float-and-sink installation.
- **Bulson surface current** U₀ = 1.46·(g·q)^(1/3), q in m³/s per metre:
  - 1.5 L/s/m → 0.36 m/s; **3 → 0.45**; 4.5 → 0.52.
  - Approach current held (≥ 95%): **0.20 / 0.275 / 0.325 m/s**.
- **Air supply:**
  - Flow 174 m³/min normal, 261 max.
  - Discharge pressure 1.06–1.22 bar(g) normal, 1.12–1.28 max.
  - Power ~436 kW at normal; 31.4 MWh ≈ USD 3,141 per 72 h event.
  - **3 oil-free screw blowers** (2 duty + 1 standby), 0.3–1.5 bar(g), ~87 m³/min each, containerised on the western tip, 11 kV cable. Always "blowers", never "compressors".
- **Approach current:** 0.08 m/s intake draw (**assumed**) + onshore current + 1% wind drift. In 2025: **mean 0.14 m/s, worst 5% 0.23 m/s**.
- **Plant intake:** Barakah draws up to 172.5 m³/s per unit, **up to ~690 m³/s for 4 units** (World Nuclear News). This is a maximum; we have no per-unit intake data.
- **Smart air, 2025 season:** mean 1.29 L/s/m = **43%** of fixed 3 L/s/m and 29% of max; the need exceeds 3 L/s/m in 0.4% of hours.
- **Forecast** (400 runs × 60 jellyfish):
  - 15 km NNW → **44%** chance, window **16–73 h**, bulk arrival 42 h, curtain on at **+13 h**, approach from the **west**.
  - 28 km N → 12%.
  - 28 km WNW → 1%.
  - Stress test: **~3% vs ~23%** of arriving jellyfish reach the mouth before protection (Qandeel vs daylight camera alone). This rounds several seeds (`results.json` → `smart_switching`: 2.7% vs 26.1%; other seeds 2.9–3.7% vs 20.2–23.6%); always show it with "~".
  - Median warning 31–46 h.
- **Dense-body risk** (share held, outflow layer 1 m / 2 m):

  | Swarm mean depth | 1 m layer | 2 m layer |
  |---|---|---|
  | 0.35 m | 94–97% | 100% |
  | 1 m | 63–82% | 86–93% |
  | 2 m | 39–70% | 63–82% |
  | 3 m | 28–64% | 49–74% |

  The thickness of the outflow layer is **not measured**.
- **Gathering:**
  - Boom: **Desmi RO-BOOM 2000 class**, 1.1 m skirt, 0.6 m freeboard, 200 m (4 × 50 m), ~66 m U-opening.
  - Bag: closed-bottom, permeable fabric, mouth 4 × 3 m, ~20 m³ for 10 t.
  - Tow at **0.2 m/s**; the bag keeps 100% (base) or 84–93% (pessimistic); tow force 2.1–3.5 kN.
  - Workboats: **8–10 m, 1–2 t bollard pull, crewed and chartered**. Uncrewed tugs are a later step.
  - Release **3 km at 330°**, ~7 h tow round the outer breakwater; ~63% drift back and are held again.
- **200 t/day bloom:** 8.3 t/h; e.g. 30 t bags + 3 shuttle tugs → 15 boats (3 km release) or 10 boats (5 km). The curtain has no tonnage limit in the model.
- **Blue Blubber** (*Catostylus mosaicus*): domed bell 30–45 cm, **no marginal tentacles, 8 frilly oral arms**, ~1–3 kg; model swim speed 0.02–0.10 m/s.
- **Detector:** YOLO11n, 116 held-out test images: recall 91%, mAP50 87%, precision 80%. These are the **`jellyfish` class** in `detector_metrics.json`; never show the `all` row (it includes an untrained `human` class, recall 45%). Show it on real images or video only.
- **Cost** (lean design, budget estimates, no vendor quotes). Read costs **only from `costs.json` → `lean`**. The top level of `costs.json` (USD 4.7–12.7 M, compressors, steel frames, owned boats) and the `cost` block in `results.json` (uncrewed USVs, compressor) are superseded designs: never show them.
  - Build USD 2.3–6.5 M; run 0.39–0.98 M a year; pilot 0.28–0.72 M.
  - One unit-day of lost output ≈ USD 1.7–2.7 M (assumed USD 50–80 per MWh).
- **Evidence:**
  - UCC trials: mixed. Waves weaken curtains, but this is **not quantified**, so it is not modelled.
  - Salmon farms: 77–80% fewer jellyfish (industry reports).
  - Lo 1991: argues against bubbles alone.
  - Japanese plant intakes: no published data.
  - The curtain is **one layer** of four: forecast, curtain, pockets and boats, existing screens.
- **Credits on screen:** "Contains modified Copernicus Sentinel data 2025"; Copernicus Marine Service; ERA5.

## 6. Physics specification (port exactly from `curtain.py run_hold`)
- **Near the mouth** (time step 2 s):
  - approach flow toward the gap centre at speed V;
  - alongshore tide 0.08·sin(2πt / 12.42 h);
  - curtain outflow sign(dy)·U₀·exp(−dist / (2·depth)), with the bubble line 5 m seaward;
  - swim speed uniform(0.02, swim_max) on a heading that wanders with σ = √(dt/120);
  - mixing σ = √(2·0.05·dt).
- **Passed:** a jellyfish counts as passed once it crosses the bubble line inside the gap.
- **Depth:** below the outflow layer the push fades. The layer thickness is a labelled assumption.
- **Far field:** 20 s steps along a distance-through-water field. Jellyfish and boats go **round the real breakwaters**, never through land.
- **Catch pockets:** they only take jellyfish that the curtain has actually held for at least 30 min.
- **Planner and smart air:** same formulas as `planning.py` and `benefits.py`.

## 7. Experience design (what a judge sees)
- **Art direction:** a real place at golden hour or bright Gulf midday. Photoreal water: depth-based colour and clarity, surf on the breakwater rock armour, caustics on the shallow seabed, wakes, foam boiling over the curtain line. Detailed rock armour, the plant's containment domes and turbine halls at their real positions, haze to the horizon.
- **Equipment:** physically correct 3D models (Blender, from the dimensions above): workboat with tow bitt and A-frame, RO-BOOM sections with floats and skirt, the closed bag, 40 ft blower containers, the pipe on sleepers.
- **Underwater:** volumetric light shafts, a bubble plume made of particles of realistic size, sediment, and Blue Blubber with translucent pulsing bells and 8 frilly oral arms that move. Deep jellyfish visibly pass under the outflow.
- **Cinematic tour** (7 steps, about 3 minutes): alert → forecast → curtain on → underwater → pockets → boats → result. Smooth camera moves, recorded narration (ElevenLabs, from `narration_lines.json`; adapt `reference/make_voice.py`, which as written reads `#voiceLines` from `qandeel_sim.html`, to read the JSON and write audio files the engine loads), large captions readable from 2 m. Attract mode after 2 minutes idle.
- **Free play:** scenario presets (typical 2025 day, worst 5% current, too little air, deep swarm, section failure, rough sea, 200 t bloom), sliders for the physical inputs, switchable sections, a real-2025 replay with Auto air and Season check, live gauges (pressure, air, power, energy), held / passed / pocketed / removed counters, and an event log.
- **Panels:**
  - **Forecast:** hour scrubber over the 400-run atlases, plus the alert numbers.
  - **AI camera:** real detections and metrics.
  - **Equipment:** real dimensions with a person for scale.
  - **Cost and payback.**
  - **Evidence:** what is proven and what is not.
- **UI quality:** a calm, professional control-room style that never hides the 3D scene on a 1366×768 laptop. Touch-friendly, keyboard accessible, and every label sourced.

## 8. Engineering quality
- The physics core is separate from rendering, deterministic with a seed, and has a per-frame time budget (sea time slows instead of the picture stuttering).
- Graphics tiers: High, Medium and Low, chosen automatically from measured FPS, plus a manual switch.
- Every data file is versioned and its source recorded in `SOURCES.md`.
- Keep `CHANGELOG.md` and a short `README.md` covering how to run, how to rebuild data and how to record the voice.

## 9. Acceptance tests (automated; must pass 3 times in a row before any milestone is "done")
| Test | Pass |
|---|---|
| Typical day: q = 3 L/s/m, V = 0.14, depth 0.35 m | ≥ 97% held |
| Worst 5% current: q = 3, V = 0.23 | ≥ 95% held |
| Too little air: q = 1, V = 0.23 | ≤ 20% held (Python `run_hold` defaults, seed 0, n = 500: 15%) |
| Deep swarm, mean depth 2 m | 39–82% held |
| Season check over 2,928 h | 1.29 L/s/m, 43%, 29%, 0.4% |
| Forecast, 15 km NNW | 44%, 16–73 h, 42 h, +13 h, west |
| Labels | boats "crewed, chartered"; air "blowers"; intake "up to ~690 m³/s" |
| Performance on the stand laptop | ≥ 60 FPS High, ≥ 30 FPS Low at 1920×1080; no crash in a 3-hour soak test |
| Offline | runs with Wi-Fi off from a USB copy |

## 10. Milestones (report with screenshots or video and FPS after each)
1. **31 Oct:** hardware benchmark and engine decision; the real site in 3D.
2. **1–2 Nov:** physics core plus all physics tests passing.
3. **3–4 Nov:** curtain, pockets, boom, boats, routes; underwater cutaway.
4. **5 Nov:** tour, narration, panels, 2025 replay.
5. **6–7 Nov:** art pass (models, water, lighting, particles); performance tiers.
6. **8 Nov: feature freeze.** Full checklist on the stand laptop; team rehearsal.
7. **9–10 Nov:** fixes only; record a 4K backup video of the tour; offline build on two USB drives.

Start now: benchmark the laptop, propose the engine with your reasons, and show me the real site in 3D.
