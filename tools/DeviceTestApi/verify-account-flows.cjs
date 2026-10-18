// DeviceTests only: the account restrictions keep every legitimate flow open — against real SQL and real tokens.
//   node tools/DeviceTestApi/verify-account-flows.cjs
// Changes no data: own-profile writes send the value already saved, registration is reached with an invalid body,
// and everything else is a read. Never prints credentials or tokens.
if (require.main !== module) return; // Importing a verifier never runs it: only `node <file>` does (see docs/testes-macos-docker.md).
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../..'), base = 'http://127.0.0.1:5099';
(async () => {
  const health = await (await fetch(base + '/device-test/health')).json();
  assert.deepEqual(health, { environment: 'DeviceTests', database: 'MeepleBoard_DeviceTests', externalDelivery: false });
  const accounts = JSON.parse(fs.readFileSync(path.join(root, '.device-tests/accounts.json'), 'utf8'));
  const login = async name => {
    const a = accounts.find(x => x.Email === name + '@meepleboard.test');
    const r = await fetch(base + '/MeepleBoard/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: a.Email, password: a.Password, deviceInfo: 'verify-account-flows' }) });
    assert.equal(r.status, 200, 'login ' + name); return { name, id: a.Id, token: (await r.json()).token };
  };
  const send = async (u, method, route, body) => {
    const r = await fetch(base + '/MeepleBoard' + route, { method, headers: { ...(u ? { Authorization: `Bearer ${u.token}` } : {}), 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) });
    const text = await r.text(); return { status: r.status, json: text && r.headers.get('content-type')?.includes('json') ? JSON.parse(text) : null };
  };
  const expect = async (u, method, route, status, body) => {
    const r = await send(u, method, route, body); assert.equal(r.status, status, `${u?.name ?? 'visitor'} ${method} ${route} → ${r.status}`); return r.json;
  };
  const pass = name => console.log('PASS ' + name);
  const [A, P, admin] = await Promise.all(['autor', 'participante', 'admin'].map(login));

  // 1. Registration and the rest of the authentication flow need no session.
  for (const route of ['/auth/register', '/auth/forgot-password', '/auth/resend-confirmation', '/auth/reset-password']) {
    const r = await send(null, 'POST', route, {});
    assert.ok(![401, 403].includes(r.status) && r.status < 500, `${route} reachable without a session → ${r.status}`);
  }
  pass('registration, password recovery and confirmation are reachable without a session (invalid bodies refused with 400, no account created)');

  // 2. The own account: read, the two preferences, the push token.
  const me = await expect(A, 'GET', '/users/me', 200);
  assert.equal(me.id, A.id);
  await expect(A, 'PATCH', '/users/me/library-privacy', 204, { libraryPrivacy: me.libraryPrivacy });
  await expect(A, 'PATCH', '/users/me/duration-sharing', 204, { enabled: me.contributesDurationStats });
  await expect(A, 'POST', '/users/push-token', 400, {});
  await expect(null, 'GET', '/users/me', 401);
  pass('the own account is read and its preferences saved (same values sent back); the push-token route answers the account (400 for an empty token); a visitor gets 401');

  // 3. Other accounts: only administrators.
  for (const [method, route, body] of [['GET', '/users'], ['GET', `/users/${P.id}`], ['PUT', `/users/${P.id}`, { id: P.id, userName: 'x', email: 'x@example.invalid' }], ['DELETE', `/users/${P.id}`]])
    await expect(A, method, route, 403, body);
  await expect(A, 'POST', '/game', 403, { name: 'Jogo não autorizado' });
  const other = await expect(admin, 'GET', `/users/${P.id}`, 200);
  assert.equal(other.id, P.id);
  pass('another account cannot be listed, read, edited or deleted by a regular account (403); an administrator reads it');

  // 4. Every read the app makes, as a regular account: none refused, none failing.
  const G = (await expect(A, 'GET', '/game/search?name=Wingspan', 200));
  const last = await send(A, 'GET', '/matches/last');
  const history = (await send(A, 'GET', `/matches/history/game/${G.id}`)).json || [];
  const matchId = last.json?.id ?? history[0]?.id;
  // The same period parameters the app sends for the statistics screens.
  const period = 'start=2026-01-01&endExclusive=2027-01-01&timeZone=Europe%2FLisbon';
  const reads = ['/users/me', '/matches/last', '/matches/pending-journal', '/game-resources/permissions', '/friendships', '/friendships/requests/incoming',
    '/friendships/requests/outgoing', '/friendships/search?query=Teste', `/friendships/profile/${P.id}`, `/friendships/${P.id}/matches`,
    `/friendships/${P.id}/matches/game/${G.id}`, `/game/${G.id}`, `/game/${G.id}/durations`, `/game/${G.id}/experiences`, `/game/${G.id}/friend-owners`,
    `/game/${G.id}/resources`, `/matches/history/game/${G.id}`, `/users/${A.id}/games`, `/users/${A.id}/played-games`, '/session/mine', '/campaigns/mine',
    '/game/rankings', '/game/rankings/mine', '/game/suggestions?query=Wing', '/game/base-search?query=Wing', '/BGG/hot', `/statistics/me?${period}`, `/statistics/me/explore?${period}`, `/statistics/me/company?${period}&groupContext=all`, `/statistics/me/matches?${period}&metric=all&offset=0&limit=25&groupContext=all`, '/statistics/me/year?year=2026&timeZone=Europe%2FLisbon',
    ...(matchId ? [`/matches/${matchId}`, `/campaigns/matches/${matchId}/journal`, `/campaigns/matches/${matchId}/journal/publication`] : [])];
  const results = [];
  for (const route of reads) results.push([route, (await send(A, 'GET', route)).status]);
  const blocked = results.filter(([, s]) => s === 401 || s === 403 || s >= 500);
  assert.deepEqual(blocked, [], 'reads the app makes must not be refused or fail');
  const byStatus = results.reduce((m, [, s]) => (m[s] = (m[s] || 0) + 1, m), {});
  pass(`${results.length} reads the app makes, as a regular account: none refused or failing (${Object.entries(byStatus).map(([s, n]) => `${n}× ${s}`).join(', ')})`);

  // 5. The administrator screens: administrators only.
  for (const route of ['/admin/experiences/reported', '/admin/game-resources', '/admin/game-resources/reports']) {
    await expect(A, 'GET', route, 403);
    await expect(admin, 'GET', route, 200);
  }
  pass('the administrator screens answer administrators (200) and refuse regular accounts (403)');
})().catch(error => { console.error('FAIL', error.message); process.exit(1); });
