// DeviceTests only: deleting an account with activity anonymises it, all or nothing — against real SQL.
//   node tools/DeviceTestApi/verify-account-deletion.cjs
// Uses the disposable «eliminar» account (the host creates it again, new and empty, on its next start).
// Gives it a friendship, a library entry, a shared rated match with a note and a publication, and a report on
// someone else's comment; an administrator deletes it; then checks what goes and what stays. The match it
// shared is deleted again at the end, so averages return to where they were. Never prints credentials or tokens.
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../..'), base = 'http://127.0.0.1:5099';
const MARK = 'Verificação de eliminação de conta';
(async () => {
  const health = await (await fetch(base + '/device-test/health')).json();
  assert.deepEqual(health, { environment: 'DeviceTests', database: 'MeepleBoard_DeviceTests', externalDelivery: false });
  // «eliminar» lives in its own file, so the verifiers that sign in with every account never meet it deleted.
  const accounts = [...JSON.parse(fs.readFileSync(path.join(root, '.device-tests/accounts.json'), 'utf8')),
    JSON.parse(fs.readFileSync(path.join(root, '.device-tests/disposable-account.json'), 'utf8'))];
  const signIn = name => {
    const a = accounts.find(x => x.Email === name + '@meepleboard.test');
    return fetch(base + '/MeepleBoard/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: a.Email, password: a.Password, deviceInfo: 'verify-account-deletion' }) });
  };
  const login = async name => {
    const r = await signIn(name);
    if (r.status !== 200 && name === 'eliminar') { console.log('SKIP «eliminar» was deleted by an earlier run; restart the DeviceTests host to recreate it.'); process.exit(0); }
    assert.equal(r.status, 200, 'login ' + name);
    const a = accounts.find(x => x.Email === name + '@meepleboard.test');
    const body = await r.json();
    return { name, id: a.Id, token: body.token, refresh: body.refreshToken };
  };
  const call = async (u, method, route, body, expected) => {
    const r = await fetch(base + '/MeepleBoard' + route, { method, headers: { Authorization: `Bearer ${u.token}`, 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined });
    if (expected) assert.equal(r.status, expected, `${u.name} ${method} ${route} → ${r.status} ${await r.clone().text()}`);
    return r.status === 204 ? null : r.headers.get('content-type')?.includes('json') ? r.json() : r.text();
  };
  const pass = name => console.log('PASS ' + name);
  const [admin, A, X] = await Promise.all(['admin', 'autor', 'eliminar'].map(login));
  const G = await call(A, 'GET', '/game/search?name=' + encodeURIComponent('Meeple Teste Cooperativo'), null, 200);
  const game = () => call(A, 'GET', `/game/${G.id}`, null, 200);
  // Matches left by an interrupted run go first.
  for (const old of await call(A, 'GET', `/matches/history/game/${G.id}`) || [])
    if ((old.scoreSummary || '').startsWith(MARK) && old.creatorId === A.id) await call(A, 'DELETE', `/matches/${old.id}`, null, 204);
  const before = await game();

  // 1. Activity that used to block the deletion.
  await call(A, 'POST', `/friendships/request/${X.id}`, null);
  const incoming = await call(X, 'GET', '/friendships/requests/incoming', null, 200) || [];
  const request = incoming.find(r => (r.fromUserId ?? r.FromUserId) === A.id);
  assert.ok(request, 'friend request received');
  await call(X, 'POST', `/friendships/accept/${request.requestId ?? request.RequestId ?? request.id}`, null);
  assert.ok((await call(A, 'GET', '/friendships', null, 200) || []).some(f => (f.id ?? f.userId) === X.id), 'friends before the deletion');
  await call(X, 'POST', `/users/${X.id}/games`, { gameId: G.id, gameName: G.name, status: 1 }, 201);
  const m = await call(A, 'POST', '/matches', { gameId: G.id, gameName: G.name, matchDate: new Date(Date.now() - 3600e3).toISOString(), isSoloGame: false,
    playerIds: [A.id, X.id], gameMode: 'COMPETITIVE', result: 'Undefined', resultPlayerIds: [], durationInMinutes: 60, durationMethod: 'Measured', scoreSummary: MARK, personalRating: 7 }, 201);
  await call(X, 'PATCH', '/users/me/duration-sharing', { enabled: true }, 204);
  assert.equal((await call(admin, 'GET', `/users/${X.id}`, null, 200)).contributesDurationStats, true, 'shares durations before the deletion');
  await call(X, 'PUT', `/campaigns/matches/${m.id}/journal`, { personalRating: 9, notes: 'PRIVADO_ELIMINAR' }, 200);
  await call(A, 'PUT', `/campaigns/matches/${m.id}/journal/publication`, { showRating: true, comment: 'Comentário do autor para a verificação', publishComment: true }, 200);
  await call(X, 'PUT', `/campaigns/matches/${m.id}/journal/publication`, { showRating: true, comment: 'Comentário que deve desaparecer', publishComment: true }, 200);
  const listed = (await call(A, 'GET', `/game/${G.id}/experiences?limit=50`, null, 200)).items;
  const authorPublication = listed.find(e => e.comment === 'Comentário do autor para a verificação');
  assert.ok(listed.some(e => e.comment === 'Comentário que deve desaparecer'), 'the publication is public before the deletion');
  await call(X, 'POST', `/experiences/${authorPublication.id}/reports`, { reason: 'Spam', detail: null }, 204);
  const rated = await game();
  assert.equal(rated.meepleBoardRatingsCount, before.meepleBoardRatingsCount + 2, 'both ratings count before the deletion');
  pass(`activity created: friendship, library, a shared match rated 7 and 9 with a note, two publications, a report by the account (${G.name}: ${rated.meepleBoardRatingsCount} ratings)`);

  // 2. Only administrators delete accounts; once.
  await call(A, 'DELETE', `/users/${X.id}`, null, 403);
  await call(admin, 'DELETE', `/users/${X.id}`, null, 204);
  await call(admin, 'DELETE', `/users/${X.id}`, null, 404);
  pass('an account with activity is deleted by an administrator (other accounts 403; a second time 404)');

  // 3. What goes.
  // A token and a refresh token issued before the deletion give no access.
  const old = { ...X, name: 'eliminar (token anterior)' };
  await call(old, 'GET', '/users/me', null, 401);
  await call(old, 'GET', '/matches/pending-journal', null, 401);
  await call(old, 'POST', '/matches', { gameId: G.id, gameName: G.name, matchDate: new Date().toISOString(), isSoloGame: true, playerIds: [X.id], scoreSummary: MARK }, 401);
  const renewed = await fetch(base + '/MeepleBoard/auth/refresh-token', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ refreshToken: X.refresh }) });
  assert.notEqual(renewed.status, 200, 'the refresh token issued before the deletion is not renewed');
  pass(`a token issued before the deletion is refused (401 on reading, on its own profile and on writing) and its refresh token is not renewed (${renewed.status})`);
  assert.notEqual((await signIn('eliminar')).status, 200, 'the deleted account can no longer sign in');
  const shared = await call(A, 'GET', `/matches/${m.id}`, null, 200);
  const ghost = shared.players.find(p => p.userId === X.id);
  assert.ok(ghost && /^deleted:[0-9a-f]{8}$/.test(ghost.userName), 'the shared match keeps an anonymised player');
  const entries = await call(A, 'GET', `/campaigns/matches/${m.id}/journal`, null, 200);
  assert.ok(!entries.some(e => e.userId === X.id) && !JSON.stringify(entries).includes('PRIVADO_ELIMINAR'), 'the diary, note and rating are gone');
  const after = await game();
  assert.equal(after.meepleBoardRatingsCount, rated.meepleBoardRatingsCount - 1, 'the rating left the MeepleBoard average');
  const listedAfter = (await call(A, 'GET', `/game/${G.id}/experiences?limit=50`, null, 200)).items;
  assert.ok(!listedAfter.some(e => e.comment === 'Comentário que deve desaparecer'), 'the publication is gone');
  assert.ok(!(await call(A, 'GET', '/friendships', null, 200) || []).some(f => (f.id ?? f.userId) === X.id), 'the friendship is gone');
  const found = await call(A, 'GET', '/friendships/search?query=' + encodeURIComponent('deleted'), null, 200) || [];
  assert.ok(!JSON.stringify(found).includes(X.id), 'a deleted account is not found in people search');
  const user = await call(admin, 'GET', `/users/${X.id}`, null, 200);
  assert.ok(!user.email && !user.profilePictureUrl, 'no email or picture left');
  assert.equal(user.contributesDurationStats, false, 'no longer contributes to community durations');
  assert.equal(shared.durationInMinutes, 60, 'the other player keeps the match and its duration');
  assert.ok(shared.players.some(p => p.userId === A.id), 'the other player is still in it');
  pass('the deleted account no longer contributes to community durations (a match counts only when every participant shares); the other player keeps the match and its 60 min');
  pass('sign-in closed; diary, note, rating and publication gone; friendship gone; not searchable; no email; the shared match keeps «' + ghost.userName + '»');

  // 4. What stays: the report still counts, without naming anyone.
  const reported = await call(admin, 'GET', '/admin/experiences/reported', null, 200);
  const kept = reported.find(r => r.id === authorPublication.id);
  assert.ok(kept && kept.reasons.Spam === 1, 'the report made by the deleted account still counts');
  await call(admin, 'POST', `/admin/experiences/${authorPublication.id}/keep`, { reason: null }, 204);
  pass('the report by the deleted account is kept and still counts for moderation (resolved again here)');

  // 5. Clean up: the match goes, and the average is back to where it was.
  await call(A, 'DELETE', `/matches/${m.id}`, null, 204);
  const end = await game();
  assert.equal(end.meepleBoardRatingsCount, before.meepleBoardRatingsCount, 'ratings back to the start');
  assert.equal(end.meepleBoardScore, before.meepleBoardScore, 'mean back to the start');
  pass(`verification match removed; «${G.name}» back to ${end.meepleBoardScore ?? '—'} with ${end.meepleBoardRatingsCount} ratings. Restart the host to recreate «eliminar».`);
})().catch(error => { console.error('FAIL', error.message); process.exit(1); });
