'use strict';
const L = require('../lib');
const { f } = L;

// Lantern festival: a riverside town at night, the sky full of paper lanterns drifting up, their light doubled in the water.
// Big lanterns rise as sprites; strings of red lanterns hang over the top corners and swing gently from their cords.
function build() {
  const W = 2400, H = 1200, r = L.rng(808);
  const c = L.canvas(W, H), b = c.b;
  b.push(`<rect width="${W}" height="${H}" fill="${c.g('sky', [[0, '#0d0509'], [.35, '#2a0b10'], [.62, '#5a1a14'], [.74, '#8a3a1a'], [1, '#2a0d0a']])}"/>`);
  b.push(c.glow(1200, 760, 1100, '#ff8a3a', .28));
  // high thin clouds lit from below
  b.push(`<rect width="${W}" height="520" fill="#000" ${c.cloud({ freq: .0028, oct: 4, seed: 5, color: L.rgb01('#ff9a5a'), contrast: 1.8, bias: -.9, stretchX: 4 })} opacity=".22"/>`);
  b.push(L.starfield(c.defs, r, { x1: W, y0: 0, y1: 420, n: 220, color: '#ffe9d6', bright: 8, maxR: 1.2 }));
  // the far lanterns: hundreds of tiny warm lights thinning out with height
  let far = '';
  for (let i = 0; i < 520; i++) {
    const y = 120 + Math.pow(r(), .6) * 560, x = r(0, W);
    const s = 1 + (y / 700) * 3.2 * r(.6, 1.2);
    far += c.glow(x, y, s * 5, '#ffb04a', .35) + `<rect x="${f(x - s * .45)}" y="${f(y - s * .6)}" width="${f(s * .9)}" height="${f(s * 1.2)}" rx="${f(s * .3)}" fill="#ffd38a" opacity="${f(r(.6, 1))}"/>`;
  }
  b.push(far);
  // mountains in layers of haze
  [[640, 90, '#2a0f16', .9], [700, 60, '#3a1418', .95]].forEach(([base, amp, col]) => {
    b.push(`<path d="${L.ridge(r, W, H, base, amp, { waves: [[.0019, 1], [.0052, .5], [.017, .12]] })}" fill="${col}"/>`);
  });
  b.push(`<rect y="560" width="${W}" height="260" fill="${c.g('mh', [[0, '#ff8a4a', 0], [.6, '#ff8a4a', .12], [1, '#ff8a4a', 0]])}"/>`);
  // the town: roofs with upturned eaves, a pagoda, lit windows
  const town = [];
  const roof = (x, y, w, h, col) => `<path d="M${x - w * .62} ${y}Q${x - w * .5} ${y - h * .12} ${x - w * .38} ${y - h * .35}L${x} ${y - h}L${x + w * .38} ${y - h * .35}Q${x + w * .5} ${y - h * .12} ${x + w * .62} ${y}Q${x} ${y - h * .22} ${x - w * .62} ${y}Z" fill="${col}"/>`;
  const building = (x, base, w, hgt, floors, col) => {
    let s = '';
    for (let k = 0; k < floors; k++) {
      const fy = base - k * hgt, fw = w * (1 - k * .16);
      s += `<rect x="${f(x - fw / 2)}" y="${f(fy - hgt * .7)}" width="${f(fw)}" height="${f(hgt * .7)}" fill="${col}"/>`;
      const nw = Math.max(1, Math.round(fw / 26));
      for (let q = 0; q < nw; q++) if (r() < .75) {
        const wx = x - fw / 2 + fw * (q + .5) / nw;
        s += `<rect x="${f(wx - 5)}" y="${f(fy - hgt * .55)}" width="10" height="${f(hgt * .34)}" fill="${r.pick(['#ffcf7a', '#ffb04a', '#ff9a3a'])}"/>`;
        town.push([wx, fy - hgt * .4]);
      }
      s += roof(x, fy - hgt * .66, fw * 1.15, hgt * .55, col);
    }
    return s;
  };
  let tw = '';
  const shore = 820;
  for (let x = -40; x < W + 40; x += r(70, 150)) {
    const big = r() < .12;
    tw += building(x, shore + r(-6, 6), r(70, 120), r(40, 60), big ? r.int(3, 5) : r.int(1, 2), L.mix('#1a0709', '#2d0e0e', r()));
  }
  b.push(tw);
  town.forEach(([x, y]) => b.push(c.glow(x, y, 26, '#ffb04a', .45)));
  b.push(`<rect y="${shore - 6}" width="${W}" height="14" fill="#120507"/>`);
  // a stone bridge
  b.push(`<path d="M1500 ${shore + 4}Q1700 ${shore - 120} 1900 ${shore + 4}L1900 ${shore + 30}Q1700 ${shore - 70} 1500 ${shore + 30}Z" fill="#180608"/>`);
  for (let k = 0; k < 9; k++) { const x = 1530 + k * 42, y = shore - 70 * Math.sin(Math.PI * (x - 1500) / 400) - 24; b.push(`<rect x="${f(x)}" y="${f(y)}" width="4" height="22" fill="#180608"/>`, c.glow(x + 2, y - 4, 18, '#ffb04a', .6), `<circle cx="${f(x + 2)}" cy="${f(y - 4)}" r="4" fill="#ffd38a"/>`); }
  // the river: dark water with the glow of the sky and long broken reflections of every light
  b.push(`<rect y="${shore + 8}" width="${W}" height="${H - shore}" fill="${c.g('rv', [[0, '#3a120e'], [.4, '#1c0809'], [1, '#0a0305']])}"/>`);
  let refl = '';
  for (let i = 0; i < 520; i++) {
    const x = r(0, W), y = r(shore + 14, H), len = r(10, 70) * (1 + (y - shore) / 300);
    refl += `<rect x="${f(x - len / 2)}" y="${f(y)}" width="${f(len)}" height="${f(r(1, 3))}" fill="${r.pick(['#ffb04a', '#ff8a3a', '#ffd38a'])}" opacity="${f(r(.15, .55) * (1 - (y - shore) / (H - shore) * .6))}"/>`;
  }
  b.push(`<g ${c.blur(1.2)}>${refl}</g>`);
  town.forEach(([x, y]) => { const d = (shore - y) * 2; b.push(`<rect x="${f(x - 4)}" y="${f(shore + 14)}" width="8" height="${f(d + 30)}" fill="${c.g('rf', [[0, '#ffb04a', .45], [1, '#ffb04a', 0]])}" ${c.blur(3)}/>`); });
  // floating river lanterns, lotus-shaped, with warm pools of light
  for (let i = 0; i < 22; i++) {
    const x = r(0, W), y = r(shore + 60, H - 40), s = .45 + (y - shore) / 400;
    b.push(c.glow(x, y, 50 * s, '#ffb04a', .55));
    b.push(`<ellipse cx="${f(x)}" cy="${f(y + 6 * s)}" rx="${f(22 * s)}" ry="${f(5 * s)}" fill="#ff9a3a" opacity=".35" ${c.blur(3)}/>`);
    for (const [dx, h0, wd] of [[-11, 12, 7], [11, 12, 7], [-5, 17, 7], [5, 17, 7], [0, 20, 6]]) b.push(`<path d="M${f(x + dx * s - wd * s)} ${f(y)}Q${f(x + dx * s - wd * s * .6)} ${f(y - h0 * s * .7)} ${f(x + dx * s)} ${f(y - h0 * s)}Q${f(x + dx * s + wd * s * .6)} ${f(y - h0 * s * .7)} ${f(x + dx * s + wd * s)} ${f(y)}Z" fill="${c.g('ll', [[0, '#fff4d0'], [1, '#ff9a4a']])}" stroke="#c2401e" stroke-opacity=".4" stroke-width="1"/>`);
    b.push(`<circle cx="${f(x)}" cy="${f(y - 8 * s)}" r="${f(3 * s)}" fill="#fff"/>`);
    b.push(`<path d="M${f(x - 18 * s)} ${f(y)}Q${f(x)} ${f(y + 6 * s)} ${f(x + 18 * s)} ${f(y)}Q${f(x)} ${f(y - 4 * s)} ${f(x - 18 * s)} ${f(y)}Z" fill="#c2401e"/>`);
  }
  const strings = [];
  for (const [x0, y0, x1, y1] of [[-20, 40, 520, 120], [1900, 120, 2420, 30]]) {
    const n = 5;
    for (let k = 0; k < n; k++) { const u = (k + .5) / n, x = x0 + (x1 - x0) * u, y = y0 + (y1 - y0) * u + Math.sin(Math.PI * u) * 70; strings.push([Math.round(x), Math.round(y + 94)]); }
  }
  b.push(cords());
  const plate = c.finish({ vignette: .6 });

  // ---- sprites -----------------------------------------------------------------------------------------------
  // a sky lantern: a paper bag glowing from the flame inside, ribs showing through, a bamboo ring at the bottom
  const sky = L.sprite(120, 170, L.rad('lg', [[0, '#ffb04a', .55], [1, '#ff6a1f', 0]], .5, .55, .5) + L.lin('pp', [[0, '#ffe2a6'], [.55, '#ffb35a'], [1, '#f07a2a']]) + L.rad('in', [[0, '#fff6d8', .95], [.5, '#ffd27a', .5], [1, '#ffd27a', 0]], .5, .82, .5),
    `<ellipse cx="60" cy="96" rx="60" ry="74" fill="url(#lg)"/>
     <path d="M30 26Q60 14 90 26L98 132Q60 140 22 132Z" fill="url(#pp)"/>
     <path d="M30 26Q60 14 90 26L98 132Q60 140 22 132Z" fill="url(#in)"/>
     <path d="M44 22L40 136M60 18V138M76 22L80 136" stroke="#c8551e" stroke-opacity=".35" stroke-width="1.4"/>
     <path d="M26 70Q60 78 94 70M24 104Q60 112 96 104" stroke="#c8551e" stroke-opacity=".25" stroke-width="1.2" fill="none"/>
     <path d="M22 132Q60 140 98 132" stroke="#5a2410" stroke-width="3" fill="none"/>
     <ellipse cx="60" cy="146" rx="7" ry="9" fill="#fff6c8"/><ellipse cx="60" cy="148" rx="3.5" ry="5" fill="#fff"/>`);
  // a hanging round lantern: red silk on gold caps with a tassel, drawn hanging from the top edge
  const hang = L.sprite(110, 260, L.rad('rl', [[0, '#ffd27a'], [.35, '#ff5a2a'], [.8, '#b3121f'], [1, '#6a0a12']], .45, .5, .6) + L.rad('rg', [[0, '#ff7a3a', .5], [1, '#ff3a1a', 0]]),
    `<line x1="55" y1="0" x2="55" y2="66" stroke="#2a0a08" stroke-width="2.5"/>
     <ellipse cx="55" cy="130" rx="55" ry="60" fill="url(#rg)"/>
     <rect x="38" y="64" width="34" height="12" rx="3" fill="#d9a441"/>
     <ellipse cx="55" cy="122" rx="44" ry="48" fill="url(#rl)"/>
     <path d="M55 74V170M32 80Q20 122 32 164M78 80Q90 122 78 164M43 76Q36 122 43 168M67 76Q74 122 67 168" stroke="#7a0a12" stroke-opacity=".45" stroke-width="1.3" fill="none"/>
     <rect x="38" y="166" width="34" height="11" rx="3" fill="#d9a441"/>
     <path d="M55 177V196" stroke="#d9a441" stroke-width="3"/>
     <path d="M48 196h14l5 52h-24z" fill="#e5383b"/><path d="M50 200V246M55 200V248M60 200V246" stroke="#7a0a12" stroke-opacity=".5"/>`);
  const sprites = { sky, hang, spark: L.softDot(24, '#ffd38a', { core: .2 }) };
  const layers = [
    { sprite: 'sky', n: 7, x: [.04, .96], y: [1.05, 1.1], size: [34, 60], op: [.55, .85], move: { dx: [-60, 60], dy: [-1500, -1400], dur: [48, 70] }, sway: { x: [14, 30], dur: [5, 9] }, twinkle: { lo: .7, hi: 1, dur: [1.5, 3] } },
    { sprite: 'sky', n: 6, x: [.04, .96], y: [1.1, 1.2], size: [70, 110], op: [.85, 1], move: { dx: [-90, 90], dy: [-1700, -1600], dur: [34, 48] }, sway: { x: [20, 40], dur: [6, 10] }, twinkle: { lo: .75, hi: 1, dur: [1.2, 2.4] } },
    { sprite: 'spark', n: 24, x: [0, 1], y: [.2, .7], size: [8, 16], op: [.4, .8], twinkle: { lo: .1, hi: 1, dur: [2, 4] } },
    { sprite: 'hang', at: strings, size: 80, origin: [.5, 0], wag: { deg: [2.5, 4.5], dur: [2.6, 3.6] } },
  ];
  return { key: 'lanternfest', scene: 'Lanterns', W, H, ax: .5, ay: .7, plate, sprites, layers };

  function cords() {
    let s = '';
    for (const [x0, y0, x1, y1] of [[-20, 40, 520, 120], [1900, 120, 2420, 30]]) {
      let d = `M${x0} ${y0}`;
      for (let k = 1; k <= 40; k++) { const u = k / 40; d += `L${f(x0 + (x1 - x0) * u)} ${f(y0 + (y1 - y0) * u + Math.sin(Math.PI * u) * 70)}`; }
      s += `<path d="${d}" stroke="#1a0606" stroke-width="3" fill="none"/>`;
    }
    return s;
  }
}
build.key = 'lanternfest';
module.exports = build;
