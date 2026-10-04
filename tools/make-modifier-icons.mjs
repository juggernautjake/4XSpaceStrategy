// ============================================================================================
// THE WORLD MODIFIER ICONS (and the Fertility index icon)
//
//   node tools/make-modifier-icons.mjs
//
// Draws every World Modifier badge, the "?" badge a level-1 survey shows, and the Fertility index's
// apple tree, as 16x16 pixel art in the same flat, few-colour style as the supplied index icons.
//
// Drawn from character grids rather than shipped as hand-made PNGs for the reason the Water icon is
// (see import-index-icons.mjs): the art can be read, diffed and regenerated, and nobody has to remember
// which shade was used. One character per pixel, '.' is transparent; each icon names its own palette.
//
// ---- THE FERTILITY ICON ---------------------------------------------------------------------
//
// It was a yellow head of grain. Yellow is the SOLAR colour on this bar, and nothing about it said
// "green", which is the Fertility ramp's colour everywhere else. An apple tree is legible at 16 pixels
// and carries the green. Written here to Index_Fertile.png, and import-index-icons.mjs no longer copies
// the grain over it.
// ============================================================================================

import sharp from 'sharp';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const PROJ = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const MOD_OUT = path.join(PROJ, 'Assets', 'Resources', 'SpaceAssets', 'ModifierIcons');
const IDX_OUT = path.join(PROJ, 'Assets', 'Resources', 'SpaceAssets', 'IndexIcons');

