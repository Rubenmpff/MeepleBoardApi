// DeviceTests only: «Terminar sessão» against real SQL — authorization, states, repetition and the race with the
// automatic close. Uses its own disposable accounts and sessions (SessionCloseFixture); the iPhone walkthrough
// accounts and sessions are never touched. Never prints credentials or tokens.
//   node tools/DeviceTestApi/verify-session-close.cjs
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../..'), base = 'http://127.0.0.1:5099';
(async () => {
  const health = await (await fetch(base + '/device-test/health')).json();
  assert.deepEqual(health, { environment: 'DeviceTests', database: 'MeepleBoard_DeviceTests', externalDelivery: false });
  const accounts = JSON.parse(fs.readFileSync(path.join(root, '.device-tests/session-close-accounts.json'), 'utf8'));
  const login = async name => {
    const a = accounts.find(x => x.Email === name + '@meepleboard.test');
    const r = await fetch(base + '/MeepleBoard/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: a.Email, password: a.Password, deviceInfo: 'verify-session-close' }) });
    assert.equal(r.status, 200, 'login ' + name);
    return { name, id: a.Id, token: (await r.json()).token };
  };
  const call = async (u, method, route, body, expected) => {
    const r = await fetch(base + '/MeepleBoard' + route, { method, headers: { ...(u ? { Authorization: `Bearer ${u.token}` } : {}), 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined });
    const text = await r.text();
    if (expected) assert.equal(r.status, expected, `${u?.name ?? 'anonymous'} ${method} ${route} → ${r.status} ${text}`);
    return { status: r.status, data: text && r.headers.get('content-type')?.includes('json') ? JSON.parse(text) : text };
  };
  const fixture = async (method, route = '') => {
    const r = await fetch(`${base}/device-test/session-close${route}`, { method });
    const text = await r.text();
    return { status: r.status, data: text ? JSON.parse(text) : null };
  };
  const stored = async id => (await fixture('GET', `/${id}`)).data;
  const pass = name => console.log('PASS ' + name);
  // SQL returns UTC without a zone designator.
  const utc = value => Date.parse(/Z|[+-]\d\d:\d\d$/.test(value) ? value : value + 'Z');
  const [organizer, guest] = await Promise.all(['sessao-organiza', 'sessao-convidada'].map(login));

  const created = await fixture('POST', '/fixture');
  assert.equal(created.status, 200, 'fixture: ' + JSON.stringify(created.data));
  const f = created.data;
  pass('disposable sessions created for the two fixture accounts: running, scheduled, past its automatic end and five races');

  // 1. Authorization: nothing changes.
  await call(guest, 'POST', `/session/${f.running}/close`, null, 403);
  await call(null, 'POST', `/session/${f.running}/close`, null, 401);
  await call(organizer, 'POST', `/session/${crypto.randomUUID()}/close`, null, 404);
  await call(guest, 'POST', `/session/${f.scheduled}/cancel`, null, 403);
  await call(null, 'POST', `/session/${f.scheduled}/cancel`, null, 401);
  await call(organizer, 'POST', `/session/${crypto.randomUUID()}/cancel`, null, 404);
  for (const id of [f.running, f.scheduled]) assert.equal((await stored(id)).endDate, null, 'a refused request changed a session');
  pass('the guest gets 403 to terminate and to cancel, without a session 401, an unknown session 404; nothing changed');

  // 2. States: a scheduled session is cancelled, not terminated; a running one is terminated, not cancelled.
  const refused = await call(organizer, 'POST', `/session/${f.scheduled}/close`, null, 409);
  assert.match(refused.data.message, /Cancelar sessão/);
  await call(organizer, 'POST', `/session/${f.running}/cancel`, null, 409);
  assert.equal((await stored(f.scheduled)).endDate, null); assert.equal((await stored(f.running)).isCancelled, false);
  pass('terminating a scheduled session is refused with 409 pointing to «Cancelar sessão»; cancelling a running one is refused with 409');

  // 3. Terminating keeps matches and players, records the actual end, and repeating changes nothing.
  const game = (await call(organizer, 'GET', '/game/search?name=' + encodeURIComponent('Meeple Teste Competitivo'), null, 200)).data;
  const averages = async () => { const g = (await call(organizer, 'GET', `/game/${game.id}`, null, 200)).data; return [g.meepleBoardScore, g.meepleBoardRatingsCount]; };
  const initial = await averages();
  const match = (await call(organizer, 'POST', '/matches', { gameId: game.id, gameName: game.name, gameSessionId: f.running, matchDate: new Date().toISOString(), isSoloGame: false,
    winnerId: organizer.id, playerIds: [organizer.id, guest.id], playerScores: [{ userId: organizer.id, score: 12 }, { userId: guest.id, score: 9 }],
    scoreSummary: 'Verificação: terminar sessão', durationInMinutes: 30, durationMethod: 'Measured', personalRating: 5 }, 201)).data;
  const before = Date.now();
  await call(organizer, 'POST', `/session/${f.running}/close`, null, 200);
  const ended = await stored(f.running);
  assert.ok(ended.endDate && Math.abs(utc(ended.endDate) - before) < 60_000, 'actual end time stored');
  assert.equal(ended.closedAutomatically, false); assert.equal(ended.players, 2); assert.equal(ended.matches, 1);
  const detail = (await call(guest, 'GET', `/session/${f.running}`, null, 200)).data;
  assert.equal(detail.status, 'Closed'); assert.ok(detail.matches.some(m => m.id === match.id), 'the match stays in the session');
  for (let i = 0; i < 3; i++) await call(organizer, 'POST', `/session/${f.running}/close`, null, 200);
  assert.deepEqual(await stored(f.running), ended, 'repeating changed the stored end');
  pass('the organizer terminates the running session: actual end stored, not automatic, 2 players and its match kept; three repeats return 200 and change nothing');

  // 4. After terminating: no new matches in it, and no cancelling.
  const late = await call(organizer, 'POST', '/matches', { gameId: game.id, gameName: game.name, gameSessionId: f.running, matchDate: new Date().toISOString(), isSoloGame: false,
    winnerId: organizer.id, playerIds: [organizer.id, guest.id], scoreSummary: 'Verificação: depois de terminar', durationInMinutes: 10, durationMethod: 'Measured', personalRating: 5 }, 409);
  assert.match(late.data.message, /encerrada/);
  await call(organizer, 'POST', `/session/${f.running}/cancel`, null, 409);
  assert.equal((await stored(f.running)).matches, 1);
  pass('a terminated session refuses new matches (409) and cancelling (409)');

  // 5. Past its automatic end but not yet stored: terminating keeps the automatic end.
  await call(organizer, 'POST', `/session/${f.due}/close`, null, 200);
  const due = await stored(f.due);
  assert.equal(utc(due.endDate), Date.parse(f.dueEnd), 'the automatic end is kept'); assert.equal(due.closedAutomatically, true);
  pass('terminating after the automatic end (or racing the job for it) stores that end, marked automatic');

  // 6. Race: several manual closes (before the automatic end) and automatic closes (at it), all at once.
  const outcomes = { manual: 0, automatic: 0 };
  for (const race of f.races) {
    const requests = [];
    for (let i = 0; i < 6; i++) {
      requests.push(call(organizer, 'POST', `/session/${race.id}/close`, null).then(r => assert.equal(r.status, 200, 'manual close in the race')));
      requests.push(fixture('POST', `/${race.id}/auto?at=${encodeURIComponent(race.autoCloseAt)}`).then(r => assert.equal(r.status, 200, 'automatic close in the race')));
    }
    await Promise.all(requests);
    const s = await stored(race.id);
    const automatic = utc(s.endDate) === Date.parse(race.autoCloseAt);
    assert.equal(s.closedAutomatically, automatic, `mixed state: end ${s.endDate}, automatic ${s.closedAutomatically}`);
    if (!automatic) assert.ok(utc(s.endDate) < Date.parse(race.autoCloseAt), 'a manual end is before the automatic one');
    outcomes[automatic ? 'automatic' : 'manual']++;
    await call(organizer, 'POST', `/session/${race.id}/close`, null, 200);
    await fixture('POST', `/${race.id}/auto?at=${encodeURIComponent(race.autoCloseAt)}`);
    assert.deepEqual(await stored(race.id), s, 'a close after the race changed the stored end');
  }
  pass(`5 races of 6 manual + 6 automatic closes each: one consistent end per session (manual first ${outcomes.manual}, automatic first ${outcomes.automatic}); later closes change nothing`);

  // 7. Cancelling the scheduled session works; then everything disposable goes.
  await call(organizer, 'POST', `/session/${f.scheduled}/cancel`, null, 204);
  assert.equal((await fixture('GET', `/${f.scheduled}`)).status, 404);
  await call(organizer, 'DELETE', `/matches/${match.id}`, null, 204);
  assert.equal((await fixture('DELETE', '/fixture')).status, 204);
  assert.deepEqual(await averages(), initial, 'the game average is back to where it was');
  pass('the organizer cancels the scheduled session (204, removed); the verification match and the disposable sessions are deleted; the game average is back to where it was');
})().catch(error => { console.error('FAIL', error.message); process.exit(1); });
