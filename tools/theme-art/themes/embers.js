'use strict';
const L = require('../lib');
const { f } = L;

// Embers: the last of a great fire. A bed of charred logs and live coal glowing through cracks along the bottom,
// heat and smoke rising into darkness. Sparks fly up as streaks and slowly dying flakes of ember drift and fade.
function build() {
  const W = 2400, H = 1200, r = L.rng(5150);
  const c = L.canvas(W, H), b = c.b;
  b.push(`<rect width="${W}" height="${H}" fill="${c.g('bg', [[0, '#050202'], [.55, '#120605'], [.8, '#3a1206'], [1, '#5a1a06']])}"/>`);
  b.push(c.glow(1200, 1220, 1300, '#ff5a1f', .45));
  b.push(c.glow(700, 1150, 500, '#ff8a2a', .35));
  b.push(c.glow(1750, 1160, 560, '#ff8a2a', .3));
  // smoke rising, lit orange from below and fading into the dark
  b.push(`<rect width="${W}" height="${H}" fill="#000" ${c.cloud({ freq: .0022, oct: 5, seed: 7, color: L.rgb01('#6a3a2a'), contrast: 1.5, bias: -.55, stretchX: .5 })} opacity=".5" mask="url(#smm)"/>`);
  c.defs.push(`<mask id="smm"><rect width="${W}" height="${H}" fill="${c.g('smg', [[0, '#000'], [.4, '#444'], [.85, '#fff'], [1, '#888']])}"/></mask>`);
  // heat haze band
  b.push(`<rect y="760" width="${W}" height="300" fill="${c.g('hz', [[0, '#ff7a2a', 0], [.7, '#ff7a2a', .14], [1, '#ff7a2a', 0]])}"/>`);
  // the coal bed: a glowing lava texture under a crust of black ash cracked open
  const bedTop = 960;
  const bedFn = L.ridgeFn(r, bedTop, 26, [[.004, 1], [.011, .4], [.03, .15]]);
  const bed = L.ridgePath(bedFn, W, H, { step: 8 });
  const clip = L.id('bc');
  c.defs.push(`<clipPath id="${clip}"><path d="${bed}"/></clipPath>`);
  // under everything a white-hot glow; on top, packed pieces of charcoal, some burning through, ash on the cold ones
  let coals = '';
  for (let y = bedTop - 30; y < H + 40; y += 16) for (let x = -30; x < W + 30; x += r(26, 50)) {
    const sz = r(16, 40) * (.7 + (y - bedTop) / 400), hot = r() < .08;
    const fill = hot ? c.rg('hc', [[0, '#ffd27a'], [.45, '#ff6a1f'], [1, '#6a1a08']], .5, .4, .7) : c.rg('cc', [[0, '#2a1a14'], [.7, '#120a07'], [1, '#050302']], .4, .35, .75);
    coals += `<path d="${L.blob(r, x + r(-8, 8), y + r(-6, 6), sz, sz * r(.55, .8), .3, 7)}" fill="${fill}"/>`;
    if (!hot && r() < .4) coals += `<path d="${L.blob(r, x, y + sz * .15, sz * .9, sz * .45, .3, 7)}" fill="none" stroke="#ff7a2a" stroke-width="${f(r(1.5, 3))}" stroke-opacity="${f(r(.3, .7))}"/>`;
    if (!hot && r() < .5) coals += `<path d="${L.blob(r, x + r(-6, 6), y - sz * .2, sz * .5, sz * .25, .4, 6)}" fill="#8a7a72" opacity="${f(r(.15, .4))}"/>`;
  }
  b.push(`<g clip-path="url(#${clip})"><rect y="900" width="${W}" height="400" fill="${c.g('hot', [[0, '#ffb04a'], [.4, '#ff6a1f'], [1, '#8a2208']])}"/>${coals}
    <rect y="900" width="${W}" height="400" fill="${c.g('ash', [[0, '#000', .25], [.3, '#000', 0], [1, '#000', .4]])}"/></g>`);
  // charred logs lying across the bed, with glowing splits and grey ash edges
  const log = (x, y, len, th, ang) => {
    let s = `<g transform="rotate(${ang} ${x} ${y})">`;
    s += `<rect x="${x - len / 2}" y="${y - th / 2}" width="${len}" height="${th}" rx="${th / 2}" fill="${c.g('lg', [[0, '#2a1810'], [.4, '#120a07'], [1, '#050302']])}"/>`;
    for (let k = 0; k < len / 26; k++) {
      const cx = x - len / 2 + r(20, len - 20), cy = y + r(-th * .25, th * .3);
      s += `<path d="M${f(cx)} ${f(cy)}l${f(r(8, 22))} ${f(r(-4, 4))}l${f(r(4, 12))} ${f(r(-3, 3))}" stroke="#ff8a2a" stroke-width="${f(r(1.5, 3.5))}" fill="none" opacity="${f(r(.5, 1))}" stroke-linecap="round"/>`;
    }
    for (let k = 0; k < 7; k++) { const yy = y - th / 2 + th * (k + .5) / 7; s += `<path d="M${f(x - len / 2 + r(10, 60))} ${f(yy)}H${f(x + len / 2 - r(20, 80))}" stroke="#3a2a22" stroke-opacity=".5" stroke-width="${f(r(1, 2.5))}"/>`; }
    s += `<path d="M${x - len / 2 + th / 2} ${y - th / 2 + 3}H${x + len / 2 - th / 2}" stroke="#9a8a80" stroke-opacity=".4" stroke-width="4" stroke-linecap="round"/>`;
    s += `<path d="M${x - len / 2 + th / 2} ${y + th / 2 - 4}H${x + len / 2 - th / 2}" stroke="#ff6a1f" stroke-opacity=".55" stroke-width="5" stroke-linecap="round" ${c.blur(2)}/>`;
    s += `<ellipse cx="${x + len / 2 - th * .3}" cy="${y}" rx="${th * .32}" ry="${th / 2 - 2}" fill="${c.rg('end', [[0, '#ffb04a'], [.35, '#ff5a1f'], [.7, '#4a1408'], [1, '#120806']])}"/>`;
    return s + '</g>';
  };
  b.push(log(820, 990, 680, 86, -6), log(1520, 1000, 760, 96, 7), log(1180, 960, 460, 66, -2), log(360, 1060, 480, 74, 3), log(2080, 1050, 560, 80, -5));
  // a shimmer of flame tongues low over the coals
  let tongues = '';
  for (let i = 0; i < 34; i++) {
    const x = r(150, W - 150), y = bedFn(x) + r(-6, 10), hgt = r(20, 90), wdt = r(10, 26);
    tongues += `<path d="M${f(x - wdt)} ${f(y)}Q${f(x - wdt * .4)} ${f(y - hgt * .5)} ${f(x + r(-6, 6))} ${f(y - hgt)}Q${f(x + wdt * .5)} ${f(y - hgt * .45)} ${f(x + wdt)} ${f(y)}Z" fill="${r.pick(['#ff5a1f', '#ff8a2a', '#ffb04a'])}" opacity="${f(r(.2, .45))}"/>`;
  }
  b.push(`<g ${c.blur(9)}>${tongues}</g>`);
  // sparks frozen in the air in the painting (the moving ones are sprites)
  for (let i = 0; i < 120; i++) { const x = r(0, W), y = r(300, 980); b.push(c.glow(x, y, r(3, 7), '#ffb04a', r(.3, .8)), `<circle cx="${f(x)}" cy="${f(y)}" r="${f(r(.6, 1.4))}" fill="#ffe0a0"/>`); }
  const plate = c.finish({ vignette: .65 });

  const sprites = {
    // a spark seen while moving: a bright head with a short streak trailing below it (it flies upward)
    spark: L.sprite(16, 70, L.lin('sp', [[0, '#fff3c8', 1], [.15, '#ffb04a', .9], [1, '#ff5a1f', 0]]) + L.rad('sh', [[0, '#ffffff'], [.4, '#ffd27a', .8], [1, '#ff8a2a', 0]]),
      `<rect x="6" y="8" width="4" height="62" rx="2" fill="url(#sp)"/><circle cx="8" cy="8" r="8" fill="url(#sh)"/>`),
    flake: L.sprite(26, 26, L.rad('fl', [[0, '#ffe0a0', 1], [.25, '#ff8a2a', .85], [.6, '#ff5a1f', .25], [1, '#ff5a1f', 0]]), `<circle cx="13" cy="13" r="13" fill="url(#fl)"/>`),
    flame: L.sprite(120, 260, L.lin('f1', [[0, '#fff6d0', .0], [.15, '#ffd27a', .7], [.5, '#ff7a1f', .75], [1, '#ff3a0a', .0]], 0, 1, 0, 0) + L.lin('f2', [[0, '#ffffff', 0], [.2, '#fff3c8', .9], [.6, '#ffb04a', .6], [1, '#ff7a1f', 0]], 0, 1, 0, 0) + L.blurF('fb', 4),
      `<g filter="url(#fb)"><path d="M60 258C14 250 8 196 30 150C44 120 40 84 56 40C60 28 62 12 60 2C78 40 96 70 98 118C100 150 112 170 108 200C104 236 88 256 60 258Z" fill="url(#f1)"/>
       <path d="M60 254C34 248 32 214 44 186C52 166 54 140 62 112C72 146 84 168 84 196C84 228 76 250 60 254Z" fill="url(#f2)"/></g>`),
    heat: L.softDot(400, '#ff7a2a', { core: .2, coreOp: .3, mid: .12 }),
  };
  const layers = [
    // low flames licking up from the logs: they stretch and sway from their base
    { sprite: 'flame', at: [[560, 975], [760, 960], [980, 948], [1150, 940], [1330, 960], [1520, 955], [1720, 972], [1950, 1010], [2150, 1020], [380, 1030]], size: [60, 110], op: [.55, .85], origin: [.5, 1], pulse: { lo: .82, hi: 1.12, dur: [.5, .9] }, wag: { deg: [3, 7], dur: [.7, 1.3] }, twinkle: { lo: .6, hi: 1, dur: [.3, .6] } },
    { sprite: 'heat', n: 5, x: [.1, .9], y: [.85, .95], size: [300, 500], op: [.5, .8], twinkle: { lo: .4, hi: 1, dur: [2, 4] } },
    { sprite: 'flake', n: 30, x: [0, 1], y: [.82, .9], size: [8, 16], op: [.7, 1], move: { dx: [-120, 120], dy: [-900, -600], dur: [8, 13] }, fade: [[0, 0], [.1, 1], [.55, .7], [1, 0]], sway: { x: [20, 50], dur: [2.5, 4.5] } },
    { sprite: 'spark', n: 26, x: [.05, .95], y: [.84, .9], size: [5, 9], op: [.8, 1], move: { dx: [-80, 80], dy: [-1000, -700], dur: [3.5, 6], ease: 'out' }, fade: [[0, 0], [.06, 1], [.5, .8], [1, 0]], sway: { x: [8, 20], dur: [1.2, 2.2] } },
  ];
  return { key: 'embers', scene: 'Embers', W, H, ax: .5, ay: 1, plate, sprites, layers };
}
build.key = 'embers';
module.exports = build;
