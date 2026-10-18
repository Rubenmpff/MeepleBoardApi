if (require.main !== module) return; // Importing a verifier never runs it: only `node <file>` does (see docs/testes-macos-docker.md).
const { run, quartet } = require('./disposable.cjs');
const assert = require('node:assert/strict');
const base = 'http://127.0.0.1:5099';
async function send(method, route, token, body, expected = 200) {
  const r = await fetch(base + route, { method, headers: { ...(token ? { Authorization: 'Bearer ' + token } : {}), ...(body ? { 'Content-Type': 'application/json' } : {}) }, body: body ? JSON.stringify(body) : undefined });
  const text = await r.text();
  assert.equal(r.status, expected, route + ' HTTP ' + r.status);
  return text ? JSON.parse(text) : null;
}
run({ label: 'sessiondates', ...quartet() }, async world => {
  const health = await send('GET', '/device-test/health');
  assert.equal(health.environment, 'DeviceTests'); assert.equal(health.database, 'MeepleBoard_DeviceTests');
  const author = world.people.autor, peer = world.people.participante;
  const token = (await world.login('autor')).token;
  const friends = await send('GET', '/MeepleBoard/friendships', token);
  assert.deepEqual(friends.map(f => f.userName).sort(), ['alheio', 'membro', 'participante'].map(r => world.people[r].userName).sort());
  assert.equal(friends.find(f => f.userName === peer.userName).id, peer.Id);
  console.log('PASS author friends: alheio, membro, participante');
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
    assert.equal(detail.players.find(p => p.userId === peer.Id).userName, peer.userName);
    assert.equal(detail.players.find(p => p.userId === peer.Id).status, 0);
    assert.ok(detail.players.every(p => p.invitedAt.endsWith('Z')));
    assert.equal(local.format(new Date(detail.scheduledStartDate)), local.format(new Date(request.scheduledStartDate)));
    fixtures.push({ id: created.id, request });
    console.log('PASS creation -> same ID detail -> list, pending invite and ' + (custom ? 'custom' : 'automatic') + ' deadline');
  }
  assert.equal(local.format(new Date('2027-10-06T15:30:00Z')), '16:30');
  assert.equal(local.format(new Date('2027-12-06T15:30:00Z')), '15:30');
  console.log('PASS Lisbon summer and winter offsets');
  console.log('No invitations accepted; no campaigns or matches created.');
}).catch(e => { console.error(e.message); process.exitCode = 1; });
