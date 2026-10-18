// DeviceTests only: Companhia «Com quem jogaste» lists only friends with shared matches under the selected filters,
// and each count equals that friend's own comparison. On dated matches (statistics-dataset.cjs) of fresh people
// (disposable.cjs), removed at the end. Never the iPhone accounts; never prints credentials or tokens.
//   node tools/DeviceTestApi/verify-company-companions.cjs
if (require.main !== module) return; // Importing a verifier never runs it: only `node <file>` does (see docs/testes-macos-docker.md).
const assert = require('node:assert/strict');
const { run, quartet } = require('./disposable.cjs'), dataset = require('./statistics-dataset.cjs');
const base = 'http://127.0.0.1:5099';
run({ label: 'companions', ...quartet() }, async world => {
  const people = Object.fromEntries(await Promise.all(['autor', 'participante', 'membro'].map(async r => [r, await world.login(r)])));
  await dataset.statisticsCases(people.autor, people.participante, people.membro);
  await dataset.companyExtras(people.autor, people.participante, people.membro);
  const login = async name => people[name].token;
  const pass = name => console.log('PASS ' + name);
  for (const name of ['participante', 'autor']) {
    const token = await login(name);
    const get = async params => { const r = await fetch(base + '/MeepleBoard/statistics/me/company?' + new URLSearchParams(Object.fromEntries(Object.entries(params).filter(([, v]) => v != null))), { headers: { Authorization: `Bearer ${token}` } }); assert.equal(r.status, 200, await r.clone().text()); return r.json(); };
    const period = { start: '2016-10-07', endExclusive: '2026-10-15', timeZone: 'Europe/Lisbon' };
    const games = (await (await fetch(base + '/MeepleBoard/statistics/me?' + new URLSearchParams(period), { headers: { Authorization: `Bearer ${token}` } })).json()).gameOptions ?? [];
    let checked = 0;
    for (const scope of [{}, ...games.map(g => ({ gameId: g.gameId })), { mode: 'COMPETITIVE' }]) {
      for (const groupContext of ['all', 'only-two', 'group']) {
        const report = await get({ ...period, ...scope, groupContext });
        assert.ok(report.companions.every(c => c.matches > 0), `${name} ${JSON.stringify(scope)} ${groupContext}: a friend with zero shared matches is listed`);
        for (const c of report.companions) {
          const pair = await get({ ...period, ...scope, groupContext, friendId: c.friendId });
          assert.equal(pair.matches, c.matches, `${name}: count differs from the comparison with ${c.name}`);
          checked++;
        }
      }
    }
    pass(`${name}: ${games.length} games × 3 contexts and a mode — only friends with shared matches; ${checked} counts equal the friend's comparison`);
  }
}).catch(error => { console.error('FAIL', error.message); process.exit(1); });