const ICONS = {
  // ---- the "something is here" badge ----
  Modifier_Unknown: {
    pal: { W: [235, 240, 248], S: [150, 165, 185] },
    rows: [
      '................',
      '.....WWWWW......',
      '....WWSSSWW.....',
      '...WWS...SWW....',
      '...WS.....WW....',
      '..........WW....',
      '.........WWS....',
      '........WWS.....',
      '.......WWS......',
      '.......WW.......',
      '.......WW.......',
      '.......SS.......',
      '................',
      '.......WW.......',
      '.......WW.......',
      '................',
    ],
  },

  // ---- Tidally Locked: a world lit on one face only ----
  Modifier_TidallyLocked: {
    pal: { Y: [255, 220, 60], L: [255, 240, 140], O: [230, 160, 30], D: [30, 40, 80], N: [15, 20, 45], E: [70, 85, 130] },
    rows: [
      '................',
      '.....OOOENNN....',
      '....OYYYDDDNN...',
      '...OYLLYYDDDNN..',
      '..OYLLYYYDDDDN..',
      '..OYLYYYYDDDDNN.',
      '.OYYYYYYYDDDDDN.',
      '.OYYYYYYYDDDDDN.',
      '.OYYYYYYYDDDDDN.',
      '.OYYYYYYYDDDDDN.',
      '..OYYYYYYDDDDNN.',
      '..OYYYYYYDDDDN..',
      '...OYYYYYDDDNN..',
      '....OYYYYDDDN...',
      '.....OOOENNN....',
      '................',
    ],
  },

  // ---- High Quality Minerals: a cut gem ----
  Modifier_HighQualityMinerals: {
    pal: { A: [255, 170, 50], B: [255, 205, 100], C: [200, 110, 20], D: [130, 65, 10], W: [255, 245, 220] },
    rows: [
      '................',
      '................',
      '....DDDDDDDD....',
      '...DBBWBBABAD...',
      '..DBWBBBAABAAD..',
      '.DBBBBBAAAAAAAD.',
      '.DDDDDDDDDDDDDD.',
      '..DAABBAAACCCD..',
      '...DAABAACCCD...',
      '....DABAACCD....',
      '.....DBACCD.....',
      '......DACD......',
      '.......DD.......',
      '................',
      '................',
      '................',
    ],
  },

  // ---- Vast Mineral Deposits: a heap of ore ----
  Modifier_VastMineralDeposits: {
    pal: { R: [150, 130, 100], S: [110, 90, 70], K: [70, 55, 45], O: [255, 170, 50], G: [255, 220, 120] },
    rows: [
      '................',
      '................',
      '................',
      '......KKKK......',
      '.....KRROSK.....',
      '.....KRGRSK.....',
      '...KKKRRSSKKK...',
      '..KRRKKSSKROSK..',
      '..KROSRKKRRRSK..',
      '.KRGRRSSKRRSSSK.',
      '.KRRRSKKRROSSKK.',
      'KRROSSKRRRGRSSSK',
      'KRRRSSKRRRRSSSSK',
      'KKKKKKKKKKKKKKKK',
      '................',
      '................',
    ],
  },

  // ---- Extreme Weather: a storm cloud and a bolt ----
  Modifier_ExtremeWeather: {
    pal: { C: [150, 110, 200], H: [200, 160, 240], D: [80, 50, 120], Y: [255, 225, 70], W: [255, 250, 200] },
    rows: [
      '.....HHH........',
      '....HCCCH.HHH...',
      '..HHCCCCCHCCCH..',
      '.HCCCCCCCCCCCCH.',
      'HCCCCCCCCCCCCCCH',
      'HCCCCCCCCCCCCCCD',
      '.DCCCCCCCCCCCCD.',
      '..DDDDDYYDDDDD..',
      '......YY........',
      '.....YYW........',
      '....YYYYYY......',
      '.......YY.......',
      '......YW........',
      '.....YY.........',
      '.....Y..........',
      '................',
    ],
  },

  // ---- Fertile World: a green globe putting out a sprout ----
  Modifier_FertileWorld: {
    pal: { G: [80, 200, 70], L: [150, 245, 110], D: [30, 110, 40], B: [40, 110, 200], S: [90, 160, 235] },
    rows: [
      '.......LL.......',
      '......LGGL......',
      '.....LG.DG......',
      '......D.D.......',
      '.....BBDBB......',
      '...BBGGDGGBB....',
      '..BGGGGGGBBSB...',
      '..BGGGBBBBSSB...',
      '.BGGGBBBBBBBSB..',
      '.BBGGBBBGGGBBB..',
      '.BBBBBBGGGGGBB..',
      '..BBBBBGGGGBB...',
      '..BSBBBBGGBBB...',
      '...BBSSBBBBB....',
      '.....BBBBB......',
      '................',
    ],
  },

  // ---- Continental Plates: a globe broken by glowing margins ----
  Modifier_ContinentalPlates: {
    pal: { P: [150, 115, 75], Q: [115, 85, 55], R: [255, 80, 40], O: [255, 160, 60], K: [70, 50, 35] },
    rows: [
      '................',
      '.....KKKKKK.....',
      '...KKPPRPPQKK...',
      '..KPPPPRPPQQQK..',
      '..KPPPRPPPQQQK..',
      '.KPPPRPPPPPQQQK.',
      '.KRRRRPPPPPRRRK.',
      '.KPPPPROOORPQQK.',
      '.KPPPPPRPPPPQQK.',
      '.KPPPPPRPPPPQQK.',
      '.KQPPPPRPPPPQQK.',
      '..KQPPRPPPPQQK..',
      '..KQQPRPPPQQQK..',
      '...KKQRQQQQKK...',
      '.....KKKKKK.....',
      '................',
    ],
  },
};

// ---- the Fertility INDEX icon: an apple tree ----
const APPLE_TREE = {
  pal: { G: [70, 180, 60], L: [140, 230, 100], D: [30, 105, 40], R: [230, 45, 40], W: [255, 160, 150], T: [120, 75, 40], U: [80, 50, 25] },
  rows: [
    '.....DDDDDD.....',
    '...DDGGGGGGDD...',
    '..DGGLLGGGRGGD..',
    '.DGLLGGGGGGGGGD.',
    '.DGLGGRWGGGLGGD.',
    'DGGGGGRRGGLLGGGD',
    'DGRGGGGGGGGGGRGD',
    'DGRWGGGLGGGGGGGD',
    '.DGGGGLLGGRWGGD.',
    '.DDGGGGGGGRRGDD.',
    '..DDDGGTGGGDDD..',
    '....DDDTUDDD....',
    '.......TU.......',
    '.......TU.......',
    '......TTUU......',
    '.....TTT.UU.....',
  ],
};

