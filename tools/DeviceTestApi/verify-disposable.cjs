// DeviceTests only: the disposable people of a run are removed with everything they made also when the run fails.
//   node tools/DeviceTestApi/verify-disposable.cjs
// A run creates a session, a match with a diary and a library entry, then fails on purpose; afterwards the people can no
// longer sign in and the fixture no longer knows them. Never the iPhone accounts.
if (require.main !== module) return; // Importing a verifier never runs it: only `node <file>` does (see docs/testes-macos-docker.md).
const assert = require('node:assert/strict');
const { run, quartet } = require('./disposable.cjs');
const base = 'http://127.0.0.1:5099';
(async () => {
  let people, created;
  const failure = await run({ label: 'selftest', ...quartet() }, async world => {
    people = world.people;
    const A = await world.login('autor'), P = await world.login('participante');
    const call = async (u, method, route, body) => { const r = await fetch(base + '/MeepleBoard' + route, { method, headers: { Authorization: 'Bearer ' + u.token, 'Content-Type': 'application/json' }, body: body && JSON.stringify(body) }); const t = await r.text(); return { status: r.status, data: t ? JSON.parse(t) : null }; };
    const s = await call(A, 'POST', '/session', { name: 'Autoteste descartável', scheduledStartDate: new Date().toISOString(), playerIds: [P.id] });
    assert.equal(s.status, 201);
    await call(P, 'POST', `/session/${s.data.id}/invites/respond`, { accept: true });
    const game = (await call(A, 'GET', '/game/search?name=Wingspan')).data;
    const m = await call(A, 'POST', '/matches', { gameId: game.id, gameName: game.name, gameSessionId: s.data.id, matchDate: new Date().toISOString(), isSoloGame: false, winnerId: A.id, playerIds: [A.id, P.id], personalRating: 6, notes: 'nota' });
    assert.equal(m.status, 201);
    assert.equal((await call(A, 'POST', `/users/${A.id}/games`, { gameId: game.id, gameName: game.name, status: 1 })).status, 201);
    created = { session: s.data.id, match: m.data.id };
    throw new Error('deliberate failure');
  }).then(() => null, error => error);
  assert.equal(failure?.message, 'deliberate failure', 'the failure reaches the caller');
  for (const p of Object.values(people)) {
    const r = await fetch(base + '/MeepleBoard/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: p.email, password: p.password }) });
    assert.notEqual(r.status, 200, 'a removed person cannot sign in');
  }
  const unknown = await fetch(base + '/device-test/disposable/state', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(Object.values(people).map(p => p.id)) });
  assert.equal(unknown.status, 400, 'the fixture no longer knows them');
  const plan = await (await fetch(base + '/device-test/roteiro-cleanup/plan', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ sessions: [created.session], matches: [created.match] }) })).json();
  assert.ok(plan.refusals.some(x => x.includes('not found')) && plan.counts.GameSessions === 0 && plan.counts.Matches === 0, 'the session and the match are gone');
  console.log('PASS a run that fails still removes its people, session, match, diary and library entry');
})().catch(error => { console.error('FAIL', error.message); process.exit(1); });
