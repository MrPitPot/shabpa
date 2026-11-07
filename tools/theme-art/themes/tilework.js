'use strict';
const L = require('../lib');
const { f } = L;

// Tilework: a glazed Persian tile wall in the star-and-cross pattern (eight-pointed turquoise stars, lapis crosses,
// gold outlines, each tile glazed by hand), framed by a pointed arch with banded borders. Light from a high window
// falls across it; a gold rosette turns slowly in two corners and a sheen of light passes over the glaze.
function build() {
  const W = 2400, H = 1200, r = L.rng(1414);
  const c = L.canvas(W, H), b = c.b;
  const d = 120, h = d / (2 * Math.SQRT2);
  b.push(`<rect width="${W}" height="${H}" fill="#0b2c5a"/>`);
  // the crosses (the ground) carry a small white-and-gold flower in their middle
  c.defs.push(L.rad('cross', [[0, '#2050a0'], [.7, '#123a7a'], [1, '#0b2c5a']], .5, .5, .7));
  c.defs.push(L.rad('glz', [[0, '#7ff0e6'], [.45, '#22b5b0'], [1, '#0a6f78']], .38, .32, .8));
  c.defs.push(L.rad('glz2', [[0, '#8ff6ea'], [.45, '#2bc0b8'], [1, '#0c7a80']], .38, .32, .8));
  c.defs.push(L.lin('gold', [[0, '#fff0b8'], [.4, '#e3be62'], [1, '#9a6e1a']], 0, 0, 1, 1));
  const starPath = (x, y, k = 1) => {
    let p = '';
    for (let i = 0; i < 16; i++) {
      const a = i * Math.PI / 8, rr = i % 2 === 0 ? h * Math.SQRT2 * k : h / Math.cos(Math.PI / 8) * k;
      p += (i ? 'L' : 'M') + f(x + Math.cos(a) * rr) + ' ' + f(y + Math.sin(a) * rr);
    }
    return p + 'Z';
  };
  let tiles = '', gold = '', deco = '';
  for (let j = -1; j <= H / d + 1; j++) for (let i = -1; i <= W / d + 1; i++) {
    const x = i * d, y = j * d;
    tiles += `<path d="${starPath(x, y)}" fill="url(#${r() < .5 ? 'glz' : 'glz2'})"/>`;
    gold += starPath(x, y);
    // inside each star: an eight-petal rosette and four arabesque curls
    for (let k = 0; k < 8; k++) {
      const a = k * Math.PI / 4;
      deco += `<path d="M${f(x)} ${f(y)}Q${f(x + Math.cos(a - .4) * 14)} ${f(y + Math.sin(a - .4) * 14)} ${f(x + Math.cos(a) * 22)} ${f(y + Math.sin(a) * 22)}Q${f(x + Math.cos(a + .4) * 14)} ${f(y + Math.sin(a + .4) * 14)} ${f(x)} ${f(y)}Z" fill="#f4f0e2" opacity=".85"/>`;
    }
    deco += `<circle cx="${x}" cy="${y}" r="5" fill="url(#gold)"/>`;
    for (let k = 0; k < 4; k++) {
      const a = k * Math.PI / 2 + Math.PI / 4, px = x + Math.cos(a) * 30, py = y + Math.sin(a) * 30;
      deco += `<path d="M${f(px)} ${f(py)}a6 6 0 1 1 ${f(Math.cos(a + 1.6) * 8)} ${f(Math.sin(a + 1.6) * 8)}" stroke="#0a4a5a" stroke-width="2" fill="none" opacity=".7"/>`;
    }
    // the cross to the lower right of every star
    const cx = x + d / 2, cy = y + d / 2;
    for (let k = 0; k < 4; k++) { const a = k * Math.PI / 2; deco += `<ellipse cx="${f(cx + Math.cos(a) * 9)}" cy="${f(cy + Math.sin(a) * 9)}" rx="8" ry="4.5" transform="rotate(${k * 90} ${f(cx + Math.cos(a) * 9)} ${f(cy + Math.sin(a) * 9)})" fill="#f4f0e2" opacity=".9"/>`; }
    deco += `<circle cx="${cx}" cy="${cy}" r="4" fill="url(#gold)"/>`;
  }
  b.push(`<rect width="${W}" height="${H}" fill="url(#cross)"/>`);
  b.push(tiles, deco);
  // grout and gold outlines (a dark line under a gold line gives the raised edge)
  b.push(`<path d="${gold}" fill="none" stroke="#06223a" stroke-width="7" opacity=".7"/>`);
  b.push(`<path d="${gold}" fill="none" stroke="url(#gold)" stroke-width="2.6"/>`);
  // the glaze: crazing, uneven gloss, darker where the kiln was hotter
  b.push(`<rect width="${W}" height="${H}" fill="#000" ${c.cloud({ freq: .02, oct: 3, seed: 3, color: [1, 1, 1], contrast: 3, bias: -1.9 })} opacity=".25"/>`);
  b.push(`<rect width="${W}" height="${H}" fill="#000" ${c.cloud({ freq: .003, oct: 3, seed: 8, color: L.rgb01('#041a30'), contrast: 2, bias: -.7 })} opacity=".55"/>`);

  // the arch: a pointed iwan in the middle, with a band of white knotwork between gold borders, deep inside
  const ax = 1200, aw = 760, top = 120, foot = H + 20;
  const arch = (inset) => {
    const x0 = ax - aw / 2 + inset, x1 = ax + aw / 2 - inset, sh = 520;
    return `M${x0} ${foot}L${x0} ${top + sh}C${x0} ${top + 210 + inset * .6} ${ax - 120} ${top + 60 + inset} ${ax} ${top + inset}C${ax + 120} ${top + 60 + inset} ${x1} ${top + 210 + inset * .6} ${x1} ${top + sh}L${x1} ${foot}`;
  };
  b.push(`<path d="${arch(0)}" fill="none" stroke="#04182c" stroke-width="64" opacity=".6" ${c.blur(14)}/>`);
  b.push(`<path d="${arch(0)}Z" fill="${c.g('ins', [[0, '#062044'], [1, '#03122a']])}"/>`);
  // inside the arch: a smaller pattern, dimmer, as if in shade
  let inner = '';
  for (let j = 0; j < 20; j++) for (let i = 0; i < 13; i++) { const x = ax - 360 + i * 60, y = top + 60 + j * 60; inner += starPath(x, y, .5); }
  const ac = L.id('ac'); c.defs.push(`<clipPath id="${ac}"><path d="${arch(40)}Z"/></clipPath>`);
  b.push(`<g clip-path="url(#${ac})"><path d="${inner}" fill="#145a78" stroke="url(#gold)" stroke-width="1.4" opacity=".55"/>${c.glow(ax, top + 380, 420, '#3dd6d0', .12)}</g>`);
  // the border bands
  b.push(`<path d="${arch(0)}" fill="none" stroke="url(#gold)" stroke-width="10"/>`);
  b.push(`<path d="${arch(14)}" fill="none" stroke="#0e6f7a" stroke-width="22"/>`);
  b.push(`<path d="${arch(14)}" fill="none" stroke="#f4f0e2" stroke-width="3" stroke-dasharray="14 9" opacity=".9"/>`);
  b.push(`<path d="${arch(28)}" fill="none" stroke="url(#gold)" stroke-width="5"/>`);
  b.push(`<path d="${arch(-18)}" fill="none" stroke="#f4f0e2" stroke-width="3" stroke-dasharray="2 10" opacity=".6"/>`);
  // light from a high window on the left, falling across the wall
  b.push(`<path d="M0 0L700 0L1500 ${H}L300 ${H}Z" fill="${c.g('ray', [[0, '#fff6d8', .2], [1, '#fff6d8', 0]])}" ${c.blur(30)}/>`);
  const plate = c.finish({ vignette: .65 });

  // a gold rosette: sixteen points, two rings of star lines
  let ros = '';
  const R0 = 240;
  for (let k = 0; k < 16; k++) {
    const a = k * Math.PI / 8;
    ros += `<path d="M${f(260 + Math.cos(a) * R0)} ${f(260 + Math.sin(a) * R0)}L${f(260 + Math.cos(a + Math.PI * 5 / 8) * R0)} ${f(260 + Math.sin(a + Math.PI * 5 / 8) * R0)}"/>`;
    ros += `<path d="M${f(260 + Math.cos(a) * R0 * .55)} ${f(260 + Math.sin(a) * R0 * .55)}L${f(260 + Math.cos(a + Math.PI * 6 / 8) * R0 * .55)} ${f(260 + Math.sin(a + Math.PI * 6 / 8) * R0 * .55)}"/>`;
  }
  const sprites = {
    rosette: L.sprite(520, 520, L.lin('g', [[0, '#fff0b8'], [.5, '#e3be62'], [1, '#b08a2a']], 0, 0, 1, 1),
      `<g stroke="url(#g)" stroke-width="2.4" fill="none" opacity=".85">${ros}<circle cx="260" cy="260" r="${R0}"/><circle cx="260" cy="260" r="${R0 * .3}"/></g>`),
    sheen: L.sprite(500, 1400, L.lin('s', [[0, '#ffffff', 0], [.5, '#ffffff', .22], [1, '#ffffff', 0]], 0, 0, 1, 0), `<rect width="500" height="1400" fill="url(#s)"/>`),
    glint: L.sprite(40, 40, L.rad('g', [[0, '#ffffff', 1], [.2, '#fff0b8', .6], [1, '#fff0b8', 0]]), `<circle cx="20" cy="20" r="20" fill="url(#g)"/><path d="M20 0V40M0 20H40" stroke="#fff" stroke-width="1.2" opacity=".8"/>`),
  };
  const glints = [];
  for (let k = 0; k < 40; k++) { const i = r.int(0, 20), j = r.int(0, 10); glints.push([i * d + (r() < .5 ? d / 2 : 0), j * d + (r() < .5 ? d / 2 : 0)]); }
  const layers = [
    { sprite: 'sheen', at: [[-400, 600]], size: 500, rot: 20, move: { dx: 3400, dy: 0, dur: 16, travel: .5, ease: 'out' }, fade: [[0, 0], [.05, 1], [.45, 1], [.5, 0], [1, 0]], phase: .2 },
    { sprite: 'glint', at: glints, size: [18, 34], twinkle: { lo: 0, hi: 1, dur: [2, 5] } },
    { sprite: 'rosette', at: [[2320, 80], [80, 1120]], size: 560, op: .55, spin: { deg: 360, dur: 160, either: false } },
  ];
  return { key: 'tilework', scene: 'Tiles', W, H, ax: .5, ay: .5, plate, sprites, layers };
}
build.key = 'tilework';
module.exports = build;
