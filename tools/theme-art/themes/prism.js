'use strict';
const L = require('../lib');
const { f } = L;

// Prism: a cluster of clear quartz crystals growing out of dark rock, lit from above. Every facet catches a different
// tint, light splits into rainbow caustics thrown across the dark wall behind. Glints sparkle on the edges and the
// rainbow patches breathe and drift.
function build() {
  const W = 2400, H = 1200, r = L.rng(9191);
  const c = L.canvas(W, H), b = c.b;
  b.push(`<rect width="${W}" height="${H}" fill="${c.g('bg', [[0, '#0d0f1c'], [.6, '#0a0c16'], [1, '#05060a']])}"/>`);
  b.push(c.glow(1300, 300, 900, '#5a4ab0', .18));
  b.push(c.glow(700, 500, 600, '#1f8a8a', .12));
  // rainbow caustics on the back wall: soft spectral bands
  const spectrum = [[0, '#ff4d6a'], [.18, '#ffb347'], [.36, '#ffe96a'], [.54, '#5eeaa4'], [.72, '#5ea8ff'], [.9, '#a78bfa'], [1, '#a78bfa']];
  for (let i = 0; i < 7; i++) {
    const x = r(200, W - 200), y = r(80, 700), len = r(300, 700), wdt = r(26, 60), ang = r(-40, 40);
    const gid = L.id('sp');
    c.defs.push(L.lin(gid, spectrum.map(([o, col]) => [o, col, .55]), 0, 0, 0, 1));
    b.push(`<g transform="rotate(${f(ang)} ${f(x)} ${f(y)})" opacity="${f(r(.12, .28))}"><rect x="${f(x - len / 2)}" y="${f(y - wdt / 2)}" width="${f(len)}" height="${f(wdt)}" rx="${f(wdt / 2)}" fill="url(#${gid})" ${c.blur(r(14, 26))}/></g>`);
  }
  // dust in the light
  for (let i = 0; i < 260; i++) b.push(`<circle cx="${f(r(0, W))}" cy="${f(r(0, 900))}" r="${f(r(.5, 1.6))}" fill="#dfe6ff" opacity="${f(r(.1, .5))}"/>`);
  // the rock bed
  b.push(`<path d="${L.ridge(r, W, H, 1010, 50, { waves: [[.002, 1], [.007, .4], [.02, .2]], jag: 6 })}" fill="${c.g('rk', [[0, '#25233a'], [1, '#08080e']])}"/>`);
  // crystals: hexagonal prisms with a pointed termination; three visible faces shaded differently
  const tints = [['#d8f6ff', '#7fd6e8', '#2a5a7a'], ['#efe2ff', '#a78bfa', '#3a2e6a'], ['#e0fff4', '#5eead4', '#1f5a52'], ['#fff0f6', '#ff9ac8', '#5a2a48']];
  const crystal = (x, y, len, wdt, ang, tint) => {
    const [hi, mid, lo] = tint;
    const tip = len * .22, hw = wdt / 2;
    let s = `<g transform="translate(${f(x)} ${f(y)}) rotate(${f(ang)})">`;
    s += `<ellipse cx="0" cy="-${f(len * .5)}" rx="${f(wdt * 1.6)}" ry="${f(len * .7)}" fill="${mid}" opacity=".12" ${c.blur(30)}/>`;
    // left face, middle face, right face
    s += `<path d="M${-hw} 0L${-hw} ${-len + tip}L${-hw * .35} ${-len - tip * .1}L${-hw * .35} 0Z" fill="${c.g('lf', [[0, lo], [1, mid]], 0, 1, 0, 0)}" opacity=".9"/>`;
    s += `<path d="M${-hw * .35} 0L${-hw * .35} ${-len - tip * .1}L${hw * .35} ${-len - tip * .1}L${hw * .35} 0Z" fill="${c.g('mf', [[0, mid], [.6, hi], [1, '#ffffff']], 0, 1, 0, 0)}" opacity=".75"/>`;
    s += `<path d="M${hw * .35} 0L${hw * .35} ${-len - tip * .1}L${hw} ${-len + tip}L${hw} 0Z" fill="${c.g('rf', [[0, lo], [1, lo]], 0, 1, 0, 0)}" opacity=".85"/>`;
    // the point: three facets meeting at the apex
    s += `<path d="M${-hw} ${-len + tip}L0 ${-len - tip * 1.6}L${-hw * .35} ${-len - tip * .1}Z" fill="${mid}" opacity=".9"/>`;
    s += `<path d="M${-hw * .35} ${-len - tip * .1}L0 ${-len - tip * 1.6}L${hw * .35} ${-len - tip * .1}Z" fill="#ffffff" opacity=".85"/>`;
    s += `<path d="M${hw * .35} ${-len - tip * .1}L0 ${-len - tip * 1.6}L${hw} ${-len + tip}Z" fill="${lo}" opacity=".9"/>`;
    // inner reflections and edge highlights
    s += `<path d="M${-hw * .2} ${-len * .2}L${hw * .1} ${-len * .75}" stroke="#ffffff" stroke-opacity=".35" stroke-width="${f(wdt * .05)}"/>`;
    s += `<path d="M${-hw * .35} 0V${-len - tip * .1}M${hw * .35} 0V${-len - tip * .1}" stroke="#ffffff" stroke-opacity=".6" stroke-width="1.6"/>`;
    s += `<path d="M${-hw} ${-len + tip}L0 ${-len - tip * 1.6}L${hw} ${-len + tip}" stroke="#ffffff" stroke-opacity=".8" stroke-width="1.4" fill="none"/>`;
    for (let k = 0; k < 4; k++) s += `<path d="M${f(r(-hw * .3, hw * .3))} ${f(-r(len * .1, len * .8))}h${f(r(6, hw * .5))}" stroke="#ffffff" stroke-opacity=".25" stroke-width="1"/>`;
    return s + '</g>';
  };
  const tips = [];
  const cluster = (cx, cy, n, scale) => {
    const list = [];
    for (let i = 0; i < n; i++) list.push({ x: cx + r(-160, 160) * scale, y: cy + r(-10, 20), len: r(160, 420) * scale, wdt: r(50, 100) * scale, ang: r(-38, 38), t: r.pick(tints) });
    list.sort((a, b2) => a.len - b2.len).reverse().forEach((k) => {
      b.push(crystal(k.x, k.y, k.len, k.wdt, k.ang, k.t));
      const a = k.ang * Math.PI / 180, tl = k.len * 1.35;
      tips.push([Math.round(k.x + Math.sin(a) * tl), Math.round(k.y - Math.cos(a) * tl)]);
    });
  };
  cluster(520, 1060, 11, 1.05);
  cluster(1900, 1050, 9, .95);
  cluster(1220, 1110, 6, .6);
  // light on the rock around the crystals
  b.push(c.glow(520, 1060, 420, '#7fd6e8', .18), c.glow(1900, 1050, 380, '#a78bfa', .18));
  const plate = c.finish({ vignette: .6 });

  const sparkle = L.sprite(80, 80, L.rad('g', [[0, '#ffffff', 1], [.12, '#ffffff', .8], [.4, '#c9d6ff', .2], [1, '#c9d6ff', 0]]),
    `<circle cx="40" cy="40" r="40" fill="url(#g)"/><path d="M40 0L43 37L80 40L43 43L40 80L37 43L0 40L37 37Z" fill="#ffffff" opacity=".9"/>`);
  const rainbow = L.sprite(500, 120, L.lin('s', spectrum.map(([o, col]) => [o, col, .5]), 0, 0, 0, 1) + L.blurF('b', 14, 30), `<rect x="30" y="20" width="440" height="80" rx="40" fill="url(#s)" filter="url(#b)"/>`);
  const sprites = { sparkle, rainbow, mote: L.softDot(16, '#e6ecff', { core: .3 }) };
  const layers = [
    { sprite: 'rainbow', n: 4, x: [.08, .92], y: [.08, .6], size: [300, 480], rot: [-35, 35], op: [.2, .4], squash: .6, twinkle: { lo: .1, hi: 1, dur: [5, 9] }, sway: { x: [30, 70], y: [10, 30], dur: [12, 18], dury: [9, 14] }, spin: { deg: [4, 8], dur: [14, 22], back: true } },
    { sprite: 'mote', n: 30, x: [0, 1], y: [.05, .8], size: [5, 10], op: [.3, .7], move: { dx: [-80, 80], dy: [-160, -60], dur: [16, 26] }, fade: [[0, 0], [.2, 1], [.8, 1], [1, 0]] },
    { sprite: 'sparkle', at: tips, size: [40, 70], twinkle: { lo: 0, hi: 1, dur: [1.6, 3.4] }, spin: { deg: 45, dur: [6, 10], back: true } },
  ];
  return { key: 'prism', scene: 'Prism', W, H, ax: .5, ay: 1, plate, sprites, layers };
}
build.key = 'prism';
module.exports = build;
