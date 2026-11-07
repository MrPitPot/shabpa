'use strict';
const L = require('../lib');
const { f } = L;

// The deep: looking along a reef wall into blue water. God rays fall from the bright surface, kelp sways in silhouette,
// coral and rocks on the floor, particles hang in the water. Fish swim past head first, bubbles wobble up and a pair of
// jellyfish pulse slowly upward.
function build() {
  const W = 2400, H = 1200, r = L.rng(2020);
  const c = L.canvas(W, H), b = c.b;
  b.push(`<rect width="${W}" height="${H}" fill="${c.g('w', [[0, '#1a8ab0'], [.25, '#0a5a80'], [.6, '#04304a'], [1, '#020e18']])}"/>`);
  // the surface seen from below: rippled bright band
  b.push(`<rect width="${W}" height="140" fill="#000" ${c.cloud({ freq: .012, oct: 3, seed: 4, color: L.rgb01('#bff4ff'), contrast: 2.4, bias: -1, stretchX: 2.5 })} opacity=".55"/>`);
  b.push(c.glow(1300, -80, 700, '#d8faff', .45));
  // god rays
  for (let k = 0; k < 10; k++) { const x = 900 + k * 90 + r(-40, 40), w0 = r(30, 90), sk = r(-300, 200); b.push(`<path d="M${f(x)} -10L${f(x + w0)} -10L${f(x + w0 * 3 + sk)} ${H}L${f(x + sk - w0)} ${H}Z" fill="${c.g('ray', [[0, '#c8f4ff', .28], [.7, '#c8f4ff', .04], [1, '#c8f4ff', 0]])}" ${c.blur(14)}/>`); }
  // distant reef shapes in haze
  b.push(`<path d="${L.ridge(r, W, H, 900, 120, { waves: [[.003, 1], [.009, .4], [.03, .15]], jag: 4 })}" fill="#063a54" opacity=".8"/>`);
  b.push(`<rect y="600" width="${W}" height="400" fill="${c.g('hz', [[0, '#0a5a80', 0], [1, '#0a5a80', .5]])}"/>`);
  // near floor: rocks, coral fans, sea anemones
  b.push(`<path d="${L.ridge(r, W, H, 1060, 50, { waves: [[.004, 1], [.012, .4]], jag: 6 })}" fill="${c.g('fl', [[0, '#0a2a38'], [1, '#02080c']])}"/>`);
  const fan = (x, y, s, col) => {
    let p = '';
    const branch = (bx, by, a, len, d) => { const ex = bx + Math.cos(a) * len, ey = by + Math.sin(a) * len; p += `M${f(bx)} ${f(by)}L${f(ex)} ${f(ey)}`; if (d > 0) { branch(ex, ey, a - r(.2, .5), len * .75, d - 1); branch(ex, ey, a + r(.2, .5), len * .75, d - 1); } };
    branch(x, y, -Math.PI / 2, 60 * s, 5);
    return `<path d="${p}" stroke="${col}" stroke-width="${f(2.2 * s)}" stroke-linecap="round" fill="none" opacity=".85"/>`;
  };
  for (const [x, s, col] of [[260, 1.6, '#c84a6a'], [640, 1.1, '#e8803a'], [1580, 1.4, '#a84ac0'], [2050, 1.8, '#d85a5a'], [1180, .9, '#e8a03a']]) b.push(fan(x, 1080, s, col));
  for (let i = 0; i < 14; i++) { const x = r(0, W), y = r(1060, 1150), s = r(.6, 1.3); let p = ''; for (let k = 0; k < 12; k++) { const a = -Math.PI / 2 + (k - 5.5) * .14; p += `<path d="M${f(x)} ${f(y)}q${f(Math.cos(a) * 20 * s)} ${f(-30 * s)} ${f(Math.cos(a) * 34 * s)} ${f(Math.sin(a) * 50 * s)}" stroke="${r.pick(['#ff8ab8', '#ffb06a', '#8af0d0'])}" stroke-width="3" fill="none" stroke-linecap="round"/>`; } b.push(p); }
  // kelp: tall wavy ribbons with leaves, dark against the light
  const kelp = (x, hgt, col) => {
    let d = `M${f(x)} ${H + 10}`, cx = x, leaves = '';
    for (let y = H; y > H - hgt; y -= 40) { cx += r(-14, 14); d += `L${f(cx)} ${f(y)}`; const s = r() < .5 ? -1 : 1; leaves += `<path d="M${f(cx)} ${f(y)}q${f(s * 30)} -10 ${f(s * 46)} -40q${f(-s * 20)} 10 ${f(-s * 46)} 30z" fill="${col}"/>`; }
    return `<path d="${d}" stroke="${col}" stroke-width="6" fill="none"/>${leaves}`;
  };
  for (const [x, hh] of [[80, 900], [160, 700], [2280, 980], [2180, 760], [2350, 600], [420, 500]]) b.push(kelp(x, hh, '#03180f'));
  // suspended particles
  for (let i = 0; i < 400; i++) b.push(`<circle cx="${f(r(0, W))}" cy="${f(r(0, H))}" r="${f(r(.5, 2))}" fill="#c8f4ff" opacity="${f(r(.08, .4))}"/>`);
  const plate = c.finish({ vignette: .6 });

  // fish drawn facing right; a school of small ones and a few big silhouettes
  const fish = (col, belly, stripe) => L.sprite(120, 60, L.lin('fb', [[0, col], [.6, col], [1, belly]], 0, 0, 0, 1),
    `<path d="M14 30C30 8 74 6 100 24Q112 30 100 36C74 54 30 52 14 30Z" fill="url(#fb)"/><path d="M18 30L2 12Q8 30 2 48Z" fill="${col}"/><path d="M58 12Q66 2 78 12Z" fill="${col}" opacity=".9"/>${stripe ? `<path d="M50 12Q56 30 50 48M70 12Q76 30 70 48" stroke="${stripe}" stroke-width="5" opacity=".8" fill="none"/>` : ''}<circle cx="92" cy="27" r="3" fill="#02080c"/><circle cx="93" cy="26" r="1" fill="#fff"/>`);
  const sprites = {
    yellow: fish('#ffd25e', '#fff0b0', '#2a3a5a'), blue: fish('#4fd1e8', '#d8faff', null), shadow: fish('#03222e', '#063a50', null),
    bubble: L.sprite(30, 30, L.rad('bb', [[0, '#ffffff', .05], [.8, '#c8f4ff', .25], [1, '#ffffff', .8]]), `<circle cx="15" cy="15" r="14" fill="url(#bb)"/><circle cx="10" cy="10" r="3" fill="#fff" opacity=".9"/>`),
    jelly: L.sprite(120, 220, L.rad('jb', [[0, '#ffd0f4', .7], [.7, '#c88ae8', .45], [1, '#c88ae8', .1]], .5, .3, .7) + L.blurF('jg', 6, 40),
      `<ellipse cx="60" cy="50" rx="56" ry="40" fill="#e0a0ff" opacity=".25" filter="url(#jg)"/><path d="M6 60Q6 10 60 8Q114 10 114 60Q100 54 86 62Q74 54 60 62Q46 54 34 62Q20 54 6 60Z" fill="url(#jb)"/>${[20, 38, 56, 72, 90].map((x, i) => `<path d="M${x} 60q${i % 2 ? 10 : -10} 40 0 80t0 70" stroke="#f0c0ff" stroke-opacity=".5" stroke-width="2" fill="none"/>`).join('')}`),
  };
  const layers = [
    { sprite: 'shadow', n: 3, x: [-.25, -.1], y: [.35, .7], size: [260, 420], op: [.5, .8], move: { dx: [2900, 3100], dy: [-60, 60], dur: [40, 60] }, wag: { deg: 3, dur: 1.6 } },
    { sprite: 'jelly', n: 2, x: [.6, .85], y: [.8, 1], size: [90, 140], op: [.7, .9], move: { dx: [-60, 60], dy: [-1100, -1000], dur: [70, 90] }, pulse: { lo: .92, hi: 1.06, dur: 2.2 }, origin: [.5, .2] },
    // a school swimming left: flipped so they face where they go
    { sprite: 'blue', n: 14, x: [1.05, 1.25], y: [.3, .45], size: [40, 64], flip: true, move: { dx: [-3100, -2900], dy: [-40, 40], dur: [26, 30] }, sway: { y: [8, 20], x: 0, dur: [2, 3] }, wag: { deg: 5, dur: .6 }, phase: .3, phaseStep: .015 },
    { sprite: 'yellow', n: 4, x: [-.15, -.05], y: [.55, .8], size: [80, 120], move: { dx: [2800, 3000], dy: [-80, 80], dur: [32, 46] }, sway: { y: [10, 24], x: 0, dur: [3, 4] }, wag: { deg: 6, dur: .7 } },
    { sprite: 'bubble', n: 26, x: [.05, .95], y: [1, 1.05], size: [8, 22], op: [.6, 1], move: { dx: [-40, 40], dy: [-1300, -1200], dur: [9, 16] }, sway: { x: [8, 20], dur: [1, 2] } },
  ];
  return { key: 'deep', scene: 'Deep', W, H, ax: .5, ay: .5, plate, sprites, layers };
}
build.key = 'deep';
module.exports = build;
