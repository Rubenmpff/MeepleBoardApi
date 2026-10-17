// DeviceTests only: «Amigos que têm este jogo» against real SQL through the running test API (port 5099).
//   node tools/DeviceTestApi/verify-game-friends.cjs
// Uses the fictitious friends the test host seeds for "participante" (Wingspan): five who may appear, five who must not.
// A privacy change made here is restored before it ends. Never prints credentials or tokens.
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../..'), base = 'http://127.0.0.1:5099';
(async () => {
  const health = await (await fetch(base + '/device-test/health')).json();
  assert.deepEqual(health, { environment: 'DeviceTests', database: 'MeepleBoard_DeviceTests', externalDelivery: false });
  const accounts = JSON.parse(fs.readFileSync(path.join(root, '.device-tests/accounts.json'), 'utf8'));
  const login = async name => { const a = accounts.find(x => x.Email === name + '@meepleboard.test'); const r = await fetch(base + '/MeepleBoard/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: a.Email, password: a.Password, deviceInfo: 'verify-game-friends' }) }); assert.equal(r.status, 200); return { id: a.Id, token: (await r.json()).token }; };
  const call = async (u, method, route, body) => { const r = await fetch(base + '/MeepleBoard' + route, { method, headers: { ...(u ? { Authorization: `Bearer ${u.token}` } : {}), 'Content-Type': 'application/json' }, body: body && JSON.stringify(body) }); const t = await r.text(); return { status: r.status, data: t ? JSON.parse(t) : null }; };
  const pass = name => console.log('PASS ' + name);
  const viewer = await login('participante'), author = await login('autor');
  const gameId = async name => (await call(viewer, 'GET', '/game/search?name=' + encodeURIComponent(name))).data.id;
  const wingspan = await gameId('Wingspan'), empty = await gameId('Meeple Teste Cooperativo'); // verify-catalog-search keeps «Solo» in the author's library on purpose
  const owners = async (who, game = wingspan) => (await call(who, 'GET', `/game/${game}/friend-owners`)).data;
  const visible = ['amiga-colecionadora', 'amigo-com-um-nome-de-utilizador-bastante-comprido', 'amiga-tabuleiro', 'amigo-dados', 'amiga-meeples'].map(n => 'Teste-' + n);
  const hidden = ['amigo-privado', 'amiga-quer-jogar', 'amigo-ja-jogou', 'pedido-pendente', 'desconhecido'].map(n => 'Teste-' + n);

  const seen = await owners(viewer);
  const names = seen.items.map(i => i.userName);
  for (const n of visible) assert.ok(names.includes(n), 'missing ' + n);
  for (const n of hidden) assert.ok(!names.includes(n), 'must not appear: ' + n);
  assert.equal(seen.total, seen.items.length);
  assert.deepEqual(names, [...names].sort((a, b) => a.localeCompare(b, 'pt', { sensitivity: 'base' })), 'sorted by name');
  assert.ok(seen.items.every(i => Object.keys(i).sort().join() === 'profilePictureUrl,userId,userName'), 'only the fields the friends list shows');
  pass(`${seen.total} friends with «Tenho» and a visible library; private library, wishlist, «Já joguei», pending request and non-friend left out`);

  const others = (await owners(author)).items.map(i => i.userName);
  assert.ok([...visible, ...hidden].every(n => !others.includes(n)), 'another user must not see these people');
  assert.equal((await owners(viewer, empty)).total, 0);
  assert.equal((await call(null, 'GET', `/game/${wingspan}/friend-owners`)).status, 401);
  assert.equal((await call(viewer, 'GET', `/game/00000000-0000-0000-0000-000000000001/friend-owners`)).status, 404);
  pass('another account sees none of them; a game nobody has is empty; anonymous 401, unknown game 404');

  const collector = await login('amiga-colecionadora');
  assert.equal((await call(collector, 'PATCH', '/users/me/library-privacy', { libraryPrivacy: 0 })).status, 204);
  try {
    assert.ok(!(await owners(viewer)).items.some(i => i.userName === 'Teste-amiga-colecionadora'), 'a library made private disappears at once');
  } finally {
    assert.equal((await call(collector, 'PATCH', '/users/me/library-privacy', { libraryPrivacy: 2 })).status, 204);
  }
  assert.ok((await owners(viewer)).items.some(i => i.userName === 'Teste-amiga-colecionadora'));
  pass('making a library private hides it immediately (restored to public)');
})().catch(error => { console.error('FAIL', error.message); process.exit(1); });
