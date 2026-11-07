'use strict';
const L = require('../lib');
const { f } = L;

// Fireflies: deep in an old forest on a summer night. Trunks in layers of blue-green dark, moonlight slanting through
// the canopy, ferns and moss, a ring of softly glowing mushrooms by a stream. Fireflies wander and blink.
function build() {
  const W = 2400, H = 1200, r = L.rng(7272);
  const c = L.canvas(W, H), b = c.b;
  b.push(`<rect width="${W}" height="${H}" fill="${c.g('bg', [[0, '#04100c'], [.5, '#0a2218'], [1, '#050c08']])}"/>`);
  b.push(c.glow(1500, 120, 900, '#8ad0b0', .18));
  // moonlight shafts through the canopy
  for (let k = 0; k < 6; k++) { const x = 1100 + k * 160 + r(-40, 40), w0 = r(40, 110); b.push(`<path d="M${f(x)} -20L${f(x + w0)} -20L${f(x + w0 - 500)} ${H}L${f(x - 520)} ${H}Z" fill="${c.g('sh', [[0, '#c8f0dc', .2], [1, '#c8f0dc', 0]])}" ${c.blur(16)}/>`); }
  // trunks: far (hazy) to near (dark), with bark lines and a moonlit edge
  const trunk = (x, w0, col, edge, roots) => {
    let s = `<path d="M${f(x - w0 / 2)} -20C${f(x - w0 / 2 + r(-10, 10))} 400 ${f(x - w0 / 2 + r(-14, 14))} 800 ${f(x - w0 * .8)} ${H + 10}L${f(x + w0 * .8)} ${H + 10}C${f(x + w0 / 2 + r(-14, 14))} 800 ${f(x + w0 / 2 + r(-10, 10))} 400 ${f(x + w0 / 2)} -20Z" fill="${col}"/>`;
    for (let k = 0; k < w0 / 8; k++) { const bx = x - w0 / 2 + r(4, w0 - 4); s += `<path d="M${f(bx)} ${f(r(-20, 600))}q${f(r(-6, 6))} ${f(r(100, 300))} ${f(r(-4, 4))} ${f(r(200, 500))}" stroke="#000" stroke-opacity=".25" stroke-width="${f(r(1, 3))}" fill="none"/>`; }
    s += `<path d="M${f(x + w0 / 2 - 3)} -20C${f(x + w0 / 2)} 400 ${f(x + w0 / 2)} 800 ${f(x + w0 * .8 - 4)} ${H}" stroke="${edge}" stroke-opacity=".35" stroke-width="${f(Math.max(2, w0 * .06))}" fill="none"/>`;
    if (roots) for (let k = 0; k < 4; k++) { const d = k < 2 ? -1 : 1; s += `<path d="M${f(x + d * w0 * .3)} ${H - 120}Q${f(x + d * w0 * (.9 + k % 2 * .5))} ${H - 60} ${f(x + d * w0 * (1.4 + k % 2 * .6))} ${H + 10}" stroke="${col}" stroke-width="${f(w0 * .18)}" fill="none" stroke-linecap="round"/>`; }
    return s;
  };
  for (let k = 0; k < 14; k++) b.push(trunk(r(0, W), r(26, 60), '#0e2a22', '#4a8a70', false));
  b.push(`<rect width="${W}" height="${H}" fill="${c.g('fog', [[0, '#3a6a58', 0], [.6, '#3a6a58', .2], [1, '#3a6a58', .05]])}"/>`);
  for (let k = 0; k < 8; k++) b.push(trunk(r(0, W), r(60, 120), '#081a14', '#6aaa88', true));
  b.push(trunk(180, 230, '#040c08', '#7ac8a0', true), trunk(2220, 260, '#040c08', '#7ac8a0', true));
  // canopy: dark leaf masses along the top
  let leaves = '';
  for (let i = 0; i < 260; i++) leaves += `<path d="${L.blob(r, r(-40, W + 40), r(-60, 160), r(30, 90), r(20, 50), .3, 8)}" fill="${r.pick(['#04100c', '#06160f', '#0a2016'])}"/>`;
  b.push(leaves);
  // the forest floor: moss, a stream catching the moon, ferns
  const ground = L.ridgeFn(r, 1010, 20);
  b.push(`<path d="${L.ridgePath(ground, W, H)}" fill="${c.g('gd', [[0, '#14321f'], [1, '#040a06']])}"/>`);
  b.push(`<path d="M600 ${H + 10}C800 1100 1100 1060 1300 1050C1500 1040 1700 1030 2000 1040L2000 1060C1700 1052 1500 1062 1300 1074C1100 1090 900 1130 760 ${H + 10}Z" fill="${c.g('st', [[0, '#3a7a68'], [1, '#c8f0dc']], 0, 0, 1, 0)}" opacity=".55"/>`);
  const fern = (x, y, s, d) => {
    let p = '';
    for (let k = 0; k < 6; k++) {
      const a = -Math.PI / 2 + d * (.3 + k * .22), len = s * r(.8, 1.1);
      const ex = x + Math.cos(a) * len, ey = y + Math.sin(a) * len * .8;
      p += `<path d="M${f(x)} ${f(y)}Q${f(x + Math.cos(a) * len * .5)} ${f(y + Math.sin(a) * len - 20)} ${f(ex)} ${f(ey)}" stroke="#1f5a3a" stroke-width="3" fill="none"/>`;
      for (let q = 1; q < 9; q++) { const u = q / 9, lx = x + (ex - x) * u, ly = y + (ey - y) * u - Math.sin(Math.PI * u) * 14; p += `<path d="M${f(lx)} ${f(ly)}l${f(-8 * (1 - u) - 4)} ${f(10 * (1 - u) + 4)}M${f(lx)} ${f(ly)}l${f(8 * (1 - u) + 4)} ${f(8 * (1 - u) + 4)}" stroke="#2a7a4a" stroke-width="2.4"/>`; }
    }
    return p;
  };
  for (let i = 0; i < 26; i++) { const x = r(0, W); b.push(fern(x, ground(x) + r(20, 120), r(60, 120), r() < .5 ? 1 : -1), fern(x + 10, ground(x) + r(20, 120), r(60, 110), r() < .5 ? 1 : -1)); }
  // glowing mushrooms in a little ring
  const shrooms = [];
  for (let k = 0; k < 9; k++) { const x = 860 + k * 64 + r(-14, 14), y = 1046 + Math.sin(k) * 14, s = r(1.2, 1.9); shrooms.push([x, y - 14 * s]);
    b.push(c.glow(x, y - 16 * s, 70 * s, '#7ae8ff', .35), `<path d="M${f(x - 4 * s)} ${f(y)}L${f(x - 3 * s)} ${f(y - 16 * s)}H${f(x + 3 * s)}L${f(x + 4 * s)} ${f(y)}Z" fill="#c8f0f0"/><path d="M${f(x - 16 * s)} ${f(y - 14 * s)}Q${f(x)} ${f(y - 38 * s)} ${f(x + 16 * s)} ${f(y - 14 * s)}Z" fill="${c.g('cap', [[0, '#e0ffff'], [1, '#3ab8d8']])}"/>`); }
  const plate = c.finish({ vignette: .6 });

  const sprites = {
    fly: L.sprite(60, 60, L.rad('f', [[0, '#ffffff', 1], [.08, '#f4ff9a', 1], [.25, '#d9f25e', .5], [1, '#c6e04a', 0]]), `<circle cx="30" cy="30" r="30" fill="url(#f)"/>`),
    glow: L.softDot(160, '#7ae8ff', { core: .2, coreOp: .45, mid: .2 }),
  };
  const layers = [
    { sprite: 'glow', at: shrooms, size: [110, 150], twinkle: { lo: .4, hi: 1, dur: [3, 5] } },
    // fireflies drift on loose loops (x and y swing at different speeds) and blink
    { sprite: 'fly', n: 34, x: [.02, .98], y: [.25, .92], size: [34, 60], sway: { x: [40, 120], y: [30, 90], dur: [6, 11], dury: [4, 8] }, twinkle: { lo: 0, hi: 1, dur: [1.2, 2.8] }, move: { dx: [-60, 60], dy: [-120, -40], dur: [20, 30] }, fade: [[0, 0], [.15, 1], [.85, 1], [1, 0]] },
    { sprite: 'fly', n: 16, x: [.1, .9], y: [.6, .95], size: [20, 32], op: [.6, .9], sway: { x: [20, 60], y: [15, 40], dur: [5, 9], dury: [3, 6] }, twinkle: { lo: 0, hi: 1, dur: [.9, 2] } },
  ];
  return { key: 'fireflies', scene: 'Fireflies', W, H, ax: .5, ay: .8, plate, sprites, layers };
}
build.key = 'fireflies';
module.exports = build;
