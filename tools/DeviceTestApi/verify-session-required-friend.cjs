const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const base = 'http://127.0.0.1:5099';
async function send(method, route, token, body, status = 200) {
  const response = await fetch(base + route, { method, headers: { ...(token ? { Authorization: 'Bearer ' + token } : {}), ...(body ? { 'Content-Type': 'application/json' } : {}) }, body: body ? JSON.stringify(body) : undefined });
  assert.equal(response.status, status, method + ' ' + route);
  const text = await response.text(); return text ? JSON.parse(text) : null;
}
(async () => {
  const health = await send('GET', '/device-test/health');
  assert.equal(health.environment, 'DeviceTests'); assert.equal(health.database, 'MeepleBoard_DeviceTests');
  const dataPath = path.resolve(__dirname, '../../.device-tests');
  const accounts = JSON.parse(fs.readFileSync(path.join(dataPath, 'accounts.json'), 'utf8'));
  const tokens = [];
  for (const a of accounts) tokens.push((await send('POST', '/MeepleBoard/auth/login', null, { email: a.Email, password: a.Password, deviceInfo: 'MeepleBoard Mobile App' })).token);
  const [author, peer, outsider, member] = accounts.map(a => a.Id);
  if (process.argv.includes('--cancel')) {
    const fixture = JSON.parse(fs.readFileSync(path.join(dataPath, 'session-rule-fixture.json'), 'utf8'));
    await send('POST', '/MeepleBoard/session/' + fixture.sessionId + '/cancel', tokens[0], undefined, 204);
    await send('GET', '/MeepleBoard/session/' + fixture.sessionId, tokens[0], undefined, 404);
    const after = await send('GET', '/MeepleBoard/session/mine', tokens[0]);
    assert.ok(!after.some(s => s.id === fixture.sessionId));
    for (const existing of fixture.existing) assert.equal(JSON.stringify(after.find(s => s.id === existing.id)), existing.snapshot);
    console.log('PASS existing cancellation deletes only the disposable fixture; all earlier sessions unchanged');
    return;
  }
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
  console.log('PASS all pre-existing sessions unchanged, including iPhone session');
  fs.writeFileSync(path.join(dataPath, 'session-rule-fixture.json'), JSON.stringify({ sessionId: created.id, rejectedName: prefix, scheduledStartDate, author, peer, member, existing: original.map(s => ({ id: s.id, snapshot: JSON.stringify(s) })) }, null, 2));
  console.log('No invitations accepted; no campaigns or matches created.');
})().catch(error => { console.error(error.message); process.exitCode = 1; });
