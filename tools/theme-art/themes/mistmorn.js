'use strict';
const L = require('../lib');
const { f } = L;

// Misty morning: a lake valley at sunrise. A soft peach sun behind layers of green hills that fade into the mist, fir
// tops on the ridges, a still lake mirroring it all, reeds and grass in front. Mist banks drift slowly, birds pass.
function build() {
  const W = 2400, H = 1200, r = L.rng(4545);
  const c = L.canvas(W, H), b = c.b;
  b.push(`<rect width="${W}" height="${H}" fill="${c.g('sky', [[0, '#dfe9e6'], [.45, '#f1e6d6'], [.62, '#f6dcc4'], [1, '#e9efe9']])}"/>`);
  b.push(c.glow(1640, 470, 620, '#ffd8b0', .65));
  b.push(`<circle cx="1640" cy="470" r="70" fill="#fff4e4" opacity=".95"/>`);
  b.push(`<rect width="${W}" height="600" fill="#000" ${c.cloud({ freq: .002, oct: 4, seed: 6, color: [1, 1, 1], contrast: 1.4, bias: -.5, stretchX: 4 })} opacity=".45"/>`);
  // hills: each layer greener and darker toward us, fir tops along the ridges, mist pooled in every valley
  const hills = [
    { base: 600, amp: 70, col: '#b9cdc2', tree: '#a9bfb2', th: [18, 34] },
    { base: 650, amp: 80, col: '#97b4a4', tree: '#88a796', th: [24, 46] },
    { base: 700, amp: 90, col: '#6e9483', tree: '#5f8574', th: [30, 60] },
    { base: 740, amp: 70, col: '#456f5e', tree: '#3a6151', th: [40, 80] },
  ];
  hills.forEach((hl, i) => {
    const fn = L.ridgeFn(r, hl.base, hl.amp, [[.0018 + i * .0003, 1], [.006, .35], [.015, .1]]);
    b.push(`<path d="${L.ridgePath(fn, W, H, { bottom: 760 })}" fill="${hl.col}"/>`);
    let t = '';
    for (let x = -20; x < W + 20; x += r(8, 22)) if (r() < .7) t += L.pine(r, x, fn(x) + r(2, 14), r(...hl.th), { color: hl.tree, tiers: 8, w: .36 });
    b.push(t);
    b.push(`<rect y="${hl.base - hl.amp}" width="${W}" height="${hl.amp * 2 + 60}" fill="${c.g('mh', [[0, '#f4f1ea', 0], [.7, '#f4f1ea', .55], [1, '#f4f1ea', .8]])}"/>`);
  });
  // the lake: a mirror of the sky and hills, broken by faint ripples
  const lake = 760;
  b.push(`<rect y="${lake}" width="${W}" height="${H - lake}" fill="${c.g('lk', [[0, '#e9e6dc'], [.3, '#cfdcd4'], [1, '#8fa89a']])}"/>`);
  b.push(`<g transform="translate(0 ${lake * 2}) scale(1 -1)" opacity=".35" ${c.blur(3)}>${c.glow(1640, 470, 520, '#ffd8b0', .8)}</g>`);
  b.push(`<rect x="1610" y="${lake}" width="60" height="320" fill="${c.g('sr', [[0, '#fff4e4', .7], [1, '#fff4e4', 0]])}" ${c.blur(8)}/>`);
  for (let i = 0; i < 160; i++) { const y = r(lake + 6, H), x = r(0, W), len = r(30, 200); b.push(`<path d="M${f(x)} ${f(y)}h${f(len)}" stroke="${r() < .5 ? '#ffffff' : '#6e8a7c'}" stroke-opacity="${f(r(.15, .4))}" stroke-width="${f(r(1, 2))}"/>`); }
  // a wooden jetty
  b.push(`<path d="M1880 ${lake + 120}L2400 ${lake + 100}L2400 ${lake + 116}L1880 ${lake + 132}Z" fill="#5a4a3a"/>`);
  for (let k = 0; k < 8; k++) { const x = 1900 + k * 70; b.push(`<rect x="${x}" y="${lake + 120 - k * 2.5}" width="7" height="60" fill="#3e3226"/>`); }
  b.push(`<rect y="${lake + 132}" x="1880" width="520" height="70" fill="#5a4a3a" opacity=".18" ${c.blur(6)}/>`);
  // reeds and grass in front, darker, with seed heads
  let reeds = '';
  for (let i = 0; i < 220; i++) {
    const x = r(-20, 700) + (r() < .3 ? r(1500, 2400) : 0), y = H + 10, len = r(80, 340), bend = r(-50, 50);
    reeds += `<path d="M${f(x)} ${y}Q${f(x + bend * .3)} ${f(y - len * .6)} ${f(x + bend)} ${f(y - len)}" stroke="${r.pick(['#3a5a44', '#2c4a36', '#4f6e52', '#6a8460'])}" stroke-width="${f(r(1.5, 4))}" fill="none" stroke-linecap="round"/>`;
    if (r() < .15) reeds += `<ellipse cx="${f(x + bend)}" cy="${f(y - len - 10)}" rx="4" ry="14" transform="rotate(${f(bend / 3)} ${f(x + bend)} ${f(y - len - 10)})" fill="#6a4a30"/>`;
  }
  b.push(reeds);
  const plate = c.finish({ vignette: .12, grain: .03 });

  const mist = L.sprite(900, 220, L.rad('m', [[0, '#ffffff', .75], [.5, '#ffffff', .35], [1, '#ffffff', 0]]), `<ellipse cx="450" cy="110" rx="450" ry="110" fill="url(#m)"/>`);
  const bird = (up) => L.sprite(60, 30, '', `<path d="M2 ${up ? 6 : 22}Q16 ${up ? 8 : 20} 30 16Q44 ${up ? 8 : 20} 58 ${up ? 6 : 22}Q44 ${up ? 12 : 18} 30 19Q16 ${up ? 12 : 18} 2 ${up ? 6 : 22}Z" fill="#33463c"/>`);
  const sprites = { mist, birdUp: bird(true), birdDown: bird(false) };
  const layers = [
    { sprite: 'mist', n: 6, x: [-.1, .9], y: [.5, .68], size: [800, 1300], op: [.45, .75], sway: { x: [120, 260], dur: [30, 50] } },
    { sprite: ['birdUp', 'birdDown'], frames: { fps: 3 }, n: 4, x: [1.05, 1.15], y: [.18, .35], size: [22, 34], op: .8, flip: true, move: { dx: [-2800, -2600], dy: [-80, 40], dur: [60, 80] }, sway: { x: 0, y: [6, 14], dur: [3, 5] } },
    { sprite: 'mist', n: 4, x: [-.1, .9], y: [.75, .95], size: [900, 1400], op: [.35, .6], sway: { x: [150, 300], dur: [36, 60] } },
  ];
  return { key: 'mistmorn', scene: 'Mist', W, H, ax: .5, ay: .7, plate, sprites, layers };
}
build.key = 'mistmorn';
module.exports = build;
