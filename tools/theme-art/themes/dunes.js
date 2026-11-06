'use strict';
const L = require('../lib');
const { f } = L;

// Desert night: the Milky Way over rolling dunes, each dune with a moonlit face, a shadowed face and wind ripples,
// a caravan of camels on a far crest. Stars twinkle, now and then a meteor, and sand blows off the crests.
function build() {
  const W = 2400, H = 1200, r = L.rng(1390);
  const c = L.canvas(W, H), b = c.b;
  b.push(`<rect width="${W}" height="${H}" fill="${c.g('sky', [[0, '#05060c'], [.45, '#121426'], [.7, '#2b2232'], [.82, '#4a3328'], [1, '#1a110b']])}"/>`);
  // the Milky Way: a diagonal band of fine nebula and dense stars with a dark dust lane
  const band = (op, freq, seed, col, bias) => `<g transform="rotate(-24 1200 400)"><rect x="-400" y="210" width="3200" height="300" fill="#000" ${c.cloud({ freq, oct: 5, seed, color: L.rgb01(col), contrast: 2, bias })} opacity="${op}" mask="url(#mwm)"/></g>`;
  c.defs.push(`<mask id="mwm"><rect x="-400" y="210" width="3200" height="300" fill="${c.g('mwg', [[0, '#000'], [.5, '#fff'], [1, '#000']])}"/><rect x="-400" y="210" width="3200" height="300" fill="${c.g('mwh', [[0, '#000'], [.25, '#000', 0], [.75, '#000', 0], [1, '#000']], 0, 0, 1, 0)}"/></mask>`);
  b.push(`<g ${c.blur(6)}>${band(.55, .004, 3, '#b8b4e8', -.6)}${band(.4, .006, 9, '#f2d2b0', -.8)}</g>`);
  b.push(`<g transform="rotate(-24 1200 400)" ${c.blur(10)}><rect x="-400" y="345" width="3200" height="26" fill="#05060c" opacity=".55" ${c.cloud({ freq: .01, oct: 3, seed: 2, color: [0, 0, 0], contrast: 3, bias: -.8 })}/></g>`);
  let mw = '';
  for (let i = 0; i < 1400; i++) {
    const t = r(-.15, 1.15), off = r.gauss() * 120;
    const x = -200 + t * 2800, y = 360 + off - (x - 1200) * Math.tan(24 * Math.PI / 180) * 1;
    mw += `<circle cx="${f(x)}" cy="${f(y)}" r="${f(r(.3, 1.1))}" fill="#fff" opacity="${f(r(.2, .9))}"/>`;
  }
  b.push(mw);
  b.push(L.starfield(c.defs, r, { x1: W, y0: 0, y1: 760, n: 700, color: '#fff3d6', bright: 30, maxR: 1.3 }));
  // crescent moon low on the right with a glow
  b.push(c.glow(1960, 300, 280, '#fff1cc', .2));
  c.defs.push(`<mask id="cres"><circle cx="1960" cy="300" r="46" fill="#fff"/><circle cx="1982" cy="288" r="42" fill="#000"/></mask>`);
  b.push(`<circle cx="1960" cy="300" r="46" fill="#fff4d8" mask="url(#cres)"/>`);
  // horizon glow
  b.push(`<rect y="560" width="${W}" height="260" fill="${c.g('hz', [[0, '#d08a4a', 0], [.7, '#d08a4a', .16], [1, '#d08a4a', 0]])}"/>`);

  // dunes, far to near: a body in shadow, the moonlit face as a band below each crest, ripples
  const layers = [
    { base: 720, amp: 50, dark: '#3a2a22', lit: '#8a6a50', fr: .0024 },
    { base: 820, amp: 70, dark: '#2e2016', lit: '#a07a52', fr: .0019 },
    { base: 950, amp: 90, dark: '#24170e', lit: '#b08550', fr: .0016 },
    { base: 1100, amp: 80, dark: '#180f08', lit: '#9a7044', fr: .0013 },
  ];
  let caravanAt = null;
  layers.forEach((d, i) => {
    const fn = L.ridgeFn(r, d.base, d.amp, [[d.fr, 1], [d.fr * 2.7, .35], [d.fr * 6.1, .1]]);
    const clip = L.id('dc');
    const body = L.ridgePath(fn, W, H, { step: 8 });
    c.defs.push(`<clipPath id="${clip}"><path d="${body}"/></clipPath>`);
    b.push(`<path d="${body}" fill="${c.g('db', [[0, d.dark], [1, '#0a0604']])}"/>`);
    b.push(`<g clip-path="url(#${clip})">${c.glow(1960, d.base - 60, 900, '#e8c8a0', .12)}</g>`);
    // the face toward the moon: a lit band under the crest, deeper where the slope turns toward the light
    const depth = (x) => { const sl = (fn(x - 30) - fn(x + 30)) / 60; return Math.max(0, sl) * (240 + i * 120) + 10; };
    let band = `M-20 ${f(fn(-20))}`;
    for (let x = -20; x <= W + 20; x += 8) band += `L${f(x)} ${f(fn(x) - 3)}`;
    for (let x = W + 20; x >= -20; x -= 8) band += `L${f(x)} ${f(fn(x) + depth(x))}`;
    b.push(`<g clip-path="url(#${clip})"><path d="${band}Z" fill="${d.lit}" opacity=".8" ${c.blur(10 + i * 4)}/></g>`);
    // crest highlight
    let crest = `M-20 ${f(fn(-20))}`;
    for (let x = -20; x <= W + 20; x += 8) crest += `L${f(x)} ${f(fn(x))}`;
    b.push(`<path d="${crest}" stroke="#e8c08a" stroke-opacity="${.18 + i * .06}" stroke-width="${1.5 + i}" fill="none" ${c.blur(1)}/>`);
    // wind ripples
    let rp = '';
    for (let k = 0; k < 40 + i * 30; k++) {
      const x = r(-20, W), y = fn(x) + r(20, 240), len = r(40, 160);
      rp += `<path d="M${f(x)} ${f(y)}q${f(len / 2)} ${f(-r(3, 8))} ${f(len)} ${f(r(-2, 4))}" stroke="${r() < .5 ? '#000' : d.lit}" stroke-opacity="${f(r(.08, .22))}" stroke-width="${f(r(1, 2.4))}" fill="none"/>`;
    }
    b.push(`<g clip-path="url(#${clip})">${rp}</g>`);
    if (i === 1) caravanAt = fn;
  });
  // a caravan on the second crest: camels with riders, in silhouette
  const camel = (x, y, s, rider) => {
    let p = `<g transform="translate(${f(x)} ${f(y)}) scale(${s})" fill="#0b0705">`;
    p += `<path d="M-30 -30Q-26 -48 -14 -46Q-6 -60 6 -46Q16 -44 20 -34L30 -40Q36 -56 42 -58Q50 -60 52 -54L50 -50L44 -50Q40 -38 34 -28Q30 -22 22 -22L-26 -22Q-34 -24 -30 -30Z"/>`;
    p += `<path d="M-24 -24L-28 0M-16 -24L-14 0M14 -24L12 0M22 -24L26 0" stroke="#0b0705" stroke-width="4" stroke-linecap="round"/>`;
    p += `<path d="M-32 -30q-6 4 -4 14" stroke="#0b0705" stroke-width="2" fill="none"/>`;
    if (rider) p += `<path d="M-6 -50q-2 -12 4 -18q8 -2 8 8q0 6 -4 10z"/><circle cx="2" cy="-74" r="5"/><path d="M-2 -70l-8 20h10z" opacity=".9"/>`;
    return p + '</g>';
  };
  let cx0 = 420;
  for (let k = 0; k < 6; k++) { const x = cx0 + k * 58; b.push(camel(x, caravanAt(x) + 2, .9, k % 2 === 0)); }
  b.push(`<path d="M${cx0 - 30} ${f(caravanAt(cx0) - 8)}C${cx0 + 100} ${f(caravanAt(cx0 + 100) - 24)} ${cx0 + 220} ${f(caravanAt(cx0 + 220) - 22)} ${cx0 + 320} ${f(caravanAt(cx0 + 320) - 10)}" stroke="#0b0705" stroke-width="1" fill="none" opacity=".6"/>`);
  const plate = c.finish({ vignette: .5 });

  const sprites = {
    star: L.sprite(40, 40, L.rad('s', [[0, '#ffffff', 1], [.15, '#fff3d6', .7], [1, '#fff3d6', 0]]), `<circle cx="20" cy="20" r="20" fill="url(#s)"/><path d="M20 2V38M2 20H38" stroke="#fff" stroke-width="1" opacity=".7"/>`),
    meteor: L.sprite(320, 10, L.lin('m', [[0, '#fff3d6', 0], [.85, '#fff3d6', .7], [1, '#ffffff', 1]], 0, 0, 1, 0), `<path d="M0 5L312 3.2A2.2 2.2 0 0 1 312 6.8Z" fill="url(#m)"/><circle cx="314" cy="5" r="3" fill="#fff"/>`),
    sand: L.sprite(360, 60, L.rad('w', [[0, '#e8c08a', .35], [1, '#e8c08a', 0]]), `<ellipse cx="180" cy="30" rx="180" ry="30" fill="url(#w)"/>`),
  };
  const lay = [
    { sprite: 'star', n: 36, x: [0, 1], y: [0, .55], size: [10, 26], op: [.5, 1], twinkle: { lo: .1, hi: 1, dur: [1.6, 4.5] } },
    // a meteor now and then: crosses in a second (moving the way its head points), then waits
    { sprite: 'meteor', n: 2, x: [.15, .6], y: [.05, .25], size: [200, 280], rot: 18, move: { dx: 620, dy: 201, dur: [9, 14], travel: .1 }, fade: [[0, 0], [.02, 1], [.08, .8], [.1, 0], [1, 0]], fps: 24 },
    // sand lifted off the crests, blowing to the left
    { sprite: 'sand', n: 7, x: [.1, 1.05], y: [.62, .9], size: [260, 420], op: [.3, .6], move: { dx: [-700, -500], dy: [-30, 10], dur: [16, 24] }, fade: [[0, 0], [.2, 1], [.8, 1], [1, 0]] },
  ];
  return { key: 'dunes', scene: 'Dunes', W, H, ax: .5, ay: .8, plate, sprites, layers: lay };
}
build.key = 'dunes';
module.exports = build;