async function draw(icon, file) {
  const N = 16;
  if (icon.rows.length !== N || icon.rows.some(r => r.length !== N))
    throw new Error(`${path.basename(file)}: every icon must be exactly 16x16`);
  const px = Buffer.alloc(N * N * 4, 0);
  for (let y = 0; y < N; y++)
    for (let x = 0; x < N; x++) {
      const ch = icon.rows[y][x];
      if (ch === '.') continue;
      const c = icon.pal[ch];
      if (!c) throw new Error(`${path.basename(file)}: no colour for '${ch}'`);
      const i = (y * N + x) * 4;
      px[i] = c[0]; px[i + 1] = c[1]; px[i + 2] = c[2]; px[i + 3] = 255;
    }
  await sharp(px, { raw: { width: N, height: N, channels: 4 } }).png().toFile(file);
}

fs.mkdirSync(MOD_OUT, { recursive: true });
for (const [name, icon] of Object.entries(ICONS)) {
  await draw(icon, path.join(MOD_OUT, `${name}.png`));
  console.log(`  ${name}.png`);
}
await draw(APPLE_TREE, path.join(IDX_OUT, 'Index_Fertile.png'));
console.log('  Index_Fertile.png  (apple tree)');

// ---- contact sheet, each badge inside its index-coloured frame, as the game draws it ----
const FRAMES = {
  Modifier_Unknown: [200, 200, 200],
  Modifier_TidallyLocked: [255, 247, 107],
  Modifier_HighQualityMinerals: [255, 173, 56],
  Modifier_VastMineralDeposits: [255, 173, 56],
  Modifier_ExtremeWeather: [224, 133, 255],
  Modifier_FertileWorld: [122, 255, 97],
  Modifier_ContinentalPlates: [255, 77, 51],
};
const names = [...Object.keys(ICONS), 'Index_Fertile'];
const CELL = 120, tiles = [];
for (let i = 0; i < names.length; i++) {
  const n = names[i];
  const f = n === 'Index_Fertile' ? path.join(IDX_OUT, 'Index_Fertile.png') : path.join(MOD_OUT, `${n}.png`);
  const fc = FRAMES[n];
  if (fc) {
    tiles.push({ input: { create: { width: 104, height: 104, channels: 4, background: { r: fc[0], g: fc[1], b: fc[2], alpha: 1 } } },
                 left: i * CELL + 8, top: 4 });
    tiles.push({ input: { create: { width: 96, height: 96, channels: 4, background: { r: 12, g: 16, b: 24, alpha: 1 } } },
                 left: i * CELL + 12, top: 8 });
  }
  tiles.push({ input: await sharp(f).resize(80, 80, { kernel: 'nearest' }).toBuffer(), left: i * CELL + 20, top: 16 });
}
let labels = `<svg xmlns="http://www.w3.org/2000/svg" width="${names.length * CELL}" height="136">`;
names.forEach((n, i) => {
  labels += `<text x="${i * CELL + CELL / 2}" y="128" fill="#c9d5e1" font-family="monospace" ` +
            `font-size="10" text-anchor="middle">${n.replace('Modifier_', '')}</text>`;
});
labels += '</svg>';
const sheet = path.join(PROJ, 'Art', '_review', 'modifier-icons.png');
fs.mkdirSync(path.dirname(sheet), { recursive: true });
await sharp({ create: { width: names.length * CELL, height: 136, channels: 4, background: { r: 18, g: 22, b: 28, alpha: 1 } } })
  .composite([...tiles, { input: Buffer.from(labels), top: 0, left: 0 }])
  .png().toFile(sheet);
console.log(`contact sheet -> ${path.relative(PROJ, sheet)}`);
