// DeviceTests only: the matches a session lists carry the same game, winner, names and scores (zero included) as each
// match read on its own, and a session member who did not play sees no match data. Its own session and match, between
// fresh people (disposable.cjs), removed at the end. Never the iPhone accounts.
//   node tools/DeviceTestApi/verify-session-match-details.cjs
if (require.main !== module) return; // Importing a verifier never runs it: only `node <file>` does (see docs/testes-macos-docker.md).
const assert = require('node:assert/strict');
const { run, quartet } = require('./disposable.cjs');
const base = 'http://127.0.0.1:5099';
async function call(method, route, token, body, expected = 200) {
  const r = await fetch(base + route, { method, headers: { ...(token ? { Authorization: 'Bearer ' + token } : {}), ...(body ? { 'Content-Type': 'application/json' } : {}) }, body: body ? JSON.stringify(body) : undefined });
  const text = await r.text();
  assert.equal(r.status, expected, method + ' ' + route + ' → ' + r.status);
  return text ? JSON.parse(text) : null;
}
run({ label: 'matchdetails', ...quartet() }, async world => {
  const [author, peer, outsider, member] = await Promise.all(['autor', 'participante', 'alheio', 'membro'].map(r => world.login(r)));
  const session = await call('POST', '/MeepleBoard/session', author.token, { name: 'Sessão de detalhe', scheduledStartDate: new Date().toISOString(), playerIds: [peer.id, member.id] }, 201);
  await call('POST', `/MeepleBoard/session/${session.id}/invites/respond`, peer.token, { accept: true });
  await call('POST', `/MeepleBoard/session/${session.id}/invites/respond`, member.token, { accept: true });
  const game = (await call('GET', '/MeepleBoard/game/suggestions?query=Meeple%20Teste%20Competitivo', author.token))[0];
  await call('POST', '/MeepleBoard/matches', author.token, { gameId: game.id, gameName: game.name, gameSessionId: session.id, matchDate: new Date().toISOString(), isSoloGame: false,
    winnerId: author.id, playerIds: [author.id, peer.id], playerScores: [{ userId: author.id, score: 17 }, { userId: peer.id, score: 0 }], personalRating: 8 }, 201);
  const route = '/MeepleBoard/session/' + session.id;
  const listed = await call('GET', route, author.token);
  assert.equal(listed.matches.length, 1);
  for (const match of listed.matches) {
    const individual = await call('GET', '/MeepleBoard/matches/' + match.id, author.token);
    assert.equal(match.gameImageUrl, individual.gameImageUrl);
    assert.equal(match.gameName, individual.gameName); assert.notEqual(match.gameName, 'Jogo Desconhecido');
    assert.equal(match.winnerId, individual.winnerId); assert.equal(match.winnerName, individual.winnerName);
    for (const p of match.players) {
      const real = individual.players.find(x => x.userId === p.userId);
      assert.ok(real); assert.equal(p.userName, real.userName); assert.notEqual(p.userName, 'Jogador Desconhecido');
      assert.equal(p.score, real.score); assert.equal(p.isWinner, real.isWinner);
    }
    assert.ok(match.players.some(p => p.score === 0));
  }
  assert.deepEqual((await call('GET', route, author.token)).matches, listed.matches, 'the same after reopening');
  assert.deepEqual((await call('GET', route, peer.token)).matches.map(m => m.id), listed.matches.map(m => m.id));
  assert.equal((await call('GET', route, member.token)).matches.length, 0, 'a session member who did not play sees no match data');
  await call('GET', route, outsider.token, null, 403); await call('GET', route, null, null, 401);
  for (const match of listed.matches) {
    await call('GET', '/MeepleBoard/matches/' + match.id, member.token, null, 403);
    await call('GET', '/MeepleBoard/matches/' + match.id, outsider.token, null, 403);
  }
  console.log('PASS session game/winner/player names match individual API; named scores including zero survive reload; privacy filters retained');
}).catch(e => { console.error('FAIL', e.message); process.exit(1); });
