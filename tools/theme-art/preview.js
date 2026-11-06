// node tools/theme-art/preview.js <key[,key]> <outDir> [w] [h] [times=0,4] [videoSeconds] [ui]
// Screenshots (and optionally a short mp4) of a theme as the app plays it, for checking paintings and the direction of motion.
'use strict';
const http = require('http');
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');
const { chromium } = require('playwright');

const root = path.resolve(__dirname, '../..');
const [keysArg, outDir, w = 1280, h = 720, times = '0,4', video = '0', ui = ''] = process.argv.slice(2);
fs.mkdirSync(outDir, { recursive: true });
const types = { '.html': 'text/html', '.json': 'application/json', '.png': 'image/png', '.jpg': 'image/jpeg' };
const server = http.createServer((req, res) => {
  const p = path.join(root, decodeURIComponent(req.url.split('?')[0]));
  if (!p.startsWith(root) || !fs.existsSync(p)) { res.writeHead(404); return res.end(); }
  res.writeHead(200, { 'content-type': types[path.extname(p)] || 'application/octet-stream' });
  fs.createReadStream(p).pipe(res);
}).listen(0, async () => {
  const port = server.address().port;
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: +w, height: +h } });
  for (const key of keysArg.split(',')) {
    await page.goto(`http://127.0.0.1:${port}/tools/theme-art/preview.html?key=${key}&w=${w}&h=${h}${ui ? '&ui=1' : ''}`);
    await page.evaluate(() => window.ready);
    for (const t of times.split(',')) {
      await page.evaluate((t) => window.renderAt(+t), t);
      await page.screenshot({ path: path.join(outDir, `${key}-${w}x${h}-t${t}.png`) });
    }
    if (+video > 0) {
      const fdir = path.join(outDir, key + '-frames');
      fs.rmSync(fdir, { recursive: true, force: true }); fs.mkdirSync(fdir);
      const fps = 15;
      for (let i = 0; i < +video * fps; i++) {
        await page.evaluate((t) => window.renderAt(t), 3 + i / fps);
        await page.screenshot({ path: path.join(fdir, String(i).padStart(4, '0') + '.jpg'), type: 'jpeg', quality: 85 });
      }
      execFileSync('ffmpeg', ['-y', '-loglevel', 'error', '-framerate', String(fps), '-i', path.join(fdir, '%04d.jpg'), '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-crf', '23', path.join(outDir, key + '.mp4')]);
      fs.rmSync(fdir, { recursive: true, force: true });
    }
    console.log('previewed', key);
  }
  await browser.close();
  server.close();
});
