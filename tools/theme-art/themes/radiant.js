'use strict';
const L = require('../lib');
const { f } = L;

// Radiant: a competitive-shooter poster. Hard angular slashes of red and teal cut a dark field, each lit along its
// edge, with halftone dots, tech line-work and a big faint chevron emblem. Shards slide in and fade, edge glints flick.
function build() {
  const W = 2400, H = 1200, r = L.rng(4655);
  const c = L.canvas(W, H), b = c.b;
  b.push(`<rect width="${W}" height="${H}" fill="${c.g('bg', [[0, '#0b1016'], [.6, '#12141c'], [1, '#2a1018']], 0, 0, 1, 1)}"/>`);
  b.push(c.glow(1900, 300, 900, '#ff4655', .2), c.glow(400, 950, 800, '#3de0c0', .14));
  // halftone dot fields in two corners
  let dots = '';
  for (let y = 0; y < H; y += 18) for (let x = 0; x < W; x += 18) {
    const d1 = Math.hypot(x - 2400, y - 0) / 900, d2 = Math.hypot(x - 0, y - 1200) / 800;
    const s = Math.max(0, 1 - Math.min(d1, d2)) * 6;
    if (s > .4) dots += `<circle cx="${x + ((y / 18) % 2) * 9}" cy="${y}" r="${f(s)}" fill="${d1 < d2 ? '#ff4655' : '#3de0c0'}" opacity=".22"/>`;
  }
  b.push(dots);
  // the big faint chevron emblem in the middle
  b.push(`<path d="M820 260L1200 260L1580 940L1340 940L1200 690L1060 940L820 940L1060 520Z" fill="none" stroke="#f5f1f2" stroke-opacity=".06" stroke-width="28"/>`);
  // slashes: parallelograms with a light gradient and a bright leading edge
  const slash = (x, y, len, th, ang, col, op) => {
    const a = ang * Math.PI / 180, dx = Math.cos(a) * len, dy = Math.sin(a) * len, sk = th * .6;
    const gid = L.id('sl');
    c.defs.push(L.lin(gid, [[0, col, 0], [.55, col, op * .7], [1, col, op]], 0, 0, 1, 0));
    let s = `<path d="M${f(x)} ${f(y)}L${f(x + dx)} ${f(y + dy)}L${f(x + dx + sk)} ${f(y + dy + th)}L${f(x + sk)} ${f(y + th)}Z" fill="url(#${gid})"/>`;
    s += `<path d="M${f(x + dx * .3)} ${f(y + dy * .3)}L${f(x + dx)} ${f(y + dy)}" stroke="#ffffff" stroke-opacity="${f(op * .8)}" stroke-width="2.5"/>`;
    return s;
  };
  for (let i = 0; i < 9; i++) b.push(slash(r(-300, 1800), r(-100, 1100), r(500, 1300), r(14, 70), -28, i % 3 === 0 ? '#3de0c0' : '#ff4655', r(.25, .7)));
  b.push(`<g ${c.blur(14)}>${slash(1300, 500, 1300, 120, -28, '#ff4655', .45)}${slash(-200, 980, 1100, 90, -28, '#3de0c0', .35)}</g>`);
  b.push(slash(1300, 500, 1300, 120, -28, '#ff4655', .85), slash(-200, 980, 1100, 90, -28, '#3de0c0', .7));
  // tech line-work: circuit traces, tick rulers and small labels
  let tech = '';
  for (let i = 0; i < 26; i++) { let x = r(0, W), y = r(0, H); let d = `M${f(x)} ${f(y)}`; for (let k = 0; k < 4; k++) { if (k % 2) y += r(-120, 120); else x += r(-200, 200); d += `L${f(x)} ${f(y)}`; } tech += `<path d="${d}" stroke="#f5f1f2" stroke-opacity=".1" stroke-width="1.5" fill="none"/><circle cx="${f(x)}" cy="${f(y)}" r="4" fill="none" stroke="#f5f1f2" stroke-opacity=".25"/>`; }
  for (let k = 0; k < 60; k++) tech += `<path d="M${80 + k * 14} ${H - 70}v${k % 5 ? -10 : -22}" stroke="#f5f1f2" stroke-opacity=".3" stroke-width="2"/>`;
  b.push(tech);
  b.push(`<text x="80" y="${H - 100}" font-family="Consolas, monospace" font-size="20" letter-spacing="6" fill="#f5f1f2" fill-opacity=".35">ROUND 13 // DEFEND</text>`);
  b.push(`<text x="${W - 80}" y="110" font-family="Consolas, monospace" font-size="20" letter-spacing="6" fill="#ff4655" fill-opacity=".6" text-anchor="end">// 0 4 : 2 7</text>`);
  // a crosshair, top left
  b.push(`<g stroke="#3de0c0" stroke-opacity=".6" stroke-width="3"><path d="M300 180v-40M300 220v40M280 200h-40M320 200h40"/></g><circle cx="300" cy="200" r="3" fill="#3de0c0"/>`);
  const plate = c.finish({ vignette: .55 });

  const shard = (col) => L.sprite(600, 80, L.lin('s', [[0, col, 0], [.7, col, .6], [1, col, .95]], 0, 0, 1, 0), `<path d="M0 30L560 0L600 40L40 80Z" fill="url(#s)"/><path d="M200 19L560 0" stroke="#ffffff" stroke-opacity=".9" stroke-width="3"/>`);
  const sprites = { red: shard('#ff4655'), teal: shard('#3de0c0'), glint: L.sprite(200, 12, L.lin('g', [[0, '#ffffff', 0], [.5, '#ffffff', 1], [1, '#ffffff', 0]], 0, 0, 1, 0), `<rect width="200" height="3" y="4.5" fill="url(#g)"/>`) };
  const dir = -28 * Math.PI / 180;
  const layers = [
    // shards slide along the slash direction, pointing the way they go
    { sprite: 'red', n: 4, x: [.1, .8], y: [.2, .9], size: [380, 620], rot: -28, move: { dx: Math.round(500 * Math.cos(dir)), dy: Math.round(500 * Math.sin(dir)), dur: [4, 7], travel: .35, ease: 'out' }, fade: [[0, 0], [.08, .9], [.3, .5], [.38, 0], [1, 0]], fps: 18 },
    { sprite: 'teal', n: 3, x: [0, .7], y: [.3, 1], size: [320, 520], rot: -28, move: { dx: Math.round(420 * Math.cos(dir)), dy: Math.round(420 * Math.sin(dir)), dur: [5, 8], travel: .35, ease: 'out' }, fade: [[0, 0], [.08, .8], [.3, .4], [.38, 0], [1, 0]], fps: 18 },
    { sprite: 'glint', n: 8, x: [0, 1], y: [0, 1], size: [140, 260], rot: -28, twinkle: { lo: 0, hi: 1, dur: [1.2, 2.6] } },
  ];
  return { key: 'radiant', scene: 'Shards', W, H, ax: .5, ay: .5, plate, sprites, layers };
}
build.key = 'radiant';
module.exports = build;
