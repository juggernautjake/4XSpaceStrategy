// ============================================================================================
// A LOCAL FILE SERVER, SO THE BROWSER CAN REACH THE ART
//
//   node tools/asset-server.mjs [--root <dir>] [--port 8787]
//
// The Meshy batch runs INSIDE the meshy.ai page, because that is where the auth token lives and
// where it stays fresh — a token lifted out into a Node script expires halfway through a hundred-model
// run and takes the rest of the batch with it.
//
// But a page on meshy.ai cannot read `C:\Users\...\4X-Ship-Models`. The browser sandbox has no file
// access, and pushing a 1.3 MB `.glb` through as base64 in a tool call is 1.7 MB of string per model —
// unworkable a hundred times over.
//
// So the files are served over loopback instead and the page fetches them like any other URL. CORS is
// wide open because the only client is a page we are driving ourselves, and the only thing reachable
// is a directory of ship models explicitly passed in on the command line.
//
// Read-only, loopback-only, one directory. It exists for the length of a batch and then stops.
// ============================================================================================

import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';

const argv = process.argv.slice(2);
const arg = (name, dflt) => { const i = argv.indexOf(name); return i >= 0 ? argv[i + 1] : dflt; };

const ROOT = path.resolve(arg('--root', 'C:/Users/lando/Downloads/4X-Ship-Models'));
const PORT = parseInt(arg('--port', '8787'), 10);

const TYPES = {
  '.glb': 'model/gltf-binary', '.gltf': 'model/gltf+json', '.fbx': 'application/octet-stream',
  '.obj': 'text/plain', '.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg',
  '.txt': 'text/plain; charset=utf-8', '.json': 'application/json',
};

const server = http.createServer((req, res) => {
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Headers', '*');
  if (req.method === 'OPTIONS') { res.writeHead(204).end(); return; }

  let rel;
  try { rel = decodeURIComponent(new URL(req.url, 'http://x').pathname); }
  catch { res.writeHead(400).end('bad url'); return; }

  // `index` lists the tree as JSON so the batch can be driven entirely from the page.
  if (rel === '/index') {
    const out = [];
    (function walk(d) {
      for (const f of fs.readdirSync(d)) {
        const fp = path.join(d, f);
        const st = fs.statSync(fp);
        if (st.isDirectory()) walk(fp);
        else out.push({ path: path.relative(ROOT, fp).replace(/\\/g, '/'), size: st.size });
      }
    })(ROOT);
    res.writeHead(200, { 'content-type': 'application/json' });
    res.end(JSON.stringify(out));
    return;
  }

  // Resolve INSIDE root and verify it stayed there. Loopback-only is not a reason to allow `../..`
  // out of the served directory.
  const abs = path.resolve(ROOT, '.' + rel);
  if (!abs.startsWith(ROOT)) { res.writeHead(403).end('outside root'); return; }
  if (!fs.existsSync(abs) || !fs.statSync(abs).isFile()) { res.writeHead(404).end('not found'); return; }

  res.writeHead(200, {
    'content-type': TYPES[path.extname(abs).toLowerCase()] || 'application/octet-stream',
    'content-length': fs.statSync(abs).size,
  });
  fs.createReadStream(abs).pipe(res);
});

server.listen(PORT, '127.0.0.1', () => {
  console.log(`serving ${ROOT}`);
  console.log(`  http://127.0.0.1:${PORT}/index`);
});
