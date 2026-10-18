// DeviceTests only: removing the rows old verifiers left on the iPhone walkthrough accounts (RoteiroCleanupFixture),
// with a recoverable copy first. The list is fixed in roteiro-cleanup-2026-10-18.json; nothing else is ever selected.
//   node tools/DeviceTestApi/roteiro-cleanup.cjs plan                       read-only: dependents, evidence, refusals, hash
//   node tools/DeviceTestApi/roteiro-cleanup.cjs backup                     read-only on SQL: every column to .device-tests/
//   node tools/DeviceTestApi/roteiro-cleanup.cjs remove <backup> <hash>     one transaction; only if SQL still equals the copy
//   node tools/DeviceTestApi/roteiro-cleanup.cjs restore <backup>           puts the copied rows back
// Validated on disposable data by verify-roteiro-cleanup.cjs. Never prints credentials or tokens (it uses none).
if (require.main !== module) return; // Importing it never runs it.
const fs = require('node:fs'), path = require('node:path');
const base = 'http://127.0.0.1:5099/device-test/roteiro-cleanup';
const list = JSON.parse(fs.readFileSync(path.join(__dirname, 'roteiro-cleanup-2026-10-18.json'), 'utf8'));
const post = async (route, body) => { const r = await fetch(base + route, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }); const t = await r.text(); return { status: r.status, data: t ? JSON.parse(t) : null }; };
(async () => {
  const health = await (await fetch('http://127.0.0.1:5099/device-test/health')).json();
  if (health.database !== 'MeepleBoard_DeviceTests') throw new Error('not DeviceTests');
  const [mode, a, b] = process.argv.slice(2);
  const selection = { sessions: list.sessions, matches: list.matches };
  if (mode === 'plan') { const r = await post('/plan', selection); console.log(JSON.stringify(r.data, null, 1)); process.exit(r.data.refusals.length ? 1 : 0); }
  if (mode === 'backup') { const r = await post('/backup', { ...selection, name: `roteiro-cleanup-backup-${new Date().toISOString().replace(/[:.]/g, '-')}` }); console.log(r.status, JSON.stringify(r.data)); process.exit(r.status === 200 ? 0 : 1); }
  if (mode === 'remove' && a && b) { const r = await post('/remove', { backup: a, confirmHash: b }); console.log(r.status, JSON.stringify(r.data)); process.exit(r.status === 200 ? 0 : 1); }
  if (mode === 'restore' && a) { const r = await post('/restore', { backup: a }); console.log(r.status, JSON.stringify(r.data)); process.exit(r.status === 200 ? 0 : 1); }
  console.log('usage: roteiro-cleanup.cjs plan | backup | remove <backup> <hash> | restore <backup>'); process.exit(2);
})().catch(e => { console.error('FAIL', e.message); process.exit(1); });
