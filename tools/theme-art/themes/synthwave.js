'use strict';
const L = require('../lib');
const { f } = L;

// Synthwave: a striped neon sun sinking behind wireframe mountains, palm silhouettes, stars, and a glowing grid floor
// rushing toward us. The grid's cross lines are sprites that start at the horizon and speed up as they come closer.
function build() {
  const W = 2400, H = 1200, r = L.rng(1984);
  const c = L.canvas(W, H), b = c.b;
  const hz = 700;
  b.push(`<rect width="${W}" height="${hz}" fill="${c.g('sky', [[0, '#0f0524'], [.45, '#2c0a4a'], [.8, '#7a1a6a'], [1, '#ff4fa8']])}"/>`);
  b.push(L.starfield(c.defs, r, { x1: W, y0: 0, y1: 420, n: 380, color: '#ffe6fa', bright: 16, maxR: 1.2 }));
  // the sun with its stripes cut out toward the bottom
  const sx = 1200, sy = 470, sr = 260;
  c.defs.push(L.lin('sun', [[0, '#fff27a'], [.45, '#ffb02e'], [.75, '#ff5a8a'], [1, '#e8288a']]));
  let cut = '';
  for (let k = 0; k < 9; k++) { const y = sy + 20 + k * 28, hgt = 3 + k * 2.4; cut += `<rect x="${sx - sr}" y="${f(y)}" width="${sr * 2}" height="${f(hgt)}" fill="#000"/>`; }
  c.defs.push(`<mask id="sunm"><rect width="${W}" height="${H}" fill="#fff"/>${cut}</mask>`);
  b.push(c.glow(sx, sy, 700, '#ff4fa8', .5));
  b.push(`<circle cx="${sx}" cy="${sy}" r="${sr}" fill="url(#sun)" mask="url(#sunm)"/>`);
  // wireframe mountains: dark fill, neon ridge, a mesh of lines down the faces
  const mtn = (x0, x1, peakX, peakH, col, edge) => {
    const pts = [[x0, hz]];
    const n = 12;
    for (let k = 1; k < n; k++) { const x = x0 + (x1 - x0) * k / n; const d = 1 - Math.abs(x - peakX) / Math.max(peakX - x0, x1 - peakX); pts.push([x, hz - peakH * Math.max(0, d) * r(.75, 1.05)]); }
    pts.push([x1, hz]);
    let s = `<path d="M${pts.map((p) => `${f(p[0])} ${f(p[1])}`).join('L')}Z" fill="${col}"/>`;
    let mesh = '';
    for (let k = 1; k < pts.length - 1; k++) mesh += `M${f(pts[k][0])} ${f(pts[k][1])}L${f(peakX + (pts[k][0] - peakX) * 1.25)} ${hz}`;
    for (let q = 1; q < 4; q++) { mesh += `M${f(pts[0][0])} ${hz}`; for (let k = 1; k < pts.length; k++) mesh += `L${f(pts[k][0])} ${f(hz - (hz - pts[k][1]) * (1 - q / 4))}`; }
    s += `<path d="${mesh}" stroke="${edge}" stroke-opacity=".35" stroke-width="1.5" fill="none"/>`;
    s += `<path d="M${pts.map((p) => `${f(p[0])} ${f(p[1])}`).join('L')}" stroke="${edge}" stroke-width="3" fill="none"/>`;
    return s;
  };
  b.push(mtn(-100, 900, 380, 300, '#1a0630', '#ff4fd8'), mtn(1500, 2500, 2000, 340, '#1a0630', '#ff4fd8'), mtn(600, 1300, 900, 170, '#24083c', '#38e8ff'), mtn(1150, 1800, 1500, 200, '#24083c', '#38e8ff'));
  // the floor: dark, with the long lines running to the vanishing point and a glow at the horizon
  b.push(`<rect y="${hz}" width="${W}" height="${H - hz}" fill="${c.g('fl', [[0, '#2a0640'], [1, '#08020f']])}"/>`);
  let grid = '';
  for (let k = -40; k <= 40; k++) grid += `M${sx + k * 18} ${hz}L${sx + k * 260} ${H + 40}`;
  b.push(`<path d="${grid}" stroke="#ff4fd8" stroke-width="2.2" opacity=".8"/>`);
  for (let k = 1; k < 16; k++) { const y = hz + Math.pow(k / 15, 2.2) * (H - hz); b.push(`<path d="M0 ${f(y)}H${W}" stroke="#ff4fd8" stroke-width="${f(1 + k * .15)}" opacity=".25"/>`); }
  b.push(`<rect y="${hz - 6}" width="${W}" height="14" fill="#ffe6fa"/>`, `<rect y="${hz - 40}" width="${W}" height="90" fill="${c.g('hg', [[0, '#ff4fd8', 0], [.5, '#ff4fd8', .6], [1, '#ff4fd8', 0]])}" ${c.blur(10)}/>`);
  // palms
  const palm = (x, base, hgt, lean) => {
    let s = `<path d="M${x} ${base}Q${x + lean * .5} ${base - hgt * .5} ${x + lean} ${base - hgt}" stroke="#0a0214" stroke-width="16" fill="none" stroke-linecap="round"/>`;
    for (let k = 0; k < 9; k++) {
      const a = -Math.PI / 2 + (k - 4) * .38, len = r(140, 200), tx = x + lean, ty = base - hgt;
      const ex = tx + Math.cos(a) * len * 1.2, ey = ty + Math.sin(a) * len * .5 + len * .45;
      s += `<path d="M${f(tx)} ${f(ty)}Q${f(tx + Math.cos(a) * len * .7)} ${f(ty + Math.sin(a) * len * .7 - 30)} ${f(ex)} ${f(ey)}Q${f(tx + Math.cos(a) * len * .6)} ${f(ty + Math.sin(a) * len * .6 - 4)} ${f(tx)} ${f(ty + 10)}Z" fill="#0a0214"/>`;
    }
    return s;
  };
  b.push(palm(160, H + 20, 760, 120), palm(360, H + 20, 560, -60), palm(2240, H + 20, 820, -140), palm(2050, H + 20, 520, 70));
  let scan = ''; for (let y = 0; y < H; y += 5) scan += `M0 ${y}H${W}`;
  b.push(`<path d="${scan}" stroke="#000" stroke-opacity=".12"/>`);
  const plate = c.finish({ vignette: .55 });

  const sprites = {
    line: L.sprite(2400, 10, L.lin('l', [[0, '#ff4fd8', 0], [.15, '#ff4fd8', 1], [.85, '#ff4fd8', 1], [1, '#ff4fd8', 0]], 0, 0, 1, 0) + L.blurF('b', 2, 20), `<rect y="3" width="2400" height="4" fill="url(#l)" filter="url(#b)"/><rect y="4" width="2400" height="2" fill="#ffd0f4"/>`),
    sun: L.softDot(600, '#ff4fa8', { core: .2, coreOp: .5, mid: .2 }),
    shoot: L.sprite(260, 8, L.lin('m', [[0, '#ffffff', 0], [1, '#ffffff', 1]], 0, 0, 1, 0), `<rect y="2" width="260" height="3" rx="1.5" fill="url(#m)"/>`),
  };
  const layers = [
    { sprite: 'sun', at: [[sx, sy]], size: 900, pulse: { lo: .9, hi: 1.06, dur: 5 }, twinkle: { lo: .5, hi: .9, dur: 4 } },
    // cross lines: from the horizon to the bottom, slow at first and faster as they come (a perspective feel)
    { sprite: 'line', n: 8, at: Array.from({ length: 8 }, () => [1200, hz + 4]), size: 2400, move: { dx: 0, dy: H - hz + 20, dur: 4, ease: 'in' }, grow: [1, 1.6], phase: 0, phaseStep: .125, fade: [[0, 0], [.1, .8], [1, 1]], fps: 18 },
    { sprite: 'shoot', n: 2, x: [.1, .6], y: [.05, .25], size: [180, 260], rot: 12, move: { dx: 700, dy: 149, dur: [8, 12], travel: .1 }, fade: [[0, 0], [.02, 1], [.1, 0], [1, 0]], fps: 24 },
  ];
  return { key: 'synthwave', scene: 'Synth', W, H, ax: .5, ay: .7, plate, sprites, layers };
}
build.key = 'synthwave';
module.exports = build;
