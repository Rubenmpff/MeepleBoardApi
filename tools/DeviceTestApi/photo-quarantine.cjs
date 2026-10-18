// DeviceTests only: stored diary photo files that no diary entry references are moved to quarantine, with an inventory,
// and can be put back. Nothing is deleted here. (docs/metadados-fotografias.md, approved 18/10/2026.)
//   node tools/DeviceTestApi/photo-quarantine.cjs move        orphans (checked again now) → .device-tests/photos-quarantine/<stamp>/
//   node tools/DeviceTestApi/photo-quarantine.cjs restore <stamp>   puts every file of that quarantine back, unchanged
// The check uses GET /device-test/disposable/photo-inventory (read-only). A referenced file is never moved.
if (require.main !== module) return; // Importing it never runs it.
const fs = require('node:fs'), path = require('node:path'), crypto = require('node:crypto');
const root = path.resolve(__dirname, '../..'), photos = path.join(root, '.device-tests/photos'), quarantine = path.join(root, '.device-tests/photos-quarantine');
const sha = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
(async () => {
  const [mode, stamp] = process.argv.slice(2);
  if (mode === 'move') {
    const health = await (await fetch('http://127.0.0.1:5099/device-test/health')).json();
    if (health.database !== 'MeepleBoard_DeviceTests') throw new Error('not DeviceTests');
    const inventory = await (await fetch('http://127.0.0.1:5099/device-test/disposable/photo-inventory')).json();
    const orphans = inventory.filter(f => f.owner === 'sem referência');
    const kept = inventory.filter(f => f.owner !== 'sem referência');
    if (orphans.length === 0) { console.log('no orphan files'); return; }
    const dir = path.join(quarantine, new Date().toISOString().replace(/[:.]/g, '-'));
    fs.mkdirSync(dir, { recursive: true, mode: 0o700 });
    const list = orphans.map(f => { const from = path.join(photos, f.file); return { file: f.file, bytes: fs.statSync(from).size, sha256: sha(from), modified: f.modified }; });
    fs.writeFileSync(path.join(dir, 'inventory.json'), JSON.stringify({ movedAt: new Date().toISOString(), from: photos, files: list, keptReferenced: kept.map(f => ({ file: f.file, owner: f.owner })) }, null, 1), { mode: 0o600 });
    for (const f of list) fs.renameSync(path.join(photos, f.file), path.join(dir, f.file));
    for (const f of list) if (sha(path.join(dir, f.file)) !== f.sha256) throw new Error('changed while moving: ' + f.file);
    console.log(`moved ${list.length} orphan files to ${path.relative(root, dir)} (inventory.json with size and SHA-256); kept ${kept.length} referenced (${kept.map(f => f.owner).join(', ')})`);
    return;
  }
  if (mode === 'restore' && stamp) {
    const dir = path.join(quarantine, path.basename(stamp));
    const { files } = JSON.parse(fs.readFileSync(path.join(dir, 'inventory.json'), 'utf8'));
    for (const f of files) {
      if (fs.existsSync(path.join(photos, f.file))) throw new Error('already present: ' + f.file);
      if (sha(path.join(dir, f.file)) !== f.sha256) throw new Error('quarantined copy differs: ' + f.file);
    }
    for (const f of files) fs.renameSync(path.join(dir, f.file), path.join(photos, f.file));
    console.log(`restored ${files.length} files to .device-tests/photos`);
    return;
  }
  console.log('usage: photo-quarantine.cjs move | restore <stamp>'); process.exit(2);
})().catch(e => { console.error('FAIL', e.message); process.exit(1); });
