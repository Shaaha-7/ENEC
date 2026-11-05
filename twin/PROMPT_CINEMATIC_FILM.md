# Prompt: Qandeel cinematic film (paste everything below the line)

---

# Qandeel: 90-Second Cinematic Film

## 1. Mission
Produce a **90-second cinematic film** that shows how Qandeel protects the Barakah Nuclear Energy Plant seawater intake (UAE) from jellyfish swarms. It opens the team's presentation and loops at the stand during the ATRC Advanced Technology Pioneers 2026 finals (ENEC track, 11–13 November 2026), judged by ENEC engineers.

Standard: **documentary / premium engineering visualisation**. Photoreal sea, light and equipment, calm and confident pacing, every claim on screen true.

**Tools (free):** Unreal Engine 5 (Lumen, Water plugin, Niagara, Movie Render Queue) if the laptop has a dedicated GPU; otherwise **Blender 4.x (Cycles/EEVEE)**. DaVinci Resolve (free) for edit, grade, captions and sound. Use the asset library from `PROMPT_3D_MODELS.md` (glTF) and the real site data in `data/` (Sentinel-2 image, mask, heights, `meta.json` frame). Narration via ElevenLabs with the key in an environment variable only. Royalty-free music with a licence that allows public display; keep the licence file.

## 2. Non-negotiable rules
1. **Every number and claim on screen comes from the verified facts below.** No invented results, no fake AI boxes, no brand logos on equipment. Assumptions are captioned "assumed" or not shown.
2. **Show the honest version.** The curtain is one layer of four: forecast, curtain, pockets and boats, existing screens. Deep jellyfish can slip under it, and the film shows that briefly. The pilot measures what is not yet known.
3. **The real site is the stage.** Barakah geometry from the Sentinel-2 data: the 967 m intake mouth at the end of the western mole, the long curved outer breakwater, the intake channel to the pumps, the warm-water discharge to the east, the 4 units on the coast. Credit "Contains modified Copernicus Sentinel data 2025".
4. **Logos:** only the official files the team provides, used only on the end card.

## 3. Verified facts (the only numbers allowed)
- 967 m mouth; curtain of 10 × 97 m HDPE pipes (200 mm, 2 mm holes) on concrete sleepers, 0.75 m above the seabed.
- Rising air drives a surface current of ~0.45 m/s out to sea; it holds approach currents up to ~0.27 m/s. In 2025 the mean approach was 0.14 m/s and the worst 5% was 0.23 m/s.
- Air: 174 m³/min at ~1.1 bar(g) from 3 oil-free screw blowers (2 duty + 1 standby) in containers on the western tip.
- Forecast: 400 simulated drifts on real 2025 Copernicus currents and ERA5 wind. A swarm 15 km NNW has a 44% chance of arrival within 16–73 h; the curtain switches on at +13 h.
- Stress test: ~3% of arriving jellyfish reach the mouth before protection, vs ~23% with a daylight camera alone; 1–2 days of warning.
- Held jellyfish gather in a dense patch in front of the middle of the curtain; a workboat sweeps it with a boom (~66 m opening) and closes the bag. Tip pockets are a backup only.
- Crewed, chartered 8–10 m workboats tow a closed bag (~10 t) at 0.2 m/s, about 7 h round the outer breakwater to a release point 3 km NNW, where the jellyfish are released alive.
- Smart air uses 43% of the energy of a fixed airflow (2025 data).
- Lean build USD 2.3–6.5 M; one-section sea pilot USD 0.28–0.72 M.
- Blue Blubber: domed 30–45 cm bell, 8 frilly oral arms, no marginal tentacles.

