# Qandeel explainer video: narration

Final video: `media/Qandeel_explainer.mp4`, 1 min 42 s, 1080p, captions plus narration.

The narration in the file is generated with an open neural text-to-speech voice
(Kokoro-82M, voice `af_heart`, Apache-2.0) by `video/narrate.py`, over a quiet
synthetic sea ambience. This is declared in the deck's AI-use statement. A team
member recording the same lines in their own voice is even better for judges:
record over the video and replace the audio track (see the end of this file).

| Time | On screen | Narration (as spoken) |
|---|---|---|
| 0:00 | Title over the 3D coast | "We are Team Qandeel. Qandeel is Arabic for jellyfish, and for lantern." |
| 0:08 | Swarm drifting toward the intake | "Every summer, Blue Blubber jellyfish swarms drift toward coastal seawater intakes. They block the screens, and almost none survive." |
| 0:17 | Alert and planner panel | "ENEC already detects swarms offshore. From that alert, Qandeel simulates hundreds of drifts on real Gulf data, to predict if, and when, they arrive." |
| 0:27 | Real dashboard clip | "This is our working dashboard, running on real 2025 data: the chance of arrival, the arrival window, when to switch on, and where to release." |
| 0:37 | Bubble curtain switches on | "When the swarm is due, a bubble curtain switches on, and holds the jellyfish outside the intake." |
| 0:43 | Underwater: bubble wall | "Nothing is cut, pumped or netted. Rising air moves the water, and the water moves the jellyfish." |
| 0:51 | Uncrewed boats close the boom | "Then two uncrewed boats close a boom around the swarm. Our model found that oil-spill booms leak jellyfish, so we added a closed-bottom bag." |
| 1:01 | Tow and release | "They tow the swarm about three kilometres and release it alive. More than half drift back, and the curtain simply holds them again." |
| 1:14 | Released, intake running | "The intake keeps running, and the jellyfish go back to the sea." |
| 1:22 | Result cards | "In a stress test on real Gulf data, a camera alone let about a quarter through. Qandeel let about three percent. Next, a sea pilot with ENEC." |
| 1:34 | Closing title | "Qandeel. Herd, don't harvest." |

In `narrate.py` some words are spelled phonetically for the synthesiser
("Kandeel", "Ee-neck", "twenty twenty-five").

## Record your own voice instead

Read each line starting at its time (a phone voice memo is fine), then:

```bash
ffmpeg -i media/Qandeel_explainer.mp4 -i my_voice.m4a -map 0:v -map 1:a -c:v copy -c:a aac -shortest media/Qandeel_explainer_team_voice.mp4
```

## Rebuild the video from source

```bash
cd video && npm install                      # three.js + Inter font
python -m http.server 8700                   # from the repo root, in another terminal
PW=<path to playwright> node render_frames.js /tmp/frames            # ~1 h on a laptop CPU
PW=<path to playwright> node render_overlay.js http://localhost:8700/video/overlay_dashboard.html overlay.png
./assemble.sh /tmp/frames ../media/Qandeel_explainer.mp4             # silent cut
pip install kokoro-onnx && npm pack kokoro-q8-shards kokoro-js       # voice model and voices
# join kokoro-q8.part0..5.bin into kokoro-q8.onnx; pack voices/*.bin into voices.npz (see narrate.py)
python narrate.py --model kokoro-q8.onnx --voices voices.npz --out narration.wav
ffmpeg -i ../media/Qandeel_explainer.mp4 -i narration.wav -map 0:v -map 1:a -c:v copy \
  -af loudnorm=I=-16:TP=-1.5 -c:a aac -b:a 160k -shortest Qandeel_explainer_voiced.mp4
```

The 3D scene (`scene.html`) reads its numbers from `qandeel/outputs/results.json`,
so re-running the simulation and re-rendering keeps the video consistent with the deck.
The 3D plant and breakwater are stylised and not to scale.
