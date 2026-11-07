'use strict';
const L = require('../lib');
const { f } = L;

// Cherry blossom: a spring dusk in plum and rose. A pale moon, misty hills and a still pond below; old dark cherry
// branches reach in from both top corners, loaded with clusters of five-petal blossoms. Petals tumble down on the
// breeze, turning as they fall.
function build() {
  const W = 2400, H = 1200, r = L.rng(3311);
  const c = L.canvas(W, H), b = c.b;
  b.push(`<rect width="${W}" height="${H}" fill="${c.g('sky', [[0, '#1a1024'], [.4, '#3a1b3e'], [.72, '#7a3a5e'], [1, '#2a1424']])}"/>`);
  b.push(c.glow(1180, 420, 520, '#ffd8e6', .3));
  b.push(`<circle cx="1180" cy="420" r="96" fill="${c.rg('mn', [[0, '#fff6f8'], [1, '#f0d0dc']], .4, .4, .6)}"/>`);
  b.push(`<rect width="${W}" height="700" fill="#000" ${c.cloud({ freq: .0024, oct: 4, seed: 21, color: L.rgb01('#e8a0c0'), contrast: 1.6, bias: -.85, stretchX: 4 })} opacity=".35"/>`);
  b.push(L.starfield(c.defs, r, { x1: W, y0: 0, y1: 500, n: 160, color: '#ffe6f0', bright: 6, maxR: 1.1 }));
  // misty hills and their reflection
  b.push(`<path d="${L.ridge(r, W, H, 760, 70)}" fill="#4a2440" opacity=".8"/>`, `<path d="${L.ridge(r, W, H, 820, 50)}" fill="#3a1a32"/>`);
  b.push(`<rect y="640" width="${W}" height="240" fill="${c.g('mist', [[0, '#f0b8d0', 0], [.7, '#f0b8d0', .25], [1, '#f0b8d0', .05]])}"/>`);
  b.push(`<rect y="870" width="${W}" height="${H - 870}" fill="${c.g('pond', [[0, '#5a2a48'], [1, '#160a14']])}"/>`);
  b.push(`<rect x="1130" y="870" width="100" height="330" fill="${c.g('mr', [[0, '#ffe6f0', .5], [1, '#ffe6f0', 0]])}" ${c.blur(10)}/>`);
  for (let i = 0; i < 120; i++) { const x = r(0, W), y = r(880, H); b.push(`<path d="M${f(x)} ${f(y)}h${f(r(20, 120))}" stroke="#ffd0e4" stroke-opacity="${f(r(.05, .2))}"/>`); }
  // fallen petals floating on the water
  for (let i = 0; i < 160; i++) { const x = r(0, W), y = r(900, H); b.push(`<ellipse cx="${f(x)}" cy="${f(y)}" rx="${f(r(3, 6))}" ry="${f(r(1.5, 3))}" fill="#ffc4d8" opacity="${f(r(.4, .8))}"/>`); }

  // branches: tapering, crooked limbs with twigs; blossoms clustered along the outer parts
  const blossoms = [];
  const limb = (x, y, ang, len, th, depth) => {
    let s = '', cx = x, cy = y, a = ang;
    const segs = 6;
    for (let k = 0; k < segs; k++) {
      a += r(-.35, .35);
      const nx = cx + Math.cos(a) * len / segs, ny = cy + Math.sin(a) * len / segs;
      const w0 = th * (1 - k / segs), w1 = th * (1 - (k + 1) / segs) + .8;
      s += `<path d="M${f(cx)} ${f(cy)}L${f(nx)} ${f(ny)}" stroke="#1a0a10" stroke-width="${f((w0 + w1) / 2)}" stroke-linecap="round"/>`;
      s += `<path d="M${f(cx)} ${f(cy - w0 * .3)}L${f(nx)} ${f(ny - w1 * .3)}" stroke="#5a3040" stroke-opacity=".5" stroke-width="${f(Math.max(1, w1 * .25))}" stroke-linecap="round"/>`;
      if (depth > 0 && k > 1 && r() < .55) s += limb(nx, ny, a + r(.5, 1) * (r() < .5 ? -1 : 1), len * r(.4, .65), w1, depth - 1);
      if (k >= 2) for (let q = 0; q < (depth === 0 ? 5 : 2); q++) blossoms.push([nx + r(-30, 30), ny + r(-24, 24), r(.7, 1.25)]);
      cx = nx; cy = ny;
    }
    return s;
  };
  b.push(limb(-40, 40, .35, 1100, 54, 2), limb(-40, 360, -.05, 700, 34, 2));
  b.push(limb(W + 40, 20, Math.PI - .4, 1000, 50, 2), limb(W + 40, 300, Math.PI + .05, 640, 30, 2));
  // blossoms: five petals with a notch, pink to white, a dark-rose centre with stamens
  c.defs.push(L.rad('pt', [[0, '#ffffff'], [.6, '#ffd0e0'], [1, '#f59ab4']], .5, .2, .9));
  c.defs.push(L.rad('pt2', [[0, '#fff6f8'], [.6, '#ffe0ea'], [1, '#ffb7c9']], .5, .2, .9));
  const flower = (x, y, s, rot) => {
    let p = `<g transform="translate(${f(x)} ${f(y)}) rotate(${f(rot)}) scale(${f(s)})">`;
    for (let k = 0; k < 5; k++) p += `<path transform="rotate(${k * 72})" d="M0 0C-9 -6 -10 -18 -4 -22L0 -19L4 -22C10 -18 9 -6 0 0Z" fill="url(#${r() < .5 ? 'pt' : 'pt2'})"/>`;
    p += `<circle r="3.2" fill="#c03a68"/>`;
    for (let k = 0; k < 7; k++) { const a = k / 7 * Math.PI * 2; p += `<path d="M0 0L${f(Math.cos(a) * 7)} ${f(Math.sin(a) * 7)}" stroke="#ffe08a" stroke-width=".8"/><circle cx="${f(Math.cos(a) * 7)}" cy="${f(Math.sin(a) * 7)}" r="1" fill="#ffe08a"/>`; }
    return p + '</g>';
  };
  let fl = '';
  for (const [x, y, s] of blossoms) { fl += c.glow(x, y, 30 * s, '#ffb7c9', .12); }
  for (const [x, y, s] of blossoms) fl += flower(x, y, s * 1.1, r(0, 72));
  // buds
  for (let i = 0; i < 80; i++) { const [x, y] = r.pick(blossoms); fl += `<ellipse cx="${f(x + r(-30, 30))}" cy="${f(y + r(-30, 30))}" rx="4" ry="6" fill="#e8508a"/>`; }
  b.push(fl);
  const plate = c.finish({ vignette: .55 });

  const petal = (seed, col) => L.sprite(40, 30, L.lin('p', [[0, '#ffffff'], [.6, col], [1, '#f07aa0']], 0, 0, 1, 1), `<path d="M4 15C8 2 26 0 36 10L32 15L36 20C26 30 8 28 4 15Z" fill="url(#p)"/><path d="M6 15Q20 14 32 15" stroke="#f07aa0" stroke-opacity=".4" stroke-width="1" fill="none"/>`);
  const sprites = { p1: petal(1, '#ffd0e0'), p2: petal(2, '#ffe6ee'), p3: petal(3, '#ffb7c9'), bloom: L.sprite(60, 60, c.defs.filter((d) => d.includes('id="pt')).join(''), flower(30, 30, 1.2, 0)) };
  const fall = { dx: [300, 700], dy: [1250, 1350], dur: [16, 26] };
  const layers = [
    { sprite: 'p1', n: 14, x: [-.15, .9], y: [-.1, -.02], size: [16, 26], op: [.75, 1], rot: [0, 360], move: fall, sway: { x: [30, 70], y: [6, 16], dur: [3, 5], dury: [2, 3.5] }, spin: { deg: [200, 400], dur: [5, 9], either: true } },
    { sprite: 'p2', n: 12, x: [-.15, .9], y: [-.1, -.02], size: [14, 22], op: [.7, 1], rot: [0, 360], move: fall, sway: { x: [30, 70], dur: [3, 5] }, spin: { deg: [200, 400], dur: [4, 8], either: true } },
    { sprite: 'p3', n: 10, x: [-.15, .9], y: [-.1, -.02], size: [18, 30], op: [.75, 1], rot: [0, 360], move: { dx: [400, 800], dy: [1300, 1400], dur: [11, 16] }, sway: { x: [40, 90], dur: [3, 5] }, spin: { deg: [300, 500], dur: [3, 6], either: true } },
    { sprite: 'bloom', n: 2, x: [.1, .8], y: [-.1, -.05], size: [40, 56], rot: [0, 72], move: { dx: [200, 400], dy: [1300, 1360], dur: [24, 32] }, sway: { x: [30, 60], dur: [4, 6] }, spin: { deg: 180, dur: [10, 16], either: true } },
  ];
  return { key: 'sakura', scene: 'Petals', W, H, ax: .5, ay: .4, plate, sprites, layers };
}
build.key = 'sakura';
module.exports = build;
