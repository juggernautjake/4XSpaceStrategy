// ============================================================================================
// COMMAND ICONS — the symbols on every fleet control
//
//   node tools/make-command-icons.mjs
//
// The fleet command bar was twenty text buttons reading "Line\nabreast" and "Withdraw\nif hurt" at
// eight point. That is legible exactly once — the first time, when the player reads all twenty — and
// never afterwards, because a wall of small words is something you parse rather than something you
// recognise. A commander picking a formation mid-engagement is not reading.
//
// ---- THE FORMATION ICONS SHOW THE FORMATION ---------------------------------------------------
//
// This is the part worth doing properly. A formation icon could be an abstract mark, and then the
// player has to learn which mark means which shape — a second thing to memorise on top of the six
// shapes themselves. Instead each one is drawn as the SHIP POSITIONS: five or six little hulls
// arranged exactly as that formation arranges them, pointing the way it points them.
//
// So the Line Abreast icon is a row of hulls side by side, Line Astern is a column, Echelon is a
// diagonal stair, Screen is small hulls in an arc in front of big ones, Globe is a ring around a
// centre. Nothing has to be learned: the icon is a diagram of the answer.
//
// ---- WHY THEY ARE WHITE MASKS -----------------------------------------------------------------
//
// Every one of these is drawn in flat white on transparent, and the UI tints it. A control has at
// least three states — available, active, unavailable — and the icons also appear in tooltips, on the
// roster and in the info panel, each wanting a different weight. Pre-rendering each icon in each
// colour is twenty-five icons times five states; tinting is twenty-five files and one multiply.
//
// It also means the whole set follows the theme. Change UITheme.Accent and every active control
// changes with it, rather than twenty-five PNGs quietly staying the old blue.
//
// ---- AND WHY 64x64 ----------------------------------------------------------------------------
//
// Drawn at 64 and displayed at 22-30, so there is real supersampling on every diagonal — a formation
// diagram is mostly diagonals and thin strokes, and at 1:1 they alias into mush. 64 is also small
// enough that all twenty-five together are a few tens of kilobytes.
// ============================================================================================

import sharp from 'sharp';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const OUT = path.join(PROJ, 'Assets', 'Resources', 'SpaceAssets', 'CommandIcons');
const S = 64;

// ---- drawing helpers ---------------------------------------------------------------------------
//
// One hull, drawn as a chevron rather than a solid triangle. A solid triangle at this size reads as a
// blob; a chevron keeps a hole in the middle and so still reads as a POINTED thing at 22 pixels,
// which is the whole job — the reader has to be able to tell which way the formation faces.
const hull = (x, y, s = 1, rot = 0) =>
  `<path d="M0,-8 L7.5,7 L0,3 L-7.5,7 Z" fill="#fff"
     transform="translate(${x},${y}) rotate(${rot}) scale(${s})"/>`;

/// A bigger hull, for the valuable ship a formation is built around.
const capital = (x, y, s = 1, rot = 0) =>
  `<path d="M0,-12 L10,10 L0,4.5 L-10,10 Z" fill="#fff"
     transform="translate(${x},${y}) rotate(${rot}) scale(${s})"/>`;

const line = (x1, y1, x2, y2, w = 3) =>
  `<line x1="${x1}" y1="${y1}" x2="${x2}" y2="${y2}" stroke="#fff" stroke-width="${w}"
     stroke-linecap="round"/>`;

const circle = (x, y, r, w = 0) => w
  ? `<circle cx="${x}" cy="${y}" r="${r}" fill="none" stroke="#fff" stroke-width="${w}"/>`
  : `<circle cx="${x}" cy="${y}" r="${r}" fill="#fff"/>`;

const arc = (x, y, r, from, to, w = 3) => {
  const a = (d) => [x + r * Math.cos(d * Math.PI / 180), y + r * Math.sin(d * Math.PI / 180)];
  const [x1, y1] = a(from), [x2, y2] = a(to);
  const large = Math.abs(to - from) > 180 ? 1 : 0;
  return `<path d="M${x1},${y1} A${r},${r} 0 ${large} 1 ${x2},${y2}" fill="none"
            stroke="#fff" stroke-width="${w}" stroke-linecap="round"/>`;
};

