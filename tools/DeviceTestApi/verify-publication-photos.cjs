// DeviceTests only: photos published with an experience (approved 18/10/2026), against real SQL and real tokens.
//   node tools/DeviceTestApi/verify-publication-photos.cjs
// Fresh people (disposable.cjs, removed at the end, also on failure): A publishes, B plays the match with A, C, D and E only
// see the community, and an administrator. Checks, as expected behaviour:
// - only A's own diary photos of that match, at most 3, and only with the confirmation; nothing published by itself;
// - the public route /experiences/{pubId}/photos/{photoId} answers any signed-in person who may see it, never a visitor,
//   names no match, diary entry or storage, respects blocks both ways, and sends Cache-Control: no-store, private;
// - after a withdrawal or hiding, new requests get 404 (copies already downloaded are out of reach and not claimed);
//   withdrawn photos stay in the diary for the players; deleting a diary photo removes it from the community;
// - anyone who sees it can report it (not its author; once while open); 3 different people hide it from the community until
//   reviewed; an administrator can decide before that; «Manter», «Retirar da comunidade» and «Remover da partida» are distinct,
//   the last two need a reason the author then sees, and the author cannot publish a moderated photo again.
if (require.main !== module) return; // Importing a verifier never runs it: only `node <file>` does (see docs/testes-macos-docker.md).
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const { run } = require('./disposable.cjs');
const base = 'http://127.0.0.1:5099';
const pass = text => console.log('PASS ' + text);

