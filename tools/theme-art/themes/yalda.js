'use strict';
const L = require('../lib');
const { f } = L;

// Yalda night: the longest night of the year around the korsi. A window onto a snowy starry night, and on the table
// cloth: pomegranates (one broken open, seeds shining), a watermelon slice, a bowl of nuts, the Divan of Hafez, and
// candles. Candle flames flicker, stars twinkle outside, snow falls past the window.
function build() {
  const W = 2400, H = 1200, r = L.rng(1221);
  const c = L.canvas(W, H), b = c.b;
  b.push(`<rect width="${W}" height="${H}" fill="${c.g('wall', [[0, '#1a060c'], [.6, '#3a0d1a'], [1, '#14060a']])}"/>`);
  // wallpaper pattern: faint paisley (boteh) rows
  let pat = '';
  for (let y = 40; y < 760; y += 110) for (let x = ((y / 110) % 2) * 70; x < W; x += 140) pat += `<path d="M${x} ${y}c20 -30 50 -20 46 10c-4 30 -36 40 -46 70c-10 -30 -24 -50 0 -80z" fill="none" stroke="#e8b04a" stroke-opacity=".12" stroke-width="2"/>`;
  b.push(pat);
  // the window: arched frame onto the night
  const wx = 1500, wy = 120, ww = 640, wh = 560;
  c.defs.push(`<clipPath id="win"><path d="M${wx} ${wy + wh}V${wy + 200}Q${wx} ${wy} ${wx + ww / 2} ${wy}Q${wx + ww} ${wy} ${wx + ww} ${wy + 200}V${wy + wh}Z"/></clipPath>`);
  b.push(`<g clip-path="url(#win)"><rect x="${wx}" y="${wy}" width="${ww}" height="${wh}" fill="${c.g('ns', [[0, '#050818'], [1, '#1a2448']])}"/>${L.starfield(c.defs, r, { x0: wx, x1: wx + ww, y0: wy, y1: wy + wh, n: 160, color: '#ffe9d0', bright: 8 })}
    <circle cx="${wx + 470}" cy="${wy + 160}" r="50" fill="#fff4dc"/>${c.glow(wx + 470, wy + 160, 160, '#fff4dc', .3)}
    <path d="${L.ridge(r, W, H, wy + 480, 30, { x0: wx - 20, x1: wx + ww + 20 })}" fill="#c8d4e8"/><path d="${L.ridge(r, W, H, wy + 520, 20, { x0: wx - 20, x1: wx + ww + 20 })}" fill="#e8f0fa"/></g>`);
  b.push(`<path d="M${wx} ${wy + wh}V${wy + 200}Q${wx} ${wy} ${wx + ww / 2} ${wy}Q${wx + ww} ${wy} ${wx + ww} ${wy + 200}V${wy + wh}" fill="none" stroke="#5a2a14" stroke-width="26"/>`);
  b.push(`<path d="M${wx + ww / 2} ${wy}V${wy + wh}M${wx} ${wy + 300}H${wx + ww}" stroke="#5a2a14" stroke-width="14"/>`);
  b.push(`<rect x="${wx - 30}" y="${wy + wh}" width="${ww + 60}" height="26" fill="#6a3418"/>`);
  // the table cloth (termeh): deep red with a gold paisley border
  b.push(`<path d="M0 780H${W}V${H}H0Z" fill="${c.g('cl', [[0, '#7a1424'], [1, '#2a060c']])}"/>`);
  let border = '';
  for (let x = 20; x < W; x += 80) border += `<path d="M${x} 820c14 -22 36 -16 34 6c-2 22 -26 30 -34 52c-8 -22 -18 -36 0 -58z" fill="#e8b04a" opacity=".55"/>`;
  b.push(`<rect y="780" width="${W}" height="14" fill="#e8b04a" opacity=".7"/>${border}<rect y="890" width="${W}" height="8" fill="#e8b04a" opacity=".5"/>`);
  b.push(c.glow(1000, 900, 900, '#ffb347', .18));
  c.defs.push(L.lin('brassY', [[0, '#5a3a10'], [.4, '#f2d080'], [1, '#6a4410']], 0, 0, 1, 0));
  // pomegranates: whole ones with a crown, and one broken open full of glassy seeds
  c.defs.push(L.rad('pom', [[0, '#ff6a7a'], [.5, '#c8142e'], [1, '#4a0410']], .35, .3, .8));
  const pom = (x, y, s) => `<g transform="translate(${x} ${y}) scale(${s})"><ellipse cx="10" cy="64" rx="70" ry="14" fill="#000" opacity=".35"/><circle r="70" fill="url(#pom)"/><path d="M-14 -66l-6 -20l12 8l8 -16l8 16l12 -8l-6 20z" fill="#8a1424"/><ellipse cx="-26" cy="-30" rx="16" ry="9" fill="#fff" opacity=".35" transform="rotate(-30 -26 -30)"/></g>`;
  b.push(pom(520, 900, 1.1), pom(680, 940, .9), pom(380, 960, .85));
  const ox = 900, oy = 980;
  let seeds = '';
  for (let k = 0; k < 90; k++) { const a = r(0, Math.PI * 2), d = Math.sqrt(r()) * 62; const x = ox + Math.cos(a) * d * 1.3, y = oy + Math.sin(a) * d * .7; seeds += `<ellipse cx="${f(x)}" cy="${f(y)}" rx="7" ry="9" transform="rotate(${f(r(0, 180))} ${f(x)} ${f(y)})" fill="${c.rg('sd', [[0, '#ffd0d8'], [.4, '#ff2a4a'], [1, '#8a0620']], .35, .3, .7)}"/>`; }
  b.push(`<ellipse cx="${ox}" cy="${oy}" rx="100" ry="58" fill="#f2d8b8"/><ellipse cx="${ox}" cy="${oy}" rx="88" ry="48" fill="#e8c8a0"/>${seeds}<path d="M${ox - 100} ${oy}Q${ox - 104} ${oy + 60} ${ox} ${oy + 72}Q${ox + 104} ${oy + 60} ${ox + 100} ${oy}" fill="url(#pom)"/>`);
  // a slice of watermelon
  b.push(`<path d="M1060 1000L1300 1000A120 120 0 0 1 1060 1000Z" fill="#1f6a34"/><path d="M1072 1000L1288 1000A108 104 0 0 1 1072 1000Z" fill="#f2f0d8"/><path d="M1084 1000L1276 1000A96 90 0 0 1 1084 1000Z" fill="#e8283a"/>`);
  for (let k = 0; k < 8; k++) b.push(`<ellipse cx="${f(1110 + k * 20)}" cy="${f(1020 + (k % 2) * 16)}" rx="3" ry="5" fill="#1a0a06"/>`);
  // a bowl of nuts and dried fruit
  b.push(`<path d="M1380 960h220q-10 70 -110 76q-100 -6 -110 -76z" fill="${c.g('bw', [[0, '#3a7aa8'], [1, '#14304a']], 0, 0, 1, 0)}"/><ellipse cx="1490" cy="960" rx="110" ry="18" fill="#2a5a80"/>`);
  for (let k = 0; k < 40; k++) b.push(`<ellipse cx="${f(1490 + r(-90, 90))}" cy="${f(950 + r(-14, 6))}" rx="${f(r(6, 11))}" ry="${f(r(4, 7))}" fill="${r.pick(['#c8925a', '#e8c890', '#6a3a1a', '#8ab04a', '#f2e0c0'])}"/>`);
  // the Divan of Hafez, open on a little stand: leather cover, a thick page block, curved pages with lines of verse
  const bx = 1880, by = 1010;
  b.push(`<ellipse cx="${bx}" cy="${by + 40}" rx="260" ry="26" fill="#000" opacity=".35" ${c.blur(8)}/>`);
  b.push(`<path d="M${bx - 250} ${by + 20}L${bx} ${by + 36}L${bx + 250} ${by + 20}L${bx + 240} ${by - 100}L${bx} ${by - 88}L${bx - 240} ${by - 100}Z" fill="#4a120e"/>`);
  b.push(`<path d="M${bx - 236} ${by + 8}L${bx} ${by + 24}L${bx + 236} ${by + 8}L${bx + 236} ${by - 2}L${bx} ${by + 14}L${bx - 236} ${by - 2}Z" fill="#d8c8a8"/>`);
  const pg = (sgn) => `M${bx} ${by + 12}C${bx + sgn * 60} ${by - 6} ${bx + sgn * 160} ${by - 4} ${bx + sgn * 232} ${by}L${bx + sgn * 226} ${by - 124}C${bx + sgn * 150} ${by - 132} ${bx + sgn * 60} ${by - 134} ${bx} ${by - 108}Z`;
  b.push(`<path d="${pg(-1)}" fill="${c.g('pl', [[0, '#e8d8b8'], [.8, '#f6ecd6'], [1, '#d8c8a0']], 0, 0, 1, 0)}"/><path d="${pg(1)}" fill="${c.g('pr', [[0, '#d8c8a0'], [.2, '#f6ecd6'], [1, '#e8d8b8']], 0, 0, 1, 0)}"/>`);
  b.push(`<path d="M${bx} ${by - 108}V${by + 12}" stroke="#8a6a4a" stroke-width="2" opacity=".6"/>`);
  // gilded margins and the hemistichs, two columns a page, written right to left
  for (const sgn of [-1, 1]) {
    b.push(`<path d="M${bx + sgn * 30} ${by - 110 + 6}C${bx + sgn * 80} ${by - 124} ${bx + sgn * 150} ${by - 120} ${bx + sgn * 206} ${by - 112}" stroke="#c8902a" stroke-width="3" fill="none" opacity=".8"/>`);
    for (let k = 0; k < 6; k++) for (const col of [0, 1]) {
      const x0 = bx + sgn * (36 + col * 92), y0 = by - 94 + k * 16 + (sgn > 0 ? col * 0 : 0);
      let d = `M${x0} ${y0}`;
      for (let q = 0; q < 7; q++) d += `q5 ${f(r(-4, 3))} ${f(sgn * 10)} 0`;
      b.push(`<path d="${d}" stroke="#2a1a10" stroke-width="2" fill="none" opacity=".75"/>`);
    }
  }
  b.push(`<path d="M${bx + 8} ${by - 104}L${bx + 14} ${by + 30}l6 -8l6 6L${bx + 18} ${by - 102}" fill="#b81c2a"/>`);
  // a niche (taghcheh) in the left wall with an oil lamp and a little mirror
  b.push(`<path d="M220 640V420Q220 330 320 300Q420 330 420 420V640Z" fill="#140408"/><path d="M220 640V420Q220 330 320 300Q420 330 420 420V640Z" fill="none" stroke="#e8b04a" stroke-opacity=".5" stroke-width="6"/>`);
  b.push(`<rect x="210" y="636" width="220" height="14" fill="#6a3418"/>`);
  b.push(`<path d="M280 636q-6 -30 40 -34q46 4 40 34z" fill="url(#brassY)"/><path d="M356 620q20 -6 26 -20" stroke="#c8902a" stroke-width="6" fill="none"/>`);
  b.push(c.glow(384, 590, 140, '#ffb347', .45));
  b.push(`<ellipse cx="320" cy="420" rx="46" ry="60" fill="#c8d0dc" opacity=".25"/><ellipse cx="320" cy="420" rx="46" ry="60" fill="none" stroke="#e8b04a" stroke-width="5" opacity=".6"/>`);
  // candles: three ivory candles of different heights with drips
  const flames = [];
  for (const [cx, hh] of [[1180, 160], [1260, 110], [2200, 140]]) {
    const top = 960 - hh;
    flames.push([cx, top - 26]);
    b.push(c.glow(cx, top - 30, 260, '#ffb347', .3));
    b.push(`<rect x="${cx - 18}" y="${top}" width="36" height="${hh}" rx="4" fill="${c.g('cn', [[0, '#c8b090'], [.4, '#fff4e0'], [1, '#a8906a']], 0, 0, 1, 0)}"/><path d="M${cx - 18} ${top + 6}q6 20 0 34M${cx + 10} ${top + 4}q4 14 0 22" stroke="#fff4e0" stroke-width="6" stroke-linecap="round"/>`);
    b.push(`<path d="M${cx} ${top}v-10" stroke="#2a1a10" stroke-width="2"/>`);
  }
  flames.push([382, 586]);
  const plate = c.finish({ vignette: .55 });

  const sprites = {
    flame: L.sprite(40, 80, L.rad('fl', [[0, '#ffffff'], [.3, '#fff0b0'], [.7, '#ffb347', .9], [1, '#ff6a1f', 0]], .5, .7, .6) + L.blurF('b', 1.2, 30),
      `<path d="M20 78C6 72 4 56 10 42C14 30 18 18 20 2C22 18 26 30 30 42C36 56 34 72 20 78Z" fill="url(#fl)" filter="url(#b)"/><ellipse cx="20" cy="66" rx="5" ry="9" fill="#5a8aff" opacity=".35"/>`),
    halo: L.softDot(220, '#ffb347', { core: .15, coreOp: .5, mid: .2 }),
    flake: L.softDot(14, '#ffffff', { core: .45, mid: .7 }),
    star: L.sprite(30, 30, L.rad('s', [[0, '#ffffff', 1], [.2, '#ffe9d0', .6], [1, '#ffe9d0', 0]]), `<circle cx="15" cy="15" r="15" fill="url(#s)"/>`),
    glint: L.softDot(20, '#ffd0d8', { core: .2, coreOp: 1, mid: .4 }),
  };
  const winStars = []; for (let k = 0; k < 12; k++) winStars.push([Math.round(r(wx + 30, wx + ww - 30)), Math.round(r(wy + 60, wy + 330))]);
  const seedGlints = []; for (let k = 0; k < 8; k++) seedGlints.push([Math.round(ox + r(-70, 70)), Math.round(oy + r(-30, 30))]);
  const layers = [
    { sprite: 'star', at: winStars, size: [14, 26], twinkle: { lo: .1, hi: 1, dur: [1.5, 3.5] } },
    { sprite: 'flake', n: 30, x: [wx / W, (wx + ww) / W], y: [wy / H - .02, wy / H], size: [5, 10], op: [.6, .95], move: { dx: [-30, 30], dy: [wh - 40, wh - 20], dur: [8, 14] }, sway: { x: [8, 18], dur: [2, 4] }, fade: [[0, 0], [.1, 1], [.85, 1], [1, 0]] },
    { sprite: 'halo', at: flames, size: [220, 280], twinkle: { lo: .6, hi: 1, dur: [.3, .6] } },
    { sprite: 'flame', at: flames, size: [24, 30], origin: [.5, 1], pulse: { lo: .88, hi: 1.1, dur: [.35, .6] }, wag: { deg: [3, 6], dur: [.6, 1.1] } },
    { sprite: 'glint', at: seedGlints, size: [10, 18], twinkle: { lo: 0, hi: 1, dur: [1.4, 3] } },
  ];
  return { key: 'yalda', scene: 'Yalda', W, H, ax: .5, ay: .8, plate, sprites, layers };
}
build.key = 'yalda';
module.exports = build;
