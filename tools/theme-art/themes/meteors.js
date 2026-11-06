'use strict';
const L = require('../lib');
const { f } = L;

// Meteor shower: a deep night over a mountain lake. The Milky Way stands over the peaks, a lone stargazer sits on a rock
// by a telescope. Meteors streak across the sky head first, all from the same corner of the sky, as a real shower does.
function build() {
  const W = 2400, H = 1200, r = L.rng(6060);
  const c = L.canvas(W, H), b = c.b;
  b.push(`<rect width="${W}" height="${H}" fill="${c.g('sky', [[0, '#04050d'], [.4, '#0c1030'], [.68, '#1c2350'], [.78, '#2c2a58'], [1, '#0a0c1c']])}"/>`);
  // the Milky Way, almost upright
  c.defs.push(`<mask id="mwm2"><rect x="300" y="200" width="2000" height="460" fill="${c.g('mw', [[0, '#000'], [.5, '#fff'], [1, '#000']], 0, 0, 0, 1)}"/></mask>`);
  b.push(`<g transform="rotate(52 1300 420)" mask="url(#mwm2)"><g ${c.blur(5)}><rect x="300" y="200" width="2000" height="460" fill="#000" ${c.cloud({ freq: .005, oct: 5, seed: 13, color: L.rgb01('#b8c4ff'), contrast: 2, bias: -.7 })} opacity=".6"/>
    <rect x="300" y="200" width="2000" height="460" fill="#000" ${c.cloud({ freq: .007, oct: 4, seed: 17, color: L.rgb01('#ffd8c0'), contrast: 2, bias: -.95 })} opacity=".5"/></g></g>`);
  let mw = '';
  for (let i = 0; i < 1600; i++) { const t = r(-.2, 1.2), off = r.gauss() * 150, a = 52 * Math.PI / 180; const x0 = 300 + t * 2000, y0 = 430 + off; const x = 1300 + (x0 - 1300) * Math.cos(a) - (y0 - 420) * Math.sin(a), y = 420 + (x0 - 1300) * Math.sin(a) + (y0 - 420) * Math.cos(a); mw += `<circle cx="${f(x)}" cy="${f(y)}" r="${f(r(.3, 1))}" fill="#fff" opacity="${f(r(.2, .85))}"/>`; }
  b.push(mw);
  b.push(L.starfield(c.defs, r, { x1: W, y0: 0, y1: 780, n: 900, color: '#eef0ff', bright: 40, maxR: 1.3 }));
  // painted meteors already in the sky (faint), all from the radiant at the top left
  for (let i = 0; i < 7; i++) { const x = r(500, 2200), y = r(60, 520), len = r(80, 220); b.push(`<path d="M${f(x)} ${f(y)}l${f(len * .95)} ${f(len * .31)}" stroke="${c.g('mt', [[0, '#fff', 0], [1, '#fff', .7]], 0, 0, 1, 0)}" stroke-width="1.6" opacity="${f(r(.3, .7))}"/>`); }
  // mountains and their reflection in the lake
  const range = L.mountains(c, r, { base: 820, amp: 300, rough: .5, detail: 6, light: -1, rock: ['#161b38', '#262e58'], snow: ['#323b66', '#6a74a6'], snowline: .35, bottom: 860 });
  b.push(range.svg);
  b.push(`<rect y="640" width="${W}" height="220" fill="${c.g('hz', [[0, '#5a5aa0', 0], [1, '#5a5aa0', .18]])}"/>`);
  const lake = 860;
  b.push(`<rect y="${lake}" width="${W}" height="${H - lake}" fill="${c.g('lk', [[0, '#141a3a'], [1, '#05060e']])}"/>`);
  b.push(`<g transform="translate(0 ${lake * 2}) scale(1 -1)" opacity=".4" ${c.blur(2)}>${range.svg.replace(/id="/g, 'data-x="')}</g>`.replace(/url\(#/g, 'url(#'));
  for (let i = 0; i < 220; i++) { const x = r(0, W), y = r(lake + 4, H); b.push(`<path d="M${f(x)} ${f(y)}h${f(r(10, 90))}" stroke="#c4ccff" stroke-opacity="${f(r(.05, .25))}" stroke-width="1"/>`); }
  for (let i = 0; i < 160; i++) b.push(`<circle cx="${f(r(0, W))}" cy="${f(r(lake + 20, H))}" r="${f(r(.4, 1.1))}" fill="#eef0ff" opacity="${f(r(.15, .45))}"/>`);
  // the near shore with a rock, the stargazer and the telescope
  b.push(`<path d="M-20 ${H + 10}L-20 1010Q300 980 620 1040Q820 1070 900 ${H + 10}Z" fill="#05060c"/>`);
  b.push(`<path d="M300 1000Q330 930 420 920Q500 918 520 990Z" fill="#07080f"/>`);
  b.push(`<g fill="#020306"><circle cx="410" cy="866" r="16"/><path d="M394 880Q384 930 398 958L452 958Q460 930 432 884Z"/><path d="M398 940L360 958L366 970L414 958Z"/>
    <path d="M520 940L560 1000M520 940L500 1002M520 940L540 1004" stroke="#020306" stroke-width="4"/><path d="M494 948L594 852L606 864L506 960Z"/><circle cx="600" cy="856" r="10"/></g>`);
  const plate = c.finish({ vignette: .5 });

  const sprites = {
    meteor: L.sprite(420, 14, L.lin('m', [[0, '#9fb0ff', 0], [.7, '#c8d2ff', .5], [.95, '#ffffff', .95], [1, '#ffffff', 1]], 0, 0, 1, 0) + L.rad('h', [[0, '#ffffff', 1], [.5, '#dfe6ff', .6], [1, '#dfe6ff', 0]]),
      `<path d="M0 7L408 4.6Q414 7 408 9.4Z" fill="url(#m)"/><circle cx="410" cy="7" r="7" fill="url(#h)"/>`),
    star: L.sprite(40, 40, L.rad('s', [[0, '#ffffff', 1], [.15, '#eef0ff', .7], [1, '#eef0ff', 0]]), `<circle cx="20" cy="20" r="20" fill="url(#s)"/><path d="M20 3V37M3 20H37" stroke="#fff" stroke-width=".9" opacity=".7"/>`),
  };
  // all meteors fly the same way (down and to the right, 18 degrees), head first
  const ang = 18, rad = ang * Math.PI / 180;
  const layers = [
    { sprite: 'star', n: 40, x: [0, 1], y: [0, .65], size: [10, 24], op: [.5, 1], twinkle: { lo: .1, hi: 1, dur: [1.5, 4.5] } },
    { sprite: 'meteor', n: 6, x: [.05, .7], y: [.03, .35], size: [240, 420], rot: ang, move: { dx: Math.round(900 * Math.cos(rad)), dy: Math.round(900 * Math.sin(rad)), dur: [5, 9], travel: .14 }, fade: [[0, 0], [.02, 1], [.1, .9], [.14, 0], [1, 0]], fps: 24 },
  ];
  return { key: 'meteors', scene: 'Meteors', W, H, ax: .5, ay: .7, plate, sprites, layers };
}
build.key = 'meteors';
module.exports = build;
