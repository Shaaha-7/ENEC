const { chromium } = require(process.env.PW);
const fs = require('fs');
(async () => {
  const out = process.argv[2], fps = 30;
  fs.mkdirSync(out, { recursive: true });
  const b = await chromium.launch({ args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'] });
  const p = await b.newPage({ viewport: { width: 1920, height: 1080 } });
  p.on('pageerror', e => console.log('pageerror:', e.message));
  await p.goto('http://localhost:8700/video/scene.html');
  await p.waitForFunction(() => window.ready === true, null, { timeout: 120000 });
  const dur = await p.evaluate(() => window.DURATION);
  const n = Math.round(dur * fps);
  for (let i = 0; i < n; i++) {
    await p.evaluate(t => window.renderAt(t), i / fps);
    await p.screenshot({ path: `${out}/f_${String(i).padStart(5, '0')}.jpg`, type: 'jpeg', quality: 90 });
    if (i % 150 === 0) console.log('frame', i, '/', n, new Date().toISOString());
  }
  console.log('done', n);
  await b.close();
})();
