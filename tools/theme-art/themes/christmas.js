'use strict';
const L = require('../lib');
const { f, id } = L;

// Christmas: a snowy village night with a hand-painted, fully decorated fir: thousands of needles lit from the moon
// side, baubles, a gold bead garland and a string of bulbs that light one after another (sprites anchored on the bulbs).
function build() {
  const W = 2400, H = 1200, r = L.rng(1225);
  const defs = [];
  const b = [];
  const g = (k, stops, ...a) => { const i = id(k); defs.push(L.lin(i, stops, ...a)); return `url(#${i})`; };
  const rg = (k, stops, ...a) => { const i = id(k); defs.push(L.rad(i, stops, ...a)); return `url(#${i})`; };
  const blur = (sd) => { const i = id('bl'); defs.push(L.blurF(i, sd)); return `filter="url(#${i})"`; };

  // sky, moon, a faint aurora
  b.push(`<rect width="${W}" height="${H}" fill="${g('sky', [[0, '#040c10'], [.45, '#0a1f24'], [.75, '#14353a'], [1, '#2a4b4c']])}"/>`);
  b.push(L.glow(defs, 520, 230, 520, '#9fd6d0', .22));
  const aur = id('au'); defs.push(L.cloudF(aur, { freq: .0022, oct: 3, seed: 8, color: L.rgb01('#63e6a8'), contrast: 2.2, bias: -1.05, stretchX: 5 }));
  b.push(`<g opacity=".38" ${blur(18)}><rect x="0" y="40" width="${W}" height="420" filter="url(#${aur})" fill="#000" mask="url(#aurm)"/></g>`);
  defs.push(`<mask id="aurm"><rect width="${W}" height="${H}" fill="${g('aurg', [[0, '#000'], [.25, '#fff'], [.6, '#fff'], [1, '#000']], 0, 0, 1, 0)}"/></mask>`);
  b.push(L.starfield(defs, r, { x1: W, y0: 0, y1: 640, n: 520, color: '#eaf6ff', bright: 26, maxR: 1.4 }));
  // moon with a soft halo and a few seas
  b.push(L.glow(defs, 520, 230, 170, '#e8fff8', .35));
  b.push(`<circle cx="520" cy="230" r="66" fill="${rg('mn', [[0, '#fbfff6'], [.7, '#e7f2e6'], [1, '#c8dccf']], .42, .4, .62)}"/>`);
  for (let i = 0; i < 7; i++) b.push(`<path d="${L.blob(r, 520 + r(-34, 34), 230 + r(-30, 30), r(6, 15), r(5, 12), .3, 7)}" fill="#b9cfc3" opacity="${f(r(.15, .3))}" ${blur(3)}/>`);

  // far snowy hills with tiny forests
  const far = L.ridgeFn(r, 690, 70, [[.0016, 1], [.0049, .35], [.011, .12]]);
  b.push(`<path d="${L.ridgePath(far, W, H)}" fill="${g('h1', [[0, '#5f8288'], [.3, '#3b5d63'], [1, '#22393e']])}"/>`);
  let fp = '';
  for (let x = -10; x < W; x += r(5, 16)) { const y = far(x) + r(4, 40); fp += L.pine(r, x, y, r(16, 38), { color: '#1b3236', tiers: 6 }); }
  b.push(`<g opacity=".85">${fp}</g>`);
  b.push(`<rect y="600" width="${W}" height="260" fill="${g('hz', [[0, '#9fc7c4', 0], [.6, '#9fc7c4', .16], [1, '#9fc7c4', 0]])}"/>`);

  // middle hills with snow-laden pines
  const mid = L.ridgeFn(r, 860, 55, [[.0021, 1], [.006, .4]]);
  b.push(`<path d="${L.ridgePath(mid, W, H)}" fill="${g('h2', [[0, '#a9c4cb'], [.15, '#7397a0'], [1, '#3a5860']])}"/>`);
  let mp = '';
  for (let x = -40; x < W + 40; x += r(18, 46)) {
    if (x > 1500 && x < 2000) continue;
    const y = mid(x) + r(6, 50), hh = r(60, 150);
    mp += L.pine(r, x, y, hh, { color: L.mix('#0f2b2a', '#24484a', r(0, .6)), tiers: 11, snow: '#dcecef' });
  }
  b.push(mp);

  // the village: small houses with warm windows
  const houses = [];
  const house = (x, y, w, h, roof, wall) => {
    let s = '';
    s += L.glow(defs, x + w / 2, y + h * .2, w * 1.5, '#ffb35c', .22);
    s += `<rect x="${x}" y="${y - h}" width="${w}" height="${h}" fill="${wall}"/>`;
    s += `<rect x="${x}" y="${y - h}" width="${w}" height="${h}" fill="${g('wl', [[0, '#000', 0], [1, '#000', .35]], 0, 0, 1, 0)}"/>`;
    for (let k = 0; k < 5; k++) s += `<line x1="${x}" x2="${x + w}" y1="${f(y - h + h * k / 5)}" y2="${f(y - h + h * k / 5)}" stroke="#000" stroke-opacity=".18" stroke-width="1.2"/>`;
    const wins = Math.max(1, Math.round(w / 44));
    for (let k = 0; k < wins; k++) {
      const wx = x + w * (k + .5) / wins - 9, wy = y - h * .62;
      s += `<rect x="${f(wx - 3)}" y="${f(wy - 3)}" width="24" height="30" fill="#3a2414"/>`;
      s += `<rect x="${f(wx)}" y="${f(wy)}" width="18" height="24" fill="${g('wn', [[0, '#ffe7a6'], [1, '#ff9d3c']])}"/>`;
      s += `<path d="M${f(wx + 9)} ${f(wy)}v24M${f(wx)} ${f(wy + 12)}h18" stroke="#5a3418" stroke-width="2"/>`;
      s += L.glow(defs, wx + 9, wy + 12, 40, '#ffc46b', .45);
    }
    // roof with a deep snow cover and an overhang
    s += `<path d="M${x - 16} ${y - h + 4}L${x + w / 2} ${y - h - roof}L${x + w + 16} ${y - h + 4}Z" fill="#3b2a26"/>`;
    s += `<path d="M${x - 20} ${y - h + 6}Q${x - 6} ${y - h - 6} ${x + w / 2} ${y - h - roof - 8}Q${x + w + 6} ${y - h - 8} ${x + w + 20} ${y - h + 6}Q${x + w + 8} ${y - h + 14} ${x + w * .75} ${y - h + 8}Q${x + w / 2} ${y - h + 16} ${x + w * .25} ${y - h + 9}Q${x - 6} ${y - h + 16} ${x - 20} ${y - h + 6}Z" fill="${g('rf', [[0, '#ffffff'], [1, '#bcd3da']])}"/>`;
    const cx = x + w * .72, cy = y - h - roof * .55;
    s += `<rect x="${f(cx)}" y="${f(cy - 34)}" width="16" height="${f(40)}" fill="#4a2f26"/><rect x="${f(cx - 3)}" y="${f(cy - 40)}" width="22" height="9" rx="3" fill="#eef6f8"/>`;
    houses.push([cx + 8, cy - 44]);
    return s;
  };
  b.push(house(980, 930, 150, 92, 64, '#6a3b2a'));
  b.push(house(1170, 905, 110, 76, 52, '#7a4a30'));
  b.push(house(1310, 942, 170, 104, 70, '#5c3424'));

  // the snowy ground in front, with drifts and warm light pools
  const ground = L.ridgeFn(r, 980, 26, [[.0018, 1], [.007, .3]]);
  b.push(`<path d="${L.ridgePath(ground, W, H)}" fill="${g('gr', [[0, '#c4d8de'], [.25, '#8eaab4'], [1, '#34505c']])}"/>`);
  b.push(L.glow(defs, 1220, 990, 360, '#ffb35c', .2));
  b.push(L.glow(defs, 1760, 1085, 520, '#ffcf7a', .22));
  for (let i = 0; i < 9; i++) {
    const x = r(0, W), y = ground(x) + r(30, 160);
    b.push(`<path d="M${f(x - r(120, 260))} ${f(y)}Q${f(x)} ${f(y - r(14, 30))} ${f(x + r(120, 260))} ${f(y)}" stroke="#ffffff" stroke-opacity=".5" stroke-width="3" fill="none" ${blur(2)}/>`);
  }
  for (let i = 0; i < 260; i++) { const x = r(0, W), y = r(1000, H); b.push(`<circle cx="${f(x)}" cy="${f(y)}" r="${f(r(.6, 1.6))}" fill="#ffffff" opacity="${f(r(.4, 1))}"/>`); }
  // a lamp post on the left
  b.push(`<rect x="300" y="760" width="10" height="290" fill="#1d2a2c"/><rect x="290" y="1040" width="30" height="14" rx="3" fill="#1d2a2c"/>`);
  b.push(`<path d="M284 760h42l-8 -40h-26z" fill="#1d2a2c"/><path d="M290 758h30l-6 -32h-18z" fill="#ffd98a"/>`);
  b.push(L.glow(defs, 305, 742, 120, '#ffd27a', .55));
  b.push(L.glow(defs, 305, 1050, 160, '#ffd27a', .28));

  // ---- the tree --------------------------------------------------------------------------------------------
  const cx = 1760, apex = 250, base = 1040, TH = base - apex;
  const halfAt = (y) => { const t = (y - apex) / TH; return 360 * Math.pow(Math.max(0, t), .92) + 8; };
  b.push(`<ellipse cx="${cx}" cy="${base + 18}" rx="400" ry="34" fill="#4c6872" opacity=".55" ${blur(10)}/>`);
  b.push(`<rect x="${cx - 26}" y="${base - 40}" width="52" height="70" fill="${g('tr', [[0, '#2a1810'], [.5, '#4a2e1c'], [1, '#22140c']], 0, 0, 1, 0)}"/>`);
  // the dark mass under the needles
  let mass = `M${cx} ${apex - 6}`;
  const tiers = 9, edgeL = [], edgeR = [];
  for (let i = 1; i <= tiers; i++) {
    const y = apex + TH * i / tiers * .96, hw = halfAt(y);
    edgeR.push([cx + hw * .62, y - TH / tiers * .55], [cx + hw * 1.04, y + 10]);
    edgeL.push([cx - hw * .62, y - TH / tiers * .55], [cx - hw * 1.04, y + 12]);
  }
  edgeR.forEach(([x, y]) => { mass += `L${f(x)} ${f(y)}`; });
  for (let i = edgeL.length - 1; i >= 0; i--) mass += `L${f(edgeL[i][0])} ${f(edgeL[i][1])}`;
  b.push(`<path d="${mass}Z" fill="#06180f"/>`);

  // needles: clusters along drooping branches, shaded by height, depth and side (moonlight from the left, warm from the bulbs)
  const tones = ['#071d12', '#0b2a19', '#0f3a21', '#14492a', '#1d5c35', '#2a7444', '#3f8c58', '#7fb49a'];
  const bins = tones.map(() => []);
  const snowBits = [];
  const branchTiers = 26;
  for (let ti = 0; ti < branchTiers; ti++) {
    const ty = apex + 20 + (TH - 60) * ti / (branchTiers - 1);
    const hw = halfAt(ty);
    const nBranch = Math.max(3, Math.round(hw / 26));
    for (let bi = 0; bi < nBranch; bi++) {
      const side = bi % 2 === 0 ? 1 : -1;
      const reach = hw * r(.55, 1.05);
      const startX = cx + side * r(0, hw * .2), startY = ty + r(-8, 8);
      const front = r() < .55;           // branches pointing at us are lighter
      const segs = Math.round(reach / 10);
      for (let s = 0; s <= segs; s++) {
        const u = s / segs;
        const bx = startX + side * reach * u;
        const by = startY + Math.pow(u, 1.7) * reach * .32 + r(-3, 3);
        const lightSide = side < 0 ? .9 : .35;
        const depth = front ? .9 : .45;
        const nn = 9;
        for (let k = 0; k < nn; k++) {
          const a = (side > 0 ? 0 : Math.PI) + r(-1.25, 1.25) + .35 * side * 0 + (r() < .5 ? .5 : -.25) * (u);
          const len = r(10, 24) * (1 - u * .35);
          const x2 = bx + Math.cos(a) * len, y2 = by + Math.abs(Math.sin(a)) * len * .55 + r(2, 9);
          let tone = .18 + depth * .35 + lightSide * .25 * u + r(-.18, .18) - (ti / branchTiers) * .08;
          if (k === 0 && r() < .08) tone += .35;
          const tb = Math.max(0, Math.min(tones.length - 2, Math.round(tone * (tones.length - 2))));
          bins[tb].push(`M${f(bx)} ${f(by)}L${f(x2)} ${f(y2)}`);
        }
        if (front && u > .25 && r() < .16) snowBits.push([bx + r(-6, 6), by - 4, r(6, 16)]);
      }
    }
  }
  bins.forEach((paths, i) => { if (paths.length) b.push(`<path d="${paths.join('')}" stroke="${tones[i]}" stroke-width="${i > 4 ? 1.6 : 2.2}" stroke-linecap="round" fill="none"/>`); });
  // snow resting on the branches
  for (const [x, y, s] of snowBits) b.push(`<path d="M${f(x - s)} ${f(y + 3)}Q${f(x)} ${f(y - s * .45)} ${f(x + s)} ${f(y + 3)}Z" fill="#eef7f8" opacity="${f(r(.7, .95))}"/>`);
  // moonlight rim on the left edge and a warm core
  b.push(`<path d="${mass}Z" fill="${g('tl', [[0, '#9fe0d0', .16], [.35, '#9fe0d0', 0], [.7, '#ffb35c', 0], [1, '#ffb35c', .06]], 0, 0, 1, 0)}"/>`);
  b.push(L.glow(defs, cx, 720, 380, '#ffcf7a', .12));

  // gold bead garland swags and the light string along them
  const bulbs = [];
  const bulbCols = ['#ff4d4d', '#ffd25e', '#4dd68c', '#5ea8ff', '#ff8ad0'];
  for (let sI = 0; sI < 5; sI++) {
    const y0 = apex + 120 + sI * 135, y1 = y0 + 70;
    const x0 = cx - halfAt(y0) * .86, x1 = cx + halfAt(y1) * .9;
    const sag = 48 + sI * 6;
    const P = (u) => { const x = x0 + (x1 - x0) * u, y = y0 + (y1 - y0) * u + Math.sin(Math.PI * u) * sag; return [x, y]; };
    let bead = '', wire = `M${f(P(0)[0])} ${f(P(0)[1])}`;
    for (let k = 1; k <= 60; k++) { const [x, y] = P(k / 60); wire += `L${f(x)} ${f(y)}`; }
    b.push(`<path d="${wire}" stroke="#0a1a10" stroke-width="2" fill="none" opacity=".8"/>`);
    // a second, gold swag a little lower
    let gw = '';
    for (let k = 0; k <= 46; k++) { const u = k / 46, [x, y] = P(u); const yy = y + 30 + Math.sin(Math.PI * u) * 10; gw += `<circle cx="${f(x)}" cy="${f(yy)}" r="3.3" fill="url(#bead)"/>`; }
    bead += gw;
    b.push(bead);
    const nb = Math.round((x1 - x0) / 46);
    for (let k = 0; k < nb; k++) {
      const [x, y] = P((k + .5) / nb);
      const c = bulbCols[(k + sI * 2) % bulbCols.length];
      b.push(`<path d="M${f(x)} ${f(y)}l0 6" stroke="#1d2a20" stroke-width="3"/>`);
      b.push(`<ellipse cx="${f(x)}" cy="${f(y + 13)}" rx="5.5" ry="8" fill="${c}" opacity=".9"/><ellipse cx="${f(x - 1.8)}" cy="${f(y + 10)}" rx="1.6" ry="2.6" fill="#fff" opacity=".7"/>`);
      bulbs.push({ x, y: y + 13, c });
    }
  }
  defs.push(L.rad('bead', [[0, '#fff3c0'], [.45, '#e5b64a'], [1, '#7a5214']], .35, .35, .7));

  // baubles with reflections
  const baubleCols = [['#ff6b6b', '#b3121f', '#4a0508'], ['#ffe6a0', '#d9a031', '#5a3a08'], ['#e9f1f6', '#9aaab6', '#2e3a44'], ['#8fc4ff', '#2a5fbf', '#0b1f4a']];
  for (const [i, c] of baubleCols.entries()) defs.push(L.rad('bb' + i, [[0, c[0]], [.55, c[1]], [1, c[2]]], .36, .32, .72));
  const placed = [];
  for (let k = 0; k < 120 && placed.length < 34; k++) {
    const y = r(apex + 110, base - 70), hw = halfAt(y) * .8, x = cx + r(-hw, hw);
    if (placed.some(([px, py]) => Math.hypot(px - x, py - y) < 62)) continue;
    placed.push([x, y]);
    const rr = r(11, 17) * (.75 + (y - apex) / TH * .4), ci = r.int(0, 3);
    b.push(`<path d="M${f(x)} ${f(y - rr - 10)}v6" stroke="#c9a24a" stroke-width="1.2"/><rect x="${f(x - 4)}" y="${f(y - rr - 5)}" width="8" height="6" rx="1.5" fill="#d8b45a"/>`);
    b.push(`<circle cx="${f(x)}" cy="${f(y)}" r="${f(rr)}" fill="url(#bb${ci})"/>`);
    b.push(`<ellipse cx="${f(x - rr * .35)}" cy="${f(y - rr * .4)}" rx="${f(rr * .28)}" ry="${f(rr * .18)}" fill="#fff" opacity=".75" transform="rotate(-30 ${f(x - rr * .35)} ${f(y - rr * .4)})"/>`);
    b.push(`<path d="M${f(x - rr * .92)} ${f(y + rr * .1)}Q${f(x)} ${f(y + rr * .45)} ${f(x + rr * .92)} ${f(y + rr * .1)}" stroke="#fff" stroke-opacity=".22" stroke-width="1.4" fill="none"/>`);
  }

  // the star on top
  const star = (cx0, cy0, R0, r0) => { let d = ''; for (let k = 0; k < 10; k++) { const a = -Math.PI / 2 + k * Math.PI / 5, rr = k % 2 ? r0 : R0; d += (k ? 'L' : 'M') + f(cx0 + Math.cos(a) * rr) + ' ' + f(cy0 + Math.sin(a) * rr); } return d + 'Z'; };
  b.push(L.glow(defs, cx, apex - 30, 150, '#ffe08a', .55));
  b.push(`<path d="${star(cx, apex - 30, 46, 19)}" fill="${g('st', [[0, '#fff6c8'], [.5, '#ffd25e'], [1, '#c98a14']], 0, 0, 1, 1)}" stroke="#8a5a0a" stroke-width="1.5"/>`);
  b.push(`<path d="${star(cx - 3, apex - 33, 22, 9)}" fill="#fffbe6" opacity=".55"/>`);

  // presents under the tree
  const gift = (x, y, w, h, col, rib) => {
    let s = `<rect x="${x}" y="${y - h}" width="${w}" height="${h}" fill="${col}"/>`;
    s += `<rect x="${x}" y="${y - h}" width="${w}" height="${h}" fill="${g('gf', [[0, '#fff', .18], [.4, '#fff', 0], [1, '#000', .35]], 0, 0, 1, 0)}"/>`;
    s += `<rect x="${x - 5}" y="${y - h - 12}" width="${w + 10}" height="16" fill="${col}"/><rect x="${x - 5}" y="${y - h - 12}" width="${w + 10}" height="16" fill="#000" opacity=".15"/>`;
    s += `<rect x="${x + w / 2 - 6}" y="${y - h - 12}" width="12" height="${h + 12}" fill="${rib}"/>`;
    s += `<path d="M${x + w / 2} ${y - h - 12}c-26 -26 -40 2 0 0c26 -26 40 2 0 0" stroke="${rib}" stroke-width="7" fill="none"/>`;
    s += `<path d="M${x - 5} ${y - h - 12}q${(w + 10) / 2} -10 ${w + 10} 0" stroke="#fff" stroke-width="5" opacity=".8" fill="none"/>`;
    return s;
  };
  b.push(gift(1560, 1072, 96, 74, '#b3121f', '#ffd25e'));
  b.push(gift(1672, 1080, 70, 56, '#1f6b3c', '#f4f0e6'));
  b.push(gift(1880, 1076, 110, 64, '#2a5fbf', '#ff6b6b'));
  b.push(gift(2004, 1084, 64, 48, '#e8c15a', '#b3121f'));

  // a few snowflakes painted in the air (the falling ones are sprites)
  b.push(`<g ${blur(1.2)}>` + Array.from({ length: 140 }, () => `<circle cx="${f(r(0, W))}" cy="${f(r(0, H))}" r="${f(r(1, 3))}" fill="#fff" opacity="${f(r(.25, .7))}"/>`).join('') + '</g>');
  // vignette and grain
  b.push(`<rect width="${W}" height="${H}" fill="${rg('vg', [[0, '#000', 0], [.7, '#000', .05], [1, '#000', .55]], .5, .5, .75)}"/>`);
  const gr = id('gr'); defs.push(L.grainF(gr, .05));
  const plate = L.svg(W, H, defs.join(''), `<g filter="url(#${gr})">${b.join('')}</g>`);

  // sprites
  const flake = (sz, soft) => ({ w: sz, h: sz, svg: L.svg(sz, sz, L.rad('f', [[0, '#ffffff', 1], [soft ? .3 : .55, '#ffffff', soft ? .55 : .9], [1, '#ffffff', 0]]), `<circle cx="${sz / 2}" cy="${sz / 2}" r="${sz / 2}" fill="url(#f)"/>`) });
  const bulbGlow = (c) => ({ w: 64, h: 64, svg: L.svg(64, 64, L.rad('b', [[0, '#ffffff', 1], [.12, c, 1], [.35, c, .45], [1, c, 0]]), `<circle cx="32" cy="32" r="32" fill="url(#b)"/>`) });
  const sprites = { flake: flake(16, false), bokeh: flake(40, true), starglow: { w: 260, h: 260, svg: L.svg(260, 260, L.rad('s', [[0, '#fffbe6', .95], [.2, '#ffe08a', .6], [.55, '#ffcf6a', .15], [1, '#ffcf6a', 0]]), `<circle cx="130" cy="130" r="130" fill="url(#s)"/><path d="M130 10V250M10 130H250" stroke="#fff6d0" stroke-width="2" opacity=".55"/>`) } };
  sprites.smoke = { w: 120, h: 120, svg: L.svg(120, 120, L.rad('m', [[0, '#c8d6d8', .55], [.6, '#c8d6d8', .2], [1, '#c8d6d8', 0]]), `<circle cx="60" cy="60" r="60" fill="url(#m)"/>`) };
  bulbCols.forEach((c, i) => { sprites['bulb' + i] = bulbGlow(c); });

  const layers = [
    { sprite: 'smoke', at: houses.flatMap((q) => [q, q, q]), size: [40, 60], op: [.5, .7], move: { dx: [30, 70], dy: [-220, -170], dur: [7, 10] }, fade: [[0, 0], [.15, .8], [1, 0]], grow: [.5, 2.2] },
    { sprite: 'flake', n: 70, x: [0, 1], y: [-.05, 0], size: [4, 8], op: [.5, .9], move: { dx: [-40, 40], dy: [1240, 1260], dur: [16, 26] }, sway: { x: [14, 34], dur: [3.5, 6] } },
  ];
  bulbCols.forEach((c, ci) => {
    const at = bulbs.filter((q) => q.c === c).map((q) => [Math.round(q.x), Math.round(q.y)]);
    layers.push({ sprite: 'bulb' + ci, at, size: [46, 56], op: 1, twinkle: { lo: .15, hi: 1, dur: 1.4 }, phase: ci / bulbCols.length, phaseStep: .37 });
  });
  layers.push({ sprite: 'starglow', at: [[cx, apex - 30]], size: 260, pulse: { lo: .85, hi: 1.12, dur: 3 }, twinkle: { lo: .6, hi: 1, dur: 2.2 } });
  layers.push({ sprite: 'bokeh', n: 14, x: [0, 1], y: [-.05, 0], size: [22, 40], op: [.25, .5], move: { dx: [-60, 60], dy: [1260, 1300], dur: [9, 14] }, sway: { x: [20, 40], dur: [4, 7] } });
  return { key: 'christmas', scene: 'Christmas', W, H, ax: .5, ay: 1, plate, sprites, layers };
}
build.key = 'christmas';
module.exports = build;
