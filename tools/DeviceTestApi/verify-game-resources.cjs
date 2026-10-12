// DeviceTests only: game resource catalogue against real SQL through the running test API (port 5099).
//   node tools/DeviceTestApi/verify-game-resources.cjs            seed the verified links, check permissions, filters, approval and reports
//   node tools/DeviceTestApi/verify-game-resources.cjs --recheck  after restarting the API: the catalogue is still there
// Seeding is idempotent (by URL). Test suggestions and reports it creates are removed or resolved before it ends.
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../..'), base = 'http://127.0.0.1:5099';
const fixture = JSON.parse(fs.readFileSync(path.join(__dirname, 'game-resources.fixture.json'), 'utf8'));
(async () => {
  const health = await (await fetch(base + '/device-test/health')).json();
  assert.deepEqual(health, { environment: 'DeviceTests', database: 'MeepleBoard_DeviceTests', externalDelivery: false });
  const accounts = JSON.parse(fs.readFileSync(path.join(root, '.device-tests/accounts.json'), 'utf8'));
  const login = async email => { const a = accounts.find(x => x.Email === email); const r = await fetch(base + '/MeepleBoard/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: a.Email, password: a.Password, deviceInfo: 'verify-game-resources' }) }); assert.equal(r.status, 200); return { id: a.Id, token: (await r.json()).token }; };
  const call = async (u, method, route, body) => { const r = await fetch(base + '/MeepleBoard' + route, { method, headers: { Authorization: `Bearer ${u.token}`, 'Content-Type': 'application/json' }, body: body && JSON.stringify(body) }); const t = await r.text(); return { status: r.status, data: t ? JSON.parse(t) : null }; };
  const admin = await login('admin@meepleboard.test'), user = await login('autor@meepleboard.test'), other = await login('participante@meepleboard.test');
  const pass = name => console.log('PASS ' + name);
  const game = (await call(user, 'GET', '/game/search?name=' + encodeURIComponent(fixture.game.name))).data;
  assert.equal(game.bggId, fixture.game.bggId);
  const list = async (who, language) => (await call(who, 'GET', `/game/${game.id}/resources?language=${language}`)).data;

  if (process.argv.includes('--recheck')) {
    const all = await list(user, 'all');
    for (const r of fixture.resources) assert.ok(all.items.some(i => i.url === new URL(r.url).toString() && i.title === r.title), 'missing after restart: ' + r.title);
    pass(`after restart: ${fixture.resources.length} verified resources are still public`);
    return;
  }

  assert.equal((await call(user, 'GET', '/game-resources/permissions')).data.canManage, false);
  assert.equal((await call(admin, 'GET', '/game-resources/permissions')).data.canManage, true);
  assert.equal((await call(user, 'GET', '/admin/game-resources')).status, 403);
  assert.equal((await call(user, 'POST', `/admin/game/${game.id}/resources`, fixture.resources[0])).status, 403);
  pass('only the administrator account can manage; the others get 403');

  const existing = (await call(admin, 'GET', `/admin/game-resources?gameId=${game.id}`)).data.map(r => r.resource.url);
  let added = 0;
  for (const r of fixture.resources) {
    if (existing.includes(new URL(r.url).toString())) continue;
    const created = await call(admin, 'POST', `/admin/game/${game.id}/resources`, r);
    assert.equal(created.status, 201, JSON.stringify(created.data)); added++;
  }
  const all = await list(user, 'all');
  assert.equal(all.total, fixture.resources.length, 'catalogue holds exactly the verified links');
  assert.equal(all.items[0].isOfficial, true, 'publisher rules first');
  pass(`seeded ${added} new verified links (${fixture.resources.length} in total)`);

  const pt = await list(user, 'pt'), en = await list(user, 'en');
  assert.ok(pt.items.every(i => i.language === null) && pt.items.length === 5, 'Portuguese shows only the language-independent models');
  assert.ok(pt.languages.some(l => l.code === 'en' && l.kinds.HowToPlay === 3 && l.kinds.Rules === 1), 'English alternatives are counted');
  assert.equal(en.items.length, fixture.resources.length);
  assert.equal((await call(user, 'GET', `/game/${game.id}/resources?language=xx1`)).status, 400);
  pass('language filter: pt = models only plus English alternatives; en and all = everything');

  const stamp = Date.now();
  const suggestion = await call(other, 'POST', `/game/${game.id}/resources/suggestions`, { kind: 'Playthrough', printCategory: null, language: 'pt', title: 'Sugestão de teste', author: null, platform: 'Teste', url: `https://example.org/meepleboard-device-test/${stamp}`, compatibility: null, isOfficial: false, isFree: null });
  assert.equal(suggestion.status, 201); assert.equal(suggestion.data.status, 'Pending');
  assert.ok(!(await list(user, 'all')).items.some(i => i.id === suggestion.data.id), 'pending suggestion is public');
  assert.equal((await call(other, 'POST', `/admin/game-resources/${suggestion.data.id}/approve`)).status, 403);
  assert.ok((await call(admin, 'GET', '/admin/game-resources?status=Pending')).data.some(r => r.resource.id === suggestion.data.id && r.suggestedBy === 'Teste-participante'));
  assert.equal((await call(admin, 'POST', `/admin/game-resources/${suggestion.data.id}/approve`)).status, 200);
  assert.ok((await list(user, 'pt')).items.some(i => i.id === suggestion.data.id), 'approved suggestion is public');
  assert.equal((await call(admin, 'DELETE', `/admin/game-resources/${suggestion.data.id}`)).status, 204);
  assert.ok(!(await list(user, 'all')).items.some(i => i.id === suggestion.data.id));
  const rejected = await call(other, 'POST', `/game/${game.id}/resources/suggestions`, { kind: 'Unboxing', printCategory: null, language: 'en', title: 'Rejeição de teste', author: null, platform: 'Teste', url: `https://example.org/meepleboard-device-test/r${stamp}`, compatibility: null, isOfficial: false, isFree: null });
  assert.equal((await call(admin, 'POST', `/admin/game-resources/${rejected.data.id}/reject`, { note: 'Teste de rejeição' })).data.status, 'Rejected');
  assert.ok(!(await list(user, 'all')).items.some(i => i.id === rejected.data.id), 'rejected suggestion is public');
  assert.equal((await call(admin, 'DELETE', `/admin/game-resources/${rejected.data.id}`)).status, 204);
  pass('suggestions stay pending until approved; rejected ones stay hidden (test suggestions removed)');

  const target = all.items.find(i => i.kind === 'HowToPlay');
  assert.equal((await call(other, 'POST', `/game-resources/${target.id}/reports`, { reason: 'WrongLanguage', comment: 'Teste' })).status, 201);
  assert.equal((await call(other, 'POST', `/game-resources/${target.id}/reports`, { reason: 'BrokenLink', comment: null })).status, 409);
  const report = (await call(admin, 'GET', '/admin/game-resources/reports')).data.find(r => r.resourceId === target.id && r.comment === 'Teste');
  assert.ok(report, 'report visible to the administrator');
  assert.equal((await call(admin, 'POST', `/admin/game-resources/reports/${report.id}/resolve`)).status, 204);
  assert.ok(!(await call(admin, 'GET', '/admin/game-resources/reports')).data.some(r => r.id === report.id));
  pass('a report reaches the administrator once per user and closes when resolved');
})().catch(error => { console.error('FAIL', error.message); process.exit(1); });
