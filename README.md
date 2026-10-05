# Project Qandeel: jellyfish swarm response simulation

Team Qandeel's entry to the ATRC Advanced Technology Pioneers 2026 ENEC challenge.
It covers managing Blue Blubber jellyfish (*Catostylus mosaicus*) swarms
**after** an early-warning alert, at a coastal seawater intake.

The simulation runs on **real Gulf ocean data** but uses no ENEC data. It
demonstrates the concept; it is not an operational forecast for a real plant.

## Quick start

```bash
python -m venv .venv
.venv\Scripts\activate            # Windows  (source .venv/bin/activate on Mac/Linux)
pip install -r requirements.txt

python -m qandeel.data_fetch              # 1a. real 2025 bloom-season currents + wind
python -m qandeel.data_fetch --forecast   # 1b. live 7-day forecast (operational mode)
python -m qandeel.run_all                 # 2. all simulations -> qandeel/outputs/fig1..fig7 + results.json
streamlit run qandeel/dashboard.py        # 3. visual dashboard (opens in your browser)
python -m pytest qandeel/tests -q         # 16 tests
```

Without the data files everything still runs, on assumed conditions; the charts
and the dashboard always say which data they used.

## 1. Ocean data: three modes

| Mode | File | How runs differ | Use |
|---|---|---|---|
| **Live forecast** | `qandeel/data/forecast.csv` (`data_fetch --forecast`) | every run starts at the alert hour; spread = forecast error that grows with lead time | operational planning, live demo |
| **Season replay** | `qandeel/data/gulf_forcing.csv` (`data_fetch`) | each run starts at a random hour of June-September 2025 | design studies, the results below |
| **Assumed** | none | idealised tide + wind | fallback |

Sources (free, no account): Copernicus Marine SMOC surface currents via the
Open-Meteo Marine API; ERA5 wind (season) or the Open-Meteo forecast (live).
The current-direction convention was checked: the mean current at the Strait of
Hormuz points north, into the Gulf, as expected for "flowing toward".

The forecast file is not committed (it goes stale within a day): re-download it
before a demo.

## 2. Measure the breakwater gap

1. In Google Earth or Google Maps, find the Barakah seawater intake opening in the breakwater.
2. Right-click each breakwater tip and copy its coordinates.
3. Run: `python -m qandeel.measure_gap --a "lat, lon" --b "lat, lon"`
   (add `--dry-run` to only print the width; `--depth 9.5` to set the depth).
4. Re-run `python -m qandeel.run_all`: curtain length, air and power update.

The depth (9.5 m) comes from the public description of the dredged intake channel
(-5.5 to -9.5 m MSL); the gap width is still the assumed 300 m until measured.
The sensitivity analysis shows gap width is the biggest driver of compressor power.

## 3. Dashboard

`streamlit run qandeel/dashboard.py`: choose the ocean data mode, type an alert
(distance, bearing, time) and see the chance of arrival, the arrival window, the
curtain switch-on clock time, the swarm moving on a map, the curtain status
(OFF / STANDBY / ON), a timeline and the release point. Press **Play** or drag
the clock.

URL presets for demos: `http://localhost:8501/?d=15&b=330&t=30`
(distance km, bearing degrees, hour). A recorded run is in
`media/dashboard_demo.mp4`.

## 4. Deck and video

- `deck/Qandeel_Deck.pdf`: the 10-slide proposal deck (16:9), with slide previews in `deck/preview/`.
  Source `deck/deck.html`; rebuild with `node deck/build.js` while `python -m http.server 8700` runs at the repo root.
  Fill in the team details on slide 1 before submitting.
- `media/Qandeel_explainer.mp4`: the 3D explainer (about 1 min 40 s) with captions; narration script and rebuild steps in `video/VIDEO_SCRIPT.md`.
  The 3D scene (`video/scene.html`, three.js) reads its numbers from `qandeel/outputs/results.json`.

## 5. Tank test

`docs/TANK_TEST.md` is a step-by-step 1:20 tank test for under about AED 300.
`python -m qandeel.tank_scale --scale 20` prints every model-scale value
(Froude scaling): for example 2 L/min of air per metre of pipe, 4.5 cm/s
approach current, a 10 cm boom skirt.

## What each part simulates

