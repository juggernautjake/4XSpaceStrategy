// ============================================================================================
// CIVILIZATION SYMBOLS — ten geometric marks, drawn as two-region masks
//
//   node tools/make-civ-symbols.mjs
//
// An empire's mark has to work at two sizes that are nothing alike: a few dozen pixels on a ship
// token in the system view, and a panel-sized crest in the UI. That rules out anything with fine
// detail, so every one of these is built from a handful of bold shapes with generous gaps — the
// silhouette carries it, and nothing depends on a line thinner than a twentieth of the frame.
//
// ---- WHY THEY ARE MASKS AND NOT PICTURES ---------------------------------------------------
//
// The player picks two colours as well as a symbol, and the symbol has to take them. So these are not
// coloured images; they are REGION MAPS:
//
//     red channel    this pixel belongs to the PRIMARY colour
//     green channel  this pixel belongs to the SECONDARY colour
//     alpha          how much of this pixel the symbol covers at all
//
// At runtime CivEmblem multiplies the two chosen colours by those channels and composites them, so
// one 256x256 file serves every colour pair anyone will ever choose. Anti-aliasing comes out of the
// renderer for free: a partly covered edge pixel gets a partial channel value and blends correctly.
//
// The alternative — pre-rendering every symbol in every colour pair — is 10 symbols x 12 x 12 colours
// = 1,440 textures to ship and keep in step. This is ten.
// ============================================================================================

import sharp from 'sharp';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const OUT = path.join(PROJ, 'Assets', 'Resources', 'SpaceAssets', 'Symbols');
const S = 256;                       // source size; the game downsamples as needed
const P = 'rgb(255,0,0)';            // primary region
const Q = 'rgb(0,255,0)';            // secondary region

// Every symbol is a body in the primary colour with a secondary detail cut through or laid over it.
// The pairing matters: a mark whose two colours are one big shape and one tiny fleck reads as a
// single-colour mark with a smudge, so each of these gives the secondary a real share of the area.
const SYMBOLS = {
  // A downward chevron with a second chevron nested inside it.
  Chevron: `
    <polygon points="128,232 20,72 66,44 128,164 190,44 236,72" fill="${P}"/>
    <polygon points="128,186 62,88 84,74 128,142 172,74 194,88" fill="${Q}"/>`,

  // A five-pointed star around a solid pentagon core.
  Star: `
    <polygon points="128,12 160,84 238,84 176,132 200,214 128,166 56,214 80,132 18,84 96,84" fill="${P}"/>
    <polygon points="128,72 176,100 176,156 128,184 80,156 80,100" fill="${Q}"/>`,

  // A triangle with an inverted triangle cut into it.
  Delta: `
    <polygon points="128,20 240,220 16,220" fill="${P}"/>
    <polygon points="128,196 74,104 182,104" fill="${Q}"/>`,

  // A world and its orbit.
  Orbit: `
    <circle cx="128" cy="128" r="58" fill="${P}"/>
    <ellipse cx="128" cy="128" rx="116" ry="46" fill="none" stroke="${Q}" stroke-width="20"
             transform="rotate(-24 128 128)"/>`,

  // A bold cross with a diamond at the crossing.
  Cross: `
    <rect x="100" y="16" width="56" height="224" fill="${P}"/>
    <rect x="16" y="100" width="224" height="56" fill="${P}"/>
    <polygon points="128,72 184,128 128,184 72,128" fill="${Q}"/>`,

  // Three raking claws, deliberately held APART. The first attempt curved all three back to a single
  // point at the top, where they overlapped into one solid leaf and the mark stopped being claws at
  // all — at token size a shape is only what its gaps say it is.
  Talon: `
    <path d="M28,236 C28,140 44,72 84,24 C74,104 68,168 74,236 Z" fill="${P}"/>
    <path d="M228,236 C228,140 212,72 172,24 C182,104 188,168 182,236 Z" fill="${P}"/>
    <path d="M128,240 C104,176 104,108 128,40 C152,108 152,176 128,240 Z" fill="${Q}"/>`,

  // A lens with a pupil.
  Eye: `
    <path d="M12,128 C60,52 196,52 244,128 C196,204 60,204 12,128 Z" fill="${P}"/>
    <circle cx="128" cy="128" r="46" fill="${Q}"/>`,

  // An anvil: a heavy trapezoid on a bar.
  Anvil: `
    <polygon points="36,60 220,60 186,144 70,144" fill="${P}"/>
    <rect x="100" y="144" width="56" height="52" fill="${P}"/>
    <rect x="52" y="196" width="152" height="40" fill="${Q}"/>`,

  // A sun with radiating spokes.
  Sunburst: `
    <g fill="${Q}">
      ${Array.from({ length: 12 }, (_, i) =>
        `<rect x="120" y="6" width="16" height="60" transform="rotate(${i * 30} 128 128)"/>`).join('\n      ')}
    </g>
    <circle cx="128" cy="128" r="72" fill="${P}"/>`,

  // A shield with a bend across it.
  Shield: `
    <path d="M128,14 L232,52 C232,150 190,214 128,242 C66,214 24,150 24,52 Z" fill="${P}"/>
    <path d="M52,74 L204,74 L204,124 L52,124 Z" fill="${Q}"/>`,
};

