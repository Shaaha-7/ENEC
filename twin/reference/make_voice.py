"""Record the simulator's narration with ElevenLabs and embed the voice pack.

Usage (key stays in your environment, never in the files):
    export ELEVENLABS_API_KEY=...            # Windows PowerShell:  $env:ELEVENLABS_API_KEY="..."
    python tools/make_voice.py --list-voices # optional: see the voices on your account
    python tools/make_voice.py [--voice VOICE_ID]
    python tools/make_offline.py             # rebuild the stand copy

Writes site/voice/<id>_<hash>.mp3 for every line in the page's #voiceLines block and fills #voicePack.
A clip only plays if its text still matches the page exactly; edited lines fall back to the live voice.
Run from plan_local/sim."""
import argparse, hashlib, json, os, re, sys
import requests

API = "https://api.elevenlabs.io/v1"
PAGE = "qandeel_sim.html"
ap = argparse.ArgumentParser()
ap.add_argument("--voice", default=os.environ.get("ELEVENLABS_VOICE_ID", "21m00Tcm4TlvDq8ikWAM"))
ap.add_argument("--model", default="eleven_multilingual_v2")
ap.add_argument("--list-voices", action="store_true")
a = ap.parse_args()
key = os.environ.get("ELEVENLABS_API_KEY")
if not key:
    sys.exit("Set ELEVENLABS_API_KEY in your environment first (do not put it in any file).")
H = {"xi-api-key": key}
if a.list_voices:
    r = requests.get(f"{API}/voices", headers=H, timeout=30); r.raise_for_status()
    for v in r.json()["voices"]:
        print(v["voice_id"], "|", v["name"], "|", v.get("labels", {}).get("accent", ""), v.get("labels", {}).get("gender", ""))
    sys.exit()

html = open(PAGE, encoding="utf-8").read()
lines = json.loads(re.search(r'<script type="application/json" id="voiceLines">(.*?)</script>', html, re.S).group(1))
os.makedirs("site/voice", exist_ok=True)
pack = {}
for lid, text in lines.items():
    h = hashlib.sha1(f"{a.voice}|{a.model}|{text}".encode()).hexdigest()[:10]
    fn = f"{lid.replace('.', '_')}_{h}.mp3"
    path = os.path.join("site/voice", fn)
    if not os.path.exists(path):
        r = requests.post(f"{API}/text-to-speech/{a.voice}", params={"output_format": "mp3_44100_128"}, headers={**H, "accept": "audio/mpeg"},
                          json={"text": text, "model_id": a.model, "voice_settings": {"stability": 0.5, "similarity_boost": 0.75}}, timeout=120)
        if r.status_code != 200:
            sys.exit(f"{lid}: ElevenLabs returned {r.status_code}: {r.text[:300]}  (try --list-voices and --voice)")
        open(path, "wb").write(r.content)
        print("recorded", fn, len(r.content) // 1024, "KB")
    pack[lid] = {"file": fn, "text": text, "voice": a.voice, "model": a.model}
for f in os.listdir("site/voice"):                       # remove clips no longer used
    if f.endswith(".mp3") and f not in {p["file"] for p in pack.values()}:
        os.remove(os.path.join("site/voice", f))
html = re.sub(r'(<script type="application/json" id="voicePack">)(.*?)(</script>)', lambda m: m.group(1) + json.dumps(pack, ensure_ascii=False) + m.group(3), html, count=1, flags=re.S)
open(PAGE, "w", encoding="utf-8").write(html)
print(f"voice pack: {len(pack)} clips embedded in {PAGE}")
