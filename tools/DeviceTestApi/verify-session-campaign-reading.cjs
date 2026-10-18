// DeviceTests only: reading sessions and campaigns — each person reads what they belong to, an outsider gets 403, a
// visitor 401, and containers never carry private match notes. Its own session, campaign and match between fresh
// people (disposable.cjs), removed at the end. Never the iPhone accounts.
//   node tools/DeviceTestApi/verify-session-campaign-reading.cjs
if (require.main !== module) return; // Importing a verifier never runs it: only `node <file>` does (see docs/testes-macos-docker.md).
const assert = require('node:assert/strict');
const { run, quartet } = require('./disposable.cjs');
const base = 'http://127.0.0.1:5099';
async function call(path, token, options = {}) {
  const r = await fetch(base + path, { ...options, headers: { ...(token ? { Authorization: 'Bearer ' + token } : {}), ...(options.body ? { 'Content-Type': 'application/json' } : {}) } });
  return { status: r.status, body: await r.text() };
}
run({ label: 'reading', ...quartet() }, async world => {
  const roles = ['autor', 'participante', 'alheio', 'membro'];
  const people = await Promise.all(roles.map(r => world.login(r)));
  const [author, peer, outsider, member] = people;
  const post = async (u, route, body, status) => { const r = await call('/MeepleBoard' + route, u.token, { method: 'POST', body: JSON.stringify(body) }); assert.equal(r.status, status, route + ' → ' + r.status + ' ' + r.body.slice(0, 200)); return r.body ? JSON.parse(r.body) : null; };
  // The author's session (participante and membro accepted) with a match whose note is private; a campaign with membro.
  const session = await post(author, '/session', { name: 'Leitura de sessão', scheduledStartDate: new Date().toISOString(), playerIds: [peer.id, member.id] }, 201);
  for (const u of [peer, member]) await post(u, `/session/${session.id}/invites/respond`, { accept: true }, 200);
  const game = JSON.parse((await call('/MeepleBoard/game/search?name=Meeple%20Teste%20Competitivo', author.token)).body);
  await post(author, '/matches', { gameId: game.id, gameName: game.name, gameSessionId: session.id, matchDate: new Date().toISOString(), isSoloGame: false, winnerId: author.id,
    playerIds: [author.id, peer.id], personalRating: 7, notes: 'PRIVATE_SQL_NOTE' }, 201);
  const campaign = await post(author, '/campaigns', { name: 'Campanha de leitura', gameId: game.id, notes: 'Notas partilhadas' }, 201);
  await post(author, `/campaigns/${campaign.id}/members/invite`, { userId: member.id }, 204);
  await post(member, `/campaigns/${campaign.id}/members/respond`, true, 204);
  const ids = { session: session.id, campaigns: campaign.id };
  for (const [index, u] of people.entries()) {
    const claims = JSON.parse(Buffer.from(u.token.split('.')[1], 'base64url').toString());
    assert.equal(claims.sub, u.id);
    console.log('PASS login ' + roles[index]);
    for (const path of ['/MeepleBoard/matches/last', '/MeepleBoard/matches/pending-journal']) {
      const response = await call(path, u.token);
      assert.ok(response.status === 200 || (path.endsWith('/last') && response.status === 404) || (path.endsWith('/pending-journal') && response.status === 204), 'Home data: ' + response.status);
    }
    for (const resource of ['session', 'campaigns']) {
      const list = await call('/MeepleBoard/' + resource + '/mine', u.token);
      assert.equal(list.status, 200);
      const rows = JSON.parse(list.body);
      const belongs = u === author || u === member || (resource === 'session' && u === peer);
      assert.equal(rows.some(r => r.id === ids[resource]), belongs, `${roles[index]} ${resource} list`);
      const detail = await call('/MeepleBoard/' + resource + '/' + ids[resource], u.token);
      assert.equal(detail.status, belongs ? 200 : 403, `${roles[index]} ${resource} detail`);
      assert.ok(!detail.body.includes('PRIVATE_SQL_'), 'No private match notes in containers');
      assert.equal((await call('/MeepleBoard/' + resource + '/mine')).status, 401);
      assert.equal((await call('/MeepleBoard/' + resource + '/' + ids[resource])).status, 401);
    }
    console.log(`PASS ${roles[index]}: sessions and campaigns readable only when a member; outsider 403; visitor 401; no private notes`);
  }
}).catch(e => { console.error('FAIL', e.message); process.exit(1); });