fs.mkdirSync(OUT, { recursive: true });

const names = Object.keys(SYMBOLS);
for (const name of names) {
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${S}" height="${S}" viewBox="0 0 ${S} ${S}">
${SYMBOLS[name]}
</svg>`;

  const file = path.join(OUT, `Symbol_${name}.png`);
  await sharp(Buffer.from(svg)).png().toFile(file);

  // Report the split, because a symbol whose secondary is a rounding error is a one-colour symbol and
  // the whole point of two colours is that both of them show.
  const { data, info } = await sharp(Buffer.from(svg)).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
  let r = 0, g = 0, a = 0;
  for (let i = 0; i < data.length; i += info.channels) {
    const alpha = data[i + 3] / 255;
    if (alpha <= 0.02) continue;
    a += alpha;
    r += (data[i] / 255) * alpha;
    g += (data[i + 1] / 255) * alpha;
  }
  const cover = (100 * a) / (S * S);
  console.log(`${name.padEnd(10)} covers ${cover.toFixed(1).padStart(5)}% of the frame  ` +
              `primary ${(100 * r / a).toFixed(0).padStart(3)}%  secondary ${(100 * g / a).toFixed(0).padStart(3)}%`);
}

// A contact sheet so the set can be judged together rather than one at a time.
const CELL = 150, COLS = 5, ROWS = Math.ceil(names.length / COLS);
const sheetW = COLS * CELL, sheetH = ROWS * (CELL + 20);
const tiles = [];
for (let i = 0; i < names.length; i++) {
  const buf = await sharp(path.join(OUT, `Symbol_${names[i]}.png`)).resize(CELL - 24, CELL - 24).toBuffer();
  tiles.push({ input: buf, left: (i % COLS) * CELL + 12, top: Math.floor(i / COLS) * (CELL + 20) + 12 });
}
let labels = `<svg xmlns="http://www.w3.org/2000/svg" width="${sheetW}" height="${sheetH}">`;
names.forEach((n, i) => {
  const x = (i % COLS) * CELL + CELL / 2, y = Math.floor(i / COLS) * (CELL + 20) + CELL + 14;
  labels += `<text x="${x}" y="${y}" fill="#c9d5e1" font-family="monospace" font-size="13" text-anchor="middle">${n}</text>`;
});
labels += `</svg>`;

const sheet = path.join(PROJ, 'Art', '_review', 'civ-symbols.png');
fs.mkdirSync(path.dirname(sheet), { recursive: true });
await sharp({ create: { width: sheetW, height: sheetH, channels: 4, background: { r: 18, g: 22, b: 28, alpha: 1 } } })
  .composite([...tiles, { input: Buffer.from(labels), top: 0, left: 0 }])
  .png().toFile(sheet);

console.log(`\n${names.length} symbols -> ${path.relative(PROJ, OUT)}`);
console.log(`contact sheet -> ${path.relative(PROJ, sheet)}`);
console.log('Red = primary region, green = secondary, alpha = coverage. CivEmblem composites them.');
