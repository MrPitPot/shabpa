'use strict';
const L = require('../lib');
const { f } = L;

// Teahouse: inside an old Persian coffeehouse in the evening. Brick arches and a tiled niche, a painted coffeehouse
// canvas on the wall, a wooden takht with a kilim, a brass samovar steaming beside a teapot and tulip glasses of tea,
// hanging brass lamps. Steam rises and curls, the lamps flicker gently.
function build() {
  const W = 2400, H = 1200, r = L.rng(1357);
  const c = L.canvas(W, H), b = c.b;
  b.push(`<rect width="${W}" height="${H}" fill="${c.g('wall', [[0, '#2a1a10'], [.6, '#4a2e1a'], [1, '#1a100a']])}"/>`);
  // brick courses with mortar
  let brick = '';
  for (let y = 0; y < 900; y += 34) for (let x = -((y / 34) % 2) * 50; x < W; x += 100) brick += `<rect x="${x + 2}" y="${y + 2}" width="96" height="30" rx="3" fill="${L.mix('#6a4026', '#8a5434', r())}" opacity="${f(r(.35, .6))}"/>`;
  b.push(brick);
  // three pointed arches (iwans) with deep shadow inside
  const arch = (cx, w0, top, foot) => `M${cx - w0 / 2} ${foot}V${top + w0 * .5}Q${cx - w0 / 2} ${top + w0 * .1} ${cx} ${top}Q${cx + w0 / 2} ${top + w0 * .1} ${cx + w0 / 2} ${top + w0 * .5}V${foot}Z`;
  for (const [x, w0] of [[400, 420], [1200, 560], [2000, 420]]) {
    b.push(`<path d="${arch(x, w0 + 40, 140, 900)}" fill="#7a4a2c"/>`, `<path d="${arch(x, w0, 170, 900)}" fill="${c.g('in', [[0, '#120a06'], [1, '#2a1a10']])}"/>`);
    b.push(`<path d="${arch(x, w0 + 20, 155, 900)}" fill="none" stroke="#2bb5b0" stroke-width="8" stroke-dasharray="20 8" opacity=".8"/>`);
  }
  // the coffeehouse painting in the middle arch: a hero on horseback, a battle in ochres and reds (simplified)
  const px = 1200, py = 470;
  b.push(`<rect x="${px - 220}" y="${py - 160}" width="440" height="300" fill="#c89a5a"/>`, `<rect x="${px - 220}" y="${py - 160}" width="440" height="300" fill="none" stroke="#3a2010" stroke-width="12"/>`);
  b.push(`<g opacity=".85"><path d="M${px - 200} ${py + 80}Q${px} ${py + 40} ${px + 200} ${py + 90}V${py + 130}H${px - 200}Z" fill="#8a5a2a"/>
    <path d="M${px - 90} ${py + 60}q20 -60 80 -60q40 0 50 -30l20 0q-6 40 -30 54q20 30 6 50l-10 -14q-30 4 -60 -2l-10 30h-14l6 -34q-30 -6 -38 6z" fill="#4a2a18"/>
    <path d="M${px - 30} ${py - 10}l-6 -50q14 -20 30 0l-4 50z" fill="#b8241a"/><circle cx="${px - 18}" cy="${py - 74}" r="14" fill="#e8b88a"/><path d="M${px - 34} ${py - 88}q16 -22 32 0z" fill="#2a2a2a"/>
    <path d="M${px - 10} ${py - 50}L${px + 80} ${py - 120}" stroke="#d8d8d8" stroke-width="5"/>
    <path d="M${px + 90} ${py + 60}l20 -80q20 -10 34 6l-10 74z" fill="#2a4a6a"/><circle cx="${px + 116}" cy="${py - 34}" r="12" fill="#e8b88a"/>
    <circle cx="${px - 140}" cy="${py - 100}" r="26" fill="#f2c27a"/></g>`);
  // tiled niche shelf on the left arch with a lamp, glasses and bottles (silhouettes lit warm)
  b.push(`<rect x="250" y="560" width="300" height="12" fill="#3a2010"/>`);
  for (let k = 0; k < 6; k++) b.push(`<path d="M${270 + k * 46} 560v-${30 + (k % 3) * 18}h20v${30 + (k % 3) * 18}z" fill="${r.pick(['#2a6a5a', '#8a2a2a', '#c89a3a'])}" opacity=".85"/>`);
  // the floor and the wooden takht with a red kilim and a bolster
  b.push(`<rect y="900" width="${W}" height="300" fill="${c.g('fl', [[0, '#3a2414'], [1, '#120a06']])}"/>`);
  b.push(`<rect x="200" y="860" width="2000" height="90" fill="#4a2a14"/><rect x="200" y="860" width="2000" height="10" fill="#8a5a30"/>`);
  for (const lx of [230, 1180, 2140]) b.push(`<rect x="${lx}" y="950" width="30" height="120" fill="#3a2010"/>`);
  // kilim: stripes and diamonds
  let kilim = `<rect x="260" y="820" width="1880" height="50" fill="#9a2a22"/>`;
  for (let x = 280; x < 2120; x += 60) kilim += `<path d="M${x} 845l20 -18l20 18l-20 18z" fill="${r.pick(['#e8b04a', '#1f4a6a', '#f2e6d0'])}"/>`;
  kilim += `<rect x="260" y="820" width="1880" height="6" fill="#e8b04a"/><rect x="260" y="864" width="1880" height="6" fill="#1f4a6a"/>`;
  b.push(kilim);
  b.push(`<rect x="1700" y="740" width="320" height="90" rx="45" fill="${c.g('bol', [[0, '#c84a3a'], [1, '#6a1a14']])}"/>`);
  for (let k = 0; k < 6; k++) b.push(`<path d="M${1740 + k * 46} 748v74" stroke="#e8b04a" stroke-width="4" opacity=".6"/>`);
  // the samovar: brass body, tap, chimney with the teapot on top
  const sx = 900, sy = 820;
  c.defs.push(L.lin('brass', [[0, '#5a3a10'], [.3, '#e8c060'], [.45, '#fff0b0'], [.65, '#c8902a'], [1, '#4a2a08']], 0, 0, 1, 0));
  b.push(c.glow(sx, sy - 160, 300, '#ffb35c', .25));
  b.push(`<path d="M${sx - 60} ${sy}h120l-20 -30h-80z" fill="url(#brass)"/>`);
  b.push(`<path d="M${sx - 70} ${sy - 30}Q${sx - 90} ${sy - 140} ${sx - 60} ${sy - 210}H${sx + 60}Q${sx + 90} ${sy - 140} ${sx + 70} ${sy - 30}Z" fill="url(#brass)"/>`);
  for (let k = 0; k < 6; k++) b.push(`<path d="M${sx - 66 + k * 26} ${sy - 40}V${sy - 200}" stroke="#5a3a10" stroke-opacity=".35" stroke-width="3"/>`);
  b.push(`<path d="M${sx - 90} ${sy - 170}q-30 10 -24 40M${sx + 90} ${sy - 170}q30 10 24 40" stroke="url(#brass)" stroke-width="10" fill="none"/>`);
  b.push(`<path d="M${sx + 70} ${sy - 80}h40v14h-12v20h-10v-20h-18z" fill="url(#brass)"/>`);
  b.push(`<rect x="${sx - 40}" y="${sy - 236}" width="80" height="26" fill="url(#brass)"/>`);
  // teapot on top, white china with blue flowers
  b.push(`<path d="M${sx - 46} ${sy - 236}Q${sx - 60} ${sy - 300} ${sx} ${sy - 306}Q${sx + 60} ${sy - 300} ${sx + 46} ${sy - 236}Z" fill="${c.g('tp', [[0, '#f8f4ec'], [1, '#b8b0a0']], 0, 0, 1, 0)}"/><path d="M${sx + 44} ${sy - 270}q34 -10 44 -40" stroke="#e8e0d0" stroke-width="10" fill="none"/><circle cx="${sx}" cy="${sy - 312}" r="8" fill="#e8e0d0"/>`);
  for (let k = 0; k < 5; k++) b.push(`<circle cx="${sx - 26 + k * 13}" cy="${sy - 266 + (k % 2) * 10}" r="5" fill="#3a5aa8" opacity=".8"/>`);
  // tea glasses on saucers: amber tea, a glint
  const glasses = [];
  for (const [gx, gy] of [[1080, 816], [1160, 818], [1250, 814], [700, 818]]) {
    glasses.push([gx, gy - 60]);
    b.push(`<ellipse cx="${gx}" cy="${gy + 4}" rx="34" ry="8" fill="#e8e0d0"/><path d="M${gx - 18} ${gy}Q${gx - 26} ${gy - 30} ${gx - 18} ${gy - 54}H${gx + 18}Q${gx + 26} ${gy - 30} ${gx + 18} ${gy}Z" fill="${c.g('tea', [[0, '#c84a10'], [1, '#7a1a06']])}" opacity=".9"/><path d="M${gx - 18} ${gy - 54}H${gx + 18}" stroke="#ffe0b0" stroke-width="3"/><path d="M${gx - 12} ${gy - 46}q-4 20 2 38" stroke="#fff" stroke-opacity=".5" stroke-width="3" fill="none"/>`);
  }
  // hanging lamps in the arches
  const lamps = [];
  for (const [lx, ly] of [[400, 360], [1200, 210], [2000, 360], [800, 250], [1600, 250]]) {
    lamps.push([lx, ly + 40]);
    b.push(`<path d="M${lx} 0V${ly}" stroke="#2a1a0a" stroke-width="3"/>`, c.glow(lx, ly + 40, 220, '#ffb35c', .4));
    b.push(`<path d="M${lx - 30} ${ly}h60l-10 70h-40z" fill="url(#brass)" opacity=".9"/><path d="M${lx - 22} ${ly + 14}h44l-6 44h-32z" fill="#ffd27a"/>`);
    for (let k = 0; k < 4; k++) b.push(`<circle cx="${lx - 15 + k * 10}" cy="${ly + 30}" r="3" fill="#8a4a10"/>`);
  }
  const plate = c.finish({ vignette: .6 });

  const steam = L.sprite(120, 240, L.blurF('s', 8, 40) + L.lin('g', [[0, '#ffffff', 0], [.4, '#f4ece0', .45], [1, '#f4ece0', .05]], 0, 0, 0, 1),
    `<path d="M60 236C40 200 80 170 60 130C40 90 80 60 64 10" stroke="url(#g)" stroke-width="22" fill="none" filter="url(#s)"/>`);
  const sprites = { steam, lamp: L.softDot(260, '#ffb35c', { core: .15, coreOp: .55, mid: .25 }) };
  const layers = [
    { sprite: 'lamp', at: lamps, size: [220, 280], twinkle: { lo: .6, hi: 1, dur: [.4, .9] }, pulse: { lo: .95, hi: 1.05, dur: [.6, 1.2] } },
    // steam: rises from the teapot and the glasses, widening and fading, swaying side to side
    { sprite: 'steam', at: [[sx + 70, sy - 350], [sx + 70, sy - 350], ...glasses, ...glasses], size: [50, 80], op: [.5, .8], origin: [.5, 1], move: { dx: [-20, 20], dy: [-160, -110], dur: [4, 6] }, fade: [[0, 0], [.2, 1], [1, 0]], grow: [.6, 1.4], wag: { deg: [6, 12], dur: [1.5, 2.5] } },
  ];
  return { key: 'teahouse', scene: 'Steam', W, H, ax: .5, ay: .8, plate, sprites, layers };
}
build.key = 'teahouse';
module.exports = build;
