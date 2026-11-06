'use strict';
const L = require('../lib');
const { f } = L;

// Space station: low orbit over the night side of the Earth. The planet's curve fills the bottom with city lights,
// cloud and the thin blue line of the atmosphere at sunrise; a truss of the station with solar panels reaches in from the
// right. A radar sweep turns on the station display, a satellite drifts past and the warning lights blink.
function build() {
  const W = 2400, H = 1200, r = L.rng(4004);
  const c = L.canvas(W, H), b = c.b;
  b.push(`<rect width="${W}" height="${H}" fill="#03050a"/>`);
  b.push(L.starfield(c.defs, r, { x1: W, y0: 0, y1: 800, n: 900, color: '#ecf4fa', bright: 30, maxR: 1.2 }));
  // the Earth: a huge circle below; night side with city lights, a sunrise glow on the left limb
  const ex = 1100, ey = 2900, er = 2200;
  c.defs.push(`<clipPath id="earth"><circle cx="${ex}" cy="${ey}" r="${er}"/></clipPath>`);
  // the atmosphere: a soft luminous shell hugging the limb
  b.push(`<circle cx="${ex}" cy="${ey}" r="${er + 26}" fill="none" stroke="#3a9ae0" stroke-width="56" opacity=".28" ${c.blur(22)}/>`);
  b.push(`<circle cx="${ex}" cy="${ey}" r="${er + 10}" fill="none" stroke="#6ac8f5" stroke-width="18" opacity=".45" ${c.blur(6)}/>`);
  b.push(`<circle cx="${ex}" cy="${ey}" r="${er}" fill="${c.rg('ea', [[0, '#04101c'], [.92, '#0a2234'], [1, '#14486a']], .5, .5, .5)}"/>`);
  // continents and clouds (turbulence inside the disc), then city lights scattered in clusters on land
  b.push(`<g clip-path="url(#earth)"><rect y="600" width="${W}" height="${H}" fill="#000" ${c.cloud({ freq: .003, oct: 5, seed: 61, color: L.rgb01('#1a2a24'), contrast: 2.4, bias: -.9 })} opacity=".9"/>
    <rect y="600" width="${W}" height="${H}" fill="#000" ${c.cloud({ freq: .006, oct: 5, seed: 62, color: L.rgb01('#6a7a8a'), contrast: 2, bias: -1 })} opacity=".45"/></g>`);
  let lights = '';
  for (let k = 0; k < 40; k++) {
    const cx = r(0, W), cy = r(760, H), n = r.int(10, 60);
    for (let q = 0; q < n; q++) { const x = cx + r.gauss() * 50, y = cy + r.gauss() * 18; if (Math.hypot(x - ex, y - ey) < er - 6) lights += `<circle cx="${f(x)}" cy="${f(y)}" r="${f(r(.6, 1.8))}" fill="${r.pick(['#ffd08a', '#ffb860', '#fff0c8'])}" opacity="${f(r(.4, 1))}"/>`; }
  }
  b.push(lights, `<g ${c.blur(3)} opacity=".7">${lights}</g>`);
  // sunrise along the left limb
  b.push(`<g clip-path="url(#earth)">${c.glow(-200, 900, 900, '#ffb860', .3)}</g>`);
  c.defs.push(`<clipPath id="limbL"><rect x="-100" y="400" width="900" height="900"/></clipPath>`);
  b.push(`<g clip-path="url(#limbL)"><circle cx="${ex}" cy="${ey}" r="${er + 12}" fill="none" stroke="${c.g('sr', [[0, '#ffc070', .9], [.3, '#ffc070', .5], [.4, '#ffc070', 0], [1, '#ffc070', 0]], 0, 0, 1, 0)}" stroke-width="22" ${c.blur(8)}/></g>`);
  b.push(`<circle cx="${ex}" cy="${ey}" r="${er + 3}" fill="none" stroke="#9ae0ff" stroke-width="3" opacity=".6"/>`);
  // the station: a truss from the right edge, solar panel wings, a module with windows
  const truss = (x0, y0, x1, y1, th) => {
    let s = `<path d="M${x0} ${y0 - th / 2}L${x1} ${y1 - th / 2}M${x0} ${y0 + th / 2}L${x1} ${y1 + th / 2}" stroke="#8a96a4" stroke-width="5"/>`;
    const n = Math.round(Math.hypot(x1 - x0, y1 - y0) / th);
    for (let k = 0; k < n; k++) { const u0 = k / n, u1 = (k + 1) / n; const ax = x0 + (x1 - x0) * u0, ay = y0 + (y1 - y0) * u0, bx = x0 + (x1 - x0) * u1, by = y0 + (y1 - y0) * u1; s += `<path d="M${f(ax)} ${f(ay - th / 2)}L${f(bx)} ${f(by + th / 2)}M${f(ax)} ${f(ay - th / 2)}L${f(ax)} ${f(ay + th / 2)}" stroke="#6a7684" stroke-width="2.5"/>`; }
    return s;
  };
  b.push(truss(1500, 330, W + 40, 260, 44));
  const panel = (x, y, w0, h0, ang) => {
    let s = `<g transform="rotate(${ang} ${x} ${y})"><rect x="${x - w0 / 2}" y="${y - h0 / 2}" width="${w0}" height="${h0}" fill="${c.g('pv', [[0, '#1a3a6a'], [.5, '#2a5a9a'], [1, '#14284a']], 0, 0, 1, 1)}" stroke="#c8a04a" stroke-width="3"/>`;
    for (let k = 1; k < 12; k++) s += `<path d="M${f(x - w0 / 2 + w0 * k / 12)} ${y - h0 / 2}v${h0}" stroke="#0a1a30" stroke-width="2"/>`;
    for (let k = 1; k < 4; k++) s += `<path d="M${x - w0 / 2} ${f(y - h0 / 2 + h0 * k / 4)}h${w0}" stroke="#0a1a30" stroke-width="2"/>`;
    s += `<rect x="${x - w0 / 2}" y="${y - h0 / 2}" width="${w0}" height="${h0}" fill="${c.g('ps', [[0, '#ffffff', .25], [.3, '#ffffff', 0], [1, '#ffffff', 0]], 0, 0, 1, 1)}"/></g>`;
    return s;
  };
  b.push(panel(1760, 130, 380, 150, -4), panel(1760, 520, 380, 150, -4), panel(2200, 110, 380, 150, -4), panel(2200, 500, 380, 150, -4));
  b.push(`<path d="M1760 200V450M2200 180V430" stroke="#8a96a4" stroke-width="8"/>`);
  b.push(`<rect x="1880" y="270" width="260" height="110" rx="40" fill="${c.g('md', [[0, '#e8ecf0'], [.5, '#a8b0b8'], [1, '#4a525a']])}"/>`);
  for (let k = 0; k < 4; k++) b.push(`<circle cx="${1930 + k * 54}" cy="325" r="11" fill="#0a1420" stroke="#4a525a" stroke-width="3"/><circle cx="${1927 + k * 54}" cy="322" r="3" fill="#9ae0ff" opacity=".7"/>`);
  // the HUD panel: a radar screen in the lower right corner of our window
  const rx = 2120, ry = 950, rr = 170;
  b.push(`<circle cx="${rx}" cy="${ry}" r="${rr + 14}" fill="#04080e" opacity=".85" stroke="#52d0f5" stroke-opacity=".6" stroke-width="2"/>`);
  for (const q of [.33, .66, 1]) b.push(`<circle cx="${rx}" cy="${ry}" r="${rr * q}" fill="none" stroke="#52d0f5" stroke-opacity=".3"/>`);
  b.push(`<path d="M${rx - rr} ${ry}H${rx + rr}M${rx} ${ry - rr}V${ry + rr}" stroke="#52d0f5" stroke-opacity=".25"/>`);
  b.push(`<text x="${rx}" y="${ry + rr + 44}" font-family="Consolas, monospace" font-size="20" letter-spacing="4" fill="#7fe3ff" fill-opacity=".7" text-anchor="middle">ORBIT 408 KM · 7.66 KM/S</text>`);
  const plate = c.finish({ vignette: .45 });

  const sprites = {
    sweep: L.sprite(340, 340, `<radialGradient id="x" cx=".5" cy=".5" r=".5"><stop offset="0" stop-color="#52d0f5" stop-opacity=".05"/><stop offset="1" stop-color="#52d0f5" stop-opacity=".55"/></radialGradient>`,
      `<path d="M170 170L340 170A170 170 0 0 0 300 60Z" fill="url(#x)"/><path d="M170 170L340 170" stroke="#9ae0ff" stroke-width="3"/>`),
    blink: L.softDot(40, '#ff4a4a', { core: .2, coreOp: 1, mid: .5 }),
    blinkG: L.softDot(40, '#7dff8a', { core: .2, coreOp: 1, mid: .5 }),
    sat: L.sprite(140, 60, '', `<rect x="56" y="18" width="28" height="24" fill="#c8a04a"/><rect x="2" y="22" width="50" height="16" fill="#2a5a9a" stroke="#8a96a4"/><rect x="88" y="22" width="50" height="16" fill="#2a5a9a" stroke="#8a96a4"/><path d="M70 18V4" stroke="#8a96a4" stroke-width="2"/><circle cx="70" cy="4" r="3" fill="#e8ecf0"/>`),
    ring: L.sprite(300, 300, '', `<circle cx="150" cy="150" r="140" fill="none" stroke="#7fe3ff" stroke-width="2.5" stroke-dasharray="10 8" opacity=".7"/>`),
    blip: L.sprite(30, 30, '', `<circle cx="15" cy="15" r="6" fill="#9ae0ff"/><circle cx="15" cy="15" r="12" fill="none" stroke="#9ae0ff" stroke-width="2" opacity=".6"/>`),
  };
  const layers = [
    { sprite: 'sweep', at: [[rx, ry]], size: 340, spin: { deg: 360, dur: 5 }, fps: 15 },
    { sprite: 'blip', at: [[rx + 70, ry - 60], [rx - 100, ry + 30], [rx + 30, ry + 120]], size: 26, twinkle: { lo: 0, hi: 1, dur: 2.5 }, phase: 0, phaseStep: .3 },
    { sprite: 'ring', at: [[rx, ry]], size: 300, op: .8, spin: { deg: -360, dur: 40 } },
    { sprite: 'blink', at: [[2140, 270], [1560, 330]], size: 34, fade: [[0, 0], [.05, 1], [.15, 0], [1, 0]], fadeDur: 1.6 },
    { sprite: 'blinkG', at: [[1880, 380]], size: 30, fade: [[0, 0], [.05, 1], [.15, 0], [1, 0]], fadeDur: 1.6, phase: .5 },
    // a satellite on a slow pass, turning a little
    { sprite: 'sat', n: 1, x: [-.05, -.05], y: [.25, .25], size: 90, move: { dx: 1700, dy: 220, dur: 70 }, spin: { deg: 30, dur: 30, back: true }, fade: [[0, 0], [.05, 1], [.9, 1], [1, 0]] },
  ];
  return { key: 'orbit', scene: 'Orbit', W, H, ax: .5, ay: .6, plate, sprites, layers };
}
build.key = 'orbit';
module.exports = build;
