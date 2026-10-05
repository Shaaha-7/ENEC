# Project Qandeel: jellyfish swarm response simulation

Team Qandeel's entry to the ATRC Advanced Technology Pioneers 2026 ENEC challenge.
It covers managing Blue Blubber jellyfish (*Catostylus mosaicus*) swarms
**after** an early-warning alert, at a coastal seawater intake.

Everything here is a **simulation on stated assumptions**. It uses no ENEC data.
The outputs demonstrate the concept; they are not predictions for a real plant.

## Quick start

```bash
python -m venv .venv
.venv\Scripts\activate            # Windows  (source .venv/bin/activate on Mac/Linux)
pip install -r requirements.txt

python -m qandeel.data_fetch       # 1. download real Gulf currents + wind (needs internet)
python -m qandeel.run_all          # 2. all simulations -> qandeel/outputs/fig1..fig5 + results.json
streamlit run qandeel/dashboard.py # 3. visual dashboard (opens in your browser)
python -m pytest qandeel/tests -q  # tests
```

Without step 1 everything still runs, on assumed conditions; the charts and the
dashboard say which forcing they used.

## 1. Real Gulf data

`python -m qandeel.data_fetch` downloads hourly data for the 2025 bloom season
(1 June to 30 September) at the point in `qandeel/site.json` (about 5 km off the
plant) and writes `qandeel/data/gulf_forcing.csv`:

- **Ocean surface currents** from the Open-Meteo Marine API, which serves the
  Copernicus Marine global ocean model (SMOC). No account needed.
- **10 m wind** from the Open-Meteo historical archive (ERA5 reanalysis).

Other dates: `python -m qandeel.data_fetch --start 2024-06-01 --end 2024-09-30`.

With the file present, each Monte Carlo run starts at a random hour of the real
record, so the spread of runs reflects real day-to-day variability.

**Check the current direction once.** The script treats `ocean_current_direction`
as the direction the water flows *toward*. Open the API documentation
(open-meteo.com/en/docs/marine-weather-api) and confirm; if it says "from", add
180 degrees in `fetch_currents`.

## 2. Measure the breakwater gap (Google Earth)

1. Open Google Earth (web or desktop) and search "Barakah Nuclear Energy Plant".
2. Zoom to the seawater intake: the opening in the breakwater on the sea side.
3. Use **Measure distance** (ruler icon) between the two breakwater tips.
4. Put the value in `qandeel/site.json` as `gap_width_m` and set `gap_width_measured` to `true`.
5. If a nautical chart or public report gives the depth at the opening, set `gap_depth_m` the same way.
6. Re-run `python -m qandeel.run_all`. Curtain length, air and power update automatically.

Keep a screenshot of the measurement for the slides.

## 3. Dashboard

`streamlit run qandeel/dashboard.py` opens a page where you type an alert
(distance, bearing, time) and see:

- the chance the swarm reaches the gap and the arrival window;
- the curtain switch-on time, as a clock time;
- a map of the swarm (60 jellyfish per run) moving hour by hour, with the release point;
- the curtain status (OFF, STANDBY, ON) and a timeline with a "now" marker.

Drag the clock slider or press **Play** to animate it for the video.
`http://localhost:8501/?t=30` opens directly at hour 30.

## What each part simulates

| File | What it simulates |
|---|---|
| `qandeel/sim/forcing.py` | Real record (analogue ensemble) or assumed tide and wind |
| `qandeel/sim/planning.py` | Swarm of 60 spread-out, swimming jellyfish per run; arrival window, switch-on, approach side, release point |
| `qandeel/sim/curtain.py` | Individual jellyfish at the gap, curtain on or off, swimming up to 10 or 20 cm/s |
| `qandeel/sim/boom.py` | Jellyfish in the boom pocket: escape under the skirt by tow speed, skirt depth and swim speed |
| `qandeel/sim/sizing.py` | Compressor air and power, boom throughput, jellyfish mass |
| `qandeel/plan_alert.py` | Text version of the plan for one alert |
| `qandeel/dashboard.py` | Visual dashboard |
| `qandeel/run_all.py` | Runs everything, writes `outputs/` |

## Main findings (real 2025 bloom-season data)

Forcing: Copernicus SMOC currents + ERA5 wind, 1 Jun - 30 Sep 2025 (currents average 0.08 m/s, max 0.25 m/s; wind mostly from N/NW).
Direction convention checked: mean current at the Strait of Hormuz points north, into the Gulf, as expected for "flowing toward".

- **Arrival:** a swarm 15 km NNW reaches the gap in 54% of runs (first jellyfish 10-77 h, bulk ~33 h; curtain on at 7 h); 28 km WNW 30%; 28 km N 34%. Real conditions make arrival uncertain, so the planner reports a probability, not just a time.
- **Curtain:** at 3 L/s per metre it holds the swarm up to 0.2 m/s approach current, still 95% if adults swim 20 cm/s; it fails above about 0.3 m/s. Real currents (mean 0.08 m/s) are mostly inside that range.
- **Boom:** jellyfish are neutrally buoyant, so an open U-boom loses them under the skirt far below the 0.35 m/s oil-boom limit. Even at 0.1 m/s a 2 m skirt keeps only 60-80% for 30 minutes. Move the boom with the current, keep holds short, tank-test a closed-bottom retention bag.
- **Release:** with mostly onshore winds, the best point within a day's tow (15 km NE) still sees ~23% drift back within 72 h; the curtain catches them again (about 1.3x handling load). Under 2% return needs 30-40 km, too far for a boom tow.

Note: the seasonal record gives a climatology-like spread. In operation the planner would replay the live 7-16 day forecast instead.

## Key assumptions

- Coast along y = 0, intake gap at the origin; gap width and depth from `site.json`.
- Assumed mode: tide 0.30 m/s along-shore, residual 0.08 m/s east, wind 6 m/s from NW.
- Forecast error: about 20% on currents (and wind in assumed mode).
- Jellyfish swim 2-10 cm/s (field data for >14 cm medusae); 20 cm/s tested for large adults.
- Curtain surface current from Bulson's relation U0 = 1.46 (g q)^(1/3).
- Boom: downflow at the apex = 25% (and 50%) of tow speed; jellyfish keep their depth with 70% of their swimming effort. Both are unknowns for the tank test.

## Why no machine-learning model

A model trained on data generated by these drift equations could only re-learn
the equations. The planner runs the physics directly and gets its uncertainty
from Monte Carlo repetition. Learning models make sense once real ENEC swarm
events exist.