run({ label: 'pubphotos', people: [{ role: 'a' }, { role: 'b' }, { role: 'c' }, { role: 'd' }, { role: 'e' }, { role: 'admin', admin: true }], friends: [['a', 'b']] }, async world => {
  const [A, B, C, D, E, admin] = await Promise.all(['a', 'b', 'c', 'd', 'e', 'admin'].map(r => world.login(r)));
  const send = (u, method, route, body, headers = {}) => fetch(base + route, { method, headers: { ...(u ? { Authorization: 'Bearer ' + u.token } : {}), ...(body && !(body instanceof FormData) ? { 'Content-Type': 'application/json' } : {}), ...headers },
    body: body instanceof FormData ? body : body ? JSON.stringify(body) : undefined });
  const call = async (u, method, route, body, expected) => {
    const r = await send(u, method, '/MeepleBoard' + route, body);
    const text = await r.text();
    if (expected) assert.equal(r.status, expected, `${method} ${route.replace(/[0-9a-f-]{36}/g, '{id}')} → ${r.status} ${text.slice(0, 160)}`);
    return text ? JSON.parse(text) : null;
  };
  const game = await call(A, 'GET', '/game/search?name=Wingspan', null, 200);
  const match = await call(A, 'POST', '/matches', { gameId: game.id, gameName: game.name, matchDate: new Date().toISOString(), isSoloGame: false, winnerId: A.id, playerIds: [A.id, B.id], personalRating: 7.5 }, 201);
  await call(A, 'PUT', `/campaigns/matches/${match.id}/journal`, { personalRating: 7.5, notes: 'NOTA PRIVADA', tags: null }, 200);
  const jpeg = fs.readFileSync(path.join(__dirname, 'fixtures/photo-metadata.jpg'));
  const upload = async u => { const form = new FormData(); form.append('file', new Blob([jpeg], { type: 'image/jpeg' }), 'p.jpg');
    return (await call(u, 'POST', `/campaigns/matches/${match.id}/journal/photos`, form, 200)).photoUrls.at(-1); };
  const diary = []; for (let i = 0; i < 4; i++) diary.push(await upload(A));
  const bPhoto = await upload(B);
  const key = p => p.split('/').pop();
  const publication = `/campaigns/matches/${match.id}/journal/publication`;
  const put = (body, expected) => call(A, 'PUT', publication, { showRating: true, comment: null, publishComment: false, ...body }, expected);
  const list = async u => (await call(u, 'GET', `/game/${game.id}/experiences?limit=50`, null, 200)).items.find(i => i.authorName === world.people.a.userName);

  // ── Choosing: own photos only, at most 3, with the confirmation ────────────────────────────────────────────────────
  await put({ photoKeys: [key(bPhoto)], photoConsent: true }, 400);
  await put({ photoKeys: [key(diary[0]), key(diary[1]), key(diary[2]), key(diary[3])], photoConsent: true }, 400);
  await put({ photoKeys: [key(diary[0])] }, 400);
  let mine = await call(A, 'GET', publication, null, 200);
  assert.equal(mine.photos.length, 0, 'nothing published by the refusals');
  mine = await put({ photoKeys: [key(diary[0]), key(diary[1])], photoConsent: true }, 200);
  assert.deepEqual(mine.photos.map(p => p.key), [key(diary[0]), key(diary[1])]);
  pass('only my own diary photos of the match, at most 3, and only with the confirmation; nothing published by a refused request');

  // ── The public route ─────────────────────────────────────────────────────────────────────────────────────────────
  let item = await list(C);
  assert.equal(item.photos.length, 2);
  for (const p of item.photos) {
    assert.match(p, /^\/MeepleBoard\/experiences\/[0-9a-f-]{36}\/photos\/[0-9a-f-]{36}$/);
    assert.ok(!p.includes(match.id) && !p.includes('journal') && !p.includes('cloudinary') && !p.includes(key(diary[0])), 'names no match, entry or storage');
  }
  assert.ok(!JSON.stringify(item).includes('NOTA PRIVADA') && !JSON.stringify(item).includes(match.id), 'no note, no match');
  const [first, second] = item.photos;
  const seen = await send(C, 'GET', first);
  assert.equal(seen.status, 200); assert.equal(seen.headers.get('cache-control'), 'no-store, private');
  const viaDiary = await send(B, 'GET', diary[0]);
  assert.deepEqual(Buffer.from(await seen.arrayBuffer()), Buffer.from(await viaDiary.arrayBuffer()), 'the same cleaned copy the players get');
  assert.equal((await send(null, 'GET', first)).status, 401, 'never a visitor');
  pass('any signed-in person sees it at /experiences/{pubId}/photos/{photoId} (no match, entry or storage), no-store, private; visitors 401');

  await call(C, 'POST', `/community/blocks/${A.id}`, null, 204);
  assert.equal((await send(C, 'GET', first)).status, 404); assert.equal(await list(C), undefined);
  await call(C, 'DELETE', `/community/blocks/${A.id}`, null, 204);
  await call(A, 'POST', `/community/blocks/${C.id}`, null, 204);
  assert.equal((await send(C, 'GET', first)).status, 404, 'a block by the author too');
  await call(A, 'DELETE', `/community/blocks/${C.id}`, null, 204);
  assert.equal((await send(C, 'GET', first)).status, 200);
  pass('a block either way: the photo answers 404 and the experience leaves the list; back after unblocking');

  // ── Withdrawing one, and the diary ───────────────────────────────────────────────────────────────────────────────
  const firstId = first.split('/').pop();
  mine = await call(A, 'DELETE', `${publication}/photos/${firstId}`, null, 200);
  assert.deepEqual(mine.photos.map(p => p.key), [key(diary[1])]);
  assert.equal((await send(C, 'GET', first)).status, 404, 'new requests after withdrawal: 404');
  assert.equal((await send(B, 'GET', diary[0])).status, 200, 'still in the diary, shared with the players');
  assert.equal((await list(C)).photos.length, 1, 'the list no longer has it');
  pass('withdrawing one photo: 404 for new requests, gone from the list, still shared with the players in the diary');

  // ── Reports and automatic hiding ─────────────────────────────────────────────────────────────────────────────────
  const reportRoute = second.replace('/MeepleBoard', '') + '/reports';
  await call(A, 'POST', reportRoute, { reason: 'Spam' }, 409);
  await call(C, 'POST', reportRoute, { reason: 'PersonalData' }, 204);
  await call(C, 'POST', reportRoute, { reason: 'PersonalData' }, 409);
  assert.equal((await send(C, 'GET', second)).status, 404, 'the reporter no longer sees what they reported');
  assert.equal((await send(D, 'GET', second)).status, 200);
  await call(D, 'POST', reportRoute, { reason: 'PersonalData' }, 204);
  assert.equal((await send(E, 'GET', second)).status, 200, 'two reports: still visible');
  await call(E, 'POST', reportRoute, { reason: 'Inappropriate' }, 204);
  assert.equal((await send(B, 'GET', second)).status, 404, 'three different people: hidden from the community');
  assert.equal((await send(B, 'GET', diary[1])).status, 200, 'still with the players');
  const entryId = diary[1].split('/').at(-2);
  let queued = (await call(admin, 'GET', '/admin/moderation/photos', null, 200)).find(p => p.journalEntryId === entryId && p.photoKey === key(diary[1]));
  assert.ok(queued.published && queued.autoHidden && queued.reasons.PersonalData === 2);
  mine = await call(A, 'GET', publication, null, 200);
  assert.equal(mine.photos[0].hiddenReason, 'AutoReports');
  pass('anyone who sees it can report it (not the author; once while open); 3 different people hide it from the community until reviewed');

  await call(admin, 'POST', `/admin/moderation/photos/${entryId}/${key(diary[1])}/keep`, {}, 204);
  assert.equal((await send(B, 'GET', second)).status, 200, '«Manter» brings it back');
  pass('«Manter» lifts the automatic hiding and resolves the reports');

  // ── An administrator before the threshold: withdraw from the community ────────────────────────────────────────────
  await call(D, 'POST', reportRoute, { reason: 'PersonalData' }, 204);
  await call(admin, 'POST', `/admin/moderation/photos/${entryId}/${key(diary[1])}/withdraw`, {}, 400);
  await call(admin, 'POST', `/admin/moderation/photos/${entryId}/${key(diary[1])}/withdraw`, { reason: 'PersonalData' }, 204);
  assert.equal((await send(D, 'GET', second)).status, 404); assert.equal((await send(B, 'GET', diary[1])).status, 200, 'withdrawn from the community, still with the players');
  mine = await call(A, 'GET', publication, null, 200);
  assert.deepEqual([mine.photos[0].hidden, mine.photos[0].hiddenReason, mine.photos[0].removed], [true, 'PersonalData', false], 'the author sees why');
  mine = await put({ photoKeys: [key(diary[1])], photoConsent: true }, 200);
  assert.equal(mine.photos.find(p => p.key === key(diary[1])).hidden, true, 'choosing it again does not publish it again');
  assert.equal((await send(D, 'GET', second)).status, 404);
  await call(A, 'DELETE', `${publication}/photos/${second.split('/').pop()}`, null, 404);
  pass('one report is enough for an administrator: «Retirar da comunidade» needs a reason, keeps it with the players, the author sees why and cannot undo it');

  // ── «Retirar tudo da comunidade» ─────────────────────────────────────────────────────────────────────────────────
  mine = await put({ photoKeys: [key(diary[1]), key(diary[2])], photoConsent: true }, 200);
  const third = (await list(C)).photos.at(-1);
  mine = await call(A, 'POST', `${publication}/withdraw-all`, null, 200);
  assert.equal(mine.showRating, false); assert.deepEqual(mine.photos.map(p => p.hiddenReason), ['PersonalData'], 'only the moderation notice stays');
  assert.equal(await list(C), undefined, 'nothing of A in the community');
  assert.equal((await send(C, 'GET', third)).status, 404); assert.equal((await send(B, 'GET', diary[2])).status, 200);
  pass('«Retirar tudo da comunidade»: rating, comment and photos leave; photos stay with the players; 404 for new requests');

  // ── Deleting from the diary removes it from the community ────────────────────────────────────────────────────────
  await put({ photoKeys: [key(diary[3])], photoConsent: true }, 200);
  const fourth = (await list(C)).photos[0];
  assert.equal((await send(C, 'GET', fourth)).status, 200);
  await call(A, 'DELETE', `/campaigns/matches/${match.id}/journal/photos?photoUrl=${encodeURIComponent(diary[3])}`, null, 200);
  assert.equal((await send(C, 'GET', fourth)).status, 404);
  mine = await call(A, 'GET', publication, null, 200);
  assert.ok(!mine.photos.some(p => p.key === key(diary[3])), 'no published copy left');
  pass('deleting a photo from the diary removes it from the community');

  // ── «Remover da partida» ─────────────────────────────────────────────────────────────────────────────────────────
  await put({ photoKeys: [key(diary[2])], photoConsent: true }, 200);
  const fifth = (await list(C)).photos[0];
  await call(E, 'POST', fifth.replace('/MeepleBoard', '') + '/reports', { reason: 'Inappropriate' }, 204);
  await call(admin, 'POST', `/admin/moderation/photos/${entryId}/${key(diary[2])}/remove`, { reason: 'Inappropriate' }, 204);
  assert.equal((await send(E, 'GET', fifth)).status, 404); assert.equal((await send(B, 'GET', diary[2])).status, 404, 'gone from the diary too');
  mine = await call(A, 'GET', publication, null, 200);
  const removed = mine.photos.find(p => p.key === key(diary[2]));
  assert.deepEqual([removed.removed, removed.hiddenReason, removed.diaryPath], [true, 'Inappropriate', null], 'the author sees it was removed and why');
  pass('«Remover da partida» (with a reason) deletes it from the diary and the community; the author sees why');
}).catch(error => { console.error('FAIL', error.message); process.exit(1); });
