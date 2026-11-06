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
