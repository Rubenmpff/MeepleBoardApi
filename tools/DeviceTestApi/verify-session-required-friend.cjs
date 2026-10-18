if (require.main !== module) return; // Importing a verifier never runs it: only `node <file>` does (see docs/testes-macos-docker.md).
const assert = require('node:assert/strict');
const { run, quartet } = require('./disposable.cjs');
const base = 'http://127.0.0.1:5099';
async function send(method, route, token, body, status = 200) {
  const response = await fetch(base + route, { method, headers: { ...(token ? { Authorization: 'Bearer ' + token } : {}), ...(body ? { 'Content-Type': 'application/json' } : {}) }, body: body ? JSON.stringify(body) : undefined });
  assert.equal(response.status, status, method + ' ' + route);
  const text = await response.text(); return text ? JSON.parse(text) : null;
}
run({ label: 'requiredfriend', ...quartet() }, async world => {
  const health = await send('GET', '/device-test/health');
  assert.equal(health.environment, 'DeviceTests'); assert.equal(health.database, 'MeepleBoard_DeviceTests');
  const logins = await Promise.all(['autor', 'participante', 'alheio', 'membro'].map(r => world.login(r)));
  const tokens = logins.map(l => l.token);
  const [author, peer, outsider, member] = logins.map(l => l.id);
  const original = await send('GET', '/MeepleBoard/session/mine', tokens[0]);
  const prefix = 'Rule rejected ' + Date.now();
  const scheduledStartDate = new Date(Date.now() + 2 * 86400000).toISOString();
  for (const playerIds of [undefined, [], [author], ['00000000-0000-0000-0000-000000000000', author]]) {
    const result = await send('POST', '/MeepleBoard/session', tokens[0], { name: prefix, scheduledStartDate, playerIds }, 400);
    assert.equal(result.message, 'Seleciona pelo menos um amigo para criar a sessão');
  }
  await send('POST', '/MeepleBoard/session', tokens[3], { name: prefix, scheduledStartDate, playerIds: [author, outsider] }, 400);
  console.log('PASS missing/empty/self-only/empty-GUID/nonfriend lists rejected with HTTP 400');
  const created = await send('POST', '/MeepleBoard/session', tokens[0], { name: 'Rule pending ' + Date.now(), scheduledStartDate, playerIds: [author, peer, peer] }, 201);
  const detailPath = '/MeepleBoard/session/' + created.id;
  let detail = await send('GET', detailPath, tokens[0]);
  assert.equal(detail.players.length, 2); assert.equal(detail.players.find(p => p.userId === peer).status, 0);
  assert.equal(new Date(detail.scheduledStartDate).toISOString(), scheduledStartDate);
  assert.equal(detail.responseDeadline, null);
  console.log('PASS accepted friendship creates pending session invitation; organizer and duplicates do not inflate selection');
  await send('POST', detailPath + '/invites/respond', tokens[1], { accept: false });
  detail = await send('GET', detailPath, tokens[0]);
  assert.equal(detail.players.find(p => p.userId === peer).status, 2);
  assert.equal(detail.status, 'Upcoming');
  await send('POST', detailPath + '/players', tokens[0], { userId: member });
  detail = await send('GET', detailPath, tokens[0]);
  assert.equal(detail.players.find(p => p.userId === member).status, 0);
  console.log('PASS refusal read back and another friend invited pending');
  const after = await send('GET', '/MeepleBoard/session/mine', tokens[0]);
  for (const existing of original) assert.deepEqual(after.find(s => s.id === existing.id), existing);
  console.log('PASS earlier sessions of these people unchanged');
  // Cancelling (organizer only) removes the session and leaves the others as they were.
  await send('POST', '/MeepleBoard/session/' + created.id + '/cancel', tokens[1], undefined, 403);
  await send('POST', '/MeepleBoard/session/' + created.id + '/cancel', tokens[0], undefined, 204);
  await send('GET', '/MeepleBoard/session/' + created.id, tokens[0], undefined, 404);
  const final = await send('GET', '/MeepleBoard/session/mine', tokens[0]);
  assert.ok(!final.some(s => s.id === created.id));
  for (const existing of original) assert.deepEqual(final.find(s => s.id === existing.id), existing);
  console.log('PASS cancellation (organizer only) removes the session; the others unchanged');
}).catch(error => { console.error(error.message); process.exitCode = 1; });
