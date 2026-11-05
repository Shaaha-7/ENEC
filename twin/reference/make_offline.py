"""Build the stand copy that runs with no internet: local three.js, local fonts, local site data.
Run from plan_local/sim:  python tools/make_offline.py   then serve:  python -m http.server 8000"""
s = open('qandeel_sim.html').read()
a = '"three":"https://cdn.jsdelivr.net/npm/three@0.160.0/build/three.module.js","three/addons/":"https://cdn.jsdelivr.net/npm/three@0.160.0/examples/jsm/"'
assert a in s; s = s.replace(a, '"three":"./vendor/three/build/three.module.js","three/addons/":"./vendor/three/examples/jsm/"')
fl = ['<link rel="preconnect" href="https://fonts.googleapis.com">\n',
      '<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Barlow:wght@400;500;600;700;800&family=Barlow+Condensed:wght@500;600;700&family=JetBrains+Mono:wght@400;600&display=swap">']
for f in fl: assert f in s; s = s.replace(f, '')
s = '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"><link rel="stylesheet" href="fonts/fonts.css"></head><body>' + s + '</body></html>'
assert 'https://' not in s.replace('https://api.elevenlabs.io', ''), [l for l in s.split('\n') if 'https://' in l][:3]
open('qandeel_sim_offline.html', 'w').write(s); print('offline copy written')
