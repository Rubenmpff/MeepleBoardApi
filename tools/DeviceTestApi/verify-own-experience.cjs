// DeviceTests only: «yours» is always the signed-in person's rating and tags — in the game history and the match
// detail — never those of whoever registered the match (approved 18/10/2026). Against real SQL, with fresh people
// (disposable.cjs) removed at the end, also on failure. Never the iPhone accounts.
//   node tools/DeviceTestApi/verify-own-experience.cjs
// A registers a match with A, B and C (A rates 7, B later rates 0, C never rates); two old matches carry a rating on the
// match itself and no diary: one registered by A, one with no known author. Averages are only read, never changed by this.
if (require.main !== module) return; // Importing a verifier never runs it: only `node <file>` does (see docs/testes-macos-docker.md).
const assert = require('node:assert/strict');
const { run } = require('./disposable.cjs');
const base = 'http://127.0.0.1:5099';
run({ label: 'ownexp', people: [{ role: 'a' }, { role: 'b' }, { role: 'c' }], friends: [['a', 'b'], ['a', 'c'], ['b', 'c']] }, async world => {
  const [A, B, C] = await Promise.all(['a', 'b', 'c'].map(r => world.login(r)));
  const call = async (u, method, route, body, expected) => {
    const r = await fetch(base + '/MeepleBoard' + route, { method, headers: { Authorization: 'Bearer ' + u.token, 'Content-Type': 'application/json' }, body: body && JSON.stringify(body) });
    const t = await r.text(); if (expected) assert.equal(r.status, expected, `${method} ${route} → ${r.status} ${t.slice(0, 200)}`);
    return t ? JSON.parse(t) : null;
  };
  const legacy = async body => { const r = await fetch(base + '/device-test/disposable/legacy-match', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }); assert.equal(r.status, 200); return (await r.json()).id; };
  const pass = name => console.log('PASS ' + name);
  const game = await call(A, 'GET', '/game/search?name=' + encodeURIComponent('Meeple Teste Cooperativo'), null, 200);
  const average = async () => { const g = await call(A, 'GET', `/game/${game.id}`, null, 200); return [g.meepleBoardScore, g.meepleBoardRatingsCount]; };

  const m1 = (await call(A, 'POST', '/matches', { gameId: game.id, gameName: game.name, matchDate: new Date(Date.now() - 3600e3).toISOString(), isSoloGame: false, gameMode: 'COMPETITIVE',
    result: 'Undefined', resultPlayerIds: [], playerIds: [A.id, B.id, C.id], personalRating: 7, tags: 'etiqueta-de-a' }, 201)).id;
  await call(B, 'PUT', `/campaigns/matches/${m1}/journal`, { personalRating: 0, tags: 'etiqueta-de-b' }, 200);
  const oldOfA = await legacy({ game: game.name, players: [A.id, B.id], creator: A.id, rating: 8, tags: 'antigo' });
  const oldUnknown = await legacy({ game: game.name, players: [A.id, B.id], creator: null, rating: 9, tags: 'sem-autor' });
  const startAverage = await average(), startOfA = (await call(A, 'GET', `/matches/rating/game/${game.id}`, null, 200)).rating;

  const history = async u => Object.fromEntries((await call(u, 'GET', `/matches/history/game/${game.id}`, null, 200)).map(m => [m.id, [m.personalRating ?? null, m.tags ?? null]]));
  const detail = async (u, id) => { const d = await call(u, 'GET', `/matches/${id}`, null, 200); return [d.personalRating ?? null, d.tags ?? null]; };
  const [hA, hB, hC] = [await history(A), await history(B), await history(C)];
  assert.deepEqual([hA[m1], hB[m1], hC[m1]], [[7, 'etiqueta-de-a'], [0, 'etiqueta-de-b'], [null, null]], 'each sees their own rating and tags');
  for (const [u, expected] of [[A, [7, 'etiqueta-de-a']], [B, [0, 'etiqueta-de-b']], [C, [null, null]]]) assert.deepEqual(await detail(u, m1), expected);
  pass('history and detail: A sees 7, B sees 0 (zero is a rating), C sees no rating although the others rated; tags are each one\'s own');

  assert.deepEqual([hA[oldOfA], hB[oldOfA]], [[8, 'antigo'], [null, null]], 'an old rating is A\'s only (A registered it)');
  assert.deepEqual([hA[oldUnknown], hB[oldUnknown]], [[null, null], [null, null]], 'no known author: nobody gets it as theirs');
  assert.deepEqual([await detail(A, oldOfA), await detail(B, oldOfA), await detail(A, oldUnknown)], [[8, 'antigo'], [null, null], [null, null]]);
  pass('old matches: the stored rating is shown as yours only with confirmed authorship; never given to the other players');

  // Consistency with the average (unchanged code): reading changes nothing; the old rating stops being A's exactly when
  // the average stops counting it (a diary entry on the match), and saving A's diary keeps the same value.
  assert.deepEqual(await average(), startAverage, 'reads change nothing');
  await call(A, 'PUT', `/campaigns/matches/${oldOfA}/journal`, { personalRating: 8, notes: 'nota de A' }, 200);
  assert.deepEqual((await history(A))[oldOfA], [8, null], 'A\'s diary now holds the same 8');
  assert.deepEqual(await average(), startAverage, 'the old 8 became A\'s diary 8: same count and mean');
  assert.equal((await call(A, 'GET', `/matches/rating/game/${game.id}`, null, 200)).rating, startOfA, '«A tua média» unchanged');
  await call(B, 'PUT', `/campaigns/matches/${oldUnknown}/journal`, { notes: 'nota de B' }, 200);
  assert.deepEqual([(await history(A))[oldUnknown], (await history(B))[oldUnknown]], [[null, null], [null, null]]);
  await call(A, 'PUT', `/campaigns/matches/${m1}/journal`, { personalRating: 5 }, 409); // a saved rating stays closed
  pass('averages and «A tua média» unchanged; an old rating saved through the diary keeps its value; saved ratings stay closed');
}).catch(error => { console.error('FAIL', error.message); process.exit(1); });
