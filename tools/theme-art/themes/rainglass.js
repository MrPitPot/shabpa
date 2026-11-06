'use strict';
const L = require('../lib');
const { f } = L;

// Rainy window: a city at night seen through wet glass. The street lights and signs melt into soft bokeh, the glass is
// fogged at the bottom and covered with hundreds of small refracting droplets; bigger drops run down it (sprites).
function build() {
  const W = 2400, H = 1200, r = L.rng(2207);
  const c = L.canvas(W, H), b = c.b;
  b.push(`<rect width="${W}" height="${H}" fill="${c.g('sky', [[0, '#070b10'], [.5, '#0e1824'], [1, '#18222c']])}"/>`);
  // blurred city: buildings, lit windows, street lights, car lights; all out of focus
  let city = '';
  for (let x = -40; x < W; x += r(60, 160)) {
    const bw = r(80, 200), bh = r(220, 640), y = 820 - bh;
    city += `<rect x="${f(x)}" y="${f(y)}" width="${f(bw)}" height="${f(bh + 400)}" fill="${L.mix('#0f1a24', '#1c2a38', r())}"/>`;
    for (let wy = y + 20; wy < 800; wy += r(22, 30)) for (let wx = x + 10; wx < x + bw - 12; wx += r(18, 26)) if (r() < .32) city += `<rect x="${f(wx)}" y="${f(wy)}" width="9" height="12" fill="${r.pick(['#ffd08a', '#ffe7b8', '#9ccbe8', '#ffb46a'])}" opacity="${f(r(.35, .9))}"/>`;
  }
  b.push(`<g ${c.blur(9)}>${city}</g>`);
  b.push(`<rect y="760" width="${W}" height="440" fill="${c.g('st', [[0, '#1a2430'], [1, '#0a0f14']])}"/>`);
  // bokeh: big soft discs with a brighter rim, as an out-of-focus lens draws them
  const bokeh = (x, y, R0, col, op) => {
    const gid = L.id('bk');
    c.defs.push(L.rad(gid, [[0, col, op * .55], [.82, col, op * .7], [.94, col, op], [1, col, 0]]));
    return `<circle cx="${f(x)}" cy="${f(y)}" r="${f(R0)}" fill="url(#${gid})"/>`;
  };
  let bk = '';
  const cols = ['#ffb35a', '#ffd38a', '#ff6a4a', '#7fb4d6', '#6ae0d0', '#ff8ab8', '#ffe7b8'];
  for (let i = 0; i < 120; i++) {
    const y = r(380, 1060), R0 = r(10, 56) * (.6 + y / 1200);
    bk += bokeh(r(-40, W + 40), y, R0, r.pick(cols), r(.15, .5));
  }
  // street lamps and car tail lights in a line
  for (let x = 80; x < W; x += 300) bk += bokeh(x + r(-30, 30), 640 + r(-20, 20), r(50, 70), '#ffcf7a', .8);
  for (let i = 0; i < 18; i++) bk += bokeh(r(0, W), r(900, 960), r(24, 40), r.pick(['#ff3a3a', '#ff5a4a']), .8);
  b.push(`<g ${c.blur(4)}>${bk}</g>`);
  b.push(c.glow(1200, 700, 1100, '#6a8fb0', .12));
  // fog of breath at the bottom of the glass
  b.push(`<rect width="${W}" height="${H}" fill="${c.g('fog', [[0, '#9fb6c8', .02], [.55, '#9fb6c8', .08], [1, '#c4d4e0', .3]])}"/>`);
  b.push(`<rect width="${W}" height="${H}" fill="#000" ${c.cloud({ freq: .0035, oct: 4, seed: 4, color: L.rgb01('#b8cada'), contrast: 1.4, bias: -.55 })} opacity=".18"/>`);
  // droplets on the glass: a dark rim, a lens of the light behind, a white glint
  let drops = '';
  for (let i = 0; i < 1500; i++) {
    const x = r(0, W), y = r(0, H), s = Math.pow(r(), 2.5) * 9 + 1.2, e = r(.8, 1.15);
    drops += `<ellipse cx="${f(x)}" cy="${f(y)}" rx="${f(s)}" ry="${f(s * e)}" fill="#0a1218" opacity=".35"/>`;
    drops += `<ellipse cx="${f(x)}" cy="${f(y + s * .1)}" rx="${f(s * .8)}" ry="${f(s * e * .8)}" fill="${r.pick(['#33475a', '#4a5f72', '#6a4a3a', '#2a3a4a'])}" opacity=".55"/>`;
    drops += `<circle cx="${f(x - s * .3)}" cy="${f(y - s * .35)}" r="${f(Math.max(.5, s * .22))}" fill="#e8f4ff" opacity=".8"/>`;
  }
  b.push(drops);
  // tracks left by drops that already ran down
  for (let i = 0; i < 26; i++) {
    const x = r(0, W), y0 = r(0, 600), y1 = y0 + r(200, 600);
    let d = `M${f(x)} ${f(y0)}`;
    for (let y = y0; y < y1; y += 20) d += `L${f(x + r(-3, 3))} ${f(y)}`;
    b.push(`<path d="${d}" stroke="#a8c4da" stroke-opacity=".12" stroke-width="${f(r(3, 6))}" fill="none" stroke-linecap="round"/>`);
  }
  // the window frame: a mullion on the right and the sill
  b.push(`<rect x="2290" y="0" width="110" height="${H}" fill="${c.g('fr', [[0, '#0b0f13'], [.2, '#1b232b'], [1, '#07090b']], 0, 0, 1, 0)}"/>`);
  b.push(`<rect y="1140" width="${W}" height="60" fill="${c.g('sl', [[0, '#1d252d'], [1, '#07090b']])}"/>`);
  const plate = c.finish({ vignette: .5 });

  const sprites = {
    // a running drop: a rounded head with a light lens and a short wet trail above it (drawn falling down)
    drop: L.sprite(28, 90, L.lin('tr', [[0, '#a8c4da', 0], [1, '#a8c4da', .35]]) + L.rad('hd', [[0, '#cfe4f4', .9], [.6, '#5a7488', .7], [1, '#0a1218', .8]], .4, .35, .7),
      `<rect x="11" y="0" width="6" height="70" rx="3" fill="url(#tr)"/><ellipse cx="14" cy="74" rx="11" ry="13" fill="url(#hd)"/><circle cx="10" cy="69" r="3" fill="#fff" opacity=".85"/>`),
    streak: L.sprite(4, 120, L.lin('st', [[0, '#c8dcec', 0], [1, '#c8dcec', .55]]), `<rect width="2" x="1" height="120" rx="1" fill="url(#st)"/>`),
  };
  const layers = [
    // rain outside (behind the glass, soft)
    { sprite: 'streak', n: 40, x: [0, 1], y: [-.15, -.05], size: [2, 3], op: [.15, .35], rot: 8, move: { dx: -170, dy: [1400, 1450], dur: [1.1, 1.6] } },
    // drops running down the glass: they wait, then run, pause, run again
    { sprite: 'drop', n: 12, x: [.03, .93], y: [-.05, .4], size: [14, 24], op: [.75, 1], move: { dx: [-20, 20], dy: [700, 1000], dur: [9, 15], ease: 'in' }, fade: [[0, 0], [.08, 1], [.85, 1], [1, 0]] },
  ];
  return { key: 'rainglass', scene: 'Rain', W, H, ax: .5, ay: .5, plate, sprites, layers };
}
build.key = 'rainglass';
module.exports = build;
