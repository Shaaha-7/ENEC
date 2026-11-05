# Qandeel simulator: plan to the finals (11–13 Nov 2026)

## Goal
One simulator that is **correct** (matches the Python models and passes tests), **reliable** (offline, no crashes, ≥ 60 FPS on the stand laptop) and **clear** (a judge understands it in 2 minutes). Polish comes after that.

## Set-up on your laptop (all free)
1. Install **Node.js LTS**, **Python 3.11+**, **Google Chrome**, **Git** and **Blender 4.x**. The engine (Unreal Engine 5, Unity 6 or three.js) is chosen on day 1 from the laptop benchmark; install it then (Epic Games Launcher or Unity Hub, both free).
2. Install **Claude Code** and start it in an empty folder, e.g. `C:\qandeel_sim`. Unzip this kit into that folder.
3. Paste the prompt from `PROMPT.md` (everything below its line) as your first message.
4. Optional voice: get your ElevenLabs key, set it as an environment variable (`setx ELEVENLABS_API_KEY your-key` on Windows, then open a new terminal). Never paste it into the chat or a file.
5. Use the **same laptop that goes to the stand** if possible, so every FPS number is real.

## Schedule
| Date | Milestone | Done when |
|---|---|---|
| 31 Oct | Project set up; real site shown in 3D; FPS measured | Screenshot + FPS on the stand laptop |
| 1–2 Nov | Physics ported + automated tests | All acceptance tests pass 3 times |
| 3 Nov | Curtain sections, pockets, boom, boats, routes round the breakwater | Boats reach the release point; counters correct |
| 4 Nov | Underwater cutaway (pipe, plume, Blue Blubber) | Deep jellyfish visibly pass; matches the depth test |
| 5 Nov | Tour + captions + voice; Forecast and AI tabs; 2025 replay + Season check | Tour runs end to end without touching anything |
| 6–7 Nov | Polish: Blender models, water, lighting, low-graphics mode | ≥ 60 FPS High, ≥ 30 FPS Low on the stand laptop |
| **8 Nov** | **Feature freeze**; full test on stand laptop; team rehearsal | Checklist below all ticked |
| 9–10 Nov | Bug fixes only; record a backup video of the full tour; pack offline copy on USB | Video + USB ready |
| 11–13 Nov | Finals | – |

## Stand checklist (8 Nov)
- [ ] Runs offline from USB on the stand laptop (no Wi-Fi)
- [ ] ≥ 60 FPS High / ≥ 30 FPS Low at 1920×1080; 3-hour soak test without a crash
- [ ] All acceptance tests pass 3 times
- [ ] Tour plays start to finish with voice; captions readable from 2 m
- [ ] Every number on screen traceable to `model/qandeel/outputs` or labelled "assumed"
- [ ] No console errors after 30 min running; attract mode starts after 2 min idle
- [ ] Backup: tour video file + the previous web simulator link

## Risks and backups
- **Laptop too slow:** Low graphics + smaller window; backup video.
- **Anything breaks on the day:** play the recorded tour video; previous simulator link as second backup.
- **A judge asks for a number we don't have:** say it is what the sea pilot measures (depth, swarm depth, wave effect).