## 4. Storyboard (90 s, 16:9, 4K/24 fps)
| # | Time | Shot | Narration (draft; tighten to fit) | On-screen text |
|---|---|---|---|---|
| 1 | 0–7 s | Dawn. Slow descending aerial over the Gulf to the Barakah coast; real breakwaters and domes resolve through haze. | "Barakah's four reactors depend on the sea." | Barakah, UAE |
| 2 | 7–14 s | Low drone over open water: a pale-blue swarm of Blue Blubber just below the surface, bells pulsing. | "Swarms of jellyfish can block its seawater intake." | – |
| 3 | 14–24 s | Map-to-3D transition: 400 faint drift paths fan out over the real site; a probability glow gathers at the mouth. | "Qandeel starts with a forecast: will the swarm arrive, and when?" | 44% · 16–73 h · curtain on +13 h |
| 4 | 24–32 s | Blower containers on the western tip; a valve opens, pressure needle rises, a soft hum. | "Ahead of the swarm, blowers send air to the seabed." | 174 m³/min · ~1.1 bar(g) |
| 5 | 32–44 s | Underwater hero shot: the pipe on its sleepers; bubbles stream from 2 mm holes; light shafts; the plume rises and turns into an outward surface current. | "A curtain of rising bubbles pushes the surface water out to sea." | ~0.45 m/s outward |
| 6 | 44–54 s | Jellyfish meet the plume and are pushed back; one deeper jellyfish drifts under (subtle red tint) and the film does not hide it. | "Jellyfish near the surface are held back. Deeper ones can slip under, which is why we measure first." | – |
| 7 | 54–62 s | Wide aerial: the held swarm packed in front of the foaming curtain line; a workboat sweeps it with a yellow boom and closes the bag. | "A boat sweeps the held swarm into a closed bag." | ~66 m boom opening |
| 8 | 62–74 s | A workboat closes the bag and tows it at walking-slow speed round the outer breakwater; wake; release of live jellyfish in open water. | "Boats tow them away in a closed bag and release them alive." | 0.2 m/s · release 3 km NNW |
| 9 | 74–84 s | Clean infographic over a slow aerial: forecast + curtain + boats + screens as four layers. | "Four layers, working together." | ~3% vs ~23% · 43% energy · USD 2.3–6.5 M build |
| 10 | 84–90 s | End card over the sea at golden hour. | "Qandeel. Herd, don't harvest." | Team Qandeel · logos · credits |

## 5. Craft requirements
- **Camera:** physically plausible drone and diver moves only; no impossible whips. 24 fps, motion blur on, 180° shutter.
- **Water:** depth-correct colour (Gulf turquoise shallows, deeper channel), real-scale waves (Hs 0.4–0.8 m), foam over the curtain line, wakes, surf on the armour.
- **Underwater:** realistic bubble sizes rising at about 0.25–0.35 m/s, plume widening upward, suspended particles, caustics on sand, gentle light shafts.
- **Jellyfish:** anatomically correct (section 3), pulsing about 1 Hz, translucent subsurface material, natural swarm spacing.
- **Grade:** natural, slightly warm; no neon sci-fi look.
- **Typography:** clean sans-serif, readable on a 55" screen from 3 m. All numbers in one consistent style.
- **Sound:** narration (ElevenLabs, calm authoritative English voice) at about −16 LUFS; subtle sea, bubble and engine sound effects; music under the narration, never over it.

## 6. Deliverables
- `qandeel_film_4k.mp4` (H.264, 3840×2160, 24 fps, about 40 Mbps) and `qandeel_film_1080.mp4`.
- `qandeel_film_loop.mp4`: a silent version with burnt-in captions for the stand loop.
- `qandeel_film_9x16.mp4`: a 30 s vertical cut for social media.
- Project files, render settings, the music licence, and `FILM_SOURCES.md` listing every number with its source.

## 7. Process
1. **Animatic first:** a rough 90 s grey-box pass with temporary narration. Show it to me before any final rendering.
2. Look-dev frames for shots 1, 5 and 8. Approve before the full render.
3. Final render, edit and grade.
4. QA checklist:
   - [ ] every number matches section 3;
   - [ ] the credits line is present;
   - [ ] no brand logos;
   - [ ] runs smoothly on the stand screen;
   - [ ] captions readable from 3 m.
5. **Lock by 8 November.**
