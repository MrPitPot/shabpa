'use strict';
const L = require('../lib');
const { f } = L;

// Frontline: night over no man's land. A low cloud ceiling lit orange by fires on the horizon, shattered trees,
// barbed wire on crooked posts, a trench line of sandbags in front. Searchlights sweep the clouds from behind the
// trench, a flare drifts down under its glow, distant shell flashes light the horizon and smoke rolls past.
function build() {
  const W = 2400, H = 1200, r = L.rng(1916);
  const c = L.canvas(W, H), b = c.b;
  b.push(`<rect width="${W}" height="${H}" fill="${c.g('sky', [[0, '#07090d'], [.45, '#1a1f2a'], [.7, '#3a2a24'], [.8, '#6a3218'], [1, '#120c0a']])}"/>`);
  b.push(`<rect width="${W}" height="760" fill="#000" ${c.cloud({ freq: .0026, oct: 5, seed: 31, color: L.rgb01('#5a5a62'), contrast: 1.6, bias: -.5, stretchX: 2.5 })} opacity=".75"/>`);
  b.push(`<rect y="380" width="${W}" height="420" fill="#000" ${c.cloud({ freq: .004, oct: 4, seed: 32, color: L.rgb01('#ff7a3a'), contrast: 1.6, bias: -.8, stretchX: 2 })} opacity=".35"/>`);
  for (const x of [300, 900, 1450, 2100]) b.push(c.glow(x, 790, r(260, 420), '#ff6a2a', .5));
  // horizon: low ruined skyline and burning points
  b.push(`<path d="${L.ridge(r, W, H, 800, 18, { jag: 8, step: 14 })}" fill="#0c0b0b"/>`);
  for (let i = 0; i < 18; i++) { const x = r(0, W); b.push(c.glow(x, 796, r(20, 60), '#ffb04a', .7), `<path d="M${f(x - 6)} 800q6 -${f(r(14, 40))} 12 0z" fill="#ffd27a"/>`); }
  // the ground of no man's land: craters, mud, puddles reflecting the sky glow
  b.push(`<rect y="800" width="${W}" height="400" fill="${c.g('gd', [[0, '#1a1410'], [1, '#060504']])}"/>`);
  for (let i = 0; i < 30; i++) { const x = r(0, W), y = r(830, 1000), rx = r(40, 140); b.push(`<ellipse cx="${f(x)}" cy="${f(y)}" rx="${f(rx)}" ry="${f(rx * .18)}" fill="#000" opacity=".5"/><ellipse cx="${f(x)}" cy="${f(y - 2)}" rx="${f(rx * .6)}" ry="${f(rx * .08)}" fill="#ff8a4a" opacity="${f(r(.02, .07))}"/>`); }
  // shattered trees
  const stump = (x, y, hgt) => {
    let s = `<path d="M${x - 8} ${y}L${x - 5} ${y - hgt}L${x - 1} ${y - hgt - 14}L${x + 3} ${y - hgt + 6}L${x + 7} ${y}Z" fill="#050404"/>`;
    for (let k = 0; k < 3; k++) { const yy = y - hgt * r(.4, .9), d = r() < .5 ? -1 : 1; s += `<path d="M${x} ${f(yy)}l${f(d * r(30, 70))} ${f(-r(10, 40))}" stroke="#050404" stroke-width="${f(r(2, 4))}"/>`; }
    return s;
  };
  for (const [x, hh] of [[260, 220], [700, 140], [1580, 260], [1900, 120], [2260, 180]]) b.push(stump(x, 830, hh));
  // barbed wire on crooked posts
  let wire = '';
  for (let k = 0; k < 18; k++) { const x = 40 + k * 140 + r(-20, 20), y = 960 + r(-10, 10); wire += `<path d="M${f(x)} ${f(y)}l${f(r(-12, 12))} -90" stroke="#050404" stroke-width="7"/>`; }
  for (let s = 0; s < 3; s++) { let d = `M0 ${880 + s * 26}`; for (let x = 0; x <= W; x += 30) d += `Q${x + 15} ${880 + s * 26 + r(-14, 14)} ${x + 30} ${880 + s * 26 + r(-4, 4)}`; wire += `<path d="${d}" stroke="#0a0808" stroke-width="2" fill="none"/>`; }
  for (let x = 0; x < W; x += 60) for (let s = 0; s < 3; s++) wire += `<path d="M${x} ${875 + s * 26}l8 8M${x + 8} ${875 + s * 26}l-8 8" stroke="#0a0808" stroke-width="2"/>`;
  b.push(wire);
  // the trench parapet: sandbags stacked along the bottom, lit faintly from the fires
  let bags = '';
  for (let row = 0; row < 4; row++) for (let x = -60 + (row % 2) * 60; x < W + 60; x += 120) {
    const y = 1060 + row * 42 + r(-4, 4);
    bags += `<path d="M${f(x)} ${f(y)}q-8 -30 20 -36h80q28 6 20 36q-4 14 -20 14h-80q-16 0 -20 -14z" fill="${c.g('bag', [[0, '#3a2e22'], [.4, '#241c14'], [1, '#0a0806']])}"/>`;
    bags += `<path d="M${f(x + 10)} ${f(y - 30)}h80" stroke="#c8925a" stroke-opacity=".14" stroke-width="3"/><path d="M${f(x + 54)} ${f(y - 34)}v44" stroke="#14100c" stroke-opacity=".4" stroke-width="2"/>`;
  }
  b.push(bags);
  const plate = c.finish({ vignette: .6 });

  const sprites = {
    // a searchlight beam drawn pointing up from its base at the bottom middle
    beam: L.sprite(400, 1300, L.lin('bm', [[0, '#eef4ff', .0], [.4, '#dfe8ff', .12], [.9, '#eef4ff', .38], [1, '#ffffff', .6]], 0, 0, 0, 1) + L.blurF('bb', 10, 20),
      `<path d="M190 1300L0 0H400L210 1300Z" fill="url(#bm)" filter="url(#bb)"/>`),
    flare: L.sprite(300, 300, L.rad('fl', [[0, '#ffffff', 1], [.05, '#fff0c8', 1], [.2, '#ffb04a', .45], [1, '#ff6a2a', 0]]), `<circle cx="150" cy="150" r="150" fill="url(#fl)"/>`),
    flash: L.softDot(300, '#ffc070', { core: .15, coreOp: .9, mid: .35 }),
    smoke: L.sprite(700, 260, L.rad('sm', [[0, '#3a3634', .6], [.6, '#2a2624', .3], [1, '#2a2624', 0]]), `<ellipse cx="350" cy="130" rx="350" ry="130" fill="url(#sm)"/>`),
  };
  const layers = [
    { sprite: 'beam', at: [[700, 450], [1750, 450]], size: 360, origin: [.5, 1], spin: { deg: 26, dur: [11, 15], back: true }, twinkle: { lo: .7, hi: 1, dur: 3 } },
    { sprite: 'flash', at: [[300, 790], [900, 800], [1450, 790], [2100, 800], [1200, 795]], size: [180, 320], fade: [[0, 0], [.03, 1], [.15, 0], [1, 0]], fadeDur: [5, 9] },
    { sprite: 'flare', n: 1, x: [.55, .65], y: [.05, .08], size: 280, move: { dx: 160, dy: 560, dur: 22 }, fade: [[0, 0], [.05, 1], [.85, .9], [1, 0]], sway: { x: 30, dur: 4 }, twinkle: { lo: .75, hi: 1, dur: .3 } },
    { sprite: 'smoke', n: 6, x: [-.2, 1], y: [.55, .85], size: [600, 1000], op: [.5, .85], move: { dx: [500, 800], dy: [-40, 10], dur: [40, 60] }, fade: [[0, 0], [.2, 1], [.8, 1], [1, 0]] },
  ];
  return { key: 'battlefield', scene: 'Smoke', W, H, ax: .5, ay: .8, plate, sprites, layers };
}
build.key = 'battlefield';
module.exports = build;
