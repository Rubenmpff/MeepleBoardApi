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
  const before = await send('GET', '/MeepleBoard/session/mine', tokens[0]);
  const scheduledStartDate = new Date(Date.now() + 3 * 86400000).toISOString();
  const accepted = await send('POST', '/MeepleBoard/session', tokens[0], { name: 'Convites SQL ' + Date.now(), scheduledStartDate, playerIds: [peer] }, 201);
  const url = '/MeepleBoard/session/' + accepted.id;
  await send('POST', url + '/players', tokens[0], { userId: member });
  await send('POST', url + '/players', tokens[0], { userId: member }, 409);
  await send('POST', url + '/players', tokens[0], { userId: outsider });
  await send('POST', url + '/players', tokens[1], { userId: author }, 409);
  const read = await send('GET', url, tokens[0]);
  assert.equal(read.players.length, 4);
  assert.ok(read.players.filter(p => !p.isOrganizer).every(p => p.status === 0));
  console.log('PASS accepted friends invited pending, duplicates rejected, organizer permission retained');
  const restricted = await send('POST', '/MeepleBoard/session', tokens[3], { name: 'Amizade SQL ' + Date.now(), scheduledStartDate, playerIds: [author] }, 201);
  const restrictedUrl = '/MeepleBoard/session/' + restricted.id;
  for (const nonfriend of [peer, outsider]) {
    const error = await send('POST', restrictedUrl + '/players', tokens[3], { userId: nonfriend }, 400);
    assert.equal(error.message, 'Só podes convidar amigos com amizade aceite.');
  }
  assert.equal((await send('GET', restrictedUrl, tokens[3])).players.length, 2);
  await send('POST', restrictedUrl + '/invites/respond', tokens[0], { accept: false });
  await send('POST', restrictedUrl + '/players', tokens[3], { userId: author }, 409);
  assert.equal((await send('GET', restrictedUrl, tokens[3])).players.find(p => p.userId === author).status, 2);
  console.log('PASS nonfriends rejected without insertion and declined invite cannot be resent');
  const after = await send('GET', '/MeepleBoard/session/mine', tokens[0]);
  for (const old of before) assert.deepEqual(after.find(s => s.id === old.id), old);
  fs.writeFileSync(path.join(dataPath, 'session-invite-friends-fixture.json'), JSON.stringify({ sessionId: accepted.id, restrictedId: restricted.id, author, peer, outsider, member }, null, 2));
  console.log('PASS all previous author sessions unchanged; no cancellation, campaigns or matches');
})().catch(error => { console.error(error.message); process.exitCode = 1; });
