'use strict';
const L = require('../lib');
const { f } = L;

// Code rain: columns of glowing glyphs (Persian letters and digits mixed with Latin and code symbols) falling through a
// deep green dark. Far columns are blurred and dim on the plate, a soft green haze glows near the floor; the near columns
// are sprites that fall, each with a white-hot head and a tail that fades upward.
const GLYPHS = 'ابپتثجچحخدذرزژسشصضطظعغفقکگلمنوهی۰۱۲۳۴۵۶۷۸۹0123456789ABCDEFXZ<>{}[]=+*#$%&';
function column(r, n, size, { head = '#f0fff4', body = '#3cff7a', dim = .05 } = {}) {
  let s = '';
  for (let k = 0; k < n; k++) {
    const t = k / (n - 1);                    // 0 at the top (oldest), 1 at the head
    const isHead = k === n - 1;
    const op = isHead ? 1 : dim + (1 - dim) * Math.pow(t, 1.6);
    const ch = GLYPHS[Math.floor(r() * GLYPHS.length)];
    const esc = ch === '<' ? '&lt;' : ch === '>' ? '&gt;' : ch === '&' ? '&amp;' : ch;
    s += `<text x="${size * .6}" y="${f(size * (k + 1) * 1.05)}" font-size="${size}" fill="${isHead ? head : body}" fill-opacity="${f(op)}" text-anchor="middle">${esc}</text>`;
  }
  return s;
}
function build() {
  const W = 2400, H = 1200, r = L.rng(1010);
  const c = L.canvas(W, H), b = c.b;
  b.push(`<rect width="${W}" height="${H}" fill="${c.g('bg', [[0, '#010603'], [.6, '#02110a'], [1, '#04200f']])}"/>`);
  const font = `font-family="DejaVu Sans, Tahoma, sans-serif" font-weight="bold"`;
  // three depths of still columns: far (tiny, blurred), mid, near-ish; each column a fading run of glyphs
  const depth = (n, size, op, blur) => {
    let s = '';
    for (let i = 0; i < n; i++) {
      const x = r(0, W), len = r.int(8, 30), top = r(-200, H - 100);
      s += `<g transform="translate(${f(x)} ${f(top)})" opacity="${f(op * r(.4, 1))}">${column(r, len, size, { head: '#c8ffd8', body: '#22c85a', dim: .02 })}</g>`;
    }
    return `<g ${font} ${blur ? c.blur(blur) : ''}>${s}</g>`;
  };
  b.push(depth(140, 14, .35, 1.6), depth(70, 22, .45, .8), depth(26, 30, .5, 0));
  // haze: a green glow rising from the bottom and a faint scanline texture
  b.push(c.glow(1200, 1300, 1300, '#1aff6a', .22), c.glow(400, 200, 700, '#0aff5a', .06));
  let scan = '';
  for (let y = 0; y < H; y += 6) scan += `<rect y="${y}" width="${W}" height="2" fill="#000" opacity=".18"/>`;
  b.push(scan);
  const plate = c.finish({ vignette: .7 });

  // falling columns: four variants, each a tall strip with the head at the bottom
  const sprites = {};
  const R = L.rng(77);
  for (let v = 0; v < 5; v++) {
    const n = 22, size = 34;
    sprites['col' + v] = L.sprite(size * 1.2, size * n * 1.05 + 14, L.blurF('g', 3, 30),
      `<g ${font} filter="url(#g)" opacity=".8">${column(L.rng(500 + v), n, size)}</g><g ${font}>${column(L.rng(500 + v), n, size)}</g>`);
  }
  sprites.flash = L.softDot(60, '#b8ffcc', { core: .15, coreOp: .9, mid: .3 });
  const cols = Object.keys(sprites).filter((k) => k.startsWith('col'));
  const layers = [
    // near columns, falling straight down at different speeds, fading in at the top and out near the floor
    ...cols.map((sp) => ({ sprite: sp, n: 5, x: [0, 1], y: [-.75, -.6], size: [26, 40], op: [.55, 1], move: { dx: 0, dy: [1900, 2100], dur: [4, 8] }, fade: [[0, 0], [.08, 1], [.8, 1], [1, 0]], fps: 15 })),
    // a few far, small and slow columns between them
    ...cols.map((sp) => ({ sprite: sp, n: 4, x: [0, 1], y: [-.7, -.6], size: [14, 20], op: [.25, .5], move: { dx: 0, dy: [1700, 1900], dur: [9, 14] }, fade: [[0, 0], [.1, 1], [.85, 1], [1, 0]], fps: 10 })),
    // glyphs that flash on and off in place
    { sprite: 'flash', n: 12, x: [0, 1], y: [.05, .95], size: [14, 26], op: [.2, .5], twinkle: { lo: 0, hi: 1, dur: [.8, 2] } },
  ];
  return { key: 'matrix', scene: 'Matrix', W, H, ax: .5, ay: .5, plate, sprites, layers };
}
build.key = 'matrix';
module.exports = build;
