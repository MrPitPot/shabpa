'use strict';
const L = require('../lib');

// Arcade: a glowing CRT game screen in purple. Pixel stars, a ringed planet, a neon city skyline on the ground line,
// the score along the top. Our own pixel aliens (three kinds, two drawings each) march together side to side, the
// player's ship patrols below and fires shots straight up, and a saucer crosses the top now and then.
function build() {
  const W = 2400, H = 1200, P = 8, r = L.rng(1978);
  const c = L.canvas(W, H), b = c.b;
  b.push(`<rect width="${W}" height="${H}" fill="${c.g('bg', [[0, '#0a0414'], [.6, '#1e0a3a'], [1, '#3a0f5a']])}"/>`);
  b.push(c.glow(1200, 1100, 1300, '#e83cc0', .22));
  // pixel stars in three sizes
  let stars = '';
  for (let i = 0; i < 260; i++) { const s = r.pick([P / 2, P / 2, P]), x = Math.round(r(0, W) / 4) * 4, y = Math.round(r(0, 820) / 4) * 4; stars += `<rect x="${x}" y="${y}" width="${s}" height="${s}" fill="${r.pick(['#ffffff', '#c8b8ff', '#ff9ae8', '#38e8ff'])}" opacity="${r(.4, 1).toFixed(2)}"/>`; }
  b.push(`<g shape-rendering="crispEdges">${stars}</g>`);
  // a ringed planet, dithered in pixel steps
  let pl = '';
  const cx = 2180, cy = 200, R0 = 96;
  for (let y = -R0; y <= R0; y += P) for (let x = -R0; x <= R0; x += P) {
    const d = Math.hypot(x, y); if (d > R0) continue;
    const lit = (x * -.6 + y * -.5) / R0 + .3;
    const tone = lit > .5 ? '#ff9ae8' : lit > .15 ? ((x + y) / P % 2 === 0 ? '#ff9ae8' : '#c04aa8') : lit > -.25 ? '#c04aa8' : ((x + y) / P % 2 === 0 ? '#c04aa8' : '#6a1a6a');
    pl += `<rect x="${cx + x}" y="${cy + y}" width="${P}" height="${P}" fill="${tone}"/>`;
  }
  for (let a = 0; a < Math.PI * 2; a += .02) { const x = Math.round(Math.cos(a) * 180 / P) * P, y = Math.round(Math.sin(a) * 36 / P) * P; if (Math.sin(a) < 0 && Math.abs(x) < R0 * .95) continue; pl += `<rect x="${cx + x}" y="${cy + y}" width="${P}" height="${P}" fill="#38e8ff"/>`; }
  b.push(`<g shape-rendering="crispEdges" transform="rotate(-12 ${cx} ${cy})">${pl}</g>`);
  // the neon city on the ground line
  let city = '';
  for (let x = 0; x < W; x += r.int(5, 12) * P) {
    const bw = r.int(6, 14) * P, bh = r.int(10, 34) * P, y = 1040 - bh;
    city += `<rect x="${x}" y="${y}" width="${bw}" height="${bh}" fill="#12062a"/><rect x="${x}" y="${y}" width="${bw}" height="${P / 2}" fill="${r.pick(['#e83cc0', '#38e8ff'])}"/>`;
    for (let wy = y + 2 * P; wy < 1030; wy += 3 * P) for (let wx = x + P; wx < x + bw - P; wx += 2 * P) if (r() < .35) city += `<rect x="${wx}" y="${wy}" width="${P}" height="${P}" fill="${r.pick(['#ff4fd8', '#38e8ff', '#ffd25e'])}" opacity=".8"/>`;
  }
  b.push(`<g shape-rendering="crispEdges">${city}</g>`);
  b.push(`<rect y="1040" width="${W}" height="6" fill="#38e8ff"/>`, c.glow(1200, 1043, 1300, '#38e8ff', .1));
  b.push(`<rect y="1046" width="${W}" height="${H - 1046}" fill="#0a0414"/>`);
  // score line in a pixel font
  const font = { 0: ['111', '101', '101', '101', '111'], 1: ['010', '110', '010', '010', '111'], 2: ['111', '001', '111', '100', '111'], 4: ['101', '101', '111', '001', '001'], S: ['111', '100', '111', '001', '111'], C: ['111', '100', '100', '100', '111'], O: ['111', '101', '101', '101', '111'], R: ['110', '101', '110', '101', '101'], E: ['111', '100', '110', '100', '111'], H: ['101', '101', '111', '101', '101'], I: ['111', '010', '010', '010', '111'], ' ': ['000', '000', '000', '000', '000'] };
  const text = (s, x, y, col, k = 6) => { let o = ''; [...s].forEach((ch, i) => (font[ch] || font[' ']).forEach((row, ry) => [...row].forEach((v, rx) => { if (v === '1') o += `<rect x="${x + (i * 4 + rx) * k}" y="${y + ry * k}" width="${k}" height="${k}" fill="${col}"/>`; }))); return o; };
  b.push(`<g shape-rendering="crispEdges">${text('SCORE 004210', 80, 50, '#ffffff')}${text('HI 012000', 1700, 50, '#ff4fd8')}</g>`);
  // CRT: scanlines and a curved glass sheen
  let scan = '';
  for (let y = 0; y < H; y += 4) scan += `M0 ${y}H${W}`;
  b.push(`<path d="${scan}" stroke="#000" stroke-opacity=".22" stroke-width="1.5"/>`);
  b.push(`<ellipse cx="900" cy="200" rx="900" ry="260" fill="#ffffff" opacity=".035"/>`);
  const plate = c.finish({ vignette: .8, grain: .04 });

  const pix = (map, col, s = 6) => {
    let out = '';
    map.forEach((row, y) => [...row].forEach((ch, x) => { if (ch === '#') out += `<rect x="${x * s}" y="${y * s}" width="${s}" height="${s}" fill="${col}"/>`; }));
    const w = map[0].length * s, h = map.length * s;
    return L.sprite(w + 24, h + 24, L.blurF('gl', 4, 40), `<g transform="translate(12 12)"><g filter="url(#gl)" opacity=".7" shape-rendering="crispEdges">${out}</g><g shape-rendering="crispEdges">${out}</g></g>`);
  };
  // three alien kinds of our own: a squid-like one, a crab with claws, a round blob with eyes
  const a1 = ['....##....', '...####...', '..######..', '.##.##.##.', '.########.', '...#..#...', '..#.##.#..', '.#.#..#.#.'];
  const a1b = ['....##....', '...####...', '..######..', '.##.##.##.', '.########.', '..#.##.#..', '.#......#.', '..#....#..'];
  const a2 = ['.#......#.', '#.#....#.#', '#.######.#', '###.##.###', '.########.', '..######..', '..#....#..', '.##....##.'];
  const a2b = ['.#......#.', '..#....#..', '..######..', '.##.##.##.', '##########', '#.######.#', '#.#....#.#', '...#..#...'];
  const a3 = ['...####...', '.########.', '##.####.##', '##########', '.########.', '.#.#..#.#.', '#........#', '.#......#.'];
  const a3b = ['...####...', '.########.', '##.####.##', '##########', '.########.', '..#.##.#..', '.#..##..#.', '#........#'];
  const ship = ['.....#.....', '....###....', '....###....', '.#########.', '###########', '###########'];
  const ufo = ['....######....', '..##########..', '.##.##.##.##.#', '##############', '..###....###..', '...#......#...'];
  const sprites = {
    a1: pix(a1, '#38e8ff'), a1b: pix(a1b, '#38e8ff'), a2: pix(a2, '#ff4fd8'), a2b: pix(a2b, '#ff4fd8'), a3: pix(a3, '#ffd25e'), a3b: pix(a3b, '#ffd25e'),
    ship: pix(ship, '#7dff8a'), ufo: pix(ufo, '#ff5a5a'),
    shot: L.sprite(10, 40, L.lin('s', [[0, '#ffffff'], [1, '#7dff8a', 0]], 0, 0, 0, 1), `<rect x="2" width="6" height="40" fill="url(#s)"/>`),
  };
  const march = { x: 220, dur: 7 };
  const layers = [
    { sprite: ['a1', 'a1b'], frames: { fps: 2 }, grid: { cols: 9, rows: 1, x: 620, y: 260, dx: 130, dy: 0 }, size: 84, sway: march, phase: 0 },
    { sprite: ['a2', 'a2b'], frames: { fps: 2 }, grid: { cols: 9, rows: 2, x: 620, y: 360, dx: 130, dy: 100 }, size: 84, sway: march, phase: 0 },
    { sprite: ['a3', 'a3b'], frames: { fps: 2 }, grid: { cols: 9, rows: 1, x: 620, y: 560, dx: 130, dy: 0 }, size: 84, sway: march, phase: 0 },
    { sprite: 'ship', at: [[1200, 990]], size: 96, sway: { x: 700, dur: 9 }, phase: .3 },
    { sprite: 'shot', n: 4, x: [.3, .7], y: [.78, .8], size: 9, move: { dx: 0, dy: -760, dur: [1.6, 2.4], travel: .5 }, fade: [[0, 1], [.48, 1], [.5, 0], [1, 0]], fps: 18 },
    { sprite: 'ufo', n: 1, x: [-.08, -.06], y: [.13, .13], size: 120, move: { dx: 2800, dy: 0, dur: 30, travel: .45 }, fade: [[0, 1], [.45, 1], [.46, 0], [1, 0]] },
  ];
  return { key: 'arcade', scene: 'Invaders', W, H, ax: .5, ay: .7, plate, sprites, layers };
}
build.key = 'arcade';
module.exports = build;
