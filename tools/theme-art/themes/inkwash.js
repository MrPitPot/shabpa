'use strict';
const L = require('../lib');
const { f } = L;

// Ink wash: a mountain landscape in the old ink-painting manner on warm rice paper. Peaks rise out of mist in washes
// from dark to almost nothing, a gnarled pine leans from a cliff, a fishing boat sits on still water, a seal in red.
// Ink blooms open slowly on the paper and a few birds cross the sky.
function build() {
  const W = 2400, H = 1200, r = L.rng(7070);
  const c = L.canvas(W, H), b = c.b;
  // paper: warm, fibrous, a little stained
  b.push(`<rect width="${W}" height="${H}" fill="#f2ede3"/>`);
  b.push(`<rect width="${W}" height="${H}" fill="#000" ${c.cloud({ freq: .0025, oct: 4, seed: 4, color: L.rgb01('#c9b896'), contrast: 1.2, bias: -.45 })} opacity=".35"/>`);
  const fib = L.id('fib');
  c.defs.push(`<filter id="${fib}" x="0" y="0" width="100%" height="100%"><feTurbulence type="fractalNoise" baseFrequency=".9 .04" numOctaves="2" seed="5"/><feColorMatrix type="matrix" values="0 0 0 0 .55  0 0 0 0 .48  0 0 0 0 .38  -2.2 0 0 0 1.25"/><feComposite in2="SourceGraphic" operator="in"/></filter>`);
  b.push(`<rect width="${W}" height="${H}" fill="#000" filter="url(#${fib})" opacity=".18"/>`);
  // a brushed edge: displacement roughens every wash so it reads as ink on paper, not a vector shape
  const rough = L.id('rough');
  c.defs.push(`<filter id="${rough}" x="-5%" y="-5%" width="110%" height="110%"><feTurbulence type="fractalNoise" baseFrequency=".035" numOctaves="3" seed="9" result="n"/><feDisplacementMap in="SourceGraphic" in2="n" scale="14" xChannelSelector="R" yChannelSelector="G"/></filter>`);
  // peaks: tall rounded karst shapes, darkest at the crest and fading into mist below
  const peak = (x, base, w, hgt, ink, fadeTo) => {
    const pts = [];
    const n = 14;
    for (let k = 0; k <= n; k++) {
      const u = k / n, px = x - w / 2 + w * u;
      const prof = Math.pow(Math.sin(Math.PI * u), .7) * hgt * (1 + .12 * Math.sin(u * 17 + x));
      pts.push([px, base - prof]);
    }
    const gid = L.id('pk');
    c.defs.push(L.lin(gid, [[0, ink, .95], [.35, ink, .55], [fadeTo, ink, 0]], 0, 0, 0, 1));
    let p = `<path d="${L.smooth([[x - w / 2, base + 40], ...pts, [x + w / 2, base + 40]], false)}Z" fill="url(#${gid})" filter="url(#${rough})"/>`;
    // dry brush texture strokes down the faces (cun)
    let cun = '';
    for (let k = 0; k < 22; k++) { const u = r(.2, .8), px = x - w / 2 + w * u, py = base - Math.pow(Math.sin(Math.PI * u), .7) * hgt + r(10, 40); cun += `<path d="M${f(px)} ${f(py)}q${f(r(-10, 10))} ${f(r(15, 35))} ${f(r(-14, 14))} ${f(r(30, 70))}" stroke="${ink}" stroke-width="${f(r(1.5, 4))}" stroke-opacity="${f(r(.15, .45))}" fill="none" stroke-linecap="round"/>`; }
    p += `<g filter="url(#${rough})">${cun}</g>`;
    // dots of moss and tiny trees along the crest
    for (let k = 0; k < 18; k++) { const u = r(.2, .8), px = x - w / 2 + w * u, py = base - Math.pow(Math.sin(Math.PI * u), .7) * hgt + r(0, 8); p += `<ellipse cx="${f(px)}" cy="${f(py)}" rx="${f(r(2, 5))}" ry="${f(r(1.5, 3.5))}" fill="${ink}" opacity="${f(r(.4, .8))}"/>`; }
    return p;
  };
  // far range, barely there
  for (let k = 0; k < 9; k++) b.push(peak(r(0, W), 760, r(180, 340), r(220, 420), '#7a7468', .5));
  b.push(`<rect y="500" width="${W}" height="500" fill="${c.g('mist1', [[0, '#f2ede3', 0], [.6, '#f2ede3', .85], [1, '#f2ede3', .2]])}"/>`);
  // middle peaks
  for (const [x, w, hh] of [[260, 300, 520], [520, 260, 420], [1700, 320, 600], [1980, 260, 460], [2250, 300, 540], [1040, 220, 300]]) b.push(peak(x, 880, w, hh, '#4a453c', .55));
  b.push(`<rect y="640" width="${W}" height="460" fill="${c.g('mist2', [[0, '#f2ede3', 0], [.55, '#f2ede3', .9], [1, '#f2ede3', .6]])}"/>`);
  // near cliff on the left with the pine
  b.push(peak(140, 1260, 520, 640, '#1f1b16', .7));
  const tx = 330, ty = 690;
  let trunk = `<path d="M${tx - 40} ${ty + 120}C${tx - 10} ${ty + 40} ${tx + 60} ${ty + 10} ${tx + 140} ${ty - 40}C${tx + 220} ${ty - 90} ${tx + 300} ${ty - 70} ${tx + 380} ${ty - 120}" stroke="#1f1b16" stroke-width="18" fill="none" stroke-linecap="round" filter="url(#${rough})"/>`;
  trunk += `<path d="M${tx + 140} ${ty - 40}C${tx + 170} ${ty - 110} ${tx + 150} ${ty - 160} ${tx + 190} ${ty - 220}" stroke="#1f1b16" stroke-width="9" fill="none" stroke-linecap="round" filter="url(#${rough})"/>`;
  // needle clusters: fans of short strokes in dark ink
  for (const [nx, ny, s] of [[tx + 380, ty - 130, 1.1], [tx + 300, ty - 90, .9], [tx + 190, ty - 230, 1], [tx + 230, ty - 60, .8], [tx + 90, ty - 10, .7], [tx + 420, ty - 100, .7], [tx + 150, ty - 170, .8]]) {
    let fan = '';
    for (let k = 0; k < 26; k++) { const a = Math.PI + r(-.2, .2) + (k / 26) * Math.PI, len = r(28, 46) * s; fan += `<path d="M${f(nx)} ${f(ny)}l${f(Math.cos(a) * len)} ${f(Math.sin(a) * len * .45)}" />`; }
    trunk += `<g stroke="#1f1b16" stroke-width="2.4" stroke-linecap="round" opacity=".85">${fan}</g><ellipse cx="${nx}" cy="${ny - 4}" rx="${46 * s}" ry="${14 * s}" fill="#1f1b16" opacity=".25" ${c.blur(6)}/>`;
  }
  b.push(trunk);
  // water: a few horizontal dry strokes, a fisherman in a small boat
  for (let k = 0; k < 26; k++) { const x = r(700, W), y = r(1000, 1160), len = r(60, 280); b.push(`<path d="M${f(x)} ${f(y)}h${f(len)}" stroke="#4a453c" stroke-opacity="${f(r(.15, .4))}" stroke-width="${f(r(1, 2.5))}" stroke-linecap="round"/>`); }
  const bx = 1500, by = 1060;
  b.push(`<path d="M${bx - 90} ${by}Q${bx} ${by + 26} ${bx + 100} ${by - 6}Q${bx} ${by + 8} ${bx - 90} ${by}Z" fill="#1f1b16"/>`);
  b.push(`<path d="M${bx - 20} ${by + 2}l14 -46l16 0l6 44z" fill="#1f1b16"/><path d="M${bx - 32} ${by - 40}q26 -22 56 0z" fill="#1f1b16"/><path d="M${bx + 10} ${by - 30}L${bx + 160} ${by - 110}" stroke="#1f1b16" stroke-width="2.5"/><path d="M${bx + 160} ${by - 110}q4 60 2 120" stroke="#1f1b16" stroke-width=".8" fill="none"/>`);
  b.push(`<path d="M${bx - 120} ${by + 14}h250" stroke="#4a453c" stroke-opacity=".4" stroke-width="2"/>`);
  // the red seal, lower right
  const sx = 2190, sy = 990;
  b.push(`<g filter="url(#${rough})" opacity=".85"><rect x="${sx}" y="${sy}" width="92" height="92" rx="6" fill="#a63a34"/>
    <path d="M${sx + 16} ${sy + 18}h22v24h-22zM${sx + 54} ${sy + 18}v58M${sx + 46} ${sy + 46}h30M${sx + 16} ${sy + 56}h26M${sx + 29} ${sy + 50}v28M${sx + 16} ${sy + 78}h26" stroke="#f2ede3" stroke-width="6" fill="none"/></g>`);
  const plate = c.finish({ vignette: .18, grain: .04 });

  const blot = (seed, sz, col) => {
    const R = L.rng(seed);
    let s = '';
    for (let k = 0; k < 6; k++) s += `<path d="${L.blob(R, sz / 2 + R(-sz * .12, sz * .12), sz / 2 + R(-sz * .12, sz * .12), sz * R(.18, .34), sz * R(.18, .34), .35, 9)}" fill="${col}" opacity="${f(R(.12, .3))}"/>`;
    return L.sprite(sz, sz, L.blurF('bb', sz * .03, 20), `<g filter="url(#bb)">${s}</g>`);
  };
  const bird = (up) => L.sprite(60, 30, '', `<path d="M2 ${up ? 6 : 22}Q16 ${up ? 8 : 20} 30 16Q44 ${up ? 8 : 20} 58 ${up ? 6 : 22}Q44 ${up ? 12 : 18} 30 19Q16 ${up ? 12 : 18} 2 ${up ? 6 : 22}Z" fill="#1f1b16"/><circle cx="32" cy="16" r="2.5" fill="#1f1b16"/>`);
  const sprites = { blot1: blot(1, 420, '#1f1b16'), blot2: blot(2, 360, '#3a352c'), blotred: blot(3, 300, '#8c2f2b'), birdUp: bird(true), birdDown: bird(false) };
  const layers = [
    { sprite: 'blot1', n: 3, x: [.1, .9], y: [.1, .5], size: [380, 520], op: .7, move: { dx: 0, dy: 0, dur: [18, 26] }, grow: [.3, 1.2], fade: [[0, 0], [.3, .6], [1, 0]] },
    { sprite: 'blot2', n: 2, x: [.3, .95], y: [.1, .4], size: [300, 420], op: .7, move: { dx: 0, dy: 0, dur: [20, 28] }, grow: [.3, 1.2], fade: [[0, 0], [.3, .55], [1, 0]] },
    { sprite: 'blotred', n: 1, x: [.6, .8], y: [.2, .35], size: 320, op: .6, move: { dx: 0, dy: 0, dur: 24 }, grow: [.3, 1.1], fade: [[0, 0], [.35, .5], [1, 0]] },
    // birds flap (two drawings) and fly to the right, the way they face
    { sprite: ['birdUp', 'birdDown'], frames: { fps: 3 }, n: 5, x: [-.15, -.05], y: [.12, .3], size: [26, 40], op: [.7, .9], move: { dx: [2700, 2900], dy: [-120, 60], dur: [50, 70] }, sway: { y: [8, 16], x: 0, dur: [3, 5] } },
  ];
  return { key: 'inkwash', scene: 'Ink', W, H, ax: .5, ay: .6, plate, sprites, layers };
}
build.key = 'inkwash';
module.exports = build;
