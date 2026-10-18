// DeviceTests only: match duration (measured, estimated, «Não sei», old zero), its correction, and the duration
// statistics of a game — «A tua média» and «Média MeepleBoard» — against real SQL.
//   node tools/DeviceTestApi/verify-durations.cjs
// Starts from a known state (every test account's contribution off, previous runs' matches removed), checks the
// rules, then leaves a small shared fixture so the community average can be seen on the iPhone: six matches of the
// chosen game by three friends who contribute (amiga-colecionadora, amiga-tabuleiro, amiga-meeples). Never prints
// credentials or tokens.
if (require.main !== module) return; // Importing a verifier never runs it: only `node <file>` does (see docs/testes-macos-docker.md).
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../..'), base = 'http://127.0.0.1:5099';
const MARK = 'Verificação de duração', FIXTURE = 'Verificação de duração · comunidade';
(async () => {
  const health = await (await fetch(base + '/device-test/health')).json();
  assert.deepEqual(health, { environment: 'DeviceTests', database: 'MeepleBoard_DeviceTests', externalDelivery: false });
  const accounts = JSON.parse(fs.readFileSync(path.join(root, '.device-tests/accounts.json'), 'utf8'));
  const login = async name => {
    const a = accounts.find(x => x.Email === name + '@meepleboard.test');
    const r = await fetch(base + '/MeepleBoard/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: a.Email, password: a.Password, deviceInfo: 'verify-durations' }) });
    assert.equal(r.status, 200, 'login ' + name); return { name, id: a.Id, token: (await r.json()).token };
  };
  const call = async (u, method, route, body, expected) => {
    const r = await fetch(base + '/MeepleBoard' + route, { method, headers: { Authorization: `Bearer ${u.token}`, 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined });
    if (expected) assert.equal(r.status, expected, `${method} ${route} → ${r.status} ${await r.clone().text()}`);
    return r.status === 204 ? null : r.headers.get('content-type')?.includes('json') ? r.json() : r.text();
  };
  const pass = name => console.log('PASS ' + name);
  const names = ['autor', 'participante', 'amiga-colecionadora', 'amiga-tabuleiro', 'amiga-meeples'];
  const [A, P, C, D, E] = await Promise.all(names.map(login));
  const everyone = [A, P, C, D, E];
  const share = (u, enabled) => call(u, 'PATCH', '/users/me/duration-sharing', { enabled }, 204);
  const durations = (u, game) => call(u, 'GET', `/game/${game.id}/durations`, null, 200);
  const match = (u, game, { players, minutes, method, mode = 'COMPETITIVE', summary = MARK }) => call(u, 'POST', '/matches', {
    gameId: game.id, gameName: game.name, matchDate: new Date(Date.now() - 3600e3).toISOString(), isSoloGame: mode === 'SOLO',
    playerIds: players.map(p => p.id), gameMode: mode, result: 'Undefined', resultPlayerIds: [], scoresEnabled: false,
    durationInMinutes: minutes, durationMethod: method, scoreSummary: summary, personalRating: 7 }, 201);

  // 0. Known state: contributions off for every account used here; matches from earlier runs removed.
  for (const u of everyone) await share(u, false);
  const me = await call(A, 'GET', '/users/me', null, 200);
  assert.equal(me.contributesDurationStats, false, 'the preference is readable by the account itself');
  const search = name => call(A, 'GET', '/game/search?name=' + encodeURIComponent(name), null, 200);
  const games = [];
  for (const n of ['Wingspan', 'Meeple Teste Cooperativo', 'Meeple Teste Competitivo', 'Meeple Teste Solo']) { const g = await search(n); if (g?.id) games.push(g); }
  for (const g of games) for (const u of everyone)
    for (const m of await call(u, 'GET', `/matches/history/game/${g.id}`) || []) // 204 when there are none
      if ((m.scoreSummary || '').startsWith(MARK) && m.creatorId === u.id) await call(u, 'DELETE', `/matches/${m.id}`, null, 204);
  // A game where the three contributing friends have no matches yet: the community set is then exactly what this script adds.
  const clean = async g => { for (const u of [C, D, E]) { const s = (await durations(u, g)).personal; for (const side of [s.solo, s.multiplayer]) if (side.count || side.excludedUnknown || side.excludedMissing || side.excludedZero) return false; } return true; };
  let G = null; for (const g of games) if (await clean(g)) { G = g; break; }
  assert.ok(G, 'no game without matches of the three friends; cannot check the community set exactly');
  pass(`known state: contributions off, earlier runs removed; community checks use «${G.name}»`);

  // 1. Save and reopen: the duration and its method come back as recorded.
  const before = (await durations(A, G)).personal;
  const m1 = await match(A, G, { players: [A, P], minutes: 65, method: 'Estimated' });
  let back = await call(A, 'GET', `/matches/${m1.id}`, null, 200);
  assert.equal(back.durationInMinutes, 65); assert.equal(back.durationMethod, 'Estimated');
  let mine = (await durations(A, G)).personal.multiplayer;
  assert.equal(mine.count, before.multiplayer.count + 1); assert.equal(mine.estimated, before.multiplayer.estimated + 1);
  pass('saved 1 h 05 min as an estimate; reopening shows 65 min · Estimated; «A tua média» counts it once');

  // 2. Correct the duration: the creator changes it; another participant cannot.
  await call(P, 'PATCH', `/matches/${m1.id}/duration`, { durationInMinutes: 70, durationMethod: 'Measured' }, 403);
  await call(A, 'PATCH', `/matches/${m1.id}/duration`, { durationInMinutes: 48, durationMethod: 'Measured' }, 204);
  back = await call(A, 'GET', `/matches/${m1.id}`, null, 200);
  assert.equal(back.durationInMinutes, 48); assert.equal(back.durationMethod, 'Measured');
  mine = (await durations(A, G)).personal.multiplayer;
  assert.equal(mine.count, before.multiplayer.count + 1, 'still one match'); assert.equal(mine.measured, before.multiplayer.measured + 1); assert.equal(mine.estimated, before.multiplayer.estimated);
  pass('the creator corrects it to 48 min · Measured (the other player gets 403); the average updates, still one match');

  // 3. «Não sei»: kept, valid, outside the average; invalid combinations refused.
  await call(A, 'PATCH', `/matches/${m1.id}/duration`, { durationInMinutes: null, durationMethod: 'Unknown' }, 204);
  back = await call(A, 'GET', `/matches/${m1.id}`, null, 200);
  assert.equal(back.durationInMinutes, null); assert.equal(back.durationMethod, 'Unknown');
  mine = (await durations(A, G)).personal.multiplayer;
  assert.equal(mine.count, before.multiplayer.count); assert.equal(mine.excludedUnknown, (before.multiplayer.excludedUnknown ?? 0) + 1);
  for (const bad of [{ durationInMinutes: 0, durationMethod: 'Measured' }, { durationInMinutes: 30, durationMethod: 'Unknown' }, { durationInMinutes: null, durationMethod: 'Estimated' }, { durationInMinutes: 30, durationMethod: 'Guess' }])
    await call(A, 'PATCH', `/matches/${m1.id}/duration`, bad, 400);
  await call(A, 'POST', '/matches', { gameId: G.id, gameName: G.name, matchDate: new Date().toISOString(), isSoloGame: false, playerIds: [A.id, P.id], gameMode: 'COMPETITIVE', result: 'Undefined', resultPlayerIds: [], durationInMinutes: 0, durationMethod: 'Estimated', scoreSummary: MARK, personalRating: 7 }, 400);
  pass('«Não sei» is saved without minutes, stays valid and is left out (counted apart); 0 min measured, minutes with «Não sei» and unknown methods are refused');

  // 4. Old zero: a match from an older client (no method, 0 min) is kept but never counts.
  const m2 = await call(A, 'POST', '/matches', { gameId: G.id, gameName: G.name, matchDate: new Date().toISOString(), isSoloGame: false, playerIds: [A.id, P.id], gameMode: 'COMPETITIVE', result: 'Undefined', resultPlayerIds: [], durationInMinutes: 0, scoreSummary: MARK, personalRating: 7 }, 201);
  back = await call(A, 'GET', `/matches/${m2.id}`, null, 200);
  assert.equal(back.durationInMinutes, 0); assert.equal(back.durationMethod, null);
  mine = (await durations(A, G)).personal.multiplayer;
  assert.equal(mine.count, before.multiplayer.count, 'zero not counted'); assert.equal(mine.excludedZero, (before.multiplayer.excludedZero ?? 0) + 1);
  pass('an old 0 min is preserved as recorded but left out of the average and the valid count');

  // 5. Mode: a solo match goes to «Solo», never mixed with multiplayer.
  const m3 = await match(A, G, { players: [A], minutes: 25, method: 'Estimated', mode: 'SOLO' });
  const both = (await durations(A, G)).personal;
  assert.equal(both.solo.count, before.solo.count + 1); assert.equal(both.multiplayer.count, before.multiplayer.count);
  pass('a solo match counts in Solo only');

  // 6. Community: nothing while nobody contributes; then only matches where every participant contributes.
  let community = (await durations(C, G)).community;
  assert.equal(community.multiplayer.available, false); assert.equal(community.multiplayer.count, 0); assert.equal(community.multiplayer.mean, null);
  for (const u of [C, D, E]) await share(u, true);
  const fixture = [[C, [C, D], 40, 'Measured'], [C, [C, E], 45, 'Estimated'], [D, [D, E], 50, 'Estimated'], [D, [D, C], 55, 'Measured'], [E, [E, C], 60, 'Estimated'], [E, [E, D, C], 180, 'Estimated']];
  const created = [];
  for (const [creator, players, minutes, method] of fixture.slice(0, 4)) created.push(await match(creator, G, { players, minutes, method, summary: FIXTURE }));
  community = (await durations(A, G)).community;
  assert.equal(community.multiplayer.available, false, '4 shared matches: below the minimum');
  for (const [creator, players, minutes, method] of fixture.slice(4)) created.push(await match(creator, G, { players, minutes, method, summary: FIXTURE }));
  const notShared = await match(C, G, { players: [C, P], minutes: 500, method: 'Measured', summary: FIXTURE }); // P does not contribute
  const views = await Promise.all([A, P, C].map(u => durations(u, G)));
  community = views[0].community.multiplayer;
  // 40, 45, 50, 55, 60, 180 → mean 71.7 → 70; median 52.5 → 55 (rounded to 5); 180 is very distant.
  assert.equal(community.available, true); assert.equal(community.count, 6, 'the match with a non-contributing participant does not count');
  assert.equal(community.mean, 70); assert.equal(community.median, 55); assert.equal(community.distant, 1); assert.equal(community.distantValues, null);
  assert.deepEqual([community.measured, community.estimated, community.notStated], [2, 4, 0]);
  for (const v of views) assert.deepEqual(v.community, views[0].community, 'the same value for everyone');
  assert.equal(views[2].viewerContributes, true); assert.equal(views[0].viewerContributes, false);
  pass('6 matches by 3 contributing people: mean 70 min, median 55, 1 very distant; the match with a non-contributing player is left out; same value for everyone');
  const json = JSON.stringify(views[0]);
  for (const leak of [...created, notShared].map(m => m.id).concat([C.id, D.id, E.id, P.id])) assert.ok(!json.includes(leak), 'no match or person identifiers in the response');
  pass('the response carries aggregates only: no match ids, no person ids');

  // 7. Withdrawal: one participant switching off removes every match they are in, from the next read.
  await share(E, false);
  community = (await durations(A, G)).community.multiplayer;
  assert.equal(community.available, false, 'only C–D matches remain (2): below the minimum');
  await share(E, true);
  community = (await durations(A, G)).community.multiplayer;
  assert.equal(community.count, 6, 'switching on again includes them again');
  pass('amiga-meeples switches off: all her matches leave the community set at once; switching on brings them back');

  // 8. Correction and deletion update the community average; each match once.
  await call(E, 'PATCH', `/matches/${created[5].id}/duration`, { durationInMinutes: 65, durationMethod: 'Measured' }, 204);
  community = (await durations(A, G)).community.multiplayer;
  assert.equal(community.mean, 55); assert.equal(community.distant, 0); assert.equal(community.count, 6);
  await call(C, 'DELETE', `/matches/${notShared.id}`, null, 204);
  await call(E, 'DELETE', `/matches/${created[4].id}`, null, 204);
  community = (await durations(A, G)).community.multiplayer;
  assert.equal(community.count, 5); assert.equal(community.mean, 50);
  created.splice(4, 1, await match(E, G, { players: [E, C], minutes: 60, method: 'Estimated', summary: FIXTURE }));
  pass('correcting 180 → 65 min moves the mean to 55; deleting a match removes it (5 left, mean 50)');

  // 9. Cleanup of the personal checks; the community fixture stays for the iPhone.
  for (const m of [m1, m2, m3]) await call(A, 'DELETE', `/matches/${m.id}`, null, 204);
  const after = (await durations(A, G)).personal;
  assert.equal(after.multiplayer.count, before.multiplayer.count); assert.equal(after.solo.count, before.solo.count);
  community = (await durations(P, G)).community.multiplayer;
  console.log(`Left for the iPhone: «${G.name}» with ${community.count} shared matches (mean ${community.mean} min); amiga-colecionadora, amiga-tabuleiro and amiga-meeples contribute; autor and participante do not.`);
  pass('personal check matches removed; averages back to where they were');
})().catch(error => { console.error('FAIL', error.message); process.exit(1); });
