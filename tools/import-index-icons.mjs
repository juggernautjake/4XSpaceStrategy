// ============================================================================================
// THE INDEX ICONS
//
//   node tools/import-index-icons.mjs [--src <folder>]
//
// Copies the supplied 16x16 index art into Resources, and draws the one that is missing.
//
// ---- WHY A TOOL AND NOT A DRAG-AND-DROP -------------------------------------------------------
//
// Two reasons. The first is that the art is named for what a player calls the thing — Minerals,
// Fertility, Weather — and the game is named for what the code calls it — Mineral, Fertile, Wind. A
// mapping that lives in a script is a mapping somebody can read; a mapping that lives in whatever
// somebody typed when they renamed a file is a mapping that breaks silently the next time a file
// arrives.
//
// The second is Water. Six indexes were supplied as five icons, and a bar of buttons with a hole in
// it is worse than a bar with one drawn icon. So Water is generated here, in the same 16x16
// palette-limited style as the rest — a droplet in the deep blue the Water ramp already ends on, so
// the button matches the highlight it switches on. Generated rather than hand-placed for the same
// reason the biome tiles are: it can be regenerated when the ramp changes, and nobody has to
// remember which shade of blue was used.
// ============================================================================================

import sharp from 'sharp';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const argv = process.argv.slice(2);
const arg = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };

const SRC = arg('--src', 'C:/Users/lando/Downloads/Index_icons_16x16/resource_icons_16x16');
const OUT = path.join(PROJ, 'Assets', 'Resources', 'SpaceAssets', 'IndexIcons');

// artwork name -> SurfaceIndexKind name. The game's enum is the authority; the art is named for the
// player-facing word, which is not always the same word.
const MAP = {
  Minerals: 'Mineral',
  Geothermal: 'Geothermal',
  Fertility: 'Fertile',
  Weather: 'Wind',
  Solar: 'Solar',
};

fs.mkdirSync(OUT, { recursive: true });

let copied = 0;
for (const [art, kind] of Object.entries(MAP)) {
  const from = path.join(SRC, `${art}_16x16.png`);
  if (!fs.existsSync(from)) { console.log(`  MISSING  ${art}_16x16.png`); continue; }
  fs.copyFileSync(from, path.join(OUT, `Index_${kind}.png`));
  console.log(`  ${art.padEnd(11)} -> Index_${kind}.png`);
  copied++;
}

// ---- Water, drawn to match --------------------------------------------------------------------
//
// Pixels rather than an SVG path, because everything else in the set is 16x16 pixel art with hard
// edges and a handful of colours, and a smoothly antialiased vector droplet dropped in among them
// would be the one icon that looked wrong. Two blues and a highlight, the same weight of outline the
// others carry.
{
  const N = 16;
  const px = Buffer.alloc(N * N * 4, 0);
  const put = (x, y, [r, g, b, a = 255]) => {
    if (x < 0 || y < 0 || x >= N || y >= N) return;
    const i = (y * N + x) * 4;
    px[i] = r; px[i + 1] = g; px[i + 2] = b; px[i + 3] = a;
  };

  // The Water ramp's own colours: a muted grey-blue for the shaded side, its fully saturated deep
  // blue for the body, and a near-white for the specular. Kept in step with SurfaceIndex.Ramp by
  // being the same numbers, which is the closest thing to a link a PNG can have.
  const DEEP = [0, 87, 255];
  const MID = [46, 120, 235];
  const DARK = [12, 44, 120];
  const LIT = [186, 220, 255];

  // A droplet: a point at the top, swelling to a round belly. Rows are half-widths, so the shape is
  // symmetric by construction and cannot come out lopsided by a pixel.
  const half = [0, 0, 1, 1, 2, 2, 3, 3, 4, 5, 5, 5, 5, 4, 2, 0];
  for (let y = 0; y < N; y++) {
    const hw = half[y];
    if (hw <= 0) continue;
    for (let dx = -hw; dx <= hw; dx++) {
      const x = 8 + dx;
      const edge = Math.abs(dx) === hw || half[y + 1] === 0;
      put(x, y, edge ? DARK : (dx < 0 ? MID : DEEP));
    }
  }
  // The highlight, upper-left of the belly — the same place the mineral icon puts its ore glint.
  put(6, 9, LIT); put(6, 10, LIT); put(7, 10, LIT);

  await sharp(px, { raw: { width: N, height: N, channels: 4 } })
    .png().toFile(path.join(OUT, 'Index_Water.png'));
  console.log('  Water       -> Index_Water.png  (drawn; none was supplied)');
  copied++;
}

// A sheet so the six can be judged together, which is the only way to tell whether the drawn one
// belongs with the five that were supplied.
const names = ['Mineral', 'Geothermal', 'Fertile', 'Wind', 'Solar', 'Water'];
const CELL = 104, tiles = [];
for (let i = 0; i < names.length; i++) {
  const f = path.join(OUT, `Index_${names[i]}.png`);
  if (!fs.existsSync(f)) continue;
  tiles.push({ input: await sharp(f).resize(96, 96, { kernel: 'nearest' }).toBuffer(),
               left: i * CELL + 4, top: 4 });
}
let labels = `<svg xmlns="http://www.w3.org/2000/svg" width="${names.length * CELL}" height="126">`;
names.forEach((n, i) => {
  labels += `<text x="${i * CELL + CELL / 2}" y="118" fill="#c9d5e1" font-family="monospace" ` +
            `font-size="12" text-anchor="middle">${n}</text>`;
});
labels += '</svg>';

const sheet = path.join(PROJ, 'Art', '_review', 'index-icons.png');
fs.mkdirSync(path.dirname(sheet), { recursive: true });
await sharp({ create: { width: names.length * CELL, height: 126, channels: 4,
                        background: { r: 18, g: 22, b: 28, alpha: 1 } } })
  .composite([...tiles, { input: Buffer.from(labels), top: 0, left: 0 }])
  .png().toFile(sheet);

console.log(`\n${copied} icons -> ${path.relative(PROJ, OUT)}`);
console.log(`contact sheet -> ${path.relative(PROJ, sheet)}`);