| File | What it does |
|---|---|
| `qandeel/sim/forcing.py` | Live forecast, season replay or assumed tide and wind |
| `qandeel/sim/planning.py` | Swarm of 60 spread-out, swimming jellyfish per run; arrival window, switch-on, approach side, release point |
| `qandeel/sim/curtain.py` | Individual jellyfish at the gap, curtain on or off, swimming up to 10 or 20 cm/s |
| `qandeel/sim/boom.py` | Jellyfish in the boom pocket: escape under the skirt; open boom vs closed-bottom retention bag |
| `qandeel/sim/sensitivity.py` | Which unknowns move the results most |
| `qandeel/sim/benefits.py` | Smart-switching hindcast, adaptive airflow, cost and emissions |
| `qandeel/sim/sizing.py` | Compressor air and power, boom throughput, jellyfish mass |
| `qandeel/data_fetch.py` | Downloads season data or the live forecast |
| `qandeel/measure_gap.py` | Gap width from two coordinates, saved to `site.json` |
| `qandeel/tank_scale.py` | Model-scale values for the tank test |
| `qandeel/plan_alert.py` | Text plan for one alert (`--forecast` for live mode) |
| `qandeel/dashboard.py` | Visual dashboard |
| `qandeel/run_all.py` | Runs everything, writes `outputs/` |

## Main findings (2025 bloom-season data)

Currents average 0.08 m/s (max 0.25 m/s); wind is mostly from the N/NW and pushes surface jellyfish toward the coast.

- **Arrival:** a swarm 15 km NNW reaches the gap in 54% of runs (first jellyfish 10-77 h, bulk ~33 h; curtain on at 7 h); 28 km WNW 30%; 28 km N 34%. The planner reports a probability, so the curtain is not run for swarms that will probably miss. With a live forecast the window narrows sharply.
- **Curtain:** at 3 L/s per metre it holds the swarm up to 0.2 m/s approach current, still 95% if adults swim 20 cm/s; it fails above about 0.3 m/s. Real currents sit inside that range. Approach current is the unknown that matters most.
- **Boom:** jellyfish are neutrally buoyant, so an open U-boom loses them under the skirt far below the 0.35 m/s oil-boom limit (at 0.2 m/s a 2 m skirt keeps 0-54%). A **closed-bottom retention bag** at the apex keeps 88-100% at 0.2 m/s across the plausible range in the model. Bag leakage and apex downflow are the unknowns to measure.
- **Capacity:** with the bag at 0.2 m/s, one boom pair gathers about 2,200-21,600 jellyfish an hour at 0.1-1 per m³ (4-43 t/h).
- **Release:** with onshore winds, the best point within a day's tow (15 km NE) still sees ~23% drift back within 72 h; the curtain catches them again (about 1.3x handling load). Under 2% return needs 30-40 km.
- **Compressor:** ~135 kW for a 300 m curtain at 9.5 m depth; ~11 MWh per long event. Gap width drives it (68-271 kW for 150-600 m).
- **Smart switching (hindcast of 100 alerts in the real 2025 season):** the planner covered all 39 swarms that truly arrived and kept the curtain off for 45 of the 61 that did not: 77% fewer curtain hours than running on every alert.
- **Adaptive airflow:** setting air hour by hour from real currents and wind uses 46% of the energy of a fixed 4.5 L/s per m. About 8% of hours (strong onshore wind) would need more than 4.5; those hours are flagged for the operator.
- **Cost (indicative, to confirm with suppliers):** USD 0.4-1.3 million capital for a 300 m gap; about USD 1,100 energy per event. One avoided 12-hour unit outage (about USD 0.5 million, EPRI figure) pays back roughly a third to all of it.
- **Emissions:** about 4.4 t CO2 per event on a gas-heavy grid; about 0.1 t if powered by the plant's own low-carbon electricity.
- **Marine life:** jellyfish raked off screens rarely survive; Qandeel keeps them in the water and releases them alive. No mesh (no entanglement), no chemicals, turtle escape gap, bubble noise only during events.

## Key assumptions

- Coast along y = 0, intake gap at the origin; gap width and depth from `site.json`.
- Forecast error: 15-20% on current strength plus a drifting bias.
- Jellyfish swim 2-10 cm/s (field data for >14 cm medusae); 20 cm/s tested for large adults.
- Curtain surface current from Bulson's relation U0 = 1.46 (g q)^(1/3).
- Boom: apex downflow = 25% (pessimistic 50%) of tow speed; jellyfish keep their depth with 70% of their swimming effort; bag fabric passes 20% of the downflow, floor at 3 m. All to be measured in the tank test.

## Why no machine-learning model

A model trained on data generated by these drift equations could only re-learn
the equations. The planner runs the physics directly and gets its uncertainty
from Monte Carlo repetition. Learning models make sense once real ENEC swarm
events exist.
