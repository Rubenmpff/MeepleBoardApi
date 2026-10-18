// DeviceTests only: «Combinar sessão» → planned game → record ANOTHER game in the same session, against real SQL.
//   node tools/DeviceTestApi/verify-planned-game-journey.cjs
// Fresh people (disposable.cjs): participante organizes, autor accepts; removed at the end, also on failure. Never the
// iPhone accounts; never prints credentials or tokens.
if (require.main !== module) return; // Importing a verifier never runs it: only `node <file>` does (see docs/testes-macos-docker.md).
const assert = require('node:assert/strict');
const { run } = require('./disposable.cjs');
const base = 'http://127.0.0.1:5099', NAME = 'Sessão de teste · jogo previsto';
run({ label: 'plannedgame', people: [{ role: 'participante' }, { role: 'autor' }], friends: [['participante', 'autor']] }, async world => {
  const call = async (u, method, route, body, expected) => { const r = await fetch(base + '/MeepleBoard' + route, { method, headers: { Authorization: `Bearer ${u.token}`, 'Content-Type': 'application/json' }, body: body && JSON.stringify(body) }); const t = await r.text(); if (expected) assert.equal(r.status, expected, t); return t ? JSON.parse(t) : null; };
  const pass = name => console.log('PASS ' + name);
  const organizer = await world.login('participante'), guest = await world.login('autor');
  const game = async name => call(organizer, 'GET', '/game/search?name=' + encodeURIComponent(name), null, 200);
  const wingspan = await game('Wingspan'), other = await game('Meeple Teste Competitivo'), third = await game('Meeple Teste Cooperativo');

  // 1. From the game page: a session with Wingspan planned; the guest accepts and it runs.
  const session = await call(organizer, 'POST', '/session', { name: NAME, location: 'Mesa de testes', scheduledStartDate: new Date().toISOString(), playerIds: [guest.id], plannedGameId: wingspan.id }, 201);
  await call(guest, 'POST', `/session/${session.id}/invites/respond`, { accept: true }, 200);
  const running = await call(organizer, 'GET', `/session/${session.id}`, null, 200);
  assert.equal(running.status, 'Active'); assert.equal(running.plannedGameName, 'Wingspan');
  pass('session created with Wingspan planned; the guest accepted and it is running');

  // 2. A match of another game in the same session: accepted and linked to the session.
  const match = await call(organizer, 'POST', '/matches', { gameId: other.id, gameName: other.name, gameSessionId: session.id, matchDate: new Date().toISOString(), isSoloGame: false,
    winnerId: organizer.id, playerIds: [organizer.id, guest.id], playerScores: [{ userId: organizer.id, score: 12 }, { userId: guest.id, score: 9 }], scoreSummary: 'Verificação: 12–9', personalRating: 7 }, 201);
  let detail = await call(organizer, 'GET', `/session/${session.id}`, null, 200);
  assert.ok(detail.matches.some(m => m.id === match.id && m.gameId === other.id), 'the match of another game is in the session');
  assert.equal(detail.plannedGameId, wingspan.id, 'recording another game does not change the planned game');
  pass('a match of Meeple Teste Competitivo is recorded in the Wingspan session and stays linked to it');

  // 3. Changing the planned game keeps the session's matches, deadline and times.
  const before = detail;
  detail = await call(organizer, 'PUT', `/session/${session.id}/planned-game`, { gameId: third.id }, 200);
  assert.equal(detail.plannedGameName, 'Meeple Teste Cooperativo');
  for (const key of ['scheduledStartDate', 'responseDeadline', 'autoCloseAt', 'latestCloseAt', 'status']) assert.deepEqual(detail[key], before[key], key);
  assert.ok(detail.matches.some(m => m.id === match.id && m.gameId === other.id), 'earlier matches untouched');
  detail = await call(organizer, 'PUT', `/session/${session.id}/planned-game`, { gameId: wingspan.id }, 200);
  const seen = await call(guest, 'GET', `/session/${session.id}`, null, 200);
  assert.equal(seen.plannedGameName, 'Wingspan'); assert.ok(seen.matches.some(m => m.id === match.id));
  pass('changing the planned game keeps the match, deadline and times; the guest sees the same session');
}).catch(error => { console.error('FAIL', error.message); process.exit(1); });
