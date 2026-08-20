// ============================================================================================
// A CONTACT SHEET OF EVERY MODEL
//
//   node tools/contact-sheet.mjs --dir Art/Active --out Art/_review/contact-sheet.png
//   node tools/contact-sheet.mjs --dir Art/Incoming --match thumbnail --cols 6
//
// Reviewing a hundred and forty ships one render at a time is slow and, worse, unreliable — by the
// thirtieth you have lost the standard you were judging the first against. Tiled together on one
// sheet the outliers are obvious at a glance: the one that came out white, the one that is a
// featureless blob, the one whose civ colour clearly does not match its siblings.
//
// Labels are drawn under each cell so a verdict can be written against a name rather than a position.
// Cells are sorted by path, which groups a civilization's ships together and puts a lineage in tier
// order — exactly the arrangement that makes a broken Mk II jump out beside its Mk I.
// ============================================================================================

import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import sharp from 'sharp';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const argv = process.argv.slice(2);
const arg = (n, d) => { const i = argv.indexOf(n); return i >= 0 ? argv[i + 1] : d; };

const DIR   = path.resolve(PROJ, arg('--dir', 'Art/Active'));
const OUT   = path.resolve(PROJ, arg('--out', 'Art/_review/contact-sheet.png'));
const MATCH = arg('--match', 'render|thumbnail');
const COLS  = parseInt(arg('--cols', '6'), 10);
const CELL  = parseInt(arg('--cell', '260'), 10);
const PAGE  = parseInt(arg('--page', '1'), 10);
const PER   = parseInt(arg('--per', '36'), 10);

const LABEL_H = 34;

function walk(dir, acc = []) {
  for (const f of fs.readdirSync(dir)) {
    const fp = path.join(dir, f);
    const st = fs.statSync(fp);
    if (st.isDirectory()) walk(fp, acc);
    else acc.push(fp);
  }
  return acc;
}

const re = new RegExp(MATCH, 'i');
const files = walk(DIR)
  .filter(f => /\.(png|jpg|jpeg|webp)$/i.test(f) && re.test(path.basename(f)))
  .sort();

if (!files.length) { console.error(`no images matching /${MATCH}/ under ${DIR}`); process.exit(1); }

const pages = Math.ceil(files.length / PER);
const slice = files.slice((PAGE - 1) * PER, PAGE * PER);
const rows = Math.ceil(slice.length / COLS);
const W = COLS * CELL;
const H = rows * (CELL + LABEL_H);

/// A readable name for a cell: the ship folder plus its civ, not the whole path.
function labelFor(f) {
  const rel = path.relative(DIR, f);
  const parts = rel.split(path.sep);
  const dir = parts.length > 1 ? parts[parts.length - 2] : '';
  // "2026-08-20_Aquarii_Carrier_01a01d27" -> "Aquarii_Carrier"
  const cleaned = dir.replace(/^\d{4}-\d{2}-\d{2}_/, '').replace(/_[0-9a-f]{8}$/i, '');
  return (cleaned || path.basename(f)).slice(0, 30);
}

const esc = s => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

const composites = [];
for (let i = 0; i < slice.length; i++) {
  const col = i % COLS, row = Math.floor(i / COLS);
  const x = col * CELL, y = row * (CELL + LABEL_H);

  // Flattened onto a mid grey: these renders are transparent PNGs, and on a white sheet a white ship
  // is invisible — which is the exact failure the sheet exists to catch.
  const img = await sharp(slice[i])
    .flatten({ background: '#3a3a3a' })
    .resize(CELL - 8, CELL - 8, { fit: 'contain', background: '#3a3a3a' })
    .png().toBuffer();
  composites.push({ input: img, left: x + 4, top: y + 4 });

  const svg = `<svg width="${CELL}" height="${LABEL_H}">
    <rect width="100%" height="100%" fill="#141414"/>
    <text x="${CELL / 2}" y="21" font-family="DejaVu Sans,Arial" font-size="13"
          fill="#e8e8e8" text-anchor="middle">${esc(labelFor(slice[i]))}</text>
  </svg>`;
  composites.push({ input: Buffer.from(svg), left: x, top: y + CELL });
}

fs.mkdirSync(path.dirname(OUT), { recursive: true });
await sharp({ create: { width: W, height: H, channels: 3, background: '#222222' } })
  .composite(composites).png().toFile(OUT);

console.log(`${slice.length} cells (page ${PAGE} of ${pages}, ${files.length} images total)`);
console.log(`-> ${path.relative(PROJ, OUT)}  ${W}x${H}`);
