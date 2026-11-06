// Shared helpers for the premium-theme paintings. Every theme is an SVG painting (the still "plate" behind the pages)
// plus a few small sprites that the app moves with plain transforms. Chromium renders them to PNG/JPEG (render.js).
'use strict';

/** A small seeded random generator, so every render of a painting is identical. */
function rng(seed) {
  let a = seed >>> 0;
  const next = () => {
    a = (a + 0x6D2B79F5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
  const r = (lo = 0, hi = 1) => lo + next() * (hi - lo);
  r.int = (lo, hi) => Math.floor(r(lo, hi + 1));
  r.pick = (arr) => arr[Math.floor(next() * arr.length)];
  r.gauss = () => { let s = 0; for (let i = 0; i < 4; i++) s += next(); return (s - 2) / 2; };
  return r;
}

let uid = 0;
const id = (p = 'g') => `${p}${++uid}`;

const f = (n) => (Math.round(n * 100) / 100).toString();

function svg(W, H, defs, body, extra = '') {
  return `<svg xmlns="http://www.w3.org/2000/svg" width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" ${extra}><defs>${defs}</defs>${body}</svg>`;
}

/** Linear gradient: stops = [[offset, color, opacity?], ...]; angle via x1,y1,x2,y2 (fractions). */
function lin(gid, stops, x1 = 0, y1 = 0, x2 = 0, y2 = 1, extra = '') {
  return `<linearGradient id="${gid}" x1="${x1}" y1="${y1}" x2="${x2}" y2="${y2}" ${extra}>${stops.map(([o, c, op = 1]) => `<stop offset="${o}" stop-color="${c}" stop-opacity="${op}"/>`).join('')}</linearGradient>`;
}

function rad(gid, stops, cx = .5, cy = .5, r = .5, extra = '') {
  return `<radialGradient id="${gid}" cx="${cx}" cy="${cy}" r="${r}" ${extra}>${stops.map(([o, c, op = 1]) => `<stop offset="${o}" stop-color="${c}" stop-opacity="${op}"/>`).join('')}</radialGradient>`;
}

const blurF = (fid, sd, pad = 50) => `<filter id="${fid}" x="-${pad}%" y="-${pad}%" width="${100 + 2 * pad}%" height="${100 + 2 * pad}%"><feGaussianBlur stdDeviation="${sd}"/></filter>`;

/** Film grain over the whole picture: breaks up gradient banding (and JPEG banding) and gives a painted surface. */
function grainF(fid, amount = .06, freq = .9, seed = 3) {
  return `<filter id="${fid}" x="0" y="0" width="100%" height="100%"><feTurbulence type="fractalNoise" baseFrequency="${freq}" numOctaves="2" seed="${seed}" result="n"/>
  <feColorMatrix in="n" type="matrix" values="0 0 0 0 .5  0 0 0 0 .5  0 0 0 0 .5  0 0 0 ${amount * 2.2} 0" result="g"/>
  <feComposite in="g" in2="SourceGraphic" operator="in" result="gi"/><feBlend in="SourceGraphic" in2="gi" mode="overlay"/></filter>`;
}

/** Cloud / smoke / nebula texture: turbulence turned into a coloured alpha mask. */
function cloudF(fid, { freq = .004, oct = 5, seed = 1, color = [1, 1, 1], contrast = 1.6, bias = -.35, stretchX = 1 } = {}) {
  const [r, g, b] = color;
  return `<filter id="${fid}" x="0" y="0" width="100%" height="100%" color-interpolation-filters="sRGB"><feTurbulence type="fractalNoise" baseFrequency="${freq / stretchX} ${freq}" numOctaves="${oct}" seed="${seed}"/>
  <feColorMatrix type="matrix" values="0 0 0 0 ${r}  0 0 0 0 ${g}  0 0 0 0 ${b}  ${contrast} 0 0 0 ${bias}"/>
  <feComposite in2="SourceGraphic" operator="in"/></filter>`;
}

const hex = (c) => c.startsWith('#') ? c : '#' + c;

/** "#RRGGBB" → [r,g,b] in 0..1 (for filter matrices). */
function rgb01(c) { c = c.replace('#', ''); return [0, 2, 4].map((i) => parseInt(c.slice(i, i + 2), 16) / 255); }

function mix(a, b, t) {
  const A = rgb01(a), B = rgb01(b);
  return '#' + A.map((v, i) => Math.round((v + (B[i] - v) * t) * 255).toString(16).padStart(2, '0')).join('');
}

/** A soft round glow (radial gradient circle). */
function glow(defs, cx, cy, r, color, op = 1, falloff = [[0, 1], [.35, .45], [1, 0]]) {
  const gid = id('gl');
  defs.push(rad(gid, falloff.map(([o, a]) => [o, color, a * op])));
  return `<circle cx="${f(cx)}" cy="${f(cy)}" r="${f(r)}" fill="url(#${gid})"/>`;
}

/** Star field: tiny dots with a few brighter stars that carry a soft halo and a cross glint. */
function starfield(defs, r, { x0 = 0, y0 = 0, x1, y1, n = 400, color = '#FFFFFF', bright = 18, minR = .4, maxR = 1.5, density = null } = {}) {
  let out = '';
  for (let i = 0; i < n; i++) {
    const x = r(x0, x1), y = density ? density(r) * (y1 - y0) + y0 : r(y0, y1);
    const s = Math.pow(r(), 3) * (maxR - minR) + minR;
    out += `<circle cx="${f(x)}" cy="${f(y)}" r="${f(s)}" fill="${color}" opacity="${f(r(.25, .95))}"/>`;
  }
  for (let i = 0; i < bright; i++) {
    const x = r(x0, x1), y = r(y0, y1), s = r(1.4, 2.6);
    out += glow(defs, x, y, s * 7, color, .35);
    out += `<circle cx="${f(x)}" cy="${f(y)}" r="${f(s)}" fill="${color}"/>`;
    if (i % 3 === 0) out += `<path d="M${f(x - s * 6)} ${f(y)}H${f(x + s * 6)}M${f(x)} ${f(y - s * 6)}V${f(y + s * 6)}" stroke="${color}" stroke-width=".7" opacity=".55"/>`;
  }
  return out;
}

/** A ridge line as a closed path filled down to the bottom: a sum of sines plus noise, for hills, dunes, mountains. */
function ridge(r, W, H, base, amp, { step = 12, waves = [[.0021, 1], [.0057, .45], [.013, .18]], jag = 0, bottom = null, x0 = -20, x1 = null } = {}) {
  x1 = x1 ?? W + 20;
  const ph = waves.map(() => r(0, Math.PI * 2));
  let d = `M${x0} ${bottom ?? H + 10}`;
  for (let x = x0; x <= x1; x += step) {
    let y = base;
    waves.forEach(([fr, a], i) => { y -= Math.sin(x * fr + ph[i]) * amp * a; });
    if (jag) y += r(-jag, jag);
    d += `L${f(x)} ${f(y)}`;
  }
  return d + `L${x1} ${bottom ?? H + 10}Z`;
}

/** The ridge height at x for the same parameters (so things can stand on a hill). */
function ridgeFn(r, base, amp, waves = [[.0021, 1], [.0057, .45], [.013, .18]]) {
  const ph = waves.map(() => r(0, Math.PI * 2));
  return (x) => { let y = base; waves.forEach(([fr, a], i) => { y -= Math.sin(x * fr + ph[i]) * amp * a; }); return y; };
}

function ridgePath(fn, W, H, { step = 10, x0 = -20, x1 = null, bottom = null, jag = 0, r = null } = {}) {
  x1 = x1 ?? W + 20;
  let d = `M${x0} ${bottom ?? H + 10}`;
  for (let x = x0; x <= x1; x += step) d += `L${f(x)} ${f(fn(x) + (jag && r ? r(-jag, jag) : 0))}`;
  return d + `L${x1} ${bottom ?? H + 10}Z`;
}

/** Points → smooth closed path (Catmull-Rom to Bezier). */
function smooth(pts, closed = true, t = .5) {
  const n = pts.length;
  const P = (i) => pts[closed ? (i + n) % n : Math.max(0, Math.min(n - 1, i))];
  let d = `M${f(pts[0][0])} ${f(pts[0][1])}`;
  const end = closed ? n : n - 1;
  for (let i = 0; i < end; i++) {
    const p0 = P(i - 1), p1 = P(i), p2 = P(i + 1), p3 = P(i + 2);
    const c1 = [p1[0] + (p2[0] - p0[0]) * t / 3, p1[1] + (p2[1] - p0[1]) * t / 3];
    const c2 = [p2[0] - (p3[0] - p1[0]) * t / 3, p2[1] - (p3[1] - p1[1]) * t / 3];
    d += `C${f(c1[0])} ${f(c1[1])} ${f(c2[0])} ${f(c2[1])} ${f(p2[0])} ${f(p2[1])}`;
  }
  return d + (closed ? 'Z' : '');
}

/** A blob: a wobbly circle as a smooth closed path. */
function blob(r, cx, cy, rx, ry, wob = .18, k = 9) {
  const pts = [];
  for (let i = 0; i < k; i++) {
    const a = i / k * Math.PI * 2;
    const m = 1 + r(-wob, wob);
    pts.push([cx + Math.cos(a) * rx * m, cy + Math.sin(a) * ry * m]);
  }
  return smooth(pts);
}

/** A conifer silhouette: a trunk with many drooping, jagged branch tiers (not three triangles). */
function pine(r, x, yBase, h, { color = '#0b2a1c', w = .42, tiers = 14, snow = null, light = null, jag = 1 } = {}) {
  const top = yBase - h;
  let d = `M${f(x)} ${f(top)}`;
  const left = [], right = [];
  for (let i = 1; i <= tiers; i++) {
    const t = i / tiers;
    const y = top + h * .92 * t;
    const half = h * w * .5 * Math.pow(t, .9) * r(.82, 1.12);
    const droop = h * .035 * r(.6, 1.3);
    right.push([x + half, y + droop], [x + half * r(.25, .45), y - h * .01 * jag]);
    left.push([x - half * r(.25, .45), y - h * .01 * jag], [x - half * r(.9, 1.08), y + droop * r(.8, 1.2)]);
  }
  for (const [px, py] of right) d += `L${f(px)} ${f(py)}`;
  d += `L${f(x + h * .03)} ${f(yBase - h * .06)}L${f(x + h * .02)} ${f(yBase)}L${f(x - h * .02)} ${f(yBase)}L${f(x - h * .03)} ${f(yBase - h * .06)}`;
  for (let i = left.length - 1; i >= 0; i--) d += `L${f(left[i][0])} ${f(left[i][1])}`;
  d += 'Z';
  let out = `<path d="${d}" fill="${color}"/>`;
  if (snow) {
    // a cap of snow on the upper side of each tier
    for (let i = 1; i <= tiers; i++) {
      const t = i / tiers;
      const y = top + h * .92 * t;
      const half = h * w * .5 * Math.pow(t, .9);
      const sw = half * r(.5, .95);
      const sx = x + r(-half * .25, half * .25);
      out += `<path d="M${f(sx - sw)} ${f(y + h * .02)}Q${f(sx)} ${f(y - h * .022)} ${f(sx + sw)} ${f(y + h * .015)}Q${f(sx)} ${f(y + h * .002)} ${f(sx - sw)} ${f(y + h * .02)}Z" fill="${snow}" opacity="${f(r(.55, .95))}"/>`;
    }
  }
  if (light) out += `<path d="${d}" fill="${light}" opacity=".0"/>`;
  return out;
}

module.exports = { rng, id, f, svg, lin, rad, blurF, grainF, cloudF, hex, rgb01, mix, glow, starfield, ridge, ridgeFn, ridgePath, smooth, blob, pine };

/** A painting in progress: defs and body lists plus gradient/filter shorthands; finish() adds vignette + grain and returns the SVG. */
function canvas(W, H) {
  const defs = [], b = [];
  const c = {
    W, H, defs, b,
    g: (k, stops, ...a) => { const i = id(k); defs.push(lin(i, stops, ...a)); return `url(#${i})`; },
    rg: (k, stops, ...a) => { const i = id(k); defs.push(rad(i, stops, ...a)); return `url(#${i})`; },
    blur: (sd, pad = 50) => { const i = id('bl'); defs.push(blurF(i, sd, pad)); return `filter="url(#${i})"`; },
    glow: (x, y, r, col, op, fall) => glow(defs, x, y, r, col, op, fall),
    cloud: (opts) => { const i = id('cl'); defs.push(cloudF(i, opts)); return `filter="url(#${i})"`; },
    finish: ({ vignette = .55, grain = .05, extra = '' } = {}) => {
      if (vignette) b.push(`<rect width="${W}" height="${H}" fill="${c.rg('vg', [[0, '#000', 0], [.7, '#000', vignette * .1], [1, '#000', vignette]], .5, .5, .75)}"/>`);
      const gr = id('gr'); defs.push(grainF(gr, grain));
      return svg(W, H, defs.join(''), `<g filter="url(#${gr})">${b.join('')}</g>${extra}`);
    },
  };
  return c;
}

/** A sprite: a small SVG with its own defs. */
function sprite(w, h, defs, body) { return { w, h, svg: svg(w, h, defs, body) }; }

/** A soft round dot (snow, dust, glow): core colour fading out. */
function softDot(size, color, { core = .35, coreOp = 1, mid = .45 } = {}) {
  return sprite(size, size, rad('d', [[0, color, coreOp], [core, color, mid], [1, color, 0]]), `<circle cx="${size / 2}" cy="${size / 2}" r="${size / 2}" fill="url(#d)"/>`);
}

module.exports.canvas = canvas;
module.exports.sprite = sprite;
module.exports.softDot = softDot;

/** A mountain range with real faces: jagged ridge (midpoint displacement), the faces turned to the light lit and the
 *  others in shadow, gullies streaking down, and snow above a ragged snowline. light: 1 = from the right, -1 = from the left. */
function mountains(c, r, { base, amp, x0 = -60, x1 = null, rough = .55, light = 1, rock = ['#1c2a3a', '#3a5068'], snow = ['#8aa4bc', '#e6f0fa'], snowline = .45, bottom = null, detail = 9 } = {}) {
  x1 = x1 ?? c.W + 60;
  const n = 2 ** detail;
  const ys = new Array(n + 1);
  ys[0] = base - r(.2, .6) * amp; ys[n] = base - r(.2, .6) * amp;
  for (let step = n; step > 1; step /= 2) {
    const sc = amp * Math.pow(rough, Math.log2(n / step)) * 1.4;
    for (let i = step / 2; i < n; i += step) ys[i] = (ys[i - step / 2] + ys[i + step / 2]) / 2 - r(-.45, 1) * sc;
  }
  const pts = [];
  for (let i = 0; i <= n; i++) { const xa = x0 + (x1 - x0) * i / n; pts.push([xa, Math.min(base, ys[i])]); if (i < n) for (let q = 1; q < 4; q++) pts.push([xa + (x1 - x0) / n * q / 4, Math.min(base, ys[i] + (ys[i + 1] - ys[i]) * q / 4 + r(-1, 1) * amp * .025)]); }
  const bot = bottom ?? c.H + 10;
  const body = `M${x0} ${bot}` + pts.map(([x, y]) => `L${f(x)} ${f(y)}`).join('') + `L${x1} ${bot}Z`;
  const clip = id('mc');
  c.defs.push(`<clipPath id="${clip}"><path d="${body}"/></clipPath>`);
  let s = `<path d="${body}" fill="${rock[0]}"/>`;
  // lit faces: from each peak, the side toward the light, down to the foot
  let lit = '', snowS = '', gul = '';
  const top = Math.min(...pts.map((p) => p[1]));
  const sl = (x) => top + (base - top) * snowline + Math.sin(x * .02) * 14 + Math.sin(x * .055) * 8;
  const coarse = ys.map((y, i) => [x0 + (x1 - x0) * i / n, Math.min(base, y)]);
  for (let i = 1; i < coarse.length - 1; i++) {
    const pts = coarse;
    const [x, y] = pts[i];
    if (!(y <= pts[i - 1][1] && y <= pts[i + 1][1])) continue;
    // walk toward the light until the ridge turns up again; the face ends in a spur running back down from that valley
    let j = i, poly = [[x, y]];
    while (j + light >= 0 && j + light < pts.length && pts[j + light][1] >= pts[j][1] - 1) { j += light; poly.push(pts[j]); }
    const [vx, vy] = pts[j], foot = Math.min(bot, base + 30);
    poly.push([vx + light * (foot - vy) * .55, foot], [x + light * (foot - y) * .3, foot]);
    lit += `<path d="M${poly.map(([a, b]) => `${f(a)} ${f(b)}`).join('L')}Z"/>`;
    // a few gullies streaking down the lit face
    for (let k = 0; k < 3; k++) { const gx = x + light * r(.1, .9) * Math.abs(vx - x), gy = y + Math.abs(gx - x) * .25; gul += `M${f(gx)} ${f(gy + r(10, 30))}l${f(-light * r(4, 20))} ${f(r(40, 120))}`; }
  }
  const fade = id('mf');
  c.defs.push(`<linearGradient id="${fade}g" x1="0" y1="0" x2="0" y2="1" gradientUnits="userSpaceOnUse" y1="${f(top)}" y2="${f(base + 30)}"><stop offset="0" stop-color="#fff"/><stop offset=".55" stop-color="#fff" stop-opacity=".75"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></linearGradient><mask id="${fade}"><rect x="${x0}" y="${f(top - 10)}" width="${x1 - x0}" height="${f(bot - top + 20)}" fill="url(#${fade}g)"/></mask>`);
  s += `<g clip-path="url(#${clip})"><g fill="${rock[1]}" opacity=".9" mask="url(#${fade})">${lit}</g>`;
  // snow cap: everything above the snowline, shadow tone, then lit tone on the lit faces
  let cap = `M${x0} ${f(sl(x0))}`;
  for (let x = x0; x <= x1; x += 12) cap += `L${f(x)} ${f(sl(x) + r(-10, 16))}`;
  cap += `L${x1} -10L${x0} -10Z`;
  const capId = id('cap');
  c.defs.push(`<clipPath id="${capId}"><path d="${cap}"/></clipPath>`);
  snowS += `<g clip-path="url(#${capId})"><path d="${body}" fill="${snow[0]}"/><g fill="${snow[1]}">${lit}</g></g>`;
  s += snowS;
  s += `<path d="${gul}" stroke="${snow[0]}" stroke-opacity=".35" stroke-width="2" fill="none" stroke-linecap="round" mask="url(#${fade})"/>`;
  s += `</g>`;
  return { svg: s, pts, at: (x) => { const i = Math.max(0, Math.min(pts.length - 1, Math.round((x - x0) / (x1 - x0) * (pts.length - 1)))); return pts[i][1]; } };
}
module.exports.mountains = mountains;
