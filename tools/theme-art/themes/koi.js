'use strict';
const L = require('../lib');
const { f, id } = L;

// Koi pond seen from above: pebbles under clear teal water, moving caustic light baked in, mossy rocks on the rim,
// lily pads and lotus flowers floating on top. The koi are detailed sprites drawn facing right; the app turns each one
// to face the way it swims along its loop, with a slow tail sway, and ripples open and fade on the surface.
function build() {
  const W = 2400, H = 1200, r = L.rng(4242);
  const defs = [], b = [];
  const g = (k, stops, ...a) => { const i = id(k); defs.push(L.lin(i, stops, ...a)); return `url(#${i})`; };
  const rg = (k, stops, ...a) => { const i = id(k); defs.push(L.rad(i, stops, ...a)); return `url(#${i})`; };
  const blur = (sd) => { const i = id('bl'); defs.push(L.blurF(i, sd)); return `filter="url(#${i})"`; };

  // the pond floor: dark silt with pebbles fading into depth
  b.push(`<rect width="${W}" height="${H}" fill="#0c2a2c"/>`);
  let pebbles = '';
  const stoneCols = ['#5d6a5e', '#7a7462', '#4b5650', '#8a8574', '#3d4844', '#6b5d4c', '#a39a84'];
  for (let i = 0; i < 1400; i++) {
    const x = r(-20, W + 20), y = r(-20, H + 20), s = r(6, 26);
    const c = r.pick(stoneCols);
    pebbles += `<path d="${L.blob(r, x, y, s, s * r(.6, .95), .22, 7)}" fill="${c}" transform="rotate(${f(r(0, 180))} ${f(x)} ${f(y)})"/>`;
  }
  b.push(`<g opacity=".55" ${blur(2.2)}>${pebbles}</g>`);
  // shading on each pebble from the light above
  b.push(`<rect width="${W}" height="${H}" fill="${rg('dp', [[0, '#0f4a4c', .25], [.6, '#0a3437', .6], [1, '#04161a', .9]], .45, .45, .75)}"/>`);
  // water tint, deeper toward the middle
  b.push(`<rect width="${W}" height="${H}" fill="${g('wt', [[0, '#1b6b6a', .35], [.5, '#0b3f44', .2], [1, '#06232a', .45]], 0, 0, 1, 1)}"/>`);
  // caustics: thin bright webs of light on the floor
  const ca = id('ca');
  defs.push(`<filter id="${ca}" x="0" y="0" width="100%" height="100%" color-interpolation-filters="sRGB"><feTurbulence type="turbulence" baseFrequency=".006 .009" numOctaves="2" seed="11"/>
    <feColorMatrix type="matrix" values="0 0 0 0 .75  0 0 0 0 1  0 0 0 0 .92  -9 0 0 0 1.45"/><feGaussianBlur stdDeviation="1.4"/><feComposite in2="SourceGraphic" operator="in"/></filter>`);
  b.push(`<rect width="${W}" height="${H}" fill="#000" filter="url(#${ca})" opacity=".16"/>`);
  // a sky reflection sweeping across the surface
  b.push(`<rect width="${W}" height="${H}" fill="${g('sk', [[0, '#bfeee6', 0], [.35, '#bfeee6', .08], [.5, '#e6fff8', .14], [.65, '#bfeee6', .06], [1, '#bfeee6', 0]], 0, 0, 1, .6)}"/>`);

  // rocks on the rim, top-left and bottom-right, with moss and a dark wet line at the water
  const rock = (x, y, rx, ry, rot) => {
    let s = `<g transform="rotate(${rot} ${x} ${y})">`;
    s += `<ellipse cx="${x + 18}" cy="${y + 26}" rx="${rx * 1.05}" ry="${ry}" fill="#020b0c" opacity=".6" ${blur(18)}/>`;
    const p = L.blob(r, x, y, rx, ry, .14, 11);
    s += `<path d="${p}" fill="${rg('rk', [[0, '#9a9a88'], [.55, '#6a6c60'], [1, '#2b2f2b']], .38, .32, .8)}"/>`;
    for (let k = 0; k < 40; k++) { const a = r(0, Math.PI * 2), d = r(0, .85); s += `<circle cx="${f(x + Math.cos(a) * rx * d)}" cy="${f(y + Math.sin(a) * ry * d)}" r="${f(r(1, 5))}" fill="${r.pick(['#3f4a3a', '#b5b29e', '#56604c'])}" opacity="${f(r(.2, .6))}"/>`; }
    for (let k = 0; k < 7; k++) { const a = r(Math.PI * 1.05, Math.PI * 1.9), d = r(.3, .8); s += `<path d="${L.blob(r, x + Math.cos(a) * rx * d, y + Math.sin(a) * ry * d, r(14, 40), r(8, 22), .35, 8)}" fill="${r.pick(['#3f6b2a', '#56823a', '#2f5521'])}" opacity="${f(r(.6, .9))}" ${blur(1.5)}/>`; }
    s += `<path d="${p}" fill="none" stroke="#0a1a1a" stroke-width="5" opacity=".5"/>`;
    return s + '</g>';
  };
  b.push(rock(80, 60, 260, 170, -12), rock(330, -40, 180, 120, 20), rock(-60, 300, 150, 140, 0));
  b.push(rock(2340, 1150, 280, 170, 8), rock(2080, 1250, 200, 130, -10), rock(2460, 880, 160, 150, 0));
  // reeds/grass from the bank at top-right and bottom-left
  let reeds = '';
  for (const [bx, by, dir] of [[2200, -10, 1], [140, 1220, -1]]) for (let i = 0; i < 70; i++) {
    const x = bx + r(-200, 220), len = r(80, 260), bend = r(-60, 60);
    reeds += `<path d="M${f(x)} ${by}Q${f(x + bend)} ${f(by + dir * len * .6)} ${f(x + bend * 1.6)} ${f(by + dir * len)}" stroke="${r.pick(['#2e5a2a', '#3f7034', '#244a22', '#6b8f3e'])}" stroke-width="${f(r(2, 5))}" fill="none" stroke-linecap="round"/>`;
  }
  b.push(`<g opacity=".9">${reeds}</g>`);

  // vignette and grain
  b.push(`<rect width="${W}" height="${H}" fill="${rg('vg', [[0, '#000', 0], [.75, '#000', .1], [1, '#000', .55]], .5, .5, .75)}"/>`);
  const gr = id('gr'); defs.push(L.grainF(gr, .045));
  const plate = L.svg(W, H, defs.join(''), `<g filter="url(#${gr})">${b.join('')}</g>`);

  // ---- sprites -----------------------------------------------------------------------------------------------
  // A koi from above, nose to the right. Body is a smooth outline, with fins, a flowing tail, scale texture and patches.
  const koi = (seed, base, patches, patchCol, extraDark) => {
    const R = L.rng(seed), d = [];
    const FW = 260, FH = 120, cy = 60;
    // outline: from nose (x=238) round the back to the tail root (x=62)
    const top = [[238, cy], [228, cy - 14], [205, cy - 24], [170, cy - 27], [130, cy - 24], [95, cy - 16], [70, cy - 7], [58, cy - 3]];
    const bot = [[58, cy + 3], [70, cy + 7], [95, cy + 16], [130, cy + 24], [170, cy + 27], [205, cy + 24], [228, cy + 14]];
    const body = L.smooth([...top, ...bot], true, .9);
    const clip = id('kc'); d.push(`<clipPath id="${clip}"><path d="${body}"/></clipPath>`);
    let s = '';
    // shadow on the floor (offset, soft) so the fish floats above the pebbles
    s += `<g transform="translate(10 16)" opacity=".45" filter="url(#ksh)"><path d="${body}" fill="#000"/><path d="M62 ${cy}C40 ${cy - 30} 18 ${cy - 34} 4 ${cy - 26}C16 ${cy - 8} 16 ${cy + 8} 4 ${cy + 26}C18 ${cy + 34} 40 ${cy + 30} 62 ${cy}Z" fill="#000"/></g>`;
    // tail: two translucent lobes with fin rays
    const tail = `M64 ${cy}C44 ${cy - 26} 22 ${cy - 40} 4 ${cy - 34}C14 ${cy - 16} 20 ${cy - 4} 14 ${cy}C20 ${cy + 4} 14 ${cy + 16} 4 ${cy + 34}C22 ${cy + 40} 44 ${cy + 26} 64 ${cy}Z`;
    s += `<path d="${tail}" fill="${base}" opacity=".72"/>`;
    for (let k = -5; k <= 5; k++) s += `<path d="M62 ${cy}Q36 ${f(cy + k * 4)} ${f(10 + Math.abs(k) * .6)} ${f(cy + k * 6.4)}" stroke="#fff" stroke-opacity=".28" stroke-width=".9" fill="none"/>`;
    if (patchCol) s += `<path d="M64 ${cy}C46 ${cy - 18} 30 ${cy - 26} 16 ${cy - 26}C26 ${cy - 10} 28 ${cy} 26 ${cy}Z" fill="${patchCol}" opacity=".55"/>`;
    // pectoral fins, pelvic fins and the dorsal fin
    for (const sd of [-1, 1]) {
      s += `<path d="M196 ${cy + sd * 20}C184 ${cy + sd * 44} 166 ${cy + sd * 54} 150 ${cy + sd * 52}C160 ${cy + sd * 40} 170 ${cy + sd * 28} 182 ${cy + sd * 20}Z" fill="${base}" opacity=".7"/>`;
      for (let k = 0; k < 5; k++) s += `<path d="M190 ${cy + sd * 21}L${f(156 + k * 5)} ${f(cy + sd * (50 - k * 3))}" stroke="#fff" stroke-opacity=".3" stroke-width=".8"/>`;
      s += `<path d="M118 ${cy + sd * 18}C110 ${cy + sd * 32} 98 ${cy + sd * 36} 88 ${cy + sd * 34}C96 ${cy + sd * 26} 104 ${cy + sd * 20} 110 ${cy + sd * 16}Z" fill="${base}" opacity=".6"/>`;
    }
    s += `<path d="${body}" fill="${base}"/>`;
    // patches clipped to the body
    let p = '';
    for (const [px, py, rx, ry] of patches) p += `<path d="${L.blob(R, px, cy + py, rx, ry, .28, 9)}" fill="${patchCol}"/>`;
    if (extraDark) for (let k = 0; k < 6; k++) p += `<path d="${L.blob(R, R(80, 200), cy + R(-16, 16), R(5, 12), R(4, 9), .4, 7)}" fill="#121212"/>`;
    // scales: rows of small arcs
    let sc = '';
    for (let x = 70; x < 215; x += 7) for (let y = cy - 26; y < cy + 27; y += 7) sc += `M${f(x + ((y / 7) % 2) * 3.5)} ${f(y)}a4 4 0 0 1 0 6`;
    s += `<g clip-path="url(#${clip})">${p}<path d="${sc}" stroke="#fff" stroke-opacity=".13" stroke-width=".8" fill="none"/>`;
    // round the body: light along the spine, shade along the flanks
    s += `<rect x="40" y="${cy - 30}" width="210" height="60" fill="url(#kspine)"/></g>`;
    // the head: eyes, nostrils and barbels
    for (const sd of [-1, 1]) {
      s += `<circle cx="222" cy="${cy + sd * 11}" r="3.6" fill="#141414"/><circle cx="223" cy="${cy + sd * 11 - 1}" r="1.2" fill="#fff" opacity=".8"/>`;
      s += `<path d="M236 ${cy + sd * 5}q8 ${sd * 6} 6 ${sd * 14}" stroke="${base}" stroke-width="1.6" fill="none" opacity=".8"/>`;
    }
    s += `<path d="M110 ${cy}L214 ${cy}" stroke="#fff" stroke-opacity=".18" stroke-width="3" stroke-linecap="round"/>`;
    d.push(L.blurF('ksh', 5, 30));
    d.push(L.lin('kspine', [[0, '#000', .35], [.3, '#000', 0], [.5, '#fff', .22], [.7, '#000', 0], [1, '#000', .4]]));
    return { w: FW, h: FH, svg: L.svg(FW, FH, d.join(''), s) };
  };
  const sprites = {
    kohaku: koi(1, '#f6efe4', [[200, -6, 26, 18], [150, 6, 22, 20], [102, -4, 18, 12]], '#e2381f', false),
    yamabuki: koi(2, '#ffb52e', [[190, 0, 30, 14]], '#ffd77a', false),
    showa: koi(3, '#1c1a1a', [[196, -2, 22, 18], [140, 8, 26, 16], [96, -6, 14, 10]], '#e8481f', false),
    sanke: koi(4, '#f6efe4', [[190, 4, 20, 14], [130, -6, 24, 14]], '#ef5a26', true),
    orange: koi(5, '#ff6a2a', [[160, 0, 30, 10]], '#ffa15a', false),
  };
  // lily pads and lotus flowers (they float above the fish)
  const pad = (seed, size, flower) => {
    const R = L.rng(seed), d = [], S = size, c = S / 2;
    let s = `<ellipse cx="${c + 8}" cy="${c + 12}" rx="${c * .9}" ry="${c * .9}" fill="#000" opacity=".35" filter="url(#ps)"/>`;
    const notch = R(0, Math.PI * 2);
    const rr = c * .86;
    s += `<path d="M${f(c)} ${f(c)}L${f(c + Math.cos(notch - .18) * rr)} ${f(c + Math.sin(notch - .18) * rr)}A${f(rr)} ${f(rr)} 0 1 0 ${f(c + Math.cos(notch + .18) * rr)} ${f(c + Math.sin(notch + .18) * rr)}Z" fill="url(#pg)"/>`;
    for (let k = 0; k < 14; k++) { const a = notch + .3 + k * (Math.PI * 2 - .6) / 13; s += `<path d="M${f(c)} ${f(c)}L${f(c + Math.cos(a) * rr * .95)} ${f(c + Math.sin(a) * rr * .95)}" stroke="#b7e08a" stroke-opacity=".28" stroke-width="1.2"/>`; }
    s += `<path d="M${f(c)} ${f(c)}m${f(-rr)} 0a${f(rr)} ${f(rr)} 0 1 0 ${f(rr * 2)} 0" fill="none" stroke="#0e3a14" stroke-opacity=".4" stroke-width="3"/>`;
    if (flower) {
      const fx = c + R(-c * .2, c * .2), fy = c + R(-c * .2, c * .2);
      for (let ring = 0; ring < 3; ring++) {
        const n = 8 - ring, len = (S * .3) * (1 - ring * .24);
        for (let k = 0; k < n; k++) {
          const a = k / n * Math.PI * 2 + ring * .4;
          s += `<path d="M${f(fx)} ${f(fy)}Q${f(fx + Math.cos(a - .35) * len * .7)} ${f(fy + Math.sin(a - .35) * len * .7)} ${f(fx + Math.cos(a) * len)} ${f(fy + Math.sin(a) * len)}Q${f(fx + Math.cos(a + .35) * len * .7)} ${f(fy + Math.sin(a + .35) * len * .7)} ${f(fx)} ${f(fy)}Z" fill="url(#lp${ring})" stroke="#b03a6a" stroke-opacity=".25" stroke-width=".8"/>`;
        }
      }
      s += `<circle cx="${f(fx)}" cy="${f(fy)}" r="${f(S * .05)}" fill="#ffd65a"/>`;
      for (let k = 0; k < 12; k++) { const a = k / 12 * Math.PI * 2; s += `<circle cx="${f(fx + Math.cos(a) * S * .065)}" cy="${f(fy + Math.sin(a) * S * .065)}" r="1.8" fill="#ffb02e"/>`; }
    }
    d.push(L.rad('pg', [[0, '#5f9e3c'], [.7, '#3a7a2c'], [1, '#235c22']], .4, .4, .7), L.blurF('ps', 6, 30));
    ['#fff4f8', '#ffc4dc', '#ff8ab8'].forEach((cc, i) => d.push(L.lin('lp' + i, [[0, cc], [1, '#ff6fa5']], 0, 0, 1, 1)));
    return { w: S + 20, h: S + 24, svg: L.svg(S + 20, S + 24, d.join(''), s) };
  };
  sprites.pad1 = pad(10, 170, true); sprites.pad2 = pad(11, 130, false); sprites.pad3 = pad(12, 200, false); sprites.pad4 = pad(13, 150, true);
  sprites.ring = { w: 300, h: 300, svg: L.svg(300, 300, '', `<circle cx="150" cy="150" r="140" fill="none" stroke="#e8fffa" stroke-width="3" opacity=".6"/><circle cx="150" cy="150" r="128" fill="none" stroke="#e8fffa" stroke-width="1.5" opacity=".3"/>`) };
  sprites.glint = { w: 90, h: 40, svg: L.svg(90, 40, L.rad('gl', [[0, '#ffffff', .7], [1, '#ffffff', 0]]), `<ellipse cx="45" cy="20" rx="45" ry="20" fill="url(#gl)"/>`) };
  sprites.petal = { w: 30, h: 22, svg: L.svg(30, 22, L.lin('pt', [[0, '#ffe6ef'], [1, '#ff9cc0']], 0, 0, 1, 1), `<path d="M2 11C8 0 22 0 28 9L24 11L28 13C22 22 8 22 2 11Z" fill="url(#pt)"/>`) };

  const layers = [
    // koi: each on its own wobbly loop, facing the way it swims, with a slow sway of the body
    { sprite: 'kohaku', n: 1, size: 250, path: { cx: 1150, cy: 560, rx: 640, ry: 300, dur: 46, dir: 1, wob: .12, lobes: 3 }, wag: { deg: 5, dur: .9 }, phase: .1 },
    { sprite: 'yamabuki', n: 1, size: 220, path: { cx: 1300, cy: 640, rx: 520, ry: 250, dur: 38, dir: -1, wob: .16, lobes: 2 }, wag: { deg: 6, dur: .8 }, phase: .55 },
    { sprite: 'showa', n: 1, size: 240, path: { cx: 1050, cy: 600, rx: 780, ry: 360, dur: 58, dir: 1, wob: .1, lobes: 4 }, wag: { deg: 5, dur: 1 }, phase: .7 },
    { sprite: 'sanke', n: 1, size: 200, path: { cx: 1250, cy: 520, rx: 420, ry: 210, dur: 34, dir: 1, wob: .2, lobes: 3 }, wag: { deg: 6, dur: .75 }, phase: .3 },
    { sprite: 'orange', n: 1, size: 180, path: { cx: 1200, cy: 600, rx: 900, ry: 420, dur: 64, dir: -1, wob: .12, lobes: 5 }, wag: { deg: 7, dur: .7 }, phase: .9 },
    // ripples opening on the surface
    { sprite: 'ring', n: 7, x: [.1, .9], y: [.1, .9], size: 260, op: .7, rot: 0, move: { dx: 0, dy: 0, dur: [6, 9] }, fade: [[0, 0], [.08, .9], [1, 0]], grow: [.1, 1.3], squash: .55 },
    // sun glints
    { sprite: 'glint', n: 10, x: [.05, .95], y: [.05, .95], size: [60, 110], op: [.3, .6], twinkle: { lo: 0, hi: 1, dur: [2, 4] }, sway: { x: [20, 40], dur: [6, 9] } },
    // lily pads float above the fish
    { sprite: 'pad3', at: [[560, 300]], size: 230, rot: 20, sway: { x: 10, y: 6, dur: 9, dury: 11 }, spin: { deg: 4, dur: 14, back: true } },
    { sprite: 'pad1', at: [[700, 420]], size: 190, rot: -40, sway: { x: 8, y: 6, dur: 10, dury: 8 }, spin: { deg: 5, dur: 12, back: true } },
    { sprite: 'pad2', at: [[430, 470]], size: 140, rot: 70, sway: { x: 8, y: 6, dur: 8, dury: 10 }, spin: { deg: 5, dur: 11, back: true } },
    { sprite: 'pad4', at: [[1880, 800]], size: 180, rot: 140, sway: { x: 10, y: 8, dur: 11, dury: 9 }, spin: { deg: 5, dur: 13, back: true } },
    { sprite: 'pad2', at: [[2030, 690]], size: 130, rot: 200, sway: { x: 8, y: 6, dur: 9, dury: 12 }, spin: { deg: 6, dur: 10, back: true } },
    { sprite: 'pad3', at: [[1720, 960]], size: 200, rot: 260, sway: { x: 8, y: 6, dur: 12, dury: 10 }, spin: { deg: 4, dur: 15, back: true } },
    { sprite: 'pad1', at: [[1500, 180]], size: 160, rot: 90, sway: { x: 8, y: 6, dur: 10, dury: 13 }, spin: { deg: 5, dur: 12, back: true } },
    // a few blossom petals drifting on the surface
    { sprite: 'petal', n: 9, x: [0, 1], y: [.05, .95], size: [16, 26], op: [.7, .95], rot: [0, 360], move: { dx: [260, 420], dy: [-60, 60], dur: [40, 60] }, fade: [[0, 0], [.1, 1], [.9, 1], [1, 0]], spin: { deg: [40, 90], dur: [10, 16], back: true } },
  ];
  return { key: 'koi', scene: 'Ripples', W, H, ax: .5, ay: .5, plate, sprites, layers };
}
build.key = 'koi';
module.exports = build;
