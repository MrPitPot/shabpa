'use strict';
const L = require('../lib');
const { f } = L;

// Cyrus the Great: the tomb at Pasargadae under a royal lapis night, six great stone steps and the gabled chamber on
// top, warm torchlight on the stone, the plain and the far mountains in moonlight. A golden winged disc, the old
// Achaemenid emblem, breathes softly in the sky; gold motes rise.
function build() {
  const W = 2400, H = 1200, r = L.rng(5300);
  const c = L.canvas(W, H), b = c.b;
  b.push(`<rect width="${W}" height="${H}" fill="${c.g('sky', [[0, '#040a20'], [.4, '#0e1c4e'], [.7, '#1c3070'], [.85, '#2c3a6a'], [1, '#0a1028']])}"/>`);
  b.push(L.starfield(c.defs, r, { x1: W, y0: 0, y1: 760, n: 700, color: '#ffe8b0', bright: 30, maxR: 1.3 }));
  b.push(c.glow(1200, 300, 700, '#3d6be0', .18));
  // the far mountains and the plain
  b.push(L.mountains(c, r, { base: 820, amp: 160, rough: .5, detail: 6, light: -1, rock: ['#101a40', '#1c2a5a'], snow: ['#1c2a5a', '#2c3c74'], snowline: .3, bottom: 860 }).svg);
  b.push(`<rect y="760" width="${W}" height="140" fill="${c.g('hz', [[0, '#5a6ab0', 0], [1, '#5a6ab0', .25]])}"/>`);
  b.push(`<rect y="860" width="${W}" height="340" fill="${c.g('pl', [[0, '#1a2448'], [1, '#070b18']])}"/>`);
  for (let i = 0; i < 500; i++) { const x = r(0, W), y = r(870, H); b.push(`<path d="M${f(x)} ${f(y)}l${f(r(-3, 3))} ${f(-r(4, 14) * (y / 900))}" stroke="${r.pick(['#2a3660', '#3a4670', '#1a2448'])}" stroke-width="1.6" opacity=".5"/>`); }
  // the tomb: six steps, each a band of big blocks lit warm from the left (torches) and cool from the moon
  const cx = 1200, baseY = 1010;
  c.defs.push(L.lin('st', [[0, '#e8b870'], [.35, '#b88a5a'], [.7, '#6a5a70'], [1, '#3a3a5a']], 0, 0, 1, 0));
  const steps = [[560, 70], [480, 58], [410, 52], [350, 46], [296, 40], [250, 34]];
  let y = baseY;
  for (const [hw, sh] of steps) {
    b.push(`<rect x="${cx - hw}" y="${y - sh}" width="${hw * 2}" height="${sh}" fill="url(#st)"/>`);
    b.push(`<rect x="${cx - hw}" y="${y - sh}" width="${hw * 2}" height="5" fill="#ffe0a0" opacity=".45"/>`);
    for (let x = cx - hw + r(30, 80); x < cx + hw - 20; x += r(70, 130)) b.push(`<path d="M${f(x)} ${y - sh + 5}V${y}" stroke="#2a2030" stroke-opacity=".45" stroke-width="2"/>`);
    b.push(`<rect x="${cx - hw}" y="${y - 10}" width="${hw * 2}" height="10" fill="#000" opacity=".25"/>`);
    y -= sh;
  }
  // the chamber with its gabled roof and narrow door
  const chW = 200, chH = 150;
  b.push(`<rect x="${cx - chW / 2}" y="${y - chH}" width="${chW}" height="${chH}" fill="url(#st)"/>`);
  for (let k = 1; k < 4; k++) b.push(`<path d="M${cx - chW / 2} ${y - chH + k * chH / 4}h${chW}" stroke="#2a2030" stroke-opacity=".4" stroke-width="2"/>`);
  b.push(`<path d="M${cx - chW / 2 - 24} ${y - chH}L${cx} ${y - chH - 92}L${cx + chW / 2 + 24} ${y - chH}Z" fill="url(#st)"/>`);
  b.push(`<path d="M${cx - chW / 2 - 24} ${y - chH}L${cx} ${y - chH - 92}" stroke="#ffe0a0" stroke-opacity=".6" stroke-width="3"/>`);
  b.push(`<path d="M${cx - chW / 2 - 30} ${y - chH + 4}H${cx + chW / 2 + 30}" stroke="#3a3a5a" stroke-width="10"/>`);
  b.push(`<rect x="${cx - 26}" y="${y - 120}" width="52" height="120" fill="#0a0812"/>`);
  b.push(c.glow(cx, y - 60, 60, '#ffb347', .3));
  // torches on both sides with warm pools of light
  for (const tx of [cx - 640, cx + 640]) {
    b.push(c.glow(tx, 900, 360, '#ffb347', .35));
    b.push(`<rect x="${tx - 5}" y="900" width="10" height="110" fill="#2a1a10"/><path d="M${tx - 14} 900h28l-6 -14h-16z" fill="#4a3018"/>`);
    b.push(`<path d="M${tx} 840Q${tx - 18} 870 ${tx - 8} 888Q${tx} 896 ${tx + 8} 888Q${tx + 18} 870 ${tx} 840Z" fill="#ffd27a"/>`);
  }
  b.push(c.glow(cx, 920, 760, '#ffb347', .14));
  const plate = c.finish({ vignette: .55 });

  // the winged disc: a ring with a sun inside, three tiers of feathers each side and a feathered tail
  let wing = '';
  for (const d of [-1, 1]) for (let t = 0; t < 3; t++) {
    const y0 = -18 + t * 14, len = 260 - t * 50;
    for (let q = 0; q < 9 - t * 2; q++) {
      const x0 = d * (54 + q * len / (9 - t * 2)), x1 = d * (54 + (q + 1) * len / (9 - t * 2));
      wing += `<path d="M${f(x0)} ${f(y0 + q * 1.5)}L${f(x1)} ${f(y0 - 6 + q * 1.2)}L${f(x1 - d * 6)} ${f(y0 + 10 + q * 1.5)}L${f(x0)} ${f(y0 + 12 + q * 1.5)}Z" fill="url(#gd)" stroke="#8a6014" stroke-width="1"/>`;
    }
  }
  let tail = '';
  for (let q = -3; q <= 3; q++) tail += `<path d="M0 46L${q * 14 - 7} 120L${q * 14 + 7} 120Z" fill="url(#gd)" stroke="#8a6014" stroke-width="1"/>`;
  const disc = L.sprite(640, 300, L.lin('gd', [[0, '#fff2b8'], [.5, '#f0c75e'], [1, '#b08020']], 0, 0, 0, 1) + L.rad('gl', [[0, '#ffe08a', .35], [1, '#ffe08a', 0]]),
    `<ellipse cx="320" cy="140" rx="320" ry="150" fill="url(#gl)"/><g transform="translate(320 120)">${wing}${tail}<circle r="44" fill="none" stroke="url(#gd)" stroke-width="9"/><circle r="28" fill="url(#gd)"/><circle r="14" fill="#fff6d0"/></g>`);
  const sprites = { disc, mote: L.softDot(14, '#ffe08a', { core: .3 }), torch: L.softDot(200, '#ffb347', { core: .2, coreOp: .6, mid: .25 }) };
  const layers = [
    { sprite: 'disc', at: [[1200, 290]], size: 560, op: .55, pulse: { lo: .96, hi: 1.04, dur: 6 }, twinkle: { lo: .55, hi: 1, dur: 6 } },
    { sprite: 'torch', at: [[560, 870], [1840, 870]], size: 200, twinkle: { lo: .5, hi: 1, dur: [.3, .5] }, pulse: { lo: .9, hi: 1.08, dur: [.4, .7] } },
    { sprite: 'mote', n: 34, x: [.2, .8], y: [.85, 1], size: [5, 11], op: [.5, 1], move: { dx: [-80, 80], dy: [-700, -400], dur: [12, 20] }, fade: [[0, 0], [.2, 1], [1, 0]], sway: { x: [10, 30], dur: [3, 6] } },
  ];
  return { key: 'cyrus', scene: 'Cyrus', W, H, ax: .5, ay: .8, plate, sprites, layers };
}
build.key = 'cyrus';
module.exports = build;
