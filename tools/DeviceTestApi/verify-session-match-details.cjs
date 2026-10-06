const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const data = path.resolve(__dirname, '../../.device-tests');
const base = 'http://127.0.0.1:5099';
async function get(route, token, expected = 200) {
  const r = await fetch(base + route, { headers: token ? { Authorization: 'Bearer ' + token } : {} });
  assert.equal(r.status, expected, route);
  return r.status === 200 ? r.json() : null;
}
(async () => {
  const health = await get('/device-test/health');
  assert.equal(health.environment, 'DeviceTests'); assert.equal(health.database, 'MeepleBoard_DeviceTests'); assert.equal(health.externalDelivery, false);
  const accounts = JSON.parse(fs.readFileSync(path.join(data, 'accounts.json'), 'utf8'));
  const fixture = JSON.parse(fs.readFileSync(path.join(data, 'session-write-fixture.json'), 'utf8'));
  const tokens = [];
  for (const a of accounts) {
    const r = await fetch(base + '/MeepleBoard/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: a.Email, password: a.Password, rememberMe: false, isMobile: true }) });
    assert.equal(r.status, 200); tokens.push((await r.json()).token);
  }
  const route = '/MeepleBoard/session/' + fixture.sessionId;
  const session = await get(route, tokens[0]);
  assert.ok(session.matches.length > 0);
  for (const match of session.matches) {
    const individual = await get('/MeepleBoard/matches/' + match.id, tokens[0]);
    assert.equal(match.gameImageUrl, individual.gameImageUrl);
    assert.equal(match.gameName, individual.gameName); assert.notEqual(match.gameName, 'Jogo Desconhecido');
    assert.equal(match.winnerId, individual.winnerId); assert.equal(match.winnerName, individual.winnerName);
    assert.ok(match.players.length > 0);
    for (const p of match.players) {
      const real = individual.players.find(x => x.userId === p.userId);
      assert.ok(real); assert.equal(p.userName, real.userName); assert.notEqual(p.userName, 'Jogador Desconhecido');
      assert.equal(p.score, real.score); assert.equal(p.isWinner, real.isWinner);
    }
    assert.ok(match.players.some(p => p.score === 0));
  }
  const reopened = await get(route, tokens[0]);
  assert.deepEqual(reopened.matches, session.matches);
  const participant = await get(route, tokens[1]);
  assert.deepEqual(participant.matches.map(m => m.id).sort(), session.matches.map(m => m.id).sort());
  assert.equal((await get(route, tokens[3])).matches.length, 0, 'Non-participant session member cannot see match data');
  await get(route, tokens[2], 403); await get(route, null, 401);
  for (const match of session.matches) {
    await get('/MeepleBoard/matches/' + match.id, tokens[3], 403);
    await get('/MeepleBoard/matches/' + match.id, tokens[2], 403);
  }
  console.log('PASS session game/winner/player names match individual API; named scores including zero survive reload; privacy filters retained');
})().catch(e => { console.error(e.message); process.exitCode = 1; });
