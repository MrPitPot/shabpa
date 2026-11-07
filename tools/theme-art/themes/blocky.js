'use strict';
const L = require('../lib');

// Blocky world: an evening landscape made of blocks, in crisp pixel art. Banded sunset sky, a square sun, hills of
// grass over dirt over stone with ore showing in the cuts, block trees, a pond. Square clouds drift, a few loose grass
// blocks float down, and pixel birds flap across.
function build() {
  const W = 2400, H = 1200, P = 12, r = L.rng(6464);
  const cols = W / P, rows = H / P;
  const px = [];
  const put = (x, y, col) => { if (x >= 0 && x < cols && y >= 0 && y < rows) px[y * cols + x] = col; };
  // sky in bands with a dither between them
  const sky = ['#2a1f4a', '#3a2a5e', '#55356e', '#7a3f74', '#a64e70', '#d0646a', '#ec8a62', '#f6b06a'];
  for (let y = 0; y < rows; y++) for (let x = 0; x < cols; x++) {
    const t = Math.min(.999, y / (rows * .62)), band = t * sky.length, i = Math.floor(band), fr = band - i;
    put(x, y, sky[Math.min(sky.length - 1, i + ((fr > .75 && (x + y) % 2 === 0) ? 1 : 0))]);
  }
  // the sun: a square with a ring of glow pixels
  const sx = 150, sy = 52;
  for (let y = -9; y <= 9; y++) for (let x = -9; x <= 9; x++) { const d = Math.max(Math.abs(x), Math.abs(y)); if (d <= 6) put(sx + x, sy + y, d <= 4 ? '#fff4c0' : '#ffe08a'); else if (d <= 9 && (x + y) % 2 === 0) put(sx + x, sy + y, '#f8c27a'); }
  // stars in the dark top
  for (let i = 0; i < 70; i++) put(r.int(0, cols), r.int(0, 18), r() < .3 ? '#ffffff' : '#c8b8f0');
  // hills: three layers, far ones hazy
  const hill = (base, amp, fr, ph, grass, dirt, stone, tree) => {
    const hs = [];
    for (let x = 0; x < cols; x++) hs.push(Math.round(base - amp * (Math.sin(x * fr + ph) * .6 + Math.sin(x * fr * 2.7 + ph * 2) * .3 + Math.sin(x * fr * 6.1) * .1)));
    // terrace the heights into steps of whole blocks
    for (let x = 0; x < cols; x++) {
      const top = hs[x];
      for (let y = top; y < rows; y++) {
        const d = y - top;
        let c = d < 2 ? grass[d === 0 ? 0 : 1] : d < 7 ? dirt[(x * 7 + y * 3) % 5 === 0 ? 1 : 0] : stone[(x * 13 + y * 7) % 9 === 0 ? 1 : 0];
        if (d >= 7 && stone[2] && (x * 31 + y * 17) % 61 === 0) c = stone[2];
        if (d === 0 && (x % 4 === 0)) c = grass[2] || c;
        put(x, y, c);
      }
      // grass tufts and flowers
      if (tree && r() < .1) put(x, top - 1, r.pick(['#5fbf3f', '#ffd25e', '#ff6a6a']));
    }
    return hs;
  };
  hill(64, 8, .05, 1, ['#6a8a7a', '#5a7a6a', '#7a9a8a'], ['#5e5a6a', '#544f60'], ['#4e4a5c', '#454257']);
  hill(76, 10, .04, 3, ['#4f9a3a', '#3f7d2e', '#62ae48'], ['#6a4a2e', '#5a3e26'], ['#5a5a5e', '#4a4a50']);
  const near = hill(86, 6, .06, 5, ['#5cbf3f', '#46a032', '#78d458'], ['#7a5232', '#664428'], ['#6e6e72', '#5c5c62', '#3ad6e0'], true);
  // a pond in a dip of the near hill
  let minX = 0; for (let x = 20; x < cols - 20; x++) if (near[x] > near[minX] || minX === 0) minX = x;
  for (let x = minX - 10; x <= minX + 10; x++) for (let y = near[minX] - 1; y < near[minX] + 3; y++) if (y >= near[x] - 1) put(x, y, y === near[minX] - 1 ? '#7ad0f0' : '#3a8ad0');
  // block trees: a trunk and a layered cube of leaves
  for (const tx of [26, 48, 120, 168, 182]) {
    const top = near[tx];
    for (let y = top - 6; y < top; y++) put(tx, y, y % 2 ? '#6a4a2a' : '#5a3e22');
    for (let y = -11; y <= -5; y++) for (let x = -3; x <= 3; x++) if (!(Math.abs(x) === 3 && (y === -11 || y === -5))) put(tx + x, top + y, (x + y) % 3 === 0 ? '#2e7a2a' : y < -9 ? '#4aa63a' : '#3a8c30');
  }
  let rects = '';
  for (let y = 0; y < rows; y++) {
    let x = 0;
    while (x < cols) { const c = px[y * cols + x]; let e = x + 1; while (e < cols && px[y * cols + e] === c) e++; rects += `<rect x="${x * P}" y="${y * P}" width="${(e - x) * P}" height="${P}" fill="${c}"/>`; x = e; }
  }
  // a faint grid line between blocks on the ground gives the block look
  let grid = '';
  for (let x = 0; x <= W; x += P * 2) grid += `M${x} 760V${H}`;
  for (let y = 760; y <= H; y += P * 2) grid += `M0 ${y}H${W}`;
  const plate = L.svg(W, H, '', `<g shape-rendering="crispEdges">${rects}<path d="${grid}" stroke="#000" stroke-opacity=".08"/></g>`);

  const pix = (map, pal, s = 8) => {
    const rowsM = map.length, colsM = map[0].length;
    let out = '';
    map.forEach((row, y) => [...row].forEach((ch, x) => { if (ch !== '.') out += `<rect x="${x * s}" y="${y * s}" width="${s}" height="${s}" fill="${pal[ch]}"/>`; }));
    return L.sprite(colsM * s, rowsM * s, '', `<g shape-rendering="crispEdges">${out}</g>`);
  };
  const cloud = pix(['....wwwww.......', '..wwwwwwwww.ww..', '.wwwwwwwwwwwwwww', 'wwwwwwwwwwwwwwww', 'ssssssssssssssss'], { w: '#fbe8f0', s: '#d8b8d0' }, 16);
  const block = pix(['gggggggg', 'GgGGgGGg', 'dGddGddd', 'dddDdddd', 'dDdddddD', 'ddddDddd', 'dddddddd', 'DdddddDd'], { g: '#78d458', G: '#5cbf3f', d: '#7a5232', D: '#5a3e22' }, 6);
  const birdA = pix(['b...b', '.b.b.', '..b..'], { b: '#2a1f4a' }, 6);
  const birdB = pix(['.....', 'bb.bb', '..b..'], { b: '#2a1f4a' }, 6);
  const layers = [
    { sprite: 'cloud', n: 6, x: [-.1, 1], y: [.08, .3], size: [180, 320], op: [.8, 1], move: { dx: [500, 800], dy: 0, dur: [60, 90] }, fade: [[0, 0], [.1, 1], [.9, 1], [1, 0]] },
    { sprite: ['birdA', 'birdB'], frames: { fps: 4 }, n: 4, x: [-.08, -.02], y: [.2, .4], size: [24, 36], move: { dx: [2600, 2800], dy: [-60, 60], dur: [40, 60] } },
    { sprite: 'block', n: 9, x: [.05, .95], y: [-.1, -.05], size: [28, 48], op: [.85, 1], move: { dx: 0, dy: [900, 1000], dur: [18, 30] }, fade: [[0, 0], [.08, 1], [.85, 1], [1, 0]], fps: 12 },
  ];
  return { key: 'blocky', scene: 'Pixels', W, H, ax: .5, ay: .8, plate, sprites: { cloud, block, birdA, birdB }, layers };
}
build.key = 'blocky';
module.exports = build;
