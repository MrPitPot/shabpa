'use strict';
const L = require('../lib');
const { f } = L;

// Storm: heavy cloud over a city on the edge of the sea. Rolling, lit-from-within storm clouds, a dark skyline with a few
// windows, waves breaking on the sea wall, wet street lights. Rain falls slanted (each streak leaning the way it falls),
// and every few seconds lightning cracks and the sky flashes.
function build() {
  const W = 2400, H = 1200, r = L.rng(8080);
  const c = L.canvas(W, H), b = c.b;
  b.push(`<rect width="${W}" height="${H}" fill="${c.g('sky', [[0, '#07090f'], [.5, '#1b2133'], [.75, '#2a3048'], [1, '#0a0c12']])}"/>`);
  // clouds: three layers of turbulence, the lower ones lit from inside
  b.push(`<rect width="${W}" height="800" fill="#000" ${c.cloud({ freq: .0018, oct: 6, seed: 41, color: L.rgb01('#5a6488'), contrast: 1.6, bias: -.45, stretchX: 2 })} opacity=".8"/>`);
  b.push(`<rect width="${W}" height="800" fill="#000" ${c.cloud({ freq: .0032, oct: 5, seed: 42, color: L.rgb01('#0c0e16'), contrast: 2, bias: -.6, stretchX: 2 })} opacity=".85"/>`);
  b.push(c.glow(1500, 380, 500, '#b7c7ff', .18), c.glow(600, 300, 400, '#b7c7ff', .12));
  // rain curtains in the distance
  for (let k = 0; k < 8; k++) { const x = r(0, W), w0 = r(120, 300); b.push(`<path d="M${f(x)} 300L${f(x + w0)} 300L${f(x + w0 - 160)} 860L${f(x - 160)} 860Z" fill="#8a96b8" opacity=".08" ${c.blur(20)}/>`); }
  // the sea and the skyline
  b.push(`<rect y="820" width="${W}" height="380" fill="${c.g('sea', [[0, '#1a2030'], [1, '#05070a']])}"/>`);
  for (let i = 0; i < 160; i++) { const x = r(0, W), y = r(830, 1000); b.push(`<path d="M${f(x)} ${f(y)}q${f(r(10, 30))} -${f(r(4, 10))} ${f(r(30, 80))} 0" stroke="#b7c7ff" stroke-opacity="${f(r(.1, .35))}" stroke-width="2" fill="none"/>`); }
  let city = '';
  for (let x = 900; x < W + 40; x += r(50, 120)) {
    const bw = r(50, 110), bh = r(120, 420);
    city += `<rect x="${f(x)}" y="${f(840 - bh)}" width="${f(bw)}" height="${f(bh)}" fill="#07080d"/>`;
    for (let wy = 840 - bh + 16; wy < 830; wy += 22) for (let wx = x + 8; wx < x + bw - 10; wx += 16) if (r() < .12) city += `<rect x="${f(wx)}" y="${f(wy)}" width="7" height="10" fill="${r.pick(['#ffd08a', '#9cc4e8'])}" opacity=".8"/>`;
    if (r() < .15) city += `<path d="M${f(x + bw / 2)} ${f(840 - bh)}v-60" stroke="#07080d" stroke-width="3"/><circle cx="${f(x + bw / 2)}" cy="${f(780 - bh)}" r="3" fill="#ff4a4a"/>`;
  }
  b.push(city);
  b.push(`<rect y="840" x="0" width="${W}" height="18" fill="#0b0d12"/>`);
  // the lighthouse on the left breakwater
  b.push(`<path d="M260 840L280 560H330L350 840Z" fill="#0b0d14"/><rect x="272" y="530" width="66" height="34" fill="#11141c"/><path d="M266 530L305 490L344 530Z" fill="#0b0d14"/>`, c.glow(305, 546, 80, '#ffe6a0', .7));
  // waves breaking on the wall
  for (let k = 0; k < 9; k++) { const x = r(0, W); b.push(`<path d="${L.blob(r, x, 846, r(50, 110), r(8, 18), .4, 9)}" fill="#c8d4f0" opacity="${f(r(.08, .2))}" ${c.blur(6)}/>`); }
  // wet promenade in front with street lamps and their reflections
  b.push(`<rect y="980" width="${W}" height="220" fill="${c.g('pr', [[0, '#141824'], [1, '#05060a']])}"/>`);
  for (let k = 0; k < 6; k++) {
    const x = 160 + k * 420;
    b.push(`<rect x="${x - 4}" y="800" width="8" height="200" fill="#05060a"/><path d="M${x - 4} 804h40v6h-40z" fill="#05060a"/>`, c.glow(x + 30, 816, 90, '#ffd08a', .6), `<rect x="${x + 10}" y="990" width="40" height="200" fill="${c.g('rf', [[0, '#ffd08a', .45], [1, '#ffd08a', 0]])}" ${c.blur(8)}/>`);
  }
  const plate = c.finish({ vignette: .55 });

  // a lightning bolt: a jagged main channel with branches, a glow around it
  let bolt = 'M120 0', x = 120;
  const branches = [];
  for (let y = 40; y <= 700; y += 40) { x += r(-40, 40); bolt += `L${f(x)} ${y}`; if (r() < .3) branches.push([x, y]); }
  let br = '';
  for (const [bx, by] of branches) { let d = `M${f(bx)} ${by}`, xx = bx, yy = by; for (let k = 0; k < 4; k++) { xx += r(-40, 50); yy += r(20, 40); d += `L${f(xx)} ${f(yy)}`; } br += d; }
  const sprites = {
    bolt: L.sprite(240, 720, L.blurF('bg', 8, 40), `<g filter="url(#bg)"><path d="${bolt}${br}" stroke="#b7c7ff" stroke-width="12" fill="none"/></g><path d="${bolt}" stroke="#ffffff" stroke-width="4" fill="none"/><path d="${br}" stroke="#e6ecff" stroke-width="2" fill="none"/>`),
    drop: L.sprite(4, 90, L.lin('d', [[0, '#c8d4f0', 0], [1, '#e6ecff', .7]]), `<rect x="1" width="2" height="90" rx="1" fill="url(#d)"/>`),
  };
  // rain falls down and to the left; each streak leans the same way it moves
  const dx = -340, dy = 1400, lean = Math.atan2(-dx, dy) * 180 / Math.PI;
  const layers = [
    { sprite: '#C8D4FF', rect: [2400, 1200], fade: [[0, 0], [.02, .35], [.04, .05], [.06, .28], [.12, 0], [1, 0]], fadeDur: 7, phase: .3 },
    { sprite: 'bolt', at: [[1440, 215]], size: 150, fade: [[0, 0], [.02, 1], [.04, .2], [.06, 1], [.12, 0], [1, 0]], fadeDur: 7, phase: .3 },
    { sprite: 'bolt', at: [[640, 260]], size: 140, flip: true, fade: [[0, 0], [.02, 1], [.05, 0], [1, 0]], fadeDur: 11, phase: .75 },
    { sprite: 'drop', n: 70, x: [0, 1.15], y: [-.15, -.08], size: [2.5, 4], op: [.3, .7], rot: lean, move: { dx, dy, dur: [.9, 1.3] }, fps: 15 },
  ];
  return { key: 'storm', scene: 'Storm', W, H, ax: .5, ay: .7, plate, sprites, layers };
}
build.key = 'storm';
module.exports = build;
