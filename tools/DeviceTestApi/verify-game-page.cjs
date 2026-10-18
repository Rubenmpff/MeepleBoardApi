// DeviceTests only: game page sheet, expansions and a session's planned game against real SQL (test API, port 5099).
//   node tools/DeviceTestApi/verify-game-page.cjs
// The session created here is cancelled (and so deleted) before it ends. Never prints credentials or tokens.
if (require.main !== module) return; // Importing a verifier never runs it: only `node <file>` does (see docs/testes-macos-docker.md).
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../..'), base = 'http://127.0.0.1:5099';
(async () => {
  const health = await (await fetch(base + '/device-test/health')).json();
  assert.deepEqual(health, { environment: 'DeviceTests', database: 'MeepleBoard_DeviceTests', externalDelivery: false });
  const accounts = JSON.parse(fs.readFileSync(path.join(root, '.device-tests/accounts.json'), 'utf8'));
  const login = async name => { const a = accounts.find(x => x.Email === name + '@meepleboard.test'); const r = await fetch(base + '/MeepleBoard/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: a.Email, password: a.Password, deviceInfo: 'verify-game-page' }) }); assert.equal(r.status, 200); return { id: a.Id, token: (await r.json()).token }; };
  const call = async (u, method, route, body) => { const r = await fetch(base + '/MeepleBoard' + route, { method, headers: { ...(u ? { Authorization: `Bearer ${u.token}` } : {}), 'Content-Type': 'application/json' }, body: body && JSON.stringify(body) }); const t = await r.text(); return { status: r.status, data: t ? JSON.parse(t) : null }; };
  const pass = name => console.log('PASS ' + name);
  const viewer = await login('participante'), friend = await login('autor');
  const find = async name => (await call(viewer, 'GET', '/game/search?name=' + encodeURIComponent(name))).data;

  const wingspan = (await call(viewer, 'GET', `/game/${(await find('Wingspan')).id}`)).data;
  assert.deepEqual([wingspan.minPlayers, wingspan.maxPlayers, wingspan.minPlayTime, wingspan.maxPlayTime, wingspan.minAge, wingspan.averageWeight], [1, 5, 40, 70, 10, null]);
  const solo = (await call(viewer, 'GET', `/game/${(await find('Meeple Teste Solo')).id}`)).data;
  assert.deepEqual([solo.minPlayTime, solo.maxPlayTime, solo.minAge, solo.averageWeight], [null, null, null, null]);
  pass('Wingspan sheet 1–5 players, 40–70 min, 10+, no complexity; fictitious game has no BGG sheet');

  const expansions = wingspan.expansions.map(e => e.name).sort();
  assert.deepEqual(expansions, ['Wingspan: European Expansion', 'Wingspan: Oceania Expansion']);
  assert.ok(wingspan.expansions.every(e => e.isExpansion && e.baseGameId === wingspan.id), 'linked to the base game');
  const library = (await call(viewer, 'GET', `/users/${viewer.id}/games`)).data;
  const owned = new Map((library.items ?? library).map(e => [e.gameId, e.status]));
  const status = name => owned.get(wingspan.expansions.find(e => e.name === name).id);
  assert.ok(status('Wingspan: European Expansion') != null && status('Wingspan: Oceania Expansion') == null, 'participante owns only the European expansion');
  pass('two expansions linked to Wingspan; participante has the European one in the library');

  const start = new Date(Date.now() + 3 * 864e5).toISOString();
  const created = await call(viewer, 'POST', '/session', { name: 'Verificação jogo previsto', scheduledStartDate: start, playerIds: [friend.id], plannedGameId: wingspan.id });
  assert.equal(created.status, 201, JSON.stringify(created.data));
  const id = created.data.id;
  try {
    assert.equal(created.data.plannedGameId, wingspan.id); assert.equal(created.data.plannedGameName, 'Wingspan');
    const before = created.data;
    const changed = await call(viewer, 'PUT', `/session/${id}/planned-game`, { gameId: solo.id });
    assert.equal(changed.status, 200); assert.equal(changed.data.plannedGameName, 'Meeple Teste Solo');
    for (const key of ['scheduledStartDate', 'responseDeadline', 'autoCloseAt', 'latestCloseAt', 'status']) assert.deepEqual(changed.data[key], before[key], key + ' changed');
    assert.equal(changed.data.matches.length, 0);
    const listed = (await call(friend, 'GET', '/session')).data.find(s => s.id === id);
    assert.equal(listed.plannedGameName, 'Meeple Teste Solo', 'guests see the planned game in the list');
    assert.equal((await call(friend, 'PUT', `/session/${id}/planned-game`, { gameId: wingspan.id })).status, 403);
    assert.equal((await call(viewer, 'PUT', `/session/${id}/planned-game`, { gameId: '00000000-0000-0000-0000-000000000001' })).status, 400);
    const cleared = await call(viewer, 'PUT', `/session/${id}/planned-game`, { gameId: null });
    assert.equal(cleared.status, 200); assert.equal(cleared.data.plannedGameId, null);
    const none = await call(viewer, 'POST', '/session', { name: 'Verificação sem jogo', scheduledStartDate: start, playerIds: [friend.id] });
    assert.equal(none.status, 201); assert.equal(none.data.plannedGameId, null);
    assert.equal((await call(viewer, 'POST', `/session/${none.data.id}/cancel`)).status, 204);
    pass('session with a planned game: created, changed and cleared by the organizer; guest 403, unknown game 400; times unchanged; sessions without a game still created');
  } finally {
    assert.equal((await call(viewer, 'POST', `/session/${id}/cancel`)).status, 204);
  }
})().catch(error => { console.error('FAIL', error.message); process.exit(1); });
