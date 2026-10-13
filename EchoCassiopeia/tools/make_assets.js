// node tools/make_assets.js — генерирует PNG в assets/
const { chromium } = require('playwright');
const fs = require('fs'), path = require('path');
(async () => {
  const b = await chromium.launch();
  const p = await b.newPage();
  await p.goto('file://' + path.resolve(__dirname, 'assets.html'));
  const out = await p.evaluate(() => exportAll());
  for (const [k, v] of Object.entries(out)) {
    fs.writeFileSync(path.resolve(__dirname, '../assets', k), Buffer.from(v.split(',')[1], 'base64'));
    console.log('assets/' + k);
  }
  await b.close();
})();
