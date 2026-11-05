"""Record the guided-tour narration with ElevenLabs for the Unity simulator.

Reads the tour lines word for word from data/narration_lines.json and writes unity_site/voice/<key>.mp3.
A clip is only re-recorded when its text, voice or model changed (hash in voice_manifest.json).
The API key is read from the ELEVENLABS_API_KEY environment variable only; it is never written to a file.
Run: python tools/make_voice_unity.py [--voice VOICE_ID]
"""
import argparse
import hashlib
import json
import os
import sys
from pathlib import Path

import requests

ROOT = Path(__file__).resolve().parent.parent
LINES = ["mission.0", "mission.1", "mission.2", "mission.3", "mission.4", "mission.5", "topic.idea"]
API = "https://api.elevenlabs.io/v1"

ap = argparse.ArgumentParser()
ap.add_argument("--voice", default=os.environ.get("ELEVENLABS_VOICE_ID", "21m00Tcm4TlvDq8ikWAM"))
ap.add_argument("--model", default="eleven_multilingual_v2")
a = ap.parse_args()
key = os.environ.get("ELEVENLABS_API_KEY")
if not key:
    sys.exit("Set ELEVENLABS_API_KEY in your environment first (never in a file).")

text = json.loads((ROOT / "data/narration_lines.json").read_text(encoding="utf-8"))
out = ROOT / "unity_site/voice"; out.mkdir(parents=True, exist_ok=True)
man_path = out / "voice_manifest.json"
manifest = json.loads(man_path.read_text()) if man_path.exists() else {}
for k in LINES:
    t = text[k]
    h = hashlib.sha1(f"{a.voice}|{a.model}|{t}".encode()).hexdigest()[:12]
    mp3 = out / f"{k.replace('.', '_')}.mp3"
    if mp3.exists() and manifest.get(k) == h:
        print("unchanged", mp3.name); continue
    r = requests.post(f"{API}/text-to-speech/{a.voice}", params={"output_format": "mp3_44100_128"},
                      headers={"xi-api-key": key, "accept": "audio/mpeg"},
                      json={"text": t, "model_id": a.model, "voice_settings": {"stability": 0.55, "similarity_boost": 0.75}},
                      timeout=120)
    if r.status_code != 200:
        sys.exit(f"{k}: ElevenLabs returned {r.status_code}: {r.text[:200]}")
    mp3.write_bytes(r.content); manifest[k] = h
    print("recorded", mp3.name, len(r.content) // 1024, "KB")
man_path.write_text(json.dumps(manifest, indent=1))
