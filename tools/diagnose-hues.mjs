// Where did the colour actually land? Prints the dominant saturated hues of each generated albedo,
// so a livery that "failed" can be told apart from a livery that landed at a hue the key missed.
// Throwaway diagnostic; verify-textures.mjs is the real check.
import fs from 'node:fs';
import path from 'node:path';
import sharp from 'sharp';

const DIR = process.argv[2] || 'Art/MeshyTextured/Aquarii';

const hsv = (r, g, b) => {
  const mx = Math.max(r, g, b), mn = Math.min(r, g, b), d = mx - mn;
  let h = 0;
  if (d > 1e-6) {
    if (mx === r) h = 60 * (((g - b) / d) % 6);
    else if (mx === g) h = 60 * ((b - r) / d + 2);
    else h = 60 * ((r - g) / d + 4);
    if (h < 0) h += 360;
  }
  return [h, mx <= 1e-6 ? 0 : d / mx, mx];
};

for (const u of fs.readdirSync(DIR).sort()) {
  const d = path.join(DIR, u);
  if (!fs.statSync(d).isDirectory()) continue;
  const a = fs.readdirSync(d).find(f => /_albedo\.png$/i.test(f));
  if (!a) continue;
  const { data, info } = await sharp(path.join(d, a))
    .resize(160, 160, { fit: 'fill' }).removeAlpha().raw().toBuffer({ resolveWithObject: true });

  const bins = new Array(18).fill(0);
  let sat = 0, tot = 0;
  for (let i = 0; i < data.length; i += info.channels) {
    const [h, s, v] = hsv(data[i] / 255, data[i + 1] / 255, data[i + 2] / 255);
    tot++;
    if (s >= 0.35 && v >= 0.12) { sat++; bins[Math.floor(h / 20)]++; }
  }
  const top = bins.map((n, i) => ({ deg: i * 20, pct: 100 * n / tot }))
    .filter(x => x.pct > 1.5).sort((x, y) => y.pct - x.pct).slice(0, 4);
  console.log(u.padEnd(30), 'sat=' + (100 * sat / tot).toFixed(0) + '%',
              'peaks:', top.map(x => `${x.deg}deg:${x.pct.toFixed(0)}%`).join('  ') || '(none)');
}
