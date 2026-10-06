const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const base = 'http://127.0.0.1:5099';
async function send(method, route, token, body, expected = 200) {
  const r = await fetch(base + route, { method, headers: { ...(token ? { Authorization: 'Bearer ' + token } : {}), ...(body ? { 'Content-Type': 'application/json' } : {}) }, body: body ? JSON.stringify(body) : undefined });
  const text = await r.text();
  assert.equal(r.status, expected, route + ' HTTP ' + r.status);
  return text ? JSON.parse(text) : null;
}
(async () => {
  const health = await send('GET', '/device-test/health');
  assert.equal(health.environment, 'DeviceTests'); assert.equal(health.database, 'MeepleBoard_DeviceTests');
  const dataDir = path.resolve(__dirname, '../../.device-tests');
  const accounts = JSON.parse(fs.readFileSync(path.join(dataDir, 'accounts.json'), 'utf8'));
  const author = accounts.find(a => a.Email === 'autor@meepleboard.test');
  const peer = accounts.find(a => a.Email === 'participante@meepleboard.test');
  const token = (await send('POST', '/MeepleBoard/auth/login', null, { email: author.Email, password: author.Password, deviceInfo: 'MeepleBoard Mobile App' })).token;
  const friends = await send('GET', '/MeepleBoard/friendships', token);
  assert.deepEqual(friends.map(f => f.userName).sort(), ['Teste-alheio', 'Teste-membro', 'Teste-participante']);
  assert.equal(friends.find(f => f.userName === 'Teste-participante').id, peer.Id);
  console.log('PASS author friends: Teste-alheio, Teste-membro, Teste-participante');
  const existingId = process.argv[2];
  if (existingId) {
  const old = await send('GET', '/MeepleBoard/session/' + existingId, token);
  assert.equal(old.name, 'Sessão iPhone 0-');
  assert.equal(old.scheduledStartDate, '2027-10-06T15:30:00Z');
  assert.equal(old.responseDeadline, null);
  const local = new Intl.DateTimeFormat('pt-PT', { timeZone: 'Europe/Lisbon', hour: '2-digit', minute: '2-digit' });
  assert.equal(local.format(new Date(old.scheduledStartDate)), '16:30');
  console.log('PASS existing iPhone session read: 15:30 UTC = 16:30 Lisbon, no custom deadline');
  }
  const local = new Intl.DateTimeFormat('pt-PT', { timeZone: 'Europe/Lisbon', hour: '2-digit', minute: '2-digit' });
  const fixtures = [];
  for (const custom of [false, true]) {
    const date = new Date(Date.now() + 3 * 86400000); date.setUTCHours(15, 30, 0, 0);
    const request = { name: 'Sessão fuso SQL ' + Date.now(), scheduledStartDate: date.toISOString(), playerIds: [peer.Id] };
    if (custom) request.responseDeadline = new Date(date.getTime() - 3600000).toISOString();
    const created = await send('POST', '/MeepleBoard/session', token, request, 201);
    const detail = await send('GET', '/MeepleBoard/session/' + created.id, token);
    const listed = (await send('GET', '/MeepleBoard/session/mine', token)).find(s => s.id === created.id);
    for (const value of [created, detail, listed]) {
      assert.equal(value.id, created.id); assert.equal(value.name, request.name);
      assert.equal(new Date(value.scheduledStartDate).toISOString(), request.scheduledStartDate);
      assert.ok(value.scheduledStartDate.endsWith('Z'));
      assert.equal(value.responseDeadline == null, !custom);
      assert.equal(new Date(value.effectiveDeadline).toISOString(), request.responseDeadline || request.scheduledStartDate);
    }
    assert.equal(detail.players.find(p => p.userId === peer.Id).userName, 'Teste-participante');
    assert.equal(detail.players.find(p => p.userId === peer.Id).status, 0);
    assert.ok(detail.players.every(p => p.invitedAt.endsWith('Z')));
    assert.equal(local.format(new Date(detail.scheduledStartDate)), local.format(new Date(request.scheduledStartDate)));
    fixtures.push({ id: created.id, request });
    console.log('PASS creation -> same ID detail -> list, pending invite and ' + (custom ? 'custom' : 'automatic') + ' deadline');
  }
  fs.writeFileSync(path.join(dataDir, 'session-date-fixture.json'), JSON.stringify(fixtures, null, 2));
  assert.equal(local.format(new Date('2027-10-06T15:30:00Z')), '16:30');
  assert.equal(local.format(new Date('2027-12-06T15:30:00Z')), '15:30');
  console.log('PASS Lisbon summer and winter offsets');
  console.log('No invitations accepted; no campaigns or matches created.');
})().catch(e => { console.error(e.message); process.exitCode = 1; });