/// An arrowhead at (x,y) pointing along `rot` degrees (0 = up).
const arrow = (x, y, s = 1, rot = 0) =>
  `<path d="M0,-7 L6,4 L0,0.5 L-6,4 Z" fill="#fff"
     transform="translate(${x},${y}) rotate(${rot}) scale(${s})"/>`;

const rect = (x, y, w, h) => `<rect x="${x}" y="${y}" width="${w}" height="${h}" fill="#fff"/>`;

// ============================================================================================
// THE SET
// ============================================================================================
const ICONS = {

  // ---- FORMATIONS: each icon IS the arrangement -----------------------------------------------

  // ---- FEWER HULLS, DRAWN BIGGER ----
  //
  // The first pass used five or six small hulls per formation, which is a more faithful diagram and an
  // unreadable icon: the contact sheet renders every icon at 24 pixels as well as at 72, and at 24 the
  // small marks collapsed into a scatter of dots. Three or four large hulls say the same thing —
  // abreast, astern, stair, wedge — and survive the size the button actually uses.

  // Leader at the point, the rest sweeping back behind it.
  Form_Wedge: `
    ${hull(32, 13, 1.5)}
    ${hull(15, 36, 1.5)} ${hull(49, 36, 1.5)}
    ${hull(32, 55, 1.2)}`,

  // One rank, every hull bearing forward. The bar is the frontage they present.
  Form_LineAbreast: `
    ${hull(11, 30, 1.35)} ${hull(32, 30, 1.35)} ${hull(53, 30, 1.35)}
    ${line(5, 47, 59, 47, 3)}`,

  // Single file. The bar is beside them, because the frontage is the narrow edge.
  Form_LineAstern: `
    ${hull(32, 12, 1.4)} ${hull(32, 33, 1.4)} ${hull(32, 54, 1.4)}
    ${line(50, 6, 50, 60, 3)}`,

  // A diagonal stair to starboard.
  Form_Echelon: `
    ${hull(13, 14, 1.35)} ${hull(32, 33, 1.35)} ${hull(51, 52, 1.35)}`,

  // Cheap hulls in an arc in FRONT; the expensive one behind them.
  Form_Screen: `
    ${hull(12, 20, 1.1)} ${hull(32, 12, 1.1)} ${hull(52, 20, 1.1)}
    ${arc(32, 34, 25, 198, 342, 3)}
    ${capital(32, 50, 1.1)}`,

  // A shell all the way round.
  Form_Globe: `
    ${circle(32, 32, 23, 3)}
    ${capital(32, 34, 0.95)}
    ${hull(32, 7, 0.85)} ${hull(57, 32, 0.85, 90)} ${hull(32, 57, 0.85, 180)} ${hull(7, 32, 0.85, 270)}`,

  // No formation: every hull flies its own line.
  Form_Free: `
    ${hull(15, 17, 1.25, -35)} ${hull(48, 15, 1.25, 30)}
    ${hull(17, 49, 1.25, 20)}  ${hull(50, 47, 1.25, -40)}`,

  // ---- PROTOCOLS: what the squadron will DO ---------------------------------------------------

  // A shield: hold what you have.
  Prot_Defensive: `
    <path d="M32,7 L54,16 C54,38 45,52 32,58 C19,52 10,38 10,16 Z"
      fill="none" stroke="#fff" stroke-width="4"/>
    ${line(32, 22, 32, 44, 3)}`,

  // Closing on something: a hull driving into a target ring.
  Prot_Aggressive: `
    ${circle(42, 22, 11, 3)}
    ${line(35, 29, 49, 15, 3)}
    ${hull(20, 44, 1.15, 45)}
    ${line(9, 55, 17, 47, 3)}`,

  // A muzzle with a bar through it.
  Prot_HoldFire: `
    ${circle(32, 32, 20, 4)}
    ${line(18, 46, 46, 18, 4)}
    ${hull(32, 32, 0.9)}`,

  // Break away, and a signal going out.
  Prot_EvadeAndReport: `
    ${hull(22, 42, 1.0, -40)}
    <path d="M14,52 C22,44 30,40 44,38" fill="none" stroke="#fff" stroke-width="3"
      stroke-dasharray="5 4" stroke-linecap="round"/>
    ${arc(46, 20, 8, -70, 70, 3)}
    ${arc(46, 20, 14, -70, 70, 3)}
    ${circle(46, 20, 3)}`,

  // Two escorts flanking what they are covering, tethered to it.
  //
  // The arc came off. With one, this was very nearly the Screen icon — arc plus capital plus small
  // hulls — and two controls that mean different things must not share a silhouette. Screen keeps the
  // arc because the arc IS the screen; Escort gets tethers, because escorting is a relationship
  // between hulls rather than a shape they stand in.
  // A small hull holding station on a big one, tethered to it.
  //
  // The flanking-pair version merged into one blob at 24 pixels: three white marks with two-pixel gaps
  // become one white mark the moment the image is scaled down. Two objects with a wide gap and a
  // dashed link between them survives, and says the same thing more directly — escorting is a
  // RELATIONSHIP between two hulls, not a shape three of them stand in.
  Prot_Escort: `
    ${capital(44, 34, 1.35)}
    ${hull(14, 34, 1.25)}
    <path d="M24,34 L32,34" stroke="#fff" stroke-width="3" stroke-dasharray="4 3" stroke-linecap="round"/>`,

  // A hull peeling away, over a hull bar most of the way gone.
  Prot_WithdrawIfHurt: `
    ${hull(30, 24, 1.5, 225)}
    <rect x="8" y="46" width="48" height="11" fill="none" stroke="#fff" stroke-width="3"/>
    ${rect(11, 49, 14, 5)}`,

  // ---- BATTLE ORDERS --------------------------------------------------------------------------

  // Everything converging on one point.
  Order_FocusFire: `
    ${circle(32, 32, 9, 3)}
    ${circle(32, 32, 2.5)}
    ${line(32, 4, 32, 18, 3)}   ${line(32, 46, 32, 60, 3)}
    ${line(4, 32, 18, 32, 3)}   ${line(46, 32, 60, 32, 3)}
    ${line(11, 11, 20, 20, 3)}  ${line(53, 11, 44, 20, 3)}`,

  // Fire spread across whatever presents itself.
  Order_EngageAtWill: `
    ${hull(32, 40, 1.15)}
    ${line(32, 30, 18, 12, 3)} ${line(32, 30, 32, 8, 3)} ${line(32, 30, 46, 12, 3)}
    ${circle(18, 9, 3.5)} ${circle(32, 6, 3.5)} ${circle(46, 9, 3.5)}`,

  // Brackets clamped round a hull: go nowhere.
  Order_HoldPosition: `
    ${hull(32, 34, 1.05)}
    <path d="M14,12 L8,12 L8,52 L14,52" fill="none" stroke="#fff" stroke-width="4"/>
    <path d="M50,12 L56,12 L56,52 L50,52" fill="none" stroke="#fff" stroke-width="4"/>`,

  // Break contact: a hull leaving through the line it was holding.
  //
  // This was a house with an arrow at it, which read as neither a house nor an arrow at 24 pixels. An
  // eject mark is unambiguous at any size and does not need the reader to recognise a building.
  Order_Withdraw: `
    ${rect(48, 8, 7, 48)}
    ${hull(22, 32, 1.6, 270)}
    ${line(32, 32, 44, 32, 4)}`,

  // A route between points.
  Order_Patrol: `
    ${circle(12, 18, 5, 3)} ${circle(52, 16, 5, 3)} ${circle(34, 50, 5, 3)}
    ${line(16, 20, 48, 18, 3)}
    ${line(50, 21, 37, 45, 3)}
    ${line(31, 47, 15, 23, 3)}`,

  // A flag: fall back to here.
  Order_Rally: `
    ${line(18, 8, 18, 58, 4)}
    <path d="M20,10 L50,18 L20,28 Z" fill="#fff"/>
    ${circle(18, 58, 5)}`,

  // ---- STRUCTURE: what is being commanded -----------------------------------------------------

  // One, three, six — the scale reads off the count without a number on it.
  Unit_Ship: `${hull(32, 34, 3.2)}`,

  Unit_Squadron: `
    ${hull(32, 17, 1.6)}
    ${hull(15, 45, 1.6)} ${hull(49, 45, 1.6)}`,

  Unit_Fleet: `
    ${hull(32, 11, 1.25)}
    ${hull(16, 34, 1.25)} ${hull(48, 34, 1.25)}
    ${hull(8, 56, 1.1)}  ${hull(32, 56, 1.1)} ${hull(56, 56, 1.1)}`,

  // Brackets pulling a group together.
  Act_Form: `
    ${hull(23, 34, 1.4)} ${hull(41, 34, 1.4)}
    <path d="M12,14 L6,14 L6,50 L12,50" fill="none" stroke="#fff" stroke-width="4"/>
    <path d="M52,14 L58,14 L58,50 L52,50" fill="none" stroke="#fff" stroke-width="4"/>`,

  // One hull leaving the rest.
  Act_Detach: `
    ${hull(18, 24, 1.35)} ${hull(18, 46, 1.35)}
    ${hull(50, 18, 1.35, 40)}
    <path d="M30,34 C38,31 43,27 46,23" fill="none" stroke="#fff" stroke-width="3"
      stroke-dasharray="5 4" stroke-linecap="round"/>`,

  // The group coming apart.
  Act_Disband: `
    ${hull(13, 15, 1.25, -45)} ${hull(51, 15, 1.25, 45)}
    ${hull(13, 49, 1.25, -135)} ${hull(51, 49, 1.25, 135)}
    ${circle(32, 32, 5, 3)}`,

  // ---- NAMING, REINFORCING, REGROUPING, AND THE TWO PATROL SHAPES -----------------------------

  // A luggage tag. Deliberately NOT a text field with a caret: a rectangle with a bar in it is what
  // Order_Withdraw and Prot_WithdrawIfHurt already look like at 24 pixels, and three controls sharing
  // a silhouette is worse than one control being slightly abstract. A tag has an outline nothing else
  // in the set has.
  Act_Rename: `
    <path d="M10,19 L38,19 L56,32 L38,45 L10,45 Z" fill="none" stroke="#fff" stroke-width="4"
      stroke-linejoin="round"/>
    ${circle(21, 32, 4.5)}`,

  // A hull, and a plus. The plus is the most legible mark in the set at 24 pixels — it is two thick
  // strokes with a wide gap around them — and "one more of these" is exactly what the control does.
  // Kept clear of the hull horizontally rather than layered over it, since overlapping white marks
  // merge into one shape the moment the icon is scaled down.
  Act_Reinforce: `
    ${hull(18, 38, 1.7)}
    ${line(46, 15, 46, 39, 5)} ${line(34, 27, 58, 27, 5)}`,

  // Four arrowheads closing on an EMPTY centre.
  //
  // The first version put a dot in the middle and the arrowheads close around it, and at 24 pixels the
  // five marks merged into one four-pointed star — which is what Act_Disband already looks like at
  // that size. Two controls that mean opposite things must not share a silhouette.
  //
  // Two changes fix it, and both matter. The centre is left HOLLOW, so the eye reads a gap being
  // closed rather than a solid star. And the arms are ORTHOGONAL where Disband's are diagonal: a plus
  // and an X stay distinguishable long after the marks inside them have stopped being readable.
  Order_Regroup: `
    ${arrow(32, 9, 1.8, 180)} ${arrow(32, 55, 1.8, 0)}
    ${arrow(9, 32, 1.8, 90)}  ${arrow(55, 32, 1.8, 270)}`,

  // Round and round: a closed circuit with one head on it to say which way.
  Order_PatrolLoop: `
    ${circle(32, 34, 17, 4)}
    ${arrow(32, 17, 1.5, 90)}`,

  // Up and back: one line, a head at each end.
  Order_PatrolShuttle: `
    ${line(18, 32, 46, 32, 5)}
    ${arrow(11, 32, 1.6, 270)} ${arrow(53, 32, 1.6, 90)}`,
};

