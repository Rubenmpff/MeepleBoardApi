// DeviceTests only: people for validating the community protections — never the iPhone walkthrough accounts.
//   node tools/DeviceTestApi/community-validation.cjs setup              people for the iPhone (nothing done yet)
//   node tools/DeviceTestApi/community-validation.cjs captures           another set with the actions already done, for the preview
//   node tools/DeviceTestApi/community-validation.cjs teardown [setup|captures|all]
// Credentials go only to .device-tests/community-validation-<set>.json (owner only); this prints roles and names.
// The people are kept (never swept by verifiers) until teardown removes them with everything they made.
if (require.main !== module) return; // Importing it never runs it.
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const { keep, remove } = require('./disposable.cjs');
const root = path.resolve(__dirname, '../..'), base = 'http://127.0.0.1:5099';
const file = set => path.join(root, '.device-tests', `community-validation-${set}.json`);
const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN5kAAAAASUVORK5CYII=', 'base64');

async function login(p) {
  const r = await fetch(base + '/MeepleBoard/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: p.email, password: p.password, deviceInfo: 'community-validation' }) });
  assert.equal(r.status, 200, 'login'); return { id: p.id, token: (await r.json()).token };
}
async function call(u, method, route, body, expected) {
  const headers = { Authorization: 'Bearer ' + u.token };
  if (body !== undefined && !(body instanceof FormData)) headers['Content-Type'] = 'application/json';
  const r = await fetch(base + '/MeepleBoard' + route, { method, headers, body: body instanceof FormData ? body : body !== undefined ? JSON.stringify(body) : undefined });
  const t = await r.text(); if (expected) assert.equal(r.status, expected, `${method} ${route} → ${r.status} ${t.slice(0, 200)}`);
  return t && r.headers.get('content-type')?.includes('json') ? JSON.parse(t) : null;
}

// Ana is the person on the iPhone. Bruno (friend) will be blocked; Carla (friend) organizes a session that invites Ana and
// Bruno (Bruno accepted) and played a match with Ana, with a photo in her diary; Duarte (friend) is reported by Ana so he
// appears in moderation; Elisa only exists to be suspended in the capture set; the administrator moderates.
async function make(set) {
  const made = await keep({ label: 'valid' + set.slice(0, 3), people: ['ana', 'bruno', 'carla', 'duarte', 'elisa', 'admin'].map(role => ({ role, admin: role === 'admin' })),
    friends: [['ana', 'bruno'], ['ana', 'carla'], ['carla', 'bruno'], ['ana', 'duarte']] });
  const people = made.people;
  fs.writeFileSync(file(set), JSON.stringify(Object.entries(people).map(([role, p]) => ({ Role: role, Email: p.email, Password: p.password, Id: p.id, UserName: p.userName })), null, 1), { mode: 0o600 });
  const [ana, bruno, carla, admin] = await Promise.all(['ana', 'bruno', 'carla', 'admin'].map(r => login(people[r])));
  const tomorrow = new Date(Date.now() + 86400e3); tomorrow.setUTCHours(19, 0, 0, 0);
  const session = await call(carla, 'POST', '/session', { name: 'Noite da validação', location: 'Casa da Carla', scheduledStartDate: tomorrow.toISOString(), playerIds: [ana.id, bruno.id] }, 201);
  await call(bruno, 'POST', `/session/${session.id}/invites/respond`, { accept: true }, 200);
  const game = await call(carla, 'GET', '/game/search?name=Wingspan', undefined, 200);
  const match = await call(carla, 'POST', '/matches', { gameId: game.id, gameName: game.name, matchDate: new Date(Date.now() - 86400e3).toISOString(), isSoloGame: false,
    gameMode: 'COMPETITIVE', result: 'Undefined', resultPlayerIds: [], playerIds: [carla.id, ana.id], personalRating: 8 }, 201);
  const form = new FormData(); form.append('file', new Blob([png], { type: 'image/png' }), 'mesa.png');
  const entry = await call(carla, 'POST', `/campaigns/matches/${match.id}/journal/photos`, form, 200);
  await call(ana, 'POST', `/community/people/${people.duarte.id}/reports`, { reason: 'Harassment', detail: 'Mensagens insistentes depois de lhe dizer que não' }, 204);
  const ids = { session: session.id, match: match.id, photo: entry.photoUrls[0] };
  if (set === 'captures') {
    await call(ana, 'POST', `/community/blocks/${people.bruno.id}`, undefined, 204);                       // Bruno accepted Carla's session
    await call(ana, 'POST', `${entry.photoUrls[0].replace('/MeepleBoard', '')}/reports`, { reason: 'PersonalData', detail: 'Aparece a morada' }, 204);
    await call(admin, 'POST', `/admin/moderation/people/${people.elisa.id}/suspend`, { days: 7, reason: 'Assédio confirmado' }, 204);
  }
  const all = JSON.parse(fs.readFileSync(file(set), 'utf8'));
  fs.writeFileSync(file(set), JSON.stringify(all.map(a => ({ ...a, Ids: ids })), null, 1), { mode: 0o600 });
  console.log(`${set}: ${Object.entries(people).map(([role, p]) => `${role} = ${p.userName}`).join(', ')}`);
  console.log(`credentials in .device-tests/community-validation-${set}.json (not printed)`);
  return ids;
}

(async () => {
  const health = await (await fetch(base + '/device-test/health')).json();
  assert.equal(health.database, 'MeepleBoard_DeviceTests');
  const [mode, which = 'all'] = process.argv.slice(2);
  if (mode === 'setup' || mode === 'captures') {
    if (fs.existsSync(file(mode))) throw new Error(`a ${mode} set already exists; run teardown ${mode} first`);
    await make(mode); return;
  }
  if (mode === 'teardown') {
    for (const set of which === 'all' ? ['setup', 'captures'] : [which]) {
      if (!fs.existsSync(file(set))) continue;
      const ids = JSON.parse(fs.readFileSync(file(set), 'utf8')).map(a => a.Id);
      const r = await remove(ids); fs.unlinkSync(file(set));
      console.log(`${set}: removed ${r.removed} people and everything they made`);
    }
    return;
  }
  console.log('usage: community-validation.cjs setup | captures | teardown [setup|captures|all]'); process.exit(2);
})().catch(e => { console.error('FAIL', e.message); process.exit(1); });
