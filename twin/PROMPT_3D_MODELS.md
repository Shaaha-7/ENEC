# Prompt: Qandeel 3D equipment models (paste everything below the line)

---

# Qandeel: Engineering-Grade 3D Asset Library

## 1. Mission
Create the **3D asset library** for the Qandeel digital twin and cinematic film. Qandeel is a jellyfish-swarm defence for the Barakah Nuclear Energy Plant seawater intake (UAE), presented at the ATRC Advanced Technology Pioneers 2026 finals (ENEC track, 11–13 November 2026), with ENEC engineers judging.

Every asset must be **dimensionally correct, physically plausible and beautiful**: the standard of a professional marine-engineering visualisation studio. An engineer must be able to hold a tape measure to any model and agree with it.

Use **Blender 4.x (free)** driven through the **Blender MCP** server if available, otherwise Blender's Python API (`bpy`) scripts that you write, run and keep in the repo, so every asset can be regenerated. Free textures only (Poly Haven, ambientCG, CC0), or procedural.

## 2. Non-negotiable rules
1. **Units: metres, 1 Blender unit = 1 m.** Y-up for glTF export, origin at a documented point (waterline centre for floating objects, seabed contact for seabed objects).
2. **Dimensions come only from the spec below** or from a public source you cite in `ASSETS.md`. Where the project does not specify a dimension, choose a realistic value, mark it **"design assumption"** in `ASSETS.md`, and keep it conservative.
3. **No brand logos or trademarks** on any model (no Desmi, Atlas Copco, Aerzen, ENEC marks). Use "class" equipment: generic shapes with the right dimensions.
4. Every asset is **regenerable from a script** in `assets/src/*.py`, with a single command to rebuild all.
5. Show a **turntable render plus a scale render** (next to a 1.8 m human figure and a 1 m grid) for every asset before calling it done.

## 3. Deliverables per asset
- `assets/<name>/<name>.glb` (glTF 2.0, embedded PBR textures, KHR_materials where useful), **LOD0 / LOD1 / LOD2** (about 100% / 35% / 10% of the triangles), plus a collision proxy where needed.
- `assets/<name>/<name>.blend` (source) and the generating script.
- `assets/<name>/turntable.mp4` (5 s, 1080p) and `scale.png`.
- An entry in `ASSETS.md`: dimensions, triangle counts per LOD, texture sizes, sources and assumptions.
- **Budgets:**
  - Hero assets (workboat, jellyfish) ≤ 60k triangles at LOD0; others ≤ 25k.
  - Textures 2K, 4K only for hero close-ups.
  - Everything must run inside a real-time engine at 60 FPS alongside the site.

## 4. Asset specifications (from the Qandeel design files)
1. **Workboat (hero).** Crewed, chartered aluminium workboat.
   - **LOA 9.0 m** (project: 8–10 m), **bollard pull 1–2 t**.
   - Design assumptions: beam about 3.2 m; small wheelhouse forward; open aft working deck; **tow bitt and a small A-frame** aft; fendering; navigation lights; railings; liferaft canister.
   - Hull with correct sheer and chine, waterline at the right draft.
   - Livery: white hull with a high-visibility orange band, no logos.
   - Animations: idle bob, and a stern towline attachment point (empty node `tow_point`).
2. **Ocean boom section, RO-BOOM 2000 class.**
   - 50 m section (the system uses 4 × 50 m = 200 m).
   - **Float freeboard 0.6 m, skirt 1.1 m below the waterline** (total 1.7 m); bottom ballast chain; end connectors.
   - Inflated cylindrical float chambers, flexible PVC/PU fabric material.
   - Build as a deformable spline-driven asset: a **U shape with a ~66 m opening**, and a moored **catch-pocket** shape.
3. **Closed-bottom catch bag.**
   - Tapered sock, **mouth 4 m × 3 m, depth 3 m, length 3.3 m, ~20 m³**, permeable mesh fabric, reinforced rim and lifting points.
   - Two states: empty, and half full (bulging, with translucent jellyfish visible inside).
4. **Blower container.**
   - 40 ft ISO container (standard external 12.19 × 2.44 × 2.59 m), weatherproof louvres, service doors, HVAC unit, cable entry.
   - Interior: one generic **oil-free screw blower package** (0.3–1.5 bar(g), ~87 m³/min class), with an outlet pipe and silencer.
   - Three units side by side on the western breakwater tip (2 duty + 1 standby), plus a small transformer kiosk (11 kV feed).
5. **Bubble-curtain section (seabed hero).**
   - **HDPE PE100 OD200 SDR17 pipe, 96.7 m long**, matt black.
   - **2 mm holes about 8.5 cm apart** along the top: use a normal or detail map for the holes, and real geometry only for the close-up LOD.
   - **Pipe axis 0.75 m above the seabed**, carried on **precast concrete sleepers**. Sleeper size and spacing are **design assumptions**: total submerged ballast is **~4.7 t per section**, so size them to match.
   - Pipe clamps, a flanged end, and a **DN150 feed-line** connection.
6. **Blue Blubber jellyfish (*Catostylus mosaicus*) (hero).**
   - **Bell diameter 30–45 cm** (build at 0.38 m, scalable), smoothly **domed**.
   - **No marginal tentacles. Eight thick, frilly oral arms** that end in club-like tips.
   - Colour: translucent blue-white to cream with a subtle radial pattern.
   - Rig: bell **pulse cycle** (shape keys, about 1 Hz) and secondary oral-arm motion.
   - Two material variants: translucent subsurface (close-up film) and real-time (alpha plus Fresnel rim glow).
   - Check against public reference photos and cite them.
7. **Breakwater armour.**
   - Barakah's breakwaters use concrete armour units; public sources mention **Core-Loc** units on the Barakah works. Verify and cite this.
   - Model a Core-Loc-style armour unit (if confirmed, otherwise a generic quarry rock set) with weathered concrete material.
   - Provide a scatter-ready instance set and a **breakwater cross-section kit**: core, filter layer, armour layer and crest.
8. **Plant massing (illustrative, far-field).**
   - Four APR-1400-class units: containment buildings with domes, turbine halls, auxiliary buildings.
   - Use public dimensions where you can cite them; otherwise mark everything **"illustrative massing"**.
   - Simple PBR, built for distance views only.
9. **Release-point marker and navigation aids.** Generic lit marker buoy for the release point (3 km at 330°) and a mooring buoy for the pocket anchors. Mark these as **design assumptions**; they are not in the project design.
10. **Human scale figure.** A neutral 1.8 m figure in PPE (hard hat, life vest) for scale shots.

## 5. Quality checklist (every asset)
- [ ] Dimensions verified in Blender with the measure tool; screenshot attached.
- [ ] Clean topology, applied transforms, correct normals, no n-gons in deforming parts.
- [ ] PBR: realistic roughness and metalness; sea-weathering on marine assets (salt, algae line at the waterline).
- [ ] glTF validates with the Khronos glTF Validator: 0 errors.
- [ ] Loads in a three.js / Unreal / Unity test scene at the correct scale with no warnings.
- [ ] `ASSETS.md` entry complete: sources, assumptions, triangle counts.

## 6. Order of work
1. Blue Blubber.
2. Curtain section with sleepers.
3. Workboat.
4. Boom and bag.
5. Blower containers.
6. Armour kit.
7. Plant massing.
8. Buoys and figure.

After each asset, show the turntable and scale renders and wait for my OK before the next.
