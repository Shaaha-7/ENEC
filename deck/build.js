// Render deck.html to Qandeel_Deck.pdf and one PNG per slide.
// Needs a local web server at the repo root:  python -m http.server 8700
// Then:  node deck/build.js   (with Playwright installed: npm i -g playwright)
const path = require('path');
const { chromium } = require(process.env.PW || 'playwright');
(async () => {
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: 1920, height: 1080 } });
  await p.goto('http://localhost:8700/deck/deck.html', { waitUntil: 'networkidle' });
  await p.evaluate(() => document.fonts.ready);
  const out = path.join(__dirname);
  await p.pdf({ path: path.join(out, 'Qandeel_Deck.pdf'), width: '1920px', height: '1080px', printBackground: true });
  const n = await p.evaluate(() => document.querySelectorAll('.slide').length);
  for (let i = 0; i < n; i++) {
    const el = (await p.$$('.slide'))[i];
    await el.screenshot({ path: path.join(out, 'preview', `slide_${String(i + 1).padStart(2, '0')}.png`) });
  }
  console.log('slides', n);
  await b.close();
})();
