// DeviceTests only: «Amigos que têm este jogo» against real SQL through the running test API (port 5099).
//   node tools/DeviceTestApi/verify-game-friends.cjs
// Fresh people (disposable.cjs) around «participante» (Wingspan): five who may appear, five who must not; removed at the
// end, also on failure. Never the iPhone accounts; never prints credentials or tokens.
if (require.main !== module) return; // Importing a verifier never runs it: only `node <file>` does (see docs/testes-macos-docker.md).
const assert = require('node:assert/strict');
const { run } = require('./disposable.cjs');
const base = 'http://127.0.0.1:5099';
const own = (role, privacy, status) => ({ role, privacy, library: [{ game: 'Wingspan', status }] });
const spec = { label: 'gamefriends', people: [{ role: 'participante' }, { role: 'autor' },
  own('amiga-colecionadora', 'Public', 'Owned'), own('amigo-com-um-nome-de-utilizador-bastante-comprido', 'FriendsOnly', 'Owned'), own('amiga-tabuleiro', 'FriendsOnly', 'Owned'),
  own('amigo-dados', 'Public', 'Owned'), own('amiga-meeples', 'Public', 'Owned'), own('amigo-privado', 'Private', 'Owned'), own('amiga-quer-jogar', 'Public', 'Wishlist'),
  own('amigo-ja-jogou', 'Public', 'Played'), own('pedido-pendente', 'Public', 'Owned'), own('desconhecido', 'Public', 'Owned')],
  friends: [['participante', 'autor'], ...['amiga-colecionadora', 'amigo-com-um-nome-de-utilizador-bastante-comprido', 'amiga-tabuleiro', 'amigo-dados', 'amiga-meeples', 'amigo-privado',
    'amiga-quer-jogar', 'amigo-ja-jogou'].map(r => ['participante', r])],
  pending: [['pedido-pendente', 'participante']] };
run(spec, async world => {
  const call = async (u, method, route, body) => { const r = await fetch(base + '/MeepleBoard' + route, { method, headers: { ...(u ? { Authorization: `Bearer ${u.token}` } : {}), 'Content-Type': 'application/json' }, body: body && JSON.stringify(body) }); const t = await r.text(); return { status: r.status, data: t ? JSON.parse(t) : null }; };
  const pass = name => console.log('PASS ' + name);
  const viewer = await world.login('participante'), author = await world.login('autor');
  const gameId = async name => (await call(viewer, 'GET', '/game/search?name=' + encodeURIComponent(name))).data.id;
  const wingspan = await gameId('Wingspan'), empty = await gameId('Meeple Teste Cooperativo'); // verify-catalog-search keeps «Solo» in the author's library on purpose
  const owners = async (who, game = wingspan) => (await call(who, 'GET', `/game/${game}/friend-owners`)).data;
  const visible = ['amiga-colecionadora', 'amigo-com-um-nome-de-utilizador-bastante-comprido', 'amiga-tabuleiro', 'amigo-dados', 'amiga-meeples'].map(n => world.people[n].userName);
  const hidden = ['amigo-privado', 'amiga-quer-jogar', 'amigo-ja-jogou', 'pedido-pendente', 'desconhecido'].map(n => world.people[n].userName);

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

  const collector = await world.login('amiga-colecionadora');
  assert.equal((await call(collector, 'PATCH', '/users/me/library-privacy', { libraryPrivacy: 0 })).status, 204);
  try {
    assert.ok(!(await owners(viewer)).items.some(i => i.userName === world.people['amiga-colecionadora'].userName), 'a library made private disappears at once');
  } finally {
    assert.equal((await call(collector, 'PATCH', '/users/me/library-privacy', { libraryPrivacy: 2 })).status, 204);
  }
  assert.ok((await owners(viewer)).items.some(i => i.userName === world.people['amiga-colecionadora'].userName));
  pass('making a library private hides it immediately (restored to public)');
}).catch(error => { console.error('FAIL', error.message); process.exit(1); });
