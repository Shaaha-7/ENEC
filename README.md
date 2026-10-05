# Project Qandeel

**Herd, don't harvest.** A plan, a simulation and a working dashboard for keeping
jellyfish swarms out of coastal seawater intakes without killing them.

Project Qandeel is Team Qandeel's entry to the **ATRC Advanced Technology Pioneers 2026**
competition, ENEC challenge:

> *How might we advance the sustainable management of seasonal jellyfish blooms in coastal environments?*

*Qandeel* (قنديل) is Arabic for jellyfish, and also for lantern.

---

## Start here (5 minutes)

| Look at this | What it is |
|---|---|
| **[enechackathon.streamlit.app](https://enechackathon.streamlit.app/?d=15&b=330&t=30)** | **The live dashboard online.** Type an alert and see the plan. No install needed. |
| [`media/Qandeel_explainer.mp4`](media/Qandeel_explainer.mp4) | 1 min 42 s 3D video with narration. **Watch this first.** |
| [`deck/Qandeel_Deck.pdf`](deck/Qandeel_Deck.pdf) | The 10-slide proposal deck we submit |
| [`media/dashboard_demo.mp4`](media/dashboard_demo.mp4) | Short recording of the dashboard working |
| [`qandeel/outputs/`](qandeel/outputs/) | The result charts (fig1 to fig7) |

---

## The idea in plain English

**The problem.** Every summer, swarms of **Blue Blubber jellyfish** (*Catostylus mosaicus*)
drift into the seawater intakes that cool coastal plants. They block the screens, the
plant may have to cut power, and almost every jellyfish raked off the screens dies.
ENEC already has an early-warning system that spots swarms offshore.
**Our question is: what do you do after the alert?**

**Our answer, in three steps:**

1. **Predict.** From the alert (where the swarm is), Qandeel simulates hundreds of
   possible drifts using real ocean currents and wind. It says: *will they arrive,
   when, and from which side?* So the defences only run when they are needed.
2. **Hold.** A **bubble curtain** (a pipe on the seabed that releases air) runs across
   the intake opening. The rising bubbles push water outward at the surface, and that
   water pushes the jellyfish away. Nothing is cut, pumped or netted.
3. **Herd and release.** Two boats tow a floating **boom** in a U-shape and gently
   gather the swarm, still in the water. A closed-bottom **retention bag** at the end
   of the U stops them escaping underneath. The boats move them away and release them
   alive, at a spot the simulation picks so they do not drift straight back.

---

## What we found (from the simulation, real 2025 Gulf data)

| Question | Answer |
|---|---|
| Does the prediction save effort? | Tested on 100 alerts: it covered **all 39 swarms that really arrived**, and kept the curtain off for 45 of the 61 that did not. **77% fewer curtain hours** than switching on for every alert. |
| Does the bubble curtain hold them? | Yes, up to about **0.2 m/s** of current at 3 L/s of air per metre (95%+ even if large adults swim 20 cm/s). Real currents there are mostly below that (average 0.08 m/s). |
| Can a normal oil-spill boom gather jellyfish? | **No.** Jellyfish float at any depth, so they slip under the skirt: a 2 m skirt keeps only 0 to 55% at 0.2 m/s. That is why we added the **closed-bottom bag**, which keeps **84 to 100%**. |
| How many can one boom pair gather? | About **2,200 to 21,600 jellyfish an hour** (4 to 43 tonnes). |
| Where to release them? | About 15 km north-east is the best within a day's tow; ~23% still drift back, and the curtain stops them again. |
| How much power? | Compressor ~**135 kW** for a 300 m opening. Changing the air hour by hour to match the current uses **46%** of the energy of running at full power. |
| What does it cost? | Roughly **USD 0.4 to 1.3 million** to build (estimate, to confirm with suppliers) and ~USD 1,100 of electricity per event. One avoided 12-hour plant outage (~USD 0.5 million) pays back a third to all of it. |
| Emissions? | ~4.4 t CO2 per event on a gas grid, ~0.1 t on the plant's own low-carbon power. |

**Be honest about this when presenting:** the simulation uses real public ocean data but
**no ENEC data**. It shows the concept works on paper. It is not a forecast for the real
plant. The next step is a 1:20 tank test (see below).

---

## What is in this repository

```
ENEC/
├── README.md              ← you are here
├── deck/                  the 10-slide deck (PDF, source HTML, slide images)
├── media/                 the explainer video and the dashboard recording
├── video/                 how the 3D video and the voice were made
├── docs/TANK_TEST.md      step-by-step plan for a cheap 1:20 tank test
├── requirements.txt       Python packages needed
└── qandeel/               the simulation (Python)
    ├── data/              real ocean data (currents + wind, June-Sept 2025)
    ├── sim/               the models (drift, curtain, boom, costs...)
    ├── outputs/           charts and results.json made by run_all
    ├── tests/             automatic checks (16 tests)
    ├── dashboard.py       the visual dashboard
    ├── run_all.py         runs every simulation
    └── site.json          size of the intake opening (width, depth)
```

---

## How to run it on your computer

You need **Python 3.10 or newer** ([python.org](https://www.python.org/downloads/)) and
**Git** (or click *Code → Download ZIP* on GitHub instead of `git clone`).

```bash
git clone https://github.com/Shaaha-7/ENEC.git
cd ENEC

python -m venv .venv
.venv\Scripts\activate          # Windows
# source .venv/bin/activate     # Mac / Linux

pip install -r requirements.txt
```

Then:

| To do this | Type |
|---|---|
| Open the dashboard in your browser | `streamlit run qandeel/dashboard.py` |
| Re-run every simulation and remake the charts (a few minutes) | `python -m qandeel.run_all` |
| Get a text plan for one alert (swarm 15 km away, bearing 330°) | `python -m qandeel.plan_alert --distance 15 --bearing 330` |
| Check that everything works | `python -m pytest qandeel/tests -q` |

### Using the dashboard

1. Pick the ocean data: **season** (real 2025 data, used for our results),
   **forecast** (live, see below) or **assumed** (simple made-up conditions).
2. Type an alert: how far away the swarm is (km), its direction (degrees) and the time.
3. Read the answer: chance of arrival, arrival window, when to switch the curtain on,
   and where to release. Press **Play** to watch the swarm move.

Demo shortcut: `http://localhost:8501/?d=15&b=330&t=30` (15 km away, bearing 330°, hour 30).

### Put the dashboard online (Streamlit Community Cloud, free)

The repo must be **public** (or your GitHub account must give Streamlit access).

1. Go to [share.streamlit.io](https://share.streamlit.io) and sign in with GitHub.
2. Click **Create app** → **Deploy a public app from GitHub**.
3. Repository `Shaaha-7/ENEC`, branch `main`, main file path `qandeel/dashboard.py`.
4. Optional: set the app URL, e.g. `qandeel`. Under **Advanced settings** choose Python 3.11.
5. Click **Deploy**. The first start takes a few minutes; after that every push to `main` updates the app.

The online app downloads a fresh 7-day forecast by itself (at most every 6 hours), so
**Live forecast** mode works without any setup. Demo link example:
`https://<your-app>.streamlit.app/?d=15&b=330&t=30`.

### Live forecast (optional, for a live demo)

`python -m qandeel.data_fetch --forecast` downloads the next 7 days of currents and wind
(free, no account). Do it on the day of the demo; it goes out of date quickly.
`python -m qandeel.data_fetch` re-downloads the 2025 season data.

---

## Still to do (team tasks)

- [ ] **Slide 1 of the deck:** add every member's name, university and role
  (edit `deck/deck.html`, then rebuild, see `deck/` below).
- [ ] **Measure the intake opening.** In Google Earth, right-click the two breakwater tips
  at the Barakah intake opening and copy their coordinates, then run
  `python -m qandeel.measure_gap --a "24.12345, 52.12345" --b "24.12345, 52.12345"`
  with **your real numbers**, and then `python -m qandeel.run_all`.
  Right now the width is an assumed 300 m; it is the number that changes the compressor size most.
- [ ] **Optional:** record the narration in a team member's own voice
  (lines and one-line command in `video/VIDEO_SCRIPT.md`).
- [ ] Everyone: be able to explain the three steps and the honest limits above.

---

## Words you will hear

| Word | Meaning |
|---|---|
| **Intake** | Where the plant sucks in seawater for cooling |
| **Breakwater gap** | The opening in the sea wall that the intake water flows through |
| **Bubble curtain** | Air released from a seabed pipe; the rising bubbles make a surface current that pushes things away |
| **Boom** | A floating barrier with a skirt hanging below, normally used for oil spills |
| **Retention bag** | Our addition: a closed-bottom pocket at the end of the boom so jellyfish cannot escape underneath |
| **Monte Carlo** | Running the same simulation hundreds of times with small random changes, to get probabilities instead of one guess |
| **Hindcast** | Testing the planner on past data where we already know what really happened |
| **Froude scaling** | The rule for shrinking a sea test into a tank so the water behaves the same way |

---

## More detail (for whoever wants to go deeper)

### Ocean data

| Mode | File | Use |
|---|---|---|
| Season replay | `qandeel/data/gulf_forcing.csv` | Real hourly data, June to September 2025 (2,928 hours). Each simulation run starts at a random hour. Used for all results above. |
| Live forecast | `qandeel/data/forecast.csv` (not saved in the repo) | Every run starts at the alert hour; the spread grows with lead time like real forecast error. |
| Assumed | none | Simple tide and wind, used if no data file is present. |

Sources: Copernicus Marine surface currents (through the Open-Meteo Marine API) and
ERA5 / Open-Meteo wind. Free and public.

### What each Python file does

| File | What it does |
|---|---|
| `qandeel/sim/forcing.py` | Loads the ocean data (forecast, season or assumed) |
| `qandeel/sim/planning.py` | Swarm of 60 jellyfish per run: chance of arrival, arrival window, switch-on time, release point |
| `qandeel/sim/curtain.py` | Individual jellyfish at the opening, curtain on or off |
| `qandeel/sim/boom.py` | Jellyfish inside the boom: who escapes under the skirt, open boom vs bag |
| `qandeel/sim/sensitivity.py` | Which unknown numbers change the results most |
| `qandeel/sim/benefits.py` | Smart-switching test, adaptive air, cost and emissions |
| `qandeel/sim/sizing.py` | Compressor air and power, boom capacity, jellyfish mass |
| `qandeel/data_fetch.py` | Downloads the ocean data |
| `qandeel/measure_gap.py` | Opening width from two map coordinates |
| `qandeel/tank_scale.py` | Tank-test sizes (`--scale 20`) |
| `qandeel/plan_alert.py` | Text plan for one alert |
| `qandeel/dashboard.py` | The dashboard |
| `qandeel/run_all.py` | Runs everything and writes `qandeel/outputs/` |

### Main assumptions (all to be checked in the tank test)

- The coast is a straight line with the intake opening in the middle; opening 300 m wide (assumed) and 9.5 m deep (public channel depth).
- Jellyfish swim 2 to 10 cm/s (field data); 20 cm/s also tested for large adults.
- Bubble curtain surface current from Bulson's formula, U0 = 1.46 (g q)^(1/3).
- Boom: water flows down at the apex at 25% (worst case 50%) of tow speed; the bag fabric lets 20% of it through.
- Forecast error: 15 to 20% on current strength plus a slowly drifting bias.

### Why there is no AI/machine-learning model

A model trained on our own simulated data could only re-learn our equations. The planner
runs the physics directly and gets its uncertainty from repeating the run hundreds of
times. Machine learning makes sense later, once real swarm records exist.

### Deck, video and tank test

- **Deck:** edit `deck/deck.html`; rebuild the PDF with `node deck/build.js` while
  `python -m http.server 8700` runs in the repo folder (needs Node.js and Playwright).
- **Video:** the 3D scene is `video/scene.html` (three.js) and reads its numbers from
  `qandeel/outputs/results.json`. The voice is an open text-to-speech model (Kokoro-82M),
  made by `video/narrate.py`. Full steps in `video/VIDEO_SCRIPT.md`.
- **Tank test:** `docs/TANK_TEST.md`, about AED 300 of materials.

### Use of AI tools

AI assistants helped search and summarise literature, check claims, draft text and write
simulation code, and an open text-to-speech model voices the video. The team reviews every
decision, assumption and figure and must be able to explain each in our own words.

---

*Project Qandeel · Team Qandeel · ATRC Advanced Technology Pioneers 2026, ENEC challenge.*
