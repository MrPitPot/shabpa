'use strict';
const L = require('../lib');
const { f } = L;

// Persepolis: the Apadana at golden hour. Tall fluted columns, a few still crowned with their double-bull capitals,
// others broken; the terrace with stepped merlons and the great stair carved with rows of tribute bearers; the
// mountain behind; long shadows and gold dust in the low sun. Dust drifts, the sun breathes, swallows cross.
function build() {
  const W = 2400, H = 1200, r = L.rng(5180);
  const c = L.canvas(W, H), b = c.b;
  b.push(`<rect width="${W}" height="${H}" fill="${c.g('sky', [[0, '#2a1a10'], [.3, '#6a3e1c'], [.6, '#c98a3a'], [.78, '#efc070'], [1, '#8a5a2a']])}"/>`);
  b.push(c.glow(1880, 520, 900, '#ffd890', .55));
  b.push(`<circle cx="1880" cy="520" r="78" fill="#fff2cc"/>`);
  b.push(c.glow(1880, 520, 200, '#fff6dc', .8));
  b.push(`<rect width="${W}" height="560" fill="#000" ${c.cloud({ freq: .0022, oct: 4, seed: 9, color: L.rgb01('#ffd0a0'), contrast: 1.6, bias: -.8, stretchX: 5 })} opacity=".35"/>`);
  // the mountain (Kuh-e Rahmat) behind, hazy
  b.push(L.mountains(c, r, { base: 700, amp: 260, rough: .5, detail: 6, light: 1, rock: ['#8a5a36', '#b88252'], snow: ['#8a5a36', '#b88252'], snowline: 0, bottom: 760 }).svg);
  b.push(`<rect y="380" width="${W}" height="400" fill="${c.g('hz', [[0, '#f2c27a', 0], [1, '#f2c27a', .55]])}"/>`);
  // the terrace: a long wall of dressed stone, merlons on top, the stair in the middle with its relief
  const tTop = 860;
  b.push(`<rect y="${tTop}" width="${W}" height="${H - tTop}" fill="${c.g('wall', [[0, '#c48a4e'], [.4, '#9a6638'], [1, '#4a2e18']])}"/>`);
  let blocks = '';
  for (let y = tTop; y < H; y += 46) for (let x = -((y / 46) % 2) * 60; x < W; x += 120) blocks += `<rect x="${x}" y="${y}" width="120" height="46" fill="none" stroke="#3a2210" stroke-opacity=".35" stroke-width="2"/>`;
  b.push(blocks);
  // stepped merlons along the top edge
  let mer = '';
  for (let x = 0; x < W; x += 70) mer += `<path d="M${x + 6} ${tTop}v-18h10v-12h10v-12h18v12h10v12h10v18z" fill="#b07a44"/><path d="M${x + 6} ${tTop}v-18h10v-12h10v-12h4v42z" fill="#e0a868" opacity=".6"/>`;
  b.push(mer);
  // the stair: a band of relief with rows of figures walking left
  const sx = 700, sw = 1000;
  b.push(`<rect x="${sx}" y="${tTop + 30}" width="${sw}" height="260" fill="#b27c46"/>`);
  for (let row = 0; row < 3; row++) {
    const y = tTop + 60 + row * 80;
    b.push(`<rect x="${sx + 20}" y="${y + 58}" width="${sw - 40}" height="6" fill="#6a4020" opacity=".6"/>`);
    for (let k = 0; k < 24; k++) {
      const x = sx + 50 + k * 39;
      b.push(`<g transform="translate(${x} ${y})"><path d="M0 58L4 26Q2 14 8 10Q8 0 15 0Q22 0 21 10Q26 16 24 28L26 58Z" fill="#7a4a24" opacity=".75"/><path d="M8 58L10 30Q11 20 15 16" stroke="#f2c27a" stroke-opacity=".5" stroke-width="2" fill="none"/>${k % 3 === 0 ? '<rect x="22" y="24" width="12" height="8" fill="#7a4a24" opacity=".7"/>' : ''}</g>`);
    }
  }
  // rosette band above the relief
  for (let k = 0; k < 40; k++) { const x = sx + 25 + k * 24.5; b.push(`<circle cx="${f(x)}" cy="${tTop + 44}" r="8" fill="#7a4a24" opacity=".6"/><circle cx="${f(x)}" cy="${tTop + 44}" r="3" fill="#f2c27a" opacity=".6"/>`); }
  // columns: base, fluted shaft lit from the right, and on some a double-bull capital
  const capital = (x, y, s) => {
    let p = `<g transform="translate(${x} ${y}) scale(${s})">`;
    p += `<rect x="-26" y="-40" width="52" height="40" fill="url(#stone)"/>`;
    for (const d of [-1, 1]) {
      // the bull's body and folded leg on each side, head and horn at the end
      p += `<path d="M0 -40L${d * 70} -40Q${d * 98} -44 ${d * 102} -64Q${d * 104} -82 ${d * 88} -86L${d * 80} -78Q${d * 70} -70 ${d * 50} -70L0 -70Z" fill="url(#stone)"/>`;
      p += `<path d="M${d * 86} -86Q${d * 96} -104 ${d * 82} -110" stroke="#5a3418" stroke-width="5" fill="none"/>`;
      p += `<path d="M${d * 70} -40L${d * 74} -18L${d * 60} -18L${d * 56} -40Z" fill="url(#stone)"/>`;
      p += `<circle cx="${d * 92}" cy="-70" r="3" fill="#3a2210"/>`;
    }
    p += `<path d="M-60 -70H60V-78H-60Z" fill="#6a4020" opacity=".5"/>`;
    return p + '</g>';
  };
  c.defs.push(L.lin('stone', [[0, '#6a4424'], [.45, '#b8824a'], [.8, '#ecbc7a'], [1, '#c8925a']], 0, 0, 1, 0));
  const cols = [];
  for (let k = 0; k < 13; k++) {
    const x = 120 + k * 180 + r(-20, 20), broken = r() < .38, depth = k % 2 === 0 ? 1 : .82;
    const topY = broken ? r(420, 700) : r(150, 240), cw = 46 * depth, base = tTop - 30;
    // long shadow thrown to the left on the terrace
    b.push(`<path d="M${f(x - cw / 2)} ${tTop - 2}L${f(x - cw / 2 - 260)} ${tTop + 70}L${f(x - cw / 2 - 200)} ${tTop + 70}L${f(x + cw / 2)} ${tTop - 2}Z" fill="#3a2210" opacity=".22" ${c.blur(4)}/>`);
    b.push(`<path d="M${f(x - cw * .9)} ${base + 30}Q${f(x - cw * .9)} ${base} ${f(x - cw * .55)} ${base - 10}H${f(x + cw * .55)}Q${f(x + cw * .9)} ${base} ${f(x + cw * .9)} ${base + 30}Z" fill="url(#stone)"/>`);
    b.push(`<rect x="${f(x - cw / 2)}" y="${f(topY)}" width="${f(cw)}" height="${f(base - 10 - topY)}" fill="url(#stone)"/>`);
    let fl = '';
    for (let q = 1; q < 8; q++) fl += `M${f(x - cw / 2 + cw * q / 8)} ${f(topY)}V${f(base - 10)}`;
    b.push(`<path d="${fl}" stroke="#4a2c14" stroke-opacity=".35" stroke-width="1.4"/>`);
    if (broken) b.push(`<path d="M${f(x - cw / 2)} ${f(topY)}l${f(cw * .3)} -14l${f(cw * .2)} 10l${f(cw * .3)} -18l${f(cw * .2)} 22z" fill="url(#stone)"/>`);
    else { b.push(capital(x, topY, .9 * depth)); cols.push([x, topY]); }
  }
  // fallen drums lying on the terrace
  for (const x of [520, 1820]) b.push(`<ellipse cx="${x}" cy="${tTop + 6}" rx="70" ry="16" fill="#6a4020" opacity=".4" ${c.blur(4)}/><rect x="${x - 60}" y="${tTop - 34}" width="120" height="36" rx="8" fill="url(#stone)"/><ellipse cx="${x + 60}" cy="${tTop - 16}" rx="10" ry="18" fill="#c8925a"/>`);
  // warm haze over everything at the bottom, light rays from the sun
  for (let k = 0; k < 7; k++) { const a = Math.PI * r(.85, 1.2), sp = .025; b.push(`<path d="M1880 520L${f(1880 + Math.cos(a - sp) * 2600)} ${f(520 + Math.sin(a - sp) * 2600)}L${f(1880 + Math.cos(a + sp) * 2600)} ${f(520 + Math.sin(a + sp) * 2600)}Z" fill="#fff2cc" opacity=".06" ${c.blur(20)}/>`); }
  b.push(`<rect y="700" width="${W}" height="500" fill="${c.g('dust', [[0, '#f2c27a', 0], [1, '#f2c27a', .2]])}"/>`);
  const plate = c.finish({ vignette: .55 });

  const swallow = (up) => L.sprite(50, 30, '', `<path d="M2 ${up ? 4 : 24}Q14 ${up ? 10 : 18} 22 15L30 15Q36 ${up ? 10 : 18} 48 ${up ? 4 : 24}Q36 ${up ? 14 : 20} 30 18L22 18Q14 ${up ? 14 : 20} 2 ${up ? 4 : 24}Z" fill="#2a160a"/><path d="M22 15L14 9M22 18L12 22" stroke="#2a160a" stroke-width="2"/><circle cx="31" cy="16" r="3" fill="#2a160a"/>`);
  const sprites = { mote: L.softDot(14, '#ffe2a0', { core: .3 }), sun: L.softDot(500, '#fff2cc', { core: .2, coreOp: .5, mid: .2 }), swUp: swallow(true), swDown: swallow(false) };
  const layers = [
    { sprite: 'sun', at: [[1880, 520]], size: 700, pulse: { lo: .9, hi: 1.1, dur: 8 }, twinkle: { lo: .5, hi: .9, dur: 6 } },
    { sprite: 'mote', n: 40, x: [0, 1], y: [.2, .95], size: [5, 11], op: [.4, .9], move: { dx: [160, 320], dy: [-60, 20], dur: [18, 30] }, fade: [[0, 0], [.2, 1], [.8, 1], [1, 0]], twinkle: { lo: .3, hi: 1, dur: [2, 4] } },
    { sprite: ['swUp', 'swDown'], frames: { fps: 5 }, n: 3, x: [-.1, -.03], y: [.2, .4], size: [26, 36], op: .85, move: { dx: [2700, 2900], dy: [-200, 100], dur: [22, 34] }, sway: { x: 0, y: [20, 40], dur: [2, 3] } },
  ];
  return { key: 'persepolis', scene: 'Persepolis', W, H, ax: .5, ay: .8, plate, sprites, layers };
}
build.key = 'persepolis';
module.exports = build;