// ============================================================================================
fs.mkdirSync(OUT, { recursive: true });

const names = Object.keys(ICONS);
const report = [];

for (const name of names) {
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${S}" height="${S}" viewBox="0 0 ${S} ${S}">
${ICONS[name]}
</svg>`;
  const file = path.join(OUT, `Cmd_${name}.png`);
  await sharp(Buffer.from(svg)).png().toFile(file);

  // How much of the frame the mark covers. An icon under about 6% is a thin scratch that will
  // disappear at 22 pixels; one over about 45% is a solid blob with no silhouette left. Both are
  // failures worth catching here rather than in the game.
  const { data, info } = await sharp(Buffer.from(svg)).ensureAlpha().raw()
    .toBuffer({ resolveWithObject: true });
  let a = 0;
  for (let i = 0; i < data.length; i += info.channels) a += data[i + 3] / 255;
  report.push({ name, cover: (100 * a) / (S * S) });
}

// ---- contact sheet, at the size they are actually used -----------------------------------------
//
// Drawn TWICE: large enough to check the drawing, and at 24 pixels — the size the command bar shows
// them at — because an icon that reads beautifully at 96 and turns to porridge at 24 is a failed
// icon, and the only way to know is to look at 24.
const COLS = 7, CELL = 104, ROWS = Math.ceil(names.length / COLS);
const sheetW = COLS * CELL, sheetH = ROWS * (CELL + 34) + 40;
const tiles = [];
for (let i = 0; i < names.length; i++) {
  const f = path.join(OUT, `Cmd_${names[i]}.png`);
  const big = await sharp(f).resize(72, 72).toBuffer();
  const small = await sharp(f).resize(24, 24).toBuffer();
  const cx = (i % COLS) * CELL, cy = Math.floor(i / COLS) * (CELL + 34);
  tiles.push({ input: big, left: cx + 8, top: cy + 8 });
  tiles.push({ input: small, left: cx + 8, top: cy + 84 });
}

let labels = `<svg xmlns="http://www.w3.org/2000/svg" width="${sheetW}" height="${sheetH}">`;
names.forEach((n, i) => {
  const cx = (i % COLS) * CELL, cy = Math.floor(i / COLS) * (CELL + 34);
  labels += `<text x="${cx + 38}" y="${cy + 126}" fill="#9fb4c8" font-family="monospace" ` +
            `font-size="9" text-anchor="middle">${n}</text>`;
});
labels += `<text x="10" y="${sheetH - 12}" fill="#6b7d8e" font-family="monospace" font-size="11">` +
          `each icon at 72px and again at 24px — the size the command bar draws it. ` +
          `White masks; the UI tints them.</text></svg>`;

const sheet = path.join(PROJ, 'Art', '_review', 'command-icons.png');
fs.mkdirSync(path.dirname(sheet), { recursive: true });
await sharp({ create: { width: sheetW, height: sheetH, channels: 4,
                        background: { r: 16, g: 20, b: 26, alpha: 1 } } })
  .composite([...tiles, { input: Buffer.from(labels), top: 0, left: 0 }])
  .png().toFile(sheet);

// ---- report ------------------------------------------------------------------------------------
report.sort((a, b) => a.cover - b.cover);
console.log(`${names.length} icons -> ${path.relative(PROJ, OUT)}\n`);
console.log('coverage (thin marks vanish at 24px, fat ones lose their silhouette)');
for (const r of report)
  console.log(`  ${r.name.padEnd(22)} ${r.cover.toFixed(1).padStart(5)}%` +
              (r.cover < 6 ? '   THIN' : r.cover > 45 ? '   HEAVY' : ''));

const bad = report.filter(r => r.cover < 6 || r.cover > 45);
console.log(`\ncontact sheet -> ${path.relative(PROJ, sheet)}`);
console.log(bad.length ? `\n${bad.length} icon(s) outside the readable band.`
                       : '\nEvery icon lands in the readable band.');
