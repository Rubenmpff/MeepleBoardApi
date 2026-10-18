// DeviceTests only: diary photos reach other participants without identifying metadata (approved 18/10/2026).
//   node tools/DeviceTestApi/verify-photo-metadata.cjs
// Fresh people (disposable.cjs, removed at the end): A uploads JPEG, PNG and WebP fixtures that carry GPS, camera, serial
// number, dates, author, XMP/IPTC, comments and text chunks, with orientation 6; B (another participant) reads each one back
// through the participants' route. Apple's ImageIO (image-metadata.swift, compiled on demand) then checks the copy B got:
// it decodes, has the same size, the same orientation and the same pixels, and no metadata tag but the orientation.
// A format the app does not send is refused. DeviceTests storage only; production storage is not reached from here.
if (require.main !== module) return; // Importing a verifier never runs it: only `node <file>` does (see docs/testes-macos-docker.md).
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict'), { execFileSync } = require('node:child_process');
const { run } = require('./disposable.cjs');
const root = path.resolve(__dirname, '../..'), base = 'http://127.0.0.1:5099', bin = path.join(root, '.device-tests/bin/image-metadata');
const src = path.join(__dirname, 'image-metadata.swift');
if (!fs.existsSync(bin) || fs.statSync(bin).mtimeMs < fs.statSync(src).mtimeMs) { fs.mkdirSync(path.dirname(bin), { recursive: true }); execFileSync('swiftc', ['-O', src, '-o', bin]); }
const inspect = files => execFileSync(bin, files).toString().trim().split('\n').map(line => JSON.parse(line));

run({ label: 'photometa', people: [{ role: 'a' }, { role: 'b' }], friends: [['a', 'b']] }, async world => {
  const [A, B] = await Promise.all(['a', 'b'].map(r => world.login(r)));
  const call = (u, method, route, init = {}) => fetch(base + route, { method, ...init, headers: { Authorization: 'Bearer ' + u.token, ...(init.headers || {}) } });
  const game = await (await call(A, 'GET', '/MeepleBoard/game/search?name=Wingspan')).json();
  const created = await call(A, 'POST', '/MeepleBoard/matches', { headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ gameId: game.id, gameName: game.name, matchDate: new Date().toISOString(), isSoloGame: false, winnerId: A.id, playerIds: [A.id, B.id], personalRating: 7 }) });
  assert.equal(created.status, 201);
  const match = await created.json();
  const upload = (bytes, name, type) => { const form = new FormData(); form.append('file', new Blob([bytes], { type }), name); return call(A, 'POST', `/MeepleBoard/campaigns/matches/${match.id}/journal/photos`, { body: form }); };
  const work = fs.mkdtempSync(path.join(root, '.device-tests/photo-metadata-'));
  try {
    for (const [name, type] of [['photo-metadata.jpg', 'image/jpeg'], ['photo-metadata.png', 'image/png'], ['photo-metadata.webp', 'image/webp'], ['photo-with-gps.jpg', 'image/jpeg']]) {
      const original = path.join(__dirname, 'fixtures', name);
      const response = await upload(fs.readFileSync(original), name, type);
      assert.equal(response.status, 200, name + ' upload ' + response.status);
      const entry = await response.json();
      const read = await call(B, 'GET', entry.photoUrls.at(-1));
      assert.equal(read.status, 200);
      const copy = path.join(work, 'received-' + name);
      fs.writeFileSync(copy, Buffer.from(await read.arrayBuffer()));
      const [before, after] = inspect([original, copy]);
      assert.ok(before.gps, name + ': the fixture carries GPS');
      assert.ok(after.decodes, name + ': decodes');
      assert.deepEqual([after.width, after.height, after.orientation, after.pixels], [before.width, before.height, before.orientation, before.pixels], name + ': same size, orientation and pixels');
      assert.deepEqual(after.tags.filter(t => t !== 'tiff:Orientation'), [], name + ': metadata left ' + JSON.stringify(after.tags));
      for (const key of ['gps', 'camera', 'artist', 'dates', 'iptc', 'pngText']) assert.equal(after[key], false, `${name}: ${key} left`);
      console.log(`PASS ${name}: B receives it without ${before.tags.filter(t => t !== 'tiff:Orientation').length} metadata tags (GPS, camera, dates, author…); orientation ${after.orientation}, ${after.width}×${after.height}, identical pixels`);
    }
    const gif = await upload(Buffer.from('GIF89a\x01\x00\x01\x00\x00\x00\x00;'), 'x.gif', 'image/gif');
    assert.equal(gif.status, 400); assert.equal((await gif.json()).message, 'Formato de imagem não suportado. Usa JPEG, PNG ou WebP.');
    const broken = await upload(fs.readFileSync(path.join(__dirname, 'fixtures/photo-metadata.jpg')).subarray(0, 200), 'broken.jpg', 'image/jpeg');
    assert.equal(broken.status, 400);
    console.log('PASS a format the app does not send (GIF) and a damaged JPEG are refused with a clear message; nothing stored');
  } finally { fs.rmSync(work, { recursive: true, force: true }); }
}).catch(error => { console.error('FAIL', error.message); process.exit(1); });
