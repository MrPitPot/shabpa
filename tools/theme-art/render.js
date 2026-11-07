// Renders every premium-theme painting with Chromium:
//   node tools/theme-art/render.js [key,key,...]
// writes src/Shabpa/Assets/themes/<key>.jpg (the plate), <key>.thumb.jpg (the store tile), <key>.<sprite>.png (moving parts)
// and src/Shabpa/Assets/themes/scenes.json (how the app lays out and moves the sprites; see Kit/ThemeScene.cs).
'use strict';
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');
const { chromium } = require('playwright');

const root = path.resolve(__dirname, '../..');
const out = path.join(root, 'src/Shabpa/Assets/themes');
const tmp = path.join(require('os').tmpdir(), 'shabpa-theme-art');
fs.mkdirSync(out, { recursive: true });
fs.mkdirSync(tmp, { recursive: true });

const only = (process.argv[2] || '').split(',').filter(Boolean);
const themeDir = path.join(__dirname, 'themes');
const builders = fs.readdirSync(themeDir).filter((n) => n.endsWith('.js')).sort()
  .flatMap((n) => [].concat(require(path.join(themeDir, n))));

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  const specFile = path.join(out, 'scenes.json');
  const specs = fs.existsSync(specFile) ? JSON.parse(fs.readFileSync(specFile, 'utf8')) : {};
  for (const build of builders) {
    const probe = build.key;
    if (only.length && !only.includes(probe)) continue;
    const t0 = Date.now();
    const th = build();
    const { key, W, H } = th;
    await page.setViewportSize({ width: W, height: H });
    await page.setContent(`<!doctype html><html><body style="margin:0;background:#000">${th.plate}</body></html>`);
    await page.waitForTimeout(50);
    const png = path.join(tmp, key + '.png');
    await page.screenshot({ path: png });
    execFileSync('convert', [png, '-strip', '-sampling-factor', '4:2:0', '-quality', '84', '-interlace', 'none', path.join(out, key + '.jpg')]);
    execFileSync('convert', [png, '-strip', '-resize', '480x240', '-quality', '86', path.join(out, key + '.thumb.jpg')]);
    const sprites = {};
    for (const [name, s] of Object.entries(th.sprites || {})) {
      await page.setViewportSize({ width: Math.ceil(s.w), height: Math.ceil(s.h) });
      await page.setContent(`<!doctype html><html><body style="margin:0;background:transparent">${s.svg}</body></html>`);
      const file = path.join(out, `${key}.${name}.png`);
      await page.screenshot({ path: file, omitBackground: true });
      execFileSync('convert', [file, '-strip', '-define', 'png:compression-level=9', file]);
      sprites[name] = [Math.ceil(s.w), Math.ceil(s.h)];
    }
    specs[key] = { scene: th.scene, w: W, h: H, ax: th.ax ?? .5, ay: th.ay ?? .5, sprites, layers: th.layers || [] };
    const kb = (p) => Math.round(fs.statSync(p).size / 1024);
    console.log(`${key}: plate ${kb(path.join(out, key + '.jpg'))} KB, ${Object.keys(sprites).length} sprites, ${Date.now() - t0} ms`);
  }
  const ordered = Object.fromEntries(Object.keys(specs).sort().map((k) => [k, specs[k]]));
  fs.writeFileSync(specFile, JSON.stringify(ordered, null, 1));
  await browser.close();
})();
