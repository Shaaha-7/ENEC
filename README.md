# Project Qandeel

**Herd, don't harvest.** A plan, a simulation and a working dashboard for keeping
jellyfish swarms out of coastal seawater intakes without killing them.

Project Qandeel is Team Qandeel's entry to the **ATRC Advanced Technology Pioneers 2026**
competition, ENEC challenge:

> *How might we advance the sustainable management of seasonal jellyfish blooms in coastal environments?*

*Qandeel* (قنديل) is Arabic for jellyfish, and also for lantern.

---

## Start here

| Look at this | What it is |
|---|---|
| **[enechackathon.streamlit.app](https://enechackathon.streamlit.app/?d=15&b=330&t=30)** | **The live dashboard online.** Type an alert and see the plan. No install needed. |
| [`media/Qandeel_explainer.mp4`](media/Qandeel_explainer.mp4) | 1 min 42 s 3D explainer video with narration |
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
3. **Herd and release.** Two **uncrewed boats**, supervised from shore, tow a floating
   **boom** in a U-shape and gently gather the swarm, still in the water. A closed-bottom
   **retention bag** at the end of the U stops them escaping underneath. They tow the
   swarm about 3 km out to sea, away from the cooling-water discharge, and release it alive. More than half drift back; the curtain
   simply holds them again.

An **AI camera** at the opening counts jellyfish and switches the curtain on if they
arrive early. Its records are the future training data for a learning model that
corrects the drift forecast.

---

## What we found (from the simulation, real 2025 Gulf data)

Base case: Copernicus currents plus 1% extra wind drift (0% and 3% are run as bounds; see below).

| Question | Answer |
|---|---|
| Is the prediction worth it? | In a **stress test** of 100 alerts (the "truth" uses physics the planner does not know: currents ×0.7–1.3, wind drift ×0.5–1.7, an unmodelled 0–3 cm/s current), **~3%** of arriving jellyfish reached the gap before the curtain was running, vs **~23%** for a daylight-only camera (mean of three runs; range 2.7–3.7% vs 20–26%). The cost: about 12 more curtain hours per alert than camera-only (~USD 500 of extra energy vs camera-only), still ~80% fewer than running on every alert. Crews get **1–2 days of warning**. |
| Is that real validation? | **No.** It is simulation against simulation. The next step is comparing the drift with real Gulf drifter tracks and ENEC swarm records. |
| Does the bubble curtain hold them? | Water approaches the opening at **0.14 m/s** on average, **0.23 m/s** in the worst 5% of hours (0.20 / 0.36 m/s if wind drift is 3%). The normal setting (3 L/s of air per metre) holds about 0.27 m/s; the design maximum (4.5) about 0.33 m/s. Over a full 82-hour event it holds 100% at 0.2 m/s, but only **78%** if large adults swim 20 cm/s. |
| Can a normal oil-spill boom gather jellyfish? | **No.** Jellyfish float at any depth, so they slip under the skirt: a 2 m skirt keeps only 2 to 55% at 0.2 m/s. A **closed-bottom bag** keeps **84 to 100%** in the model. |
| How fast can the boats clear them? | The boom can only move 0.2 m/s through the water, and near the mouth the water flows *toward* the intake, so tows are slow over the ground (median 7 h for 3 km, up to a day against strong inflow; sometimes blocked). Net removal is about **0.4 t/h** per boom unit, about **1.4 t/h** with three uncrewed tugs swapping bags. The curtain does the bulk of the work; the boats clear what builds up. |
| What happens to a held swarm? | Boats clear only ~0.4–1.4 t/h, so a 300 t bloom (Oman) would take them 9–30 days: they clear what piles up at the mouth, not the whole swarm. The curtain holds the swarm while the sea moves it on: in the 2025 data the flow runs along the coast at ~0.07 m/s (~6 km a day) and turns offshore 22% of the time (onshore spells last a median 14 h). The curtain stays on until the planner's window has passed and the camera has seen no jellyfish at the mouth for 6 h. |
| Where to release them? | About **3 km** north-north-west of the mouth removes the most per hour: ~60% drift back and the curtain holds them again (2.7× handling). Release is kept between west and north-north-east, away from the cooling-water discharge to the east. |
| Don't released jellyfish strand anyway? | Left alone, **55%** of the same swarm strands within 4 days (summer winds blow onshore). Released 3 km out, 29% strand, most of them still within 10 km of the plant, so release points need refining with real data. |
| Why not harvest them? | Blue Blubber is fished for food elsewhere (e.g. Australia). A full bag could go to a licensed processor instead of release; release is the default while there is no UAE market. |
| How much power? | The intake mouth we measured is **967 m** wide, so the curtain needs 2.9 m³/s of air: a compressor of ~**440 kW**, ~31 MWh per long event. Setting the air hour by hour uses **29–46%** of the energy of full power (needs a variable-speed compressor). A shorter curtain further inside the channel would cut this; to compare with ENEC. |
| What does it cost? | Roughly **USD 1.2 to 3.5 million** to build (967 m curtain, compressor, boom, bags, two uncrewed boats, sensors) and **USD 0.18 to 0.45 million a year** to run (operators on call, boat upkeep, diver cleaning, servicing, energy). Estimates, to confirm with suppliers. For scale: Torness (UK) lost an estimated ~£1 million (~USD 1.5 million) per day while jellyfish kept it shut in 2011 (press estimate), so avoiding 0.8–2.3 such outage days repays the capital. |
| Emissions? | ~12.6 t CO2 per event on a gas grid, ~0.4 t on the plant's own low-carbon power. |

### How much the wind assumption matters

The ocean model already includes wind-driven and wave drift; the extra wind drift felt by
jellyfish just below the surface is uncertain. Results at 0%, 1% (base) and 3% extra:

| Extra wind drift | Arrival chance: 15 km NNW / 28 km WNW / 28 km N | Approach at the gap (mean / worst 5%) | Through before the curtain: Qandeel vs camera |
|---|---|---|---|
| 0% | 39% / 4% / 3% | 0.11 / 0.18 m/s | 4.2% vs 18% |
| 1% (base) | 44% / 1% / 12% | 0.14 / 0.23 m/s | 2.7% vs 26% |
| 3% | 40% / 1% / 29% | 0.20 / 0.36 m/s | 0.7% vs 26% |

The curtain is sized for the 1% base case. At 3%, the worst 5% of hours reach 0.36 m/s, above the ~0.33 m/s the design maximum (4.5 L/s per m) holds: about 8% of hours would be flagged to operators.

### Limitations

- The simulation uses real public ocean data but **no ENEC data**; it is not a forecast for the real plant.
- Currents come from one ocean-model grid point (~9 km cells) through a free service that rounds speed to
  ~0.03 m/s (only ~10 distinct speeds in the season). `python -m qandeel.fetch_copernicus` downloads the
  full-precision data with a free Copernicus Marine account.
- The coast is modelled as a straight line through the intake mouth, and the curtain model has no depth: near the bed a bubble curtain draws water
  *toward* it, so deeper or night-time jellyfish could pass underneath. The sea pilot checks this.
- Swim speeds and depths come from Australian studies of the same species; the species at Barakah and the
  weight of 30–45 cm adults (the mass formula was fitted on 2–20 cm animals) need confirming.
- "Arrived" means within 2 km of the opening; within 500 m the 15 km NNW alert drops from 44% to 31%.

**Risks an ENEC engineer will ask about:**

- *Bubbles sucked into the cooling pumps:* the curtain is on the seaward side; bubbles surface in under a minute, in the outer basin; a bubble detector watches the intake line; the curtain moves further out if the pilot shows carry-over.
- *Approvals:* **FANR** for any change at the intake, **EAD** for marine works and moving live animals, Coast Guard and plant security for boats. Kit stays outside safety-related intake structures.
- *Sediment and bubbly water:* diffusers sit 0.5–1 m above the bed; turbidity is watched at the intake; no boats or divers in the bubble zone while it runs (bubbly water gives less buoyancy).
- *Jellyfish crowded in the bag during a long tow:* the bag stays underwater, where jellyfish are nearly weightless, so we expect far less crushing than in a net hauled out of the water, and seawater flows through the fabric. This is reasoning, not a measurement: we found no published study of jellyfish survival in crowded holding. The bag is filled at most halfway, the base-case tow is ~7 h, and if a tow runs past 12 h the bag is opened early at sea (any returners meet the curtain again). Survival 24 h after release is a pilot pass mark; if it is low, a full bag goes to a licensed processor instead.
- *Air trapped under a bell:* coarse bubbles push jellyfish away at the surface rather than through the plume; checked in the sea pilot.
- *Wildlife:* turtles, dolphins and dugongs (escape gap, slow tow, camera stop on sighting); seagrass and protected areas kept clear of the pipe and the release points.

## What is in this repository

```
ENEC/
├── README.md              ← you are here
├── deck/                  the 10-slide proposal deck (PDF)
├── media/                 the explainer video, the dashboard recording and the annotated Barakah site map
├── requirements.txt       Python packages needed
└── qandeel/               the simulation (Python)
    ├── data/              real ocean data (currents + wind, June-Sept 2025)
    ├── sim/               the models (drift, curtain, boom, costs...)
    ├── outputs/           charts and results.json made by run_all
    ├── tests/             automatic checks (25 tests, incl. the reported numbers on real data)
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

## References

Sources checked for the claims in the deck and this README. Press reports are marked as such.

**The species and the region**
- Aravind, A. (2024). *Reproductive Biology of the Jellyfish Outbreak Along the UAE Coastal Waters.* PhD dissertation, UAE University. Surveys Dec 2019–Aug 2022; *Catostylus mosaicus* and *Cassiopea andromeda* were the two dominant species. https://scholarworks.uaeu.ac.ae/all_dissertations/314
- SeaLifeBase, *Catostylus mosaicus* species summary: maximum size ~35 cm; pelagic-neritic, 0–1 m (estimate); bays and estuaries. https://www.sealifebase.se/summary/Catostylus-mosaicus.html
- Swimming: *Jet-paddling jellies: swimming performance in the Rhizostomeae jellyfish Catostylus mosaicus*, Journal of Experimental Biology 221 (2018), jeb191148 (kinematics of small adults, bells 2.3–4.6 cm). https://journals.biologists.com/jeb/article/221/24/jeb191148/20581 . A size-series study reports mean speeds of ~1 cm/s (small juveniles) to ~4.3 cm/s (adults with small bells) (table via ResearchGate; source paper to confirm). Speeds of 30–35 cm adults have not been measured, so the model tests 10 and 20 cm/s.
- Edible jellyfish fishery for *C. mosaicus* in Victoria, Australia: FRDC project report 1999/138. https://www.frdc.com.au/sites/default/files/products/1999-138-DLD.pdf

**Jellyfish at intakes**
- Torness, UK, 2011 (both reactors shut ~1 week; est. ~£1 M/day) and Oskarshamn, Sweden, 2013 (unit 3, *Aurelia aurita*): press reports, e.g. National Geographic (2013) https://www.nationalgeographic.com/environment/article/jellyfish-invasion-shuts-down-nuclear-plant ; Vice (2021) https://vice.com/en/article/jellyfish-keep-attacking-nuclear-power-plants
- Oman: ~300 t of jellyfish damaged intake screens at the Ghubra desalination plant, halving output (Gulf News, press report). https://gulfnews.com/news/uae/general/jellyfish-choke-oman-desalination-plants-1.355525
- Kuwait: proposal for LiDAR, drones and water jets to keep jellyfish from power-plant intakes (PAAET). https://www.paaet.edu.kw/media/mimbolit/preventing-jellyfish-attacks-on-electrical-power-plants-in-kuwait.pdf
- JEROS jellyfish-removal robots (KAIST): ~900 kg/h in 2013 field tests, jellyfish shredded (IEEE Spectrum). https://spectrum.ieee.org/jeros-jellyfish-elimination-robotic-swarm

**Bubble curtains and booms**
- Bulson, P.S. (1968). The theory and design of bubble breakwaters. *Coastal Engineering 1968*, ASCE. https://ascelibrary.com/doi/10.1061/9780872620131.064 ; Bulson, P.S. (1961). Currents produced by an air curtain in deep water. *Dock and Harbour Authority* 42.
- Big bubble curtains for pile-driving noise at offshore wind farms: e.g. ~500 m nozzle hose laid around each pile (Trianel Borkum). https://hydrotechnik-luebeck.de/air-bubbles-protect-north-sea-porpoises
- Oil-boom escape speed: about 0.35 m/s (0.7 knots), largely independent of skirt depth (University of Vermont Sea Grant, use of booms in oil-pollution response). https://uvm.edu/seagrant/sites/default/files/uploads/TIP3UseofBoomsinOilPollutionResponse.pdf

**Ocean data**
- Copernicus Marine Service, Global Ocean Physics Analysis and Forecast (SMOC surface currents), via the Open-Meteo Marine API. https://open-meteo.com/en/docs/marine-weather-api
- ERA5 reanalysis (ECMWF / Copernicus Climate Change Service), via the Open-Meteo archive API. https://open-meteo.com/en/docs/historical-weather-api

**Not found in the literature (our assumptions):** the weight of 30–45 cm Blue Blubbers (the ENEC brief gives 30–45 cm; SeaLifeBase lists ~35 cm), jellyfish survival in a crowded holding bag, the downflow under a towed boom for jellyfish, and bag leakage. These are flagged as assumptions in the deck and are what the sea pilot measures.

---

## Glossary

| Word | Meaning |
|---|---|
| **Intake** | Where the plant sucks in seawater for cooling |
| **Breakwater gap** | The opening in the sea wall that the intake water flows through |
| **Bubble curtain** | Air released from a seabed pipe; the rising bubbles make a surface current that pushes things away |
| **Boom** | A floating barrier with a skirt hanging below, normally used for oil spills |
| **Retention bag** | Our addition: a closed-bottom pocket at the end of the boom so jellyfish cannot escape underneath |
| **Monte Carlo** | Running the same simulation hundreds of times with small random changes, to get probabilities instead of one guess |
| **Hindcast** | Testing the planner on past data where we already know what really happened |

---

## Technical detail

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
| `qandeel/sim/planning.py` | Swarm of 60 jellyfish per run: chance of arrival, arrival window, switch-on time, release point (best net removal per hour) |
| `qandeel/sim/curtain.py` | Individual jellyfish at the opening, curtain on or off |
| `qandeel/sim/boom.py` | Jellyfish inside the boom: who escapes under the skirt, open boom vs bag |
| `qandeel/sim/sensitivity.py` | Which unknown numbers change the results most |
| `qandeel/sim/benefits.py` | Stress test vs camera-only, adaptive air, cost and emissions |
| `qandeel/sim/sizing.py` | Compressor air and power, boom capacity, herding logistics (round trips), jellyfish mass |
| `qandeel/data_fetch.py` | Downloads the ocean data (Open-Meteo) |
| `qandeel/fetch_copernicus.py` | Full-precision currents direct from Copernicus Marine (free account) |
| `qandeel/measure_gap.py` | Opening width from two map coordinates |
| `qandeel/plan_alert.py` | Text plan for one alert |
| `qandeel/dashboard.py` | The dashboard |
| `qandeel/run_all.py` | Runs everything and writes `qandeel/outputs/` |

### Main assumptions (to be checked with ENEC data and the sea pilot)

- The coast is a straight line through the intake mouth. The mouth is 967 m wide, measured on satellite images between the two breakwater tips (`qandeel/site.json`); depth 9.5 m from the public channel description (not surveyed).
- Jellyfish swim 2 to 10 cm/s (field data); 20 cm/s also tested for large adults.
- Bubble curtain surface current from a Bulson-type relation, U0 = 1.46 (g q)^(1/3) (Bulson 1968). The (g q)^(1/3) scaling is well established; the coefficient 1.46 should be confirmed against the original paper and in the pilot.
- Boom: water flows down at the apex at 25% (worst case 50%) of tow speed; the bag fabric lets 20% of it through.
- Forecast error: 15 to 20% on current strength plus a slowly drifting bias.
- Currents and wind come from one model grid point offshore of the site; the curtain model treats the flow as flat (no depth layers).
- Retention bag holds ~10 t; the boom moves at most 0.2 m/s through the water (over the ground this depends on the current and the intake draw) and returns empty at 1.5 m/s.
- Extra wind drift on top of the ocean-model current: 1% of wind speed (0% and 3% as bounds).
- Stranded jellyfish stay stranded; they are not counted as arriving later.
- An optical gap camera only works in daylight (06:00-18:00 local).

### Where the AI and autonomy are

- **Uncrewed boats** tow the boom and shuttle full bags, supervised from shore.
- **AI gap camera:** a detection model counts jellyfish at the opening and triggers the curtain.
- **Autonomous planning loop:** alert in, plan out (switch-on time, airflow, boat side, release point), re-planned on every new forecast.
- **Learning model, later:** the drift planner itself is physics, not machine learning, because a model trained on our own
  simulations could only re-learn our equations. Once the camera and ENEC have real swarm records, a learning model
  can correct the physics forecast with them.

### Deck and video

- **Deck:** `deck/Qandeel_Deck.pdf`, 10 slides (16:9). All its numbers come from `qandeel/outputs/results.json`.
- **Video:** `media/Qandeel_explainer.mp4`, 1 min 42 s: a 3D animation (three.js) driven by the same results,
  with a real dashboard clip and narration from an open text-to-speech model (Kokoro-82M).
- **Site map:** `media/barakah_site_plan.jpg`, satellite image (© Google) annotated by the team: intake mouth
  (967 m, measured between the breakwater tips), curtain line, camera, compressor house, boat base, discharge.

### Use of AI tools

AI assistants helped search and summarise literature, check claims, draft text and write
simulation code, and an open text-to-speech model voices the video. The team reviews every
decision, assumption and figure and must be able to explain each in our own words.

---

*Project Qandeel · Team Qandeel · ATRC Advanced Technology Pioneers 2026, ENEC challenge.*
