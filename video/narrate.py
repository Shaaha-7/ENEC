"""Generate the narration track for the explainer with an open neural voice (Kokoro-82M).

    pip install kokoro-onnx
    npm pack kokoro-q8-shards kokoro-js     # model weights + voice styles (Apache-2.0)
    python video/narrate.py --model kokoro-q8.onnx --voices voices.npz --out narration.wav

Each line is placed at the start of its scene; if a line is longer than its
slot it is spoken slightly faster (up to 1.1x). A quiet synthetic sea ambience
sits underneath. The words match video/VIDEO_SCRIPT.md, with a few spelled
phonetically for the synthesiser (Kandeel, Ee-neck).
"""
import argparse
import wave

import numpy as np

SR = 24000
LINES = [  # (start s, end s, text)
    (0.6, 8.0, "We are Team Kandeel. Kandeel is Arabic for jellyfish, and for lantern."),
    (8.6, 17.0, "Every summer, Blue Blubber jellyfish swarms drift toward coastal seawater intakes. "
                "They block the screens, and almost none survive."),
    (17.3, 27.0, "Ee-neck already detects swarms offshore. From that alert, Kandeel simulates hundreds of drifts "
                 "on real Gulf data, to predict if, and when, they arrive."),
    (27.3, 37.0, "This is our working dashboard, running on real twenty twenty-five data: the chance of arrival, "
                 "the arrival window, when to switch on, and where to release."),
    (37.3, 43.0, "When the swarm is due, a bubble curtain switches on, and holds the jellyfish outside the intake."),
    (43.3, 51.0, "Nothing is cut, pumped or netted. Rising air moves the water, and the water moves the jellyfish."),
    (51.3, 61.0, "Then two uncrewed boats close a boom around the swarm. Our model found that oil-spill booms leak "
                 "jellyfish, so we added a closed-bottom bag."),
    (61.3, 74.0, "They tow the swarm about three kilometres and release it alive. More than half drift back, "
                 "and the curtain simply holds them again."),
    (74.3, 82.0, "The intake keeps running, and the jellyfish go back to the sea."),
    (82.3, 94.0, "In a stress test on real Gulf data, a camera alone let about a quarter through. "
                 "Qandeel let about three percent. Next, a one to twenty tank test."),
    (94.6, 101.6, "Kandeel. Herd, don't harvest."),
]
TOTAL = 102.0


def sea_ambience(n, rng):
    """Soft surf: low-passed noise with slow swells, very quiet."""
    noise = rng.normal(0, 1, n)
    k = int(SR * 0.004)
    smooth = np.convolve(noise, np.ones(k) / k, mode="same")
    t = np.arange(n) / SR
    swell = 0.55 + 0.45 * np.sin(2 * np.pi * t / 7.5) * np.sin(2 * np.pi * t / 19.0 + 1.0)
    amb = smooth * swell
    return 0.035 * amb / (np.abs(amb).max() + 1e-9)


def main():
    from kokoro_onnx import Kokoro

    ap = argparse.ArgumentParser()
    ap.add_argument("--model", required=True)
    ap.add_argument("--voices", required=True)
    ap.add_argument("--voice", default="af_heart")
    ap.add_argument("--out", default="narration.wav")
    a = ap.parse_args()
    k = Kokoro(a.model, a.voices)
    track = np.zeros(int(TOTAL * SR), dtype=np.float32)
    for start, end, text in LINES:
        speed = 1.0
        while True:
            audio, sr = k.create(text, voice=a.voice, speed=speed, lang="en-us")
            if len(audio) / sr <= end - start or speed >= 1.1:
                break
            speed = round(speed + 0.05, 2)
        i = int(start * SR)
        audio = audio[: len(track) - i]
        track[i:i + len(audio)] += audio
        print(f"{start:5.1f}s  {len(audio) / SR:4.1f}s of {end - start:4.1f}s  speed {speed}")
    voice_peak = np.abs(track).max()
    track = 0.89 * track / voice_peak + sea_ambience(len(track), np.random.default_rng(3))
    fade = int(1.5 * SR)
    track[-fade:] *= np.linspace(1, 0, fade)
    track = np.clip(track, -1, 1)
    with wave.open(a.out, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes((track * 32767).astype("<i2").tobytes())
    print("wrote", a.out)


if __name__ == "__main__":
    main()
