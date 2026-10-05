# Qandeel explainer video: narration script

Final cut: `media/Qandeel_explainer.mp4`, about 1 min 42 s (the brief allows 1-2 minutes).
The video has on-screen captions and no voice. Record the narration below over it
(phone voice memo is fine; a team member's own voice is better than synthetic speech),
then combine in any editor (CapCut, Clipchamp, iMovie), or with ffmpeg:

```bash
ffmpeg -i media/Qandeel_explainer.mp4 -i narration.m4a -c:v copy -c:a aac -shortest media/Qandeel_explainer_voice.mp4
```

Aim for a calm pace (~2.3 words per second). Timings are approximate.

| Time | On screen | Narration |
|---|---|---|
| 0:00-0:08 | Title over the 3D coast | "We are Team Qandeel. Qandeel is Arabic for jellyfish, and for lantern." |
| 0:08-0:17 | Swarm drifting toward the intake | "Every summer, swarms of Blue Blubber jellyfish drift toward coastal seawater intakes. They block the screens, crews rake them out by hand, and almost none survive." |
| 0:17-0:27 | Alert and planner panel | "ENEC already detects swarms offshore. Qandeel starts there: it simulates hundreds of possible drifts on real Gulf currents and wind, and tells the crew if the swarm will arrive, and when." |
| 0:27-0:37 | Real dashboard clip | "This is our working dashboard, running on real 2025 data: the chance of arrival, the arrival window, when to switch on, and where to release." |
| 0:37-0:43 | Bubble curtain switches on | "Only when the swarm is due, a bubble curtain across the breakwater opening switches on. Rising air creates a surface current that holds the jellyfish outside." |
| 0:43-0:51 | Underwater: bubble wall | "Nothing is cut, pumped or netted. The water itself does the work." |
| 0:51-1:01 | Boats close the boom | "Then a boom gathers the swarm, still in the water. Our model found that oil-spill booms leak jellyfish, because they don't float back up like oil. So we added a closed-bottom retention bag." |
| 1:01-1:14 | Tow and release | "The boom moves with the current and releases them alive, at a point we choose by simulating where they drift next." |
| 1:14-1:22 | Released, intake running | "The intake keeps running, and the jellyfish go back to the sea." |
| 1:22-1:34 | Result cards | "In a test of one hundred alerts on real Gulf data, we covered every swarm that arrived, with seventy-seven percent less curtain running time. Next, a one-to-twenty tank test to measure it." |
| 1:34-1:42 | Closing title | "Qandeel. Herd, don't harvest." |

## Rebuild the video

```bash
cd video && npm install                      # three.js + Inter font
python -m http.server 8700                   # from the repo root, in another terminal
PW=<path to playwright> node render_frames.js /tmp/frames     # ~1 h on a laptop CPU
PW=<path to playwright> node render_overlay.js http://localhost:8700/video/overlay_dashboard.html overlay.png
./assemble.sh /tmp/frames ../media/Qandeel_explainer.mp4
```

The 3D scene (`scene.html`) reads its numbers from `qandeel/outputs/results.json`,
so re-running the simulation and re-rendering keeps the video consistent with the deck.
The 3D plant and breakwater are stylised and not to scale.
