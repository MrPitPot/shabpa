'use strict';
const L = require('../lib');
const { f } = L;

// Snowfall: a quiet winter night in the mountains. Moonlight through thin cloud, a valley of snow-laden firs in blue
// layers, a log cabin with warm windows and smoke. Snow falls in three depths: fine and slow far away, big and soft near.
function build() {
  const W = 2400, H = 1200, r = L.rng(3131);
  const c = L.canvas(W, H), b = c.b;
  b.push(`<rect width="${W}" height="${H}" fill="${c.g('sky', [[0, '#050c16'], [.4, '#0e2136'], [.75, '#1d3a58'], [1, '#2a4a68']])}"/>`);
  b.push(c.glow(1650, 210, 700, '#a8d4ff', .22));
  b.push(`<circle cx="1650" cy="210" r="58" fill="${c.rg('mn', [[0, '#ffffff'], [.8, '#e6f2ff'], [1, '#c4daf0']], .4, .4, .6)}"/>`);
  b.push(c.glow(1650, 210, 120, '#e6f2ff', .5));
  // thin clouds over the moon
  b.push(`<rect width="${W}" height="600" fill="#000" ${c.cloud({ freq: .0025, oct: 5, seed: 12, color: L.rgb01('#8fb0d0'), contrast: 1.6, bias: -.75, stretchX: 3 })} opacity=".5"/>`);
  b.push(L.starfield(c.defs, r, { x1: W, y0: 0, y1: 520, n: 300, color: '#e6f2ff', bright: 10, maxR: 1.2 }));
  // mountains: jagged ridges, faces toward the moon lit, snow above a ragged line
  b.push(L.mountains(c, r, { base: 720, amp: 340, rough: .5, detail: 6, light: 1, rock: ['#132234', '#34506c'], snow: ['#58718e', '#dceafa'], snowline: .5 }).svg);
  b.push(`<rect y="420" width="${W}" height="420" fill="${c.g('m1', [[0, '#a8c8e8', 0], [1, '#a8c8e8', .28]])}"/>`);
  b.push(L.mountains(c, r, { base: 860, amp: 170, rough: .5, detail: 6, light: 1, rock: ['#0f1e30', '#24384e'], snow: ['#5a7898', '#b4cce4'], snowline: .55 }).svg);
  b.push(`<rect y="560" width="${W}" height="300" fill="${c.g('mist', [[0, '#a8c8e8', 0], [.6, '#a8c8e8', .2], [1, '#a8c8e8', 0]])}"/>`);
  // forest layers
  const forest = (base, amp, hMin, hMax, col, snow, gap, seed) => {
    const R = L.rng(seed), fn = L.ridgeFn(R, base, amp);
    let s = `<path d="${L.ridgePath(fn, W, H)}" fill="${col}"/>`;
    for (let x = -30; x < W + 30; x += R(gap * .5, gap)) { const y = fn(x) + R(0, 30); s += L.pine(R, x, y, R(hMin, hMax), { color: col, tiers: 10, snow }); }
    return s;
  };
  b.push(forest(800, 40, 50, 110, '#1a3048', '#9ab8d4', 22, 5));
  b.push(forest(900, 50, 80, 170, '#132539', '#c4d8ec', 30, 6));
  // the snowfield with a cabin
  const field = L.ridgeFn(r, 1010, 30, [[.0015, 1], [.006, .3]]);
  b.push(`<path d="${L.ridgePath(field, W, H)}" fill="${c.g('fd', [[0, '#9ab8d0'], [.3, '#6a8aa8'], [1, '#2a3e54']])}"/>`);
  const cx = 760, cy = field(760) + 6;
  b.push(c.glow(cx + 60, cy - 40, 340, '#ffb35c', .18));
  b.push(`<rect x="${cx - 110}" y="${cy - 110}" width="230" height="110" fill="#3a2418"/>`);
  for (let k = 0; k < 9; k++) b.push(`<rect x="${cx - 112}" y="${cy - 110 + k * 12.2}" width="234" height="11" rx="5.5" fill="${L.mix('#4a2e1c', '#2a180e', r())}"/>`);
  b.push(`<path d="M${cx - 140} ${cy - 104}L${cx + 5} ${cy - 210}L${cx + 150} ${cy - 104}Z" fill="#2a1a12"/>`);
  b.push(`<path d="M${cx - 148} ${cy - 100}Q${cx - 60} ${cy - 160} ${cx + 5} ${cy - 222}Q${cx + 70} ${cy - 160} ${cx + 158} ${cy - 100}Q${cx + 120} ${cy - 88} ${cx + 60} ${cy - 96}Q${cx} ${cy - 84} ${cx - 60} ${cy - 96}Q${cx - 110} ${cy - 86} ${cx - 148} ${cy - 100}Z" fill="${c.g('rf', [[0, '#f4faff'], [1, '#a8c0d8']])}"/>`);
  b.push(`<rect x="${cx + 70}" y="${cy - 200}" width="22" height="60" fill="#2a1a12"/><rect x="${cx + 66}" y="${cy - 206}" width="30" height="10" rx="4" fill="#e6f2ff"/>`);
  for (const wx of [cx - 80, cx + 40]) {
    b.push(`<rect x="${wx - 4}" y="${cy - 84}" width="48" height="48" fill="#1a0e08"/><rect x="${wx}" y="${cy - 80}" width="40" height="40" fill="${c.g('wn', [[0, '#ffe7a6'], [1, '#ff9a3a']])}"/><path d="M${wx + 20} ${cy - 80}v40M${wx} ${cy - 60}h40" stroke="#3a1e0e" stroke-width="3"/>`);
    b.push(c.glow(wx + 20, cy - 60, 90, '#ffc46b', .5));
    b.push(`<path d="M${wx - 20} ${cy + 4}L${wx + 60} ${cy + 4}L${wx + 110} ${cy + 120}L${wx - 70} ${cy + 120}Z" fill="#ffb35c" opacity=".12" ${c.blur(14)}/>`);
  }
  b.push(`<path d="M${cx - 116} ${cy}Q${cx} ${cy - 18} ${cx + 126} ${cy}L${cx + 140} ${cy + 10}L${cx - 130} ${cy + 10}Z" fill="#dbe8f4"/>`);
  // a fence half buried and footprints toward the door
  for (let k = 0; k < 14; k++) { const x = 1050 + k * 46, y = field(1050 + k * 46); b.push(`<rect x="${x}" y="${f(y - 40)}" width="8" height="46" fill="#2a1a12"/><path d="M${x - 4} ${f(y - 42)}q8 -8 16 0z" fill="#e6f2ff"/>`); }
  b.push(`<path d="M1046 ${f(field(1046) - 30)}L1700 ${f(field(1700) - 30)}M1046 ${f(field(1046) - 16)}L1700 ${f(field(1700) - 16)}" stroke="#2a1a12" stroke-width="4"/>`);
  for (let k = 0; k < 16; k++) { const x = 900 + k * 34, y = field(900 + k * 34) + 40 + k * 6; b.push(`<ellipse cx="${x}" cy="${f(y + (k % 2) * 10)}" rx="8" ry="4" fill="#3a5068" opacity=".5"/>`); }
  // snow resting in the foreground with sparkles
  b.push(`<path d="${L.ridgePath(L.ridgeFn(r, 1150, 25), W, H)}" fill="${c.g('fg', [[0, '#b8cfe4'], [1, '#4a6480']])}"/>`);
  for (let i = 0; i < 320; i++) b.push(`<circle cx="${f(r(0, W))}" cy="${f(r(1020, H))}" r="${f(r(.6, 1.8))}" fill="#fff" opacity="${f(r(.3, 1))}"/>`);
  const plate = c.finish({ vignette: .5 });

  const sprites = {
    fine: L.softDot(10, '#f2f8ff', { core: .5, mid: .8 }),
    flake: L.softDot(18, '#f2f8ff', { core: .45, mid: .7 }),
    near: L.softDot(48, '#f2f8ff', { core: .25, coreOp: .8, mid: .45 }),
    smoke: L.softDot(140, '#c8d6e4', { core: .3, coreOp: .45, mid: .2 }),
  };
  const layers = [
    { sprite: 'smoke', at: [[cx + 81, cy - 220], [cx + 81, cy - 220], [cx + 81, cy - 220]], size: [50, 70], op: .6, move: { dx: [-60, -20], dy: [-260, -200], dur: [8, 11] }, fade: [[0, 0], [.15, .8], [1, 0]], grow: [.5, 2.4] },
    { sprite: 'fine', n: 70, x: [0, 1], y: [-.05, 0], size: [4, 7], op: [.4, .8], move: { dx: [-60, 20], dy: [1250, 1260], dur: [22, 34] }, sway: { x: [10, 24], dur: [4, 7] } },
    { sprite: 'flake', n: 46, x: [0, 1], y: [-.05, 0], size: [8, 13], op: [.6, .95], move: { dx: [-80, 30], dy: [1260, 1280], dur: [13, 20] }, sway: { x: [18, 40], dur: [3.5, 6] } },
    { sprite: 'near', n: 12, x: [0, 1], y: [-.08, -.04], size: [24, 44], op: [.4, .7], move: { dx: [-120, 40], dy: [1300, 1340], dur: [7, 11] }, sway: { x: [30, 60], dur: [3, 5] } },
  ];
  return { key: 'snowfall', scene: 'Snow', W, H, ax: .5, ay: .8, plate, sprites, layers };
}
build.key = 'snowfall';
module.exports = build;
