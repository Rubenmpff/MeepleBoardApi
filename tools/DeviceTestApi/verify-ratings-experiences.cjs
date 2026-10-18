// DeviceTests only: ratings closed per experience, MeepleBoard rating and its counts, the ranking, experience
// publications, reports and moderation — against real SQL.
//   node tools/DeviceTestApi/verify-ratings-experiences.cjs
// Recalculates every game once (the approved recalculation; individual ratings are only read), then works on
// «Meeple Teste Cooperativo» with matches it creates and deletes again, so averages return to where they were.
// Fresh people (disposable.cjs): amiga-colecionadora with two friends who play with her, two others and an
// administrator; removed at the end, also on failure. Never the iPhone accounts; never prints credentials or tokens.
if (require.main !== module) return; // Importing a verifier never runs it: only `node <file>` does (see docs/testes-macos-docker.md).
const assert = require('node:assert/strict');
const { run } = require('./disposable.cjs');
const base = 'http://127.0.0.1:5099';
const MARK = 'Verificação de avaliações';
run({ label: 'ratings', people: [{ role: 'admin', admin: true }, ...['amiga-colecionadora', 'amiga-tabuleiro', 'amiga-meeples', 'amigo-dados', 'participante'].map(role => ({ role }))],
  friends: [['amiga-colecionadora', 'amiga-tabuleiro'], ['amiga-colecionadora', 'amiga-meeples']] }, async world => {
  const call = async (u, method, route, body, expected) => {
    const r = await fetch(base + '/MeepleBoard' + route, { method, headers: { Authorization: `Bearer ${u.token}`, 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined });
    if (expected) assert.equal(r.status, expected, `${u.name} ${method} ${route} → ${r.status} ${await r.clone().text()}`);
    return r.status === 204 ? null : r.headers.get('content-type')?.includes('json') ? r.json() : r.text();
  };
  const pass = name => console.log('PASS ' + name);
  const [admin, C, D, E, F, P] = await Promise.all(['admin', 'amiga-colecionadora', 'amiga-tabuleiro', 'amiga-meeples', 'amigo-dados', 'participante'].map(r => world.login(r)));
  const G = await call(C, 'GET', '/game/search?name=' + encodeURIComponent('Meeple Teste Cooperativo'), null, 200);
  const game = () => call(C, 'GET', `/game/${G.id}`, null, 200);
  const mine = (u, matchId) => call(u, 'GET', `/campaigns/matches/${matchId}/journal/publication`, null, 200);
  const journal = (u, matchId, body, expected) => call(u, 'PUT', `/campaigns/matches/${matchId}/journal`, body, expected);
  const list = u => call(u, 'GET', `/game/${G.id}/experiences?limit=50`, null, 200);
  const created = [];
  const match = async (creator, players, rating, extra = {}) => {
    const m = await call(creator, 'POST', '/matches', { gameId: G.id, gameName: G.name, matchDate: new Date(Date.now() - 3600e3).toISOString(), isSoloGame: false,
      playerIds: players.map(p => p.id), gameMode: 'COMPETITIVE', result: 'Undefined', resultPlayerIds: [], durationMethod: 'Unknown', scoreSummary: MARK, personalRating: rating, notes: 'Nota privada de teste', ...extra }, 201);
    created.push([creator, m.id]); return m;
  };

  // 0. Earlier runs removed; the approved recalculation of every game (reads ratings, writes only game means).
  for (const u of [C, D]) for (const m of await call(u, 'GET', `/matches/history/game/${G.id}`) || [])
    if ((m.scoreSummary || '').startsWith(MARK) && m.creatorId === u.id) await call(u, 'DELETE', `/matches/${m.id}`, null, 204);
  await call(C, 'POST', '/game/rankings/recompute', null, 403);
  const recompute = await call(admin, 'POST', '/game/rankings/recompute', null, 200);
  const before = await game();
  pass(`every game recalculated (only administrators may); «${G.name}» starts at ${before.meepleBoardScore ?? '—'} with ${before.meepleBoardRatingsCount} ratings from ${before.meepleBoardRaters} people (${JSON.stringify(recompute).slice(0, 80)})`);

  // 1. The rating given at registration is the creator's rating of the experience, zero included.
  const m1 = await match(C, [C, D], 0);
  let entries = await call(C, 'GET', `/campaigns/matches/${m1.id}/journal`, null, 200);
  assert.equal(entries.find(e => e.userId === C.id).personalRating, 0, 'zero is saved as a rating');
  const pendingC = await call(C, 'GET', '/matches/pending-journal') || []; // 204 when nothing is pending
  const pendingD = await call(D, 'GET', '/matches/pending-journal', null, 200) || [];
  assert.ok(!pendingC.some(m => m.id === m1.id), 'zero counts as rated'); assert.ok(pendingD.some(m => m.id === m1.id), 'the friend still has it pending');
  pass('the registration rating (0) is saved as the creator\'s rating; zero is not «por avaliar»; the friend\'s pending match is theirs only');

  // 2. Closed: a different value is refused, texts still change, sending no rating keeps it.
  await journal(C, m1.id, { personalRating: 5, notes: 'x' }, 409);
  await journal(C, m1.id, { personalRating: 0, notes: 'Notas novas', tags: 'teste' }, 200);
  await journal(C, m1.id, { personalRating: null, notes: 'Só texto' }, 200);
  entries = await call(C, 'GET', `/campaigns/matches/${m1.id}/journal`, null, 200);
  assert.equal(entries.find(e => e.userId === C.id).personalRating, 0); assert.equal(entries.find(e => e.userId === C.id).notes, 'Só texto');
  assert.ok(!('notes' in (entries.find(e => e.userId === D.id) ?? {})) || entries.find(e => e.userId === D.id)?.notes == null, 'other people never get my notes');
  pass('a saved rating cannot change (409); notes and tags still save; sending no rating keeps 0');

  // 3. Permissions: only participants rate or publish; the friend rates once.
  await journal(P, m1.id, { personalRating: 6 }, 403);
  await call(P, 'GET', `/campaigns/matches/${m1.id}/journal/publication`, null, 403);
  await journal(D, m1.id, { personalRating: 7.5, notes: 'A nota privada da amiga' }, 200);
  await journal(D, m1.id, { personalRating: 8 }, 409);
  pass('a non-participant cannot rate (403) nor see publications of the match; the friend rates 7,5 once (8 refused)');

  // 4. MeepleBoard rating: one per experience, official matches only; your average includes unofficial ones.
  let g = await game();
  assert.equal(g.meepleBoardRatingsCount, before.meepleBoardRatingsCount + 2);
  const myBefore = (await call(C, 'GET', `/matches/rating/game/${G.id}`)).rating;
  const unofficial = await match(C, [C, D], 10, { unofficialModeJustification: 'Variante de teste da verificação' });
  g = await game();
  assert.equal(g.meepleBoardRatingsCount, before.meepleBoardRatingsCount + 2, 'unofficial match left out of the MeepleBoard rating');
  const myAfter = (await call(C, 'GET', `/matches/rating/game/${G.id}`)).rating;
  assert.ok(Number(myAfter) > Number(myBefore ?? 0), `your average includes the unofficial 10 (${myBefore} → ${myAfter})`);
  pass(`MeepleBoard counts each experience (${g.meepleBoardRatingsCount} ratings, ${g.meepleBoardRaters} people) and leaves out the unofficial match; «A tua média» includes it`);

  // 5. Deleting a match updates the rating at once.
  await call(C, 'DELETE', `/matches/${unofficial.id}`, null, 204); created.splice(created.findIndex(x => x[1] === unofficial.id), 1);
  const m2 = await match(C, [C, D], 9);
  assert.equal((await game()).meepleBoardRatingsCount, before.meepleBoardRatingsCount + 3);
  await call(C, 'DELETE', `/matches/${m2.id}`, null, 204); created.splice(created.findIndex(x => x[1] === m2.id), 1);
  assert.equal((await game()).meepleBoardRatingsCount, before.meepleBoardRatingsCount + 2, 'deleting removes its rating from the mean');
  pass('deleting a match removes its ratings from the MeepleBoard rating immediately');

  // 6. Publishing: independent choices; never a new rating; public responses carry no private data.
  await call(D, 'PUT', `/campaigns/matches/${m1.id}/journal/publication`, { showRating: true, comment: 'Gostei muito desta partida.', publishComment: true }, 200);
  let all = await list(P); let item = all.items.find(i => i.authorName && i.comment === 'Gostei muito desta partida.');
  assert.ok(item, 'visible to any signed-in person'); assert.equal(item.rating, 7.5); assert.equal(item.isMine, false); assert.equal(item.canReport, true);
  assert.deepEqual(Object.keys(item).sort(), ['authorName', 'canReport', 'comment', 'edited', 'id', 'isMine', 'mode', 'month', 'ownMatchId', 'players', 'rating'].sort(), 'only the approved fields');
  assert.equal(item.ownMatchId, null, 'the match is never given to other readers');
  const json = JSON.stringify(all);
  for (const leak of [C.id, D.id, 'A nota privada da amiga', 'Só texto', 'Nota privada de teste']) assert.ok(!json.includes(leak), 'no match id, people ids or notes: ' + leak.slice(0, 12));
  assert.equal((await game()).meepleBoardRatingsCount, before.meepleBoardRatingsCount + 2, 'publishing does not add a contribution');
  const own = (await list(D)).items.find(i => i.id === item.id); assert.equal(own.isMine, true); assert.equal(own.canReport, false);
  assert.equal(own.ownMatchId, m1.id, 'only the author gets their own match, to manage it');
  await call(D, 'POST', `/campaigns/matches/${m1.id}/journal/publication/withdraw-rating`, null, 200);
  item = (await list(P)).items.find(i => i.id === item.id); assert.equal(item.rating, null); assert.equal(item.comment, 'Gostei muito desta partida.');
  entries = await call(D, 'GET', `/campaigns/matches/${m1.id}/journal`, null, 200);
  assert.equal(entries.find(e => e.userId === D.id).personalRating, 7.5, 'withdrawing keeps the saved rating');
  assert.equal((await game()).meepleBoardRatingsCount, before.meepleBoardRatingsCount + 2, 'and its contribution');
  await call(D, 'POST', `/campaigns/matches/${m1.id}/journal/publication/withdraw-comment`, null, 200);
  assert.ok(!(await list(P)).items.some(i => i.id === item.id), 'nothing published, nothing listed');
  assert.equal((await mine(D, m1.id)).comment, 'Gostei muito desta partida.', 'the withdrawn comment stays private for the author');
  const m3 = await match(C, [C, E], 6);
  await call(E, 'PUT', `/campaigns/matches/${m3.id}/journal/publication`, { showRating: true, publishComment: false }, 409);
  pass('rating and comment are published and withdrawn separately; the saved rating and its contribution never change; no ids, notes or other participants; unrated experiences cannot publish (409)');

  // 7. Reports and moderation.
  await call(D, 'PUT', `/campaigns/matches/${m1.id}/journal/publication`, { showRating: false, comment: 'Gostei muito desta partida.', publishComment: true }, 200);
  const pubId = (await list(P)).items.find(i => i.comment === 'Gostei muito desta partida.').id;
  await call(D, 'POST', `/experiences/${pubId}/reports`, { reason: 'Spam' }, 409);
  await call(P, 'POST', `/experiences/${pubId}/reports`, { reason: 'Nope' }, 400);
  await call(P, 'POST', `/experiences/${pubId}/reports`, { reason: 'PersonalData', detail: 'teste' }, 204);
  await call(P, 'POST', `/experiences/${pubId}/reports`, { reason: 'PersonalData' }, 409);
  assert.ok(!(await list(P)).items.some(i => i.id === pubId), 'hidden for the person who reported');
  assert.ok((await list(C)).items.some(i => i.id === pubId), 'still visible to others');
  await call(P, 'GET', '/admin/experiences/reported', null, 403);
  await call(F, 'POST', `/experiences/${pubId}/reports`, { reason: 'OffTopic' }, 204);
  await call(E, 'POST', `/experiences/${pubId}/reports`, { reason: 'PersonalData' }, 204);
  assert.ok(!(await list(C)).items.some(i => i.id === pubId), '3 distinct reports hide it until review');
  let reported = (await call(admin, 'GET', '/admin/experiences/reported', null, 200)).find(r => r.id === pubId);
  assert.equal(reported.hidden, true); assert.equal(reported.reasons.PersonalData, 2); assert.equal(reported.reasons.OffTopic, 1);
  await call(admin, 'POST', `/admin/experiences/${pubId}/keep`, {}, 204);
  assert.ok((await list(C)).items.some(i => i.id === pubId), 'kept: visible again');
  await call(admin, 'POST', `/admin/experiences/${pubId}/hide`, { reason: 'PersonalData' }, 204);
  const hiddenMine = await mine(D, m1.id); assert.equal(hiddenMine.hidden, true); assert.equal(hiddenMine.hiddenReason, 'PersonalData');
  assert.ok(!(await list(C)).items.some(i => i.id === pubId));
  entries = await call(D, 'GET', `/campaigns/matches/${m1.id}/journal`, null, 200); assert.equal(entries.find(e => e.userId === D.id).personalRating, 7.5, 'moderation never touches the rating');
  await call(admin, 'POST', `/admin/experiences/${pubId}/restore`, {}, 204);
  await call(D, 'POST', `/campaigns/matches/${m1.id}/journal/publication/delete-comment`, null, 200);
  pass('reports: not your own, known reasons, one each; hidden for the reporter; 3 people hide until review; keep, hide (author sees the reason) and restore by administrators only; the rating stays');

  // 8. Ranking: at least 5 ratings from 3 people; equal means share the position.
  const ranking = (await call(P, 'GET', '/game/rankings?source=meepleboard&pageSize=50', null, 200)).data;
  for (const r of ranking) assert.ok(r.meepleBoardRatingsCount >= 5 && r.meepleBoardRaters >= 3, `${r.name} in the ranking without the minimum`);
  for (const r of ranking) assert.equal(r.meepleBoardRank, 1 + ranking.filter(x => x.meepleBoardScore > r.meepleBoardScore).length, 'shared positions');
  assert.ok(!ranking.some(r => r.id === G.id && (g.meepleBoardRatingsCount < 5 || g.meepleBoardRaters < 3)), 'a game below the minimum stays out');
  pass(`ranking: ${ranking.length} games, all with ≥5 ratings from ≥3 people; positions shared on equal means (${ranking.map(r => `${r.meepleBoardRank}. ${r.name}`).join(', ') || 'none yet'})`);

  // 9. Clean up: the game returns to its starting rating.
  for (const [u, id] of created.reverse()) await call(u, 'DELETE', `/matches/${id}`, null, 204);
  const after = await game();
  assert.equal(after.meepleBoardRatingsCount, before.meepleBoardRatingsCount); assert.equal(after.meepleBoardScore, before.meepleBoardScore);
  pass('the verification matches are removed; the MeepleBoard rating is back to its starting value');
}).catch(error => { console.error('FAIL', error.message); process.exit(1); });
