// DeviceTests only: session writes against real SQL — invitations, acceptance, a match in the session, scores (zero
// included), the diary (a saved rating is closed) and the creator's update. Fresh people for this run (disposable.cjs:
// autor friends with participante, alheio and membro), removed at the end, also on failure. Never the iPhone accounts.
//   node tools/DeviceTestApi/verify-session-writes.cjs
if (require.main !== module) return; // Importing a verifier never runs it: only `node <file>` does (see docs/testes-macos-docker.md).
const assert = require('node:assert/strict');
const { run, quartet } = require('./disposable.cjs');
const base = 'http://127.0.0.1:5099';
async function send(method, route, token, body, expected = 200) {
  const r = await fetch(base + route, { method, headers: { ...(token ? { Authorization: 'Bearer ' + token } : {}), ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}) }, body: body !== undefined ? JSON.stringify(body) : undefined });
  const text = await r.text();
  assert.equal(r.status, expected, method + ' ' + route + ' HTTP ' + r.status + ' (response length ' + text.length + ')');
  return text ? JSON.parse(text) : null;
}
run({ label: 'sessionwrites', ...quartet() }, async world => {
  const logins = await Promise.all(['autor', 'participante', 'alheio', 'membro'].map(r => world.login(r)));
  const tokens = logins.map(l => l.token);
  const [author, peer, outsider, member] = logins.map(l => l.id);
  const session = await send('POST', '/MeepleBoard/session', tokens[0], { name: 'Sessão SQL escrita ' + Date.now(), location: 'Mesa de testes', scheduledStartDate: new Date().toISOString(), playerIds: [peer] }, 201);
  let reloaded = await send('GET', '/MeepleBoard/session/' + session.id, tokens[0]);
  assert.equal(reloaded.location, 'Mesa de testes'); assert.ok(reloaded.players.some(p => p.userId === peer && (p.status === 0 || p.status === 'Pending')));
  console.log('PASS session created and pending invitation read back');
  await send('POST', '/MeepleBoard/session/' + session.id + '/players', tokens[1], { userId: member }, 409);
  await send('POST', '/MeepleBoard/session/' + session.id + '/players', tokens[0], { userId: member });
  await send('GET', '/MeepleBoard/session/' + session.id, tokens[2], undefined, 403);
  await send('GET', '/MeepleBoard/session/' + session.id, undefined, undefined, 401);
  await send('POST', '/MeepleBoard/session/' + session.id + '/invites/respond', tokens[1], { accept: true });
  reloaded = await send('GET', '/MeepleBoard/session/' + session.id, tokens[0]);
  assert.equal(reloaded.status, 'Active'); assert.ok(reloaded.players.some(p => p.userId === peer && (p.status === 1 || p.status === 'Accepted')));
  console.log('PASS organiser-only invitation, acceptance and active state persisted');
  const suggestions = await send('GET', '/MeepleBoard/game/suggestions?query=Meeple%20Teste%20Competitivo', tokens[0]);
  const game = suggestions[0];
  const payload = { gameId: game.id, gameName: game.name, gameSessionId: session.id, matchDate: new Date().toISOString(), isSoloGame: false, winnerId: author, playerIds: [author, peer], playerScores: [{ userId: author, score: 17 }, { userId: peer, score: 0 }], scoreSummary: 'Resultado SQL: 17–0', personalRating: 8, notes: 'SESSION_PRIVATE_AUTHOR', tags: 'teste' };
  // Refusals: a member who has not accepted, and someone outside the session.
  await send('POST', '/MeepleBoard/matches', tokens[3], { ...payload, playerIds: [member, peer], playerScores: [{ userId: member, score: 17 }, { userId: peer, score: 0 }], winnerId: member }, 403);
  await send('POST', '/MeepleBoard/matches', tokens[2], payload, 403);
  const match = await send('POST', '/MeepleBoard/matches', tokens[0], payload, 201);
  const detailPath = '/MeepleBoard/matches/' + match.id;
  let detail = await send('GET', detailPath, tokens[0]);
  assert.equal(detail.winnerId, author); assert.equal(detail.players.find(p => p.userId === peer).score, 0); assert.equal(detail.players.find(p => p.userId === author).score, 17);
  assert.ok(!JSON.stringify(detail).includes('SESSION_PRIVATE_AUTHOR'));
  reloaded = await send('GET', '/MeepleBoard/session/' + session.id, tokens[0]); assert.ok(reloaded.matches.some(m => m.id === match.id));
  const journalPath = '/MeepleBoard/campaigns/matches/' + match.id + '/journal';
  await send('PUT', journalPath, tokens[1], { personalRating: 0, notes: 'SESSION_PRIVATE_PEER', tags: 'partilhado' });
  // The rating given at registration (8) is closed: a different value is refused; notes and tags still change.
  await send('PUT', journalPath, tokens[0], { personalRating: 9, notes: 'x' }, 409);
  await send('PUT', journalPath, tokens[0], { personalRating: 8, notes: 'SESSION_PRIVATE_AUTHOR_EDITED', tags: 'editado' });
  const journal = await send('GET', journalPath, tokens[0]);
  assert.equal(journal.find(e => e.userId === author).personalRating, 8); assert.equal(journal.find(e => e.userId === author).notes, 'SESSION_PRIVATE_AUTHOR_EDITED');
  assert.ok(!JSON.stringify(journal).includes('SESSION_PRIVATE_PEER'));
  const peerJournal = await send('GET', journalPath, tokens[1]); assert.equal(peerJournal.find(e => e.userId === peer).notes, 'SESSION_PRIVATE_PEER');
  assert.equal(peerJournal.find(e => e.userId === peer).personalRating, 0, 'zero is a rating');
  await send('PUT', journalPath, tokens[1], { personalRating: 3 }, 409);
  console.log('PASS session match scores, winner, private journal edits and peer contribution read back; a saved rating (8, and 0) cannot change (409)');
  const update = { id: match.id, gameId: game.id, gameName: game.name, matchDate: detail.matchDate, isSoloGame: false, winnerId: author, players: detail.players, scoreSummary: 'Atualizado sem 404' };
  await send('PUT', detailPath, tokens[1], update, 403); await send('PUT', detailPath, tokens[0], update, 204);
  detail = await send('GET', detailPath, tokens[1]); assert.equal(detail.scoreSummary, 'Atualizado sem 404'); assert.equal(detail.players.find(p => p.userId === peer).score, 0);
  await send('POST', '/MeepleBoard/session/' + session.id + '/invites/respond', tokens[3], { accept: false });
  reloaded = await send('GET', '/MeepleBoard/session/' + session.id, tokens[0]); assert.ok(reloaded.players.some(p => p.userId === member && (p.status === 2 || p.status === 'Declined')));
  console.log('PASS creator update 204, persisted score zero and declined invitation');
  const closure = await send('POST', '/MeepleBoard/matches', tokens[0], { ...payload, notes: 'CLOSURE_AUTHOR' }, 201);
  const closureJournal = '/MeepleBoard/campaigns/matches/' + closure.id + '/journal';
  await send('PUT', closureJournal, tokens[1], { personalRating: 7, notes: 'CLOSURE_PEER' });
  const closed = await send('GET', '/MeepleBoard/matches/' + closure.id, tokens[0]);
  assert.equal(closed.journalStatus, 'Closed');
  await send('PUT', closureJournal, tokens[0], { personalRating: 8, notes: 'CLOSURE_AUTHOR_EDITED' });
  const entries = await send('GET', closureJournal, tokens[0]);
  assert.equal(entries.find(e => e.userId === author).notes, 'CLOSURE_AUTHOR_EDITED');
  assert.ok(!JSON.stringify(entries).includes('CLOSURE_PEER'));
  console.log('PASS diary closure and subsequent own edit (same rating) without graph tracking errors');
  // What verify-session-match-details checked on this data: the session's match list matches each match.
  const listed = (await send('GET', '/MeepleBoard/session/' + session.id, tokens[0])).matches;
  for (const m of listed) {
    const one = await send('GET', '/MeepleBoard/matches/' + m.id, tokens[0]);
    assert.equal(m.gameName, one.gameName); assert.equal(m.winnerId, one.winnerId);
    for (const p of m.players) assert.equal(p.score, one.players.find(x => x.userId === p.userId).score);
  }
  assert.equal((await send('GET', '/MeepleBoard/session/' + session.id, tokens[3])).matches.length, 0, 'a session member who did not play sees no match data');
  console.log('PASS the session lists each match as the match itself; a member who did not play sees none');
}).catch(e => { console.error('FAIL', e.message); process.exit(1); });
