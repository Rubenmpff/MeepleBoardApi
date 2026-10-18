// DeviceTests only: game resource catalogue against real SQL through the running test API (port 5099).
//   node tools/DeviceTestApi/verify-game-resources.cjs            seed the verified links, check permissions, filters, approval and reports
//   node tools/DeviceTestApi/verify-game-resources.cjs --recheck  after restarting the API: the catalogue is still there
// Seeding is idempotent (by URL): the curated Wingspan catalogue is product content and stays. The people who act are
// fresh (disposable.cjs: an administrator and two users), removed at the end with their suggestions and reports.
// Never the iPhone accounts.
if (require.main !== module) return; // Importing a verifier never runs it: only `node <file>` does (see docs/testes-macos-docker.md).
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const { run } = require('./disposable.cjs');
const base = 'http://127.0.0.1:5099';
const fixture = JSON.parse(fs.readFileSync(path.join(__dirname, 'game-resources.fixture.json'), 'utf8'));
run({ label: 'resources', people: [{ role: 'admin', admin: true }, { role: 'autor' }, { role: 'participante' }] }, async world => {
  const call = async (u, method, route, body) => { const r = await fetch(base + '/MeepleBoard' + route, { method, headers: { Authorization: `Bearer ${u.token}`, 'Content-Type': 'application/json' }, body: body && JSON.stringify(body) }); const t = await r.text(); return { status: r.status, data: t ? JSON.parse(t) : null }; };
  const admin = await world.login('admin'), user = await world.login('autor'), other = await world.login('participante');
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

  const existing = new Map((await call(admin, 'GET', `/admin/game-resources?gameId=${game.id}`)).data.map(r => [r.resource.url, r.resource]));
  const fields = Object.keys(fixture.resources[0]);
  let added = 0, updated = 0;
  for (const r of fixture.resources) {
    const current = existing.get(new URL(r.url).toString());
    if (!current) {
      const created = await call(admin, 'POST', `/admin/game/${game.id}/resources`, r);
      assert.equal(created.status, 201, JSON.stringify(created.data)); added++;
    } else if (fields.some(k => k !== 'url' && (current[k] ?? null) !== (r[k] ?? null))) {
      const changed = await call(admin, 'PUT', `/admin/game-resources/${current.id}`, r);
      assert.equal(changed.status, 200, JSON.stringify(changed.data)); updated++;
    }
  }
  const all = await list(user, 'all');
  assert.equal(all.total, fixture.resources.length, 'catalogue holds exactly the verified links');
  assert.equal(all.items[0].isOfficial, true, 'publisher rules first');
  const groups = all.items.map(i => i.expansion ?? '');
  assert.deepEqual(groups, [...groups].sort((a, b) => (a !== '') - (b !== '') || a.localeCompare(b)), 'base game first, then expansions');
  assert.ok(all.items.filter(i => i.thumbnailUrl).length === 8 && all.items.every(i => !i.thumbnailUrl || i.thumbnailUrl.startsWith('https://')), 'thumbnails from the original sites');
  pass(`seeded ${added} new and updated ${updated} verified links (${fixture.resources.length} in total); base game first, 8 thumbnails`);

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
  assert.ok((await call(admin, 'GET', '/admin/game-resources?status=Pending')).data.some(r => r.resource.id === suggestion.data.id && r.suggestedBy === world.people.participante.userName));
  assert.equal((await call(admin, 'POST', `/admin/game-resources/${suggestion.data.id}/approve`)).status, 200);
  assert.ok((await list(user, 'pt')).items.some(i => i.id === suggestion.data.id), 'approved suggestion is public');
  assert.equal((await call(admin, 'DELETE', `/admin/game-resources/${suggestion.data.id}`)).status, 204);
  assert.ok(!(await list(user, 'all')).items.some(i => i.id === suggestion.data.id));
  const rejected = await call(other, 'POST', `/game/${game.id}/resources/suggestions`, { kind: 'Unboxing', printCategory: null, language: 'en', title: 'Rejeição de teste', author: null, platform: 'Teste', url: `https://example.org/meepleboard-device-test/r${stamp}`, compatibility: null, isOfficial: false, isFree: null });
  assert.equal((await call(admin, 'POST', `/admin/game-resources/${rejected.data.id}/reject`, { note: 'Teste de rejeição' })).data.status, 'Rejected');
  assert.ok(!(await list(user, 'all')).items.some(i => i.id === rejected.data.id), 'rejected suggestion is public');
  assert.equal((await call(admin, 'DELETE', `/admin/game-resources/${rejected.data.id}`)).status, 204);
  pass('suggestions stay pending until approved; rejected ones stay hidden (test suggestions removed)');

  // Every other game: no invented content, the same empty catalogue and the same suggestion path.
  const games = (await call(user, 'GET', '/game?pageSize=100')).data.data;
  const others = games.filter(g => g.id !== game.id);
  assert.ok(others.length > 0, 'expected games besides Wingspan');
  for (const g of others) {
    const r = await call(user, 'GET', `/game/${g.id}/resources?language=all`);
    assert.equal(r.status, 200, g.name);
    assert.equal(r.data.total, 0, 'only Wingspan has a curated catalogue: ' + g.name);
  }
  const empty = others[0];
  const pending = await call(other, 'POST', `/game/${empty.id}/resources/suggestions`, { kind: 'HowToPlay', printCategory: null, language: 'pt', title: 'Sugestão de teste', author: null, platform: 'Teste', url: `https://example.org/meepleboard-device-test/e${stamp}`, compatibility: null, isOfficial: false, isFree: null });
  assert.equal(pending.status, 201); assert.equal(pending.data.status, 'Pending');
  assert.equal((await call(user, 'GET', `/game/${empty.id}/resources?language=all`)).data.total, 0, 'pending suggestion is public');
  assert.equal((await call(admin, 'DELETE', `/admin/game-resources/${pending.data.id}`)).status, 204);
  pass(`${others.length} other games: empty catalogue, suggestions accepted as pending (test suggestion removed)`);

  const target = all.items.find(i => i.kind === 'HowToPlay');
  assert.equal((await call(other, 'POST', `/game-resources/${target.id}/reports`, { reason: 'WrongLanguage', comment: 'Teste' })).status, 201);
  assert.equal((await call(other, 'POST', `/game-resources/${target.id}/reports`, { reason: 'BrokenLink', comment: null })).status, 409);
  const report = (await call(admin, 'GET', '/admin/game-resources/reports')).data.find(r => r.resourceId === target.id && r.comment === 'Teste');
  assert.ok(report, 'report visible to the administrator');
  assert.equal((await call(admin, 'POST', `/admin/game-resources/reports/${report.id}/resolve`)).status, 204);
  assert.ok(!(await call(admin, 'GET', '/admin/game-resources/reports')).data.some(r => r.id === report.id));
  pass('a report reaches the administrator once per user and closes when resolved');
}).catch(error => { console.error('FAIL', error.message); process.exit(1); });
