'use strict';
const L = require('../lib');
const { f } = L;

// Bauhaus: a printed poster on warm paper. A large composition of primary shapes (a red sun, a blue square turned on its
// corner, a yellow half disc, black bars and thin rules, a field of halftone dots, overlapping translucent inks where the
// shapes cross), all with soft print texture. Small shapes float and turn slowly over it.
function build() {
  const W = 2400, H = 1200, r = L.rng(1919);
  const c = L.canvas(W, H), b = c.b;
  const red = '#d8382a', blue = '#1f4fa0', yellow = '#f2b81c', ink = '#1c1a18';
  b.push(`<rect width="${W}" height="${H}" fill="${c.rg('pp', [[0, '#f7efdf'], [.7, '#efe4cc'], [1, '#e2d3b4']], .45, .45, .8)}"/>`);
  // paper fibres
  b.push(`<rect width="${W}" height="${H}" fill="#000" ${c.cloud({ freq: .02, oct: 3, seed: 9, color: L.rgb01('#b89c6a'), contrast: 1.4, bias: -.6, stretchX: 3 })} opacity=".25"/>`);
  // construction grid, very faint
  let grid = '';
  for (let x = 0; x <= W; x += 120) grid += `<path d="M${x} 0V${H}" stroke="${ink}" stroke-opacity=".05"/>`;
  for (let y = 0; y <= H; y += 120) grid += `<path d="M0 ${y}H${W}" stroke="${ink}" stroke-opacity=".05"/>`;
  b.push(grid);
  // halftone dot field, dots shrinking toward the edge of a circle
  let dots = '';
  for (let y = 120; y < 1100; y += 26) for (let x = 120; x < 900; x += 26) {
    const d = Math.hypot(x - 420, y - 640) / 460; if (d > 1) continue;
    dots += `<circle cx="${x + ((y / 26) % 2) * 13}" cy="${y}" r="${f(9 * (1 - d) + .6)}" fill="${ink}" opacity=".85"/>`;
  }
  b.push(dots);
  // the big shapes; multiply where they overlap like layered inks
  b.push(`<g style="mix-blend-mode:multiply">
    <circle cx="1240" cy="520" r="330" fill="${red}"/>
    <path d="M1460 300L1760 600L1460 900L1160 600Z" fill="${blue}" opacity=".92"/>
    <path d="M520 1000A300 300 0 0 1 1120 1000Z" fill="${yellow}"/>
    <circle cx="1950" cy="330" r="150" fill="${yellow}"/>
    <rect x="1840" y="640" width="380" height="380" fill="${red}" opacity=".9"/>
  </g>`);
  // black bars, thin rules, an arc and a ring
  b.push(`<rect x="140" y="160" width="900" height="34" fill="${ink}"/><rect x="980" y="860" width="1180" height="18" fill="${ink}"/>
    <rect x="2020" y="120" width="26" height="980" fill="${ink}"/>
    <path d="M100 400L1100 1150M1600 80L2350 830" stroke="${ink}" stroke-width="3"/>
    <path d="M1700 1120A260 260 0 0 1 2220 1120" fill="none" stroke="${blue}" stroke-width="40"/>
    <circle cx="700" cy="330" r="90" fill="none" stroke="${ink}" stroke-width="16"/>
    <path d="M300 820L460 540L620 820Z" fill="${blue}"/>`);
  // stripes on the yellow half disc
  for (let k = 0; k < 6; k++) b.push(`<path d="M${600 + k * 90} 1000V${f(1000 - Math.sqrt(Math.max(0, 300 * 300 - Math.pow(600 + k * 90 - 820, 2))))}" stroke="${ink}" stroke-width="10" opacity=".85"/>`);
  const plate = c.finish({ vignette: .18, grain: .07 });

  // floating shapes, small, flat ink
  const sh = (w, body) => L.sprite(w, w, '', body);
  const sprites = {
    circle: sh(80, `<circle cx="40" cy="40" r="38" fill="${red}"/>`),
    tri: sh(80, `<path d="M40 6L76 72H4Z" fill="${blue}"/>`),
    half: sh(80, `<path d="M4 58A36 36 0 0 1 76 58Z" fill="${yellow}"/>`),
    square: sh(80, `<rect x="14" y="14" width="52" height="52" fill="${ink}"/>`),
    ring: sh(80, `<circle cx="40" cy="40" r="30" fill="none" stroke="${blue}" stroke-width="10"/>`),
    cross: sh(80, `<path d="M40 8V72M8 40H72" stroke="${red}" stroke-width="12"/>`),
  };
  const drift = (sprite, n) => ({ sprite, n, x: [.03, .97], y: [.08, .92], size: [34, 70], op: [.85, 1], spin: { deg: 360, dur: [30, 60], either: true }, sway: { x: [30, 70], y: [20, 50], dur: [6, 10], dury: [7, 12] } });
  const layers = Object.keys(sprites).map((k) => drift(k, 3));
  return { key: 'bauhaus', scene: 'Shapes', W, H, ax: .5, ay: .5, plate, sprites, layers };
}
build.key = 'bauhaus';
module.exports = build;
