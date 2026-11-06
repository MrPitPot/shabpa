'use strict';
const L = require('../lib');
const { f } = L;

// Warzone: a ruined city at dusk seen through a tactical display. Olive haze, amber sky, broken towers, a tank and a
// helicopter in silhouette, smoke columns. Over it the HUD: corner brackets, a compass tape, a reticle and range marks.
// A radar sweep turns in the corner, tracer rounds fly up-right head first, and target markers blink.
function build() {
  const W = 2400, H = 1200, r = L.rng(4040);
  const c = L.canvas(W, H), b = c.b;
  b.push(`<rect width="${W}" height="${H}" fill="${c.g('sky', [[0, '#14170f'], [.45, '#3a3418'], [.7, '#8a5a1c'], [.82, '#b8742a'], [1, '#2a2414']])}"/>`);
  b.push(c.glow(1500, 800, 900, '#f2a33a', .3));
  // smoke columns leaning with the wind
  for (const [x, w0] of [[520, 140], [1320, 200], [2050, 160]]) {
    b.push(`<path d="M${x - w0 / 2} 900C${x - w0} 600 ${x + 200} 400 ${x + 500} 0L${x + 900} 0C${x + 500} 400 ${x + w0} 640 ${x + w0 / 2} 900Z" fill="#000" ${c.cloud({ freq: .006, oct: 5, seed: x, color: L.rgb01('#2a2a22'), contrast: 1.8, bias: -.3 })} opacity=".7"/>`);
    b.push(c.glow(x, 880, 200, '#ff8a2a', .45));
  }
  // the city: far towers in haze, near ruins in black with holes and rebar
  const city = (base, hMin, hMax, col, ruin) => {
    let s = '';
    for (let x = -40; x < W + 40; x += r(70, 160)) {
      const bw = r(60, 150), bh = r(hMin, hMax);
      let p = `M${f(x)} ${base}V${f(base - bh)}`;
      if (ruin && r() < .7) { const k = r.int(3, 6); for (let q = 1; q <= k; q++) p += `L${f(x + bw * q / k)} ${f(base - bh + r(-10, 60) * (q % 2 ? 1 : -.3))}`; }
      else p += `H${f(x + bw)}`;
      p += `V${base}Z`;
      s += `<path d="${p}" fill="${col}"/>`;
      if (ruin) for (let q = 0; q < 6; q++) s += `<rect x="${f(x + r(8, bw - 20))}" y="${f(base - r(20, bh - 30))}" width="${f(r(8, 18))}" height="${f(r(10, 22))}" fill="${r() < .2 ? '#ff9a3a' : '#5a4a2a'}" opacity="${f(r(.3, .8))}"/>`;
    }
    return s;
  };
  b.push(city(880, 160, 420, '#4a4428', false));
  b.push(`<rect y="600" width="${W}" height="320" fill="${c.g('h1', [[0, '#a87a3a', 0], [1, '#a87a3a', .35]])}"/>`);
  b.push(city(960, 120, 340, '#1a1a10', true));
  b.push(`<rect y="940" width="${W}" height="260" fill="${c.g('gr', [[0, '#1c1c12'], [1, '#0a0a06']])}"/>`);
  // rubble and a tank in silhouette
  for (let i = 0; i < 60; i++) b.push(`<path d="${L.blob(r, r(0, W), r(950, 1010), r(10, 40), r(6, 18), .4, 6)}" fill="#0f0f09"/>`);
  const tank = (x, y, s) => `<g transform="translate(${x} ${y}) scale(${s})" fill="#070805"><path d="M-160 0Q-170 -30 -140 -36H140Q170 -30 160 0Q150 20 120 22H-120Q-150 20 -160 0Z"/><path d="M-110 -36L-90 -70H80L104 -36Z"/><rect x="60" y="-64" width="230" height="12" rx="4"/><rect x="-60" y="-86" width="50" height="18"/>${[-120, -80, -40, 0, 40, 80, 120].map((q) => `<circle cx="${q}" cy="2" r="14" fill="#1c1c12"/>`).join('')}</g>`;
  b.push(tank(1780, 1000, 1));
  const heli = `<g transform="translate(620 300) rotate(-6)" fill="#0c0c08"><path d="M-110 0Q-100 -30 -40 -32H40Q80 -30 92 -6Q96 10 70 14H-60Z"/><path d="M-110 -6L-250 -20L-250 -8L-110 6Z"/><path d="M-250 -40V12" stroke="#0c0c08" stroke-width="6"/><path d="M-40 -32V-46" stroke="#0c0c08" stroke-width="8"/><path d="M-260 -48H190" stroke="#0c0c08" stroke-width="5" opacity=".7"/><path d="M-60 18H60M-50 14V18M40 14V18" stroke="#0c0c08" stroke-width="4"/></g>`;
  b.push(heli);
  // HUD: scanlines, corner brackets, compass tape, reticle, range ladder
  let scan = '';
  for (let y = 0; y < H; y += 4) scan += `M0 ${y}H${W}`;
  b.push(`<path d="${scan}" stroke="#000" stroke-opacity=".12" stroke-width="1.2"/>`);
  const hud = '#e8c070';
  let hd = '';
  for (const [x, y, dx, dy] of [[60, 60, 1, 1], [W - 60, 60, -1, 1], [60, H - 60, 1, -1], [W - 60, H - 60, -1, -1]]) hd += `M${x} ${y + dy * 90}V${y}H${x + dx * 90}`;
  for (let k = 0; k <= 40; k++) { const x = 700 + k * 25; hd += `M${x} 60V${k % 5 === 0 ? 84 : 72}`; }
  hd += `M1200 92l-10 -14h20z`;
  hd += `M1200 520v-60M1200 680v60M1120 600h-60M1280 600h60`;
  for (let k = 0; k < 8; k++) { const y = 360 + k * 60; hd += `M${W - 220} ${y}h${k % 2 ? 20 : 40}`; }
  b.push(`<path d="${hd}" stroke="${hud}" stroke-opacity=".75" stroke-width="2.4" fill="none"/>`);
  b.push(`<circle cx="1200" cy="600" r="70" fill="none" stroke="${hud}" stroke-opacity=".5" stroke-width="1.6" stroke-dasharray="6 10"/><circle cx="1200" cy="600" r="3" fill="${hud}"/>`);
  const txt = (x, y, s, size = 22, anchor = 'start') => `<text x="${x}" y="${y}" font-family="Consolas, monospace" font-size="${size}" fill="${hud}" fill-opacity=".8" text-anchor="${anchor}" letter-spacing="2">${s}</text>`;
  b.push(txt(700, 120, 'N', 22, 'middle'), txt(950, 120, 'NE', 22, 'middle'), txt(1200, 120, '045', 22, 'middle'), txt(1450, 120, 'E', 22, 'middle'), txt(1700, 120, 'SE', 22, 'middle'));
  b.push(txt(90, H - 90, 'GRID 38S MB 4417 2093'), txt(90, H - 122, 'ALT 0412  SPD 000'), txt(W - 90, 120, 'REC ●', 22, 'end'), txt(W - 240, 350, 'RNG', 18), txt(1290, 690, '1250m', 18));
  // the radar dish in the lower right
  const rx = W - 260, ry = H - 260, rr = 170;
  b.push(`<circle cx="${rx}" cy="${ry}" r="${rr}" fill="#0c1008" fill-opacity=".7" stroke="${hud}" stroke-opacity=".7" stroke-width="2"/>`);
  b.push(`<circle cx="${rx}" cy="${ry}" r="${rr * .66}" fill="none" stroke="${hud}" stroke-opacity=".35"/><circle cx="${rx}" cy="${ry}" r="${rr * .33}" fill="none" stroke="${hud}" stroke-opacity=".35"/><path d="M${rx - rr} ${ry}H${rx + rr}M${rx} ${ry - rr}V${ry + rr}" stroke="${hud}" stroke-opacity=".3"/>`);
  const plate = c.finish({ vignette: .6 });

  const sprites = {
    sweep: L.sprite(340, 340, `<radialGradient id="x" cx=".5" cy=".5" r=".5"><stop offset="0" stop-color="#f2a33a" stop-opacity=".1"/><stop offset="1" stop-color="#f2a33a" stop-opacity=".5"/></radialGradient>`,
      `<path d="M170 170L340 170A170 170 0 0 0 290 50Z" fill="url(#x)" opacity=".9"/><path d="M170 170L340 170" stroke="#ffd27a" stroke-width="3"/>`),
    tracer: L.sprite(160, 8, L.lin('t', [[0, '#ffb347', 0], [.8, '#ffd27a', .8], [1, '#fff6d0', 1]], 0, 0, 1, 0), `<rect width="160" height="4" y="2" rx="2" fill="url(#t)"/>`),
    blip: L.sprite(40, 40, '', `<circle cx="20" cy="20" r="8" fill="#ffd27a"/><circle cx="20" cy="20" r="17" fill="none" stroke="#ffd27a" stroke-width="2" opacity=".6"/>`),
    mark: L.sprite(80, 80, '', `<path d="M4 24V4H24M56 4H76V24M76 56V76H56M24 76H4V56" stroke="#ffb347" stroke-width="4" fill="none"/><circle cx="40" cy="40" r="4" fill="#ffb347"/>`),
    flash: L.softDot(160, '#ffd27a', { core: .15, coreOp: .9, mid: .3 }),
  };
  const ang = -24, rad = ang * Math.PI / 180;
  const layers = [
    { sprite: 'sweep', at: [[rx, ry]], size: 340, spin: { deg: 360, dur: 4 }, fps: 15 },
    { sprite: 'blip', at: [[rx + 60, ry - 80], [rx - 90, ry + 40], [rx + 20, ry + 110]], size: 30, twinkle: { lo: 0, hi: 1, dur: 2 }, phaseStep: .3, phase: 0 },
    // tracers: fly up and to the right, the bright head leading
    { sprite: 'tracer', n: 7, x: [.05, .5], y: [.75, .9], size: [110, 170], rot: ang, move: { dx: Math.round(1500 * Math.cos(rad)), dy: Math.round(1500 * Math.sin(rad)), dur: [3, 6], travel: .25 }, fade: [[0, 0], [.02, 1], [.22, .8], [.25, 0], [1, 0]], fps: 24 },
    { sprite: 'flash', at: [[640, 880], [1400, 900], [2000, 890], [1100, 930]], size: [140, 220], fade: [[0, 0], [.03, 1], [.12, 0], [1, 0]], fadeDur: [4, 7] },
    { sprite: 'mark', at: [[1780, 940], [620, 300], [980, 820]], size: [60, 90], twinkle: { lo: .2, hi: 1, dur: [.8, 1.4] }, pulse: { lo: .92, hi: 1.08, dur: 1.2 } },
  ];
  return { key: 'warzone', scene: 'Tactical', W, H, ax: .5, ay: .7, plate, sprites, layers };
}
build.key = 'warzone';
module.exports = build;
