'use strict';
const L = require('../lib');
const { f } = L;

// Galaxy: a spiral galaxy seen at an angle, its bright core, two dusty arms full of blue star clusters and pink
// nebulae, on a deep field of distant stars and faint galaxies. The galaxy itself is a sprite that turns very slowly;
// stars twinkle in front.
function build() {
  const W = 2400, H = 1200, r = L.rng(3141);
  const c = L.canvas(W, H), b = c.b;
  b.push(`<rect width="${W}" height="${H}" fill="${c.rg('bg', [[0, '#1a0c38'], [.5, '#0c0620'], [1, '#04020a']], .6, .5, .8)}"/>`);
  b.push(`<rect width="${W}" height="${H}" fill="#000" ${c.cloud({ freq: .0016, oct: 5, seed: 51, color: L.rgb01('#6a3aa8'), contrast: 1.6, bias: -.7 })} opacity=".5"/>`);
  b.push(`<rect width="${W}" height="${H}" fill="#000" ${c.cloud({ freq: .0024, oct: 4, seed: 52, color: L.rgb01('#c84a90'), contrast: 1.8, bias: -.95 })} opacity=".35"/>`);
  b.push(L.starfield(c.defs, r, { x1: W, y0: 0, y1: H, n: 1400, color: '#f3eeff', bright: 50, maxR: 1.3 }));
  // faint far galaxies
  for (let i = 0; i < 9; i++) { const x = r(0, W), y = r(0, H), s = r(10, 26); b.push(`<ellipse cx="${f(x)}" cy="${f(y)}" rx="${f(s)}" ry="${f(s * r(.3, .6))}" transform="rotate(${f(r(0, 180))} ${f(x)} ${f(y)})" fill="#e8d8ff" opacity=".35" ${c.blur(2)}/>`); }
  const plate = c.finish({ vignette: .5 });

  // the galaxy sprite: drawn face-on in a square; the app squashes it to an ellipse and turns it
  const S = 1100, C = S / 2, R = L.rng(77);
  let gx = '';
  gx += L.glow([], 0, 0, 0, '#fff', 0);
  const defs = [L.rad('core', [[0, '#ffffff', 1], [.06, '#fff4d8', .95], [.2, '#ffd0a0', .55], [.45, '#c88ae8', .2], [1, '#c88ae8', 0]]), L.blurF('b1', 6, 20), L.blurF('b2', 2, 20), L.blurF('b3', 14, 20)];
  let dust = '', starsA = '', neb = '', clusters = '';
  for (let arm = 0; arm < 2; arm++) for (let k = 0; k < 2600; k++) {
    const t = Math.pow(R(), .8), th = t * 2.6 * Math.PI + arm * Math.PI;
    const rad = 30 + t * 470, spread = 10 + t * 42;
    const x = C + Math.cos(th) * rad + R.gauss() * spread, y = C + Math.sin(th) * rad + R.gauss() * spread;
    const col = R() < .6 ? '#cfe0ff' : R() < .5 ? '#ffffff' : '#ffd8e8';
    starsA += `<circle cx="${f(x)}" cy="${f(y)}" r="${f(R(.6, 1.8))}" fill="${col}" opacity="${f(R(.3, .9))}"/>`;
    if (k % 6 === 0) dust += `<circle cx="${f(x + Math.cos(th + 1.4) * 14)}" cy="${f(y + Math.sin(th + 1.4) * 14)}" r="${f(R(6, 16))}" fill="#120818" opacity="${f(R(.15, .35))}"/>`;
    if (k % 30 === 0) neb += `<circle cx="${f(x)}" cy="${f(y)}" r="${f(R(14, 40))}" fill="${R() < .6 ? '#ff6ab8' : '#8a7aff'}" opacity="${f(R(.12, .3))}"/>`;
    if (k % 90 === 0) clusters += `<circle cx="${f(x)}" cy="${f(y)}" r="${f(R(8, 16))}" fill="#bfe0ff" opacity=".35"/>`;
  }
  // diffuse glow of the disc
  gx = `<circle cx="${C}" cy="${C}" r="${C * .95}" fill="url(#halo)"/>` +
    `<g filter="url(#b3)">${neb}</g><g filter="url(#b1)">${clusters}</g><g filter="url(#b1)">${dust}</g><g filter="url(#b2)" opacity=".7">${starsA}</g>${starsA}` +
    `<circle cx="${C}" cy="${C}" r="${C * .55}" fill="url(#core)"/>`;
  defs.push(L.rad('halo', [[0, '#c8a0ff', .35], [.4, '#8a5ac8', .12], [1, '#8a5ac8', 0]]));
  const sprites = {
    galaxy: L.sprite(S, S, defs.join(''), gx),
    star: L.sprite(40, 40, L.rad('s', [[0, '#ffffff', 1], [.15, '#f3eeff', .7], [1, '#c9a0ff', 0]]), `<circle cx="20" cy="20" r="20" fill="url(#s)"/><path d="M20 2V38M2 20H38" stroke="#fff" stroke-width=".9" opacity=".7"/>`),
  };
  const layers = [
    { sprite: 'galaxy', at: [[1300, 600]], size: 1500, tilt: { squash: .45, rot: -20 }, spin: { deg: -360, dur: 600 }, fps: 8 },
    { sprite: 'star', n: 46, x: [0, 1], y: [0, 1], size: [10, 28], op: [.5, 1], twinkle: { lo: .1, hi: 1, dur: [1.5, 4.5] } },
  ];
  return { key: 'galaxy', scene: 'Galaxy', W, H, ax: .5, ay: .5, plate, sprites, layers };
}
build.key = 'galaxy';
module.exports = build;
