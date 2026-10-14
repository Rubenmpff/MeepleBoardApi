// DeviceTests only: automatic session end against real SQL through the running test API (port 5099).
//   node tools/DeviceTestApi/verify-session-auto-close.cjs            full check; leaves one fictitious running session
//   node tools/DeviceTestApi/verify-session-auto-close.cjs --recheck  after restarting the API: ends and limits unchanged
// Uses the fictitious "autor" (organizer) and "participante" accounts; never prints credentials or tokens.
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../..'), base = 'http://127.0.0.1:5099';
const state = path.join(root, '.device-tests/session-auto-close.json');
const HOUR = 3600000, sleep = ms => new Promise(r => setTimeout(r, ms));
(async () => {
  const health = await (await fetch(base + '/device-test/health')).json();
  assert.deepEqual(health, { environment: 'DeviceTests', database: 'MeepleBoard_DeviceTests', externalDelivery: false });
  const accounts = JSON.parse(fs.readFileSync(path.join(root, '.device-tests/accounts.json'), 'utf8'));
  const login = async email => { const a = accounts.find(x => x.Email === email); const r = await fetch(base + '/MeepleBoard/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: a.Email, password: a.Password, deviceInfo: 'verify-session-auto-close' }) }); assert.equal(r.status, 200); return { id: a.Id, token: (await r.json()).token }; };
  const call = async (u, method, route, body) => { const r = await fetch(base + '/MeepleBoard' + route, { method, headers: { Authorization: `Bearer ${u.token}`, 'Content-Type': 'application/json' }, body: body && JSON.stringify(body) }); const t = await r.text(); return { status: r.status, data: t ? JSON.parse(t) : null }; };
  const organizer = await login('autor@meepleboard.test'), guest = await login('participante@meepleboard.test');
  const mine = async () => (await call(organizer, 'GET', '/session/mine')).data;
  const pass = name => console.log('PASS ' + name);

  if (process.argv.includes('--recheck')) {
    const saved = JSON.parse(fs.readFileSync(state, 'utf8'));
    const now = Object.fromEntries((await mine()).map(s => [s.id, s]));
    for (const [id, before] of Object.entries(saved.sessions)) {
      assert.deepEqual([now[id].status, now[id].endDate, now[id].autoCloseAt, now[id].latestCloseAt], before, 'changed after restart: ' + id);
    }
    pass(`after restart: ${Object.keys(saved.sessions).length} sessions keep status, end and limit`);
    return;
  }

  // Legacy sessions left active: closed at the scheduled time + 24 h (or the chosen end), with players and matches kept.
  const sessions = await mine();
  const auto = sessions.filter(s => s.closedAutomatically);
  assert.ok(auto.length > 0, 'expected legacy sessions closed automatically');
  for (const s of auto) {
    assert.equal(s.status, 'Closed');
    assert.equal(Date.parse(s.endDate), Date.parse(s.autoCloseAt), 'endDate is the due time: ' + s.name);
    assert.ok(Date.parse(s.autoCloseAt) <= Date.parse(s.latestCloseAt) && Date.parse(s.autoCloseAt) - Date.parse(s.scheduledStartDate) >= 0);
    assert.ok(!sessions.some(x => x.id === s.id && x.status === 'Active'));
  }
  const withMatches = [];
  for (const s of auto) { const d = (await call(organizer, 'GET', '/session/' + s.id)).data; if (d.matches.length) withMatches.push([d.name, d.matches.length, d.players.length]); }
  assert.ok(withMatches.length > 0, 'a closed session with matches should keep them');
  pass(`${auto.length} legacy sessions ended at their due time; ${withMatches.length} keep matches (e.g. ${withMatches[0][1]} matches, ${withMatches[0][2]} players)`);

  // Running the job again (the host runs it every minute) changes nothing.
  const before = JSON.stringify(auto.map(s => [s.id, s.endDate]));
  await sleep(65000);
  const after = JSON.stringify((await mine()).filter(s => s.closedAutomatically && auto.some(a => a.id === s.id)).map(s => [s.id, s.endDate]));
  assert.equal(after, before);
  pass('the job ran again without changing ended sessions');

  // A new session: starts at its scheduled time, ends 24 h later, response deadline independent, organizer-only changes within 48 h.
  const scheduled = new Date(Date.now() + 40000);
  const created = await call(organizer, 'POST', '/session', { name: 'Sessão de teste · fim automático', location: 'DeviceTests', scheduledStartDate: scheduled.toISOString(), responseDeadline: new Date(Date.now() + 20000).toISOString(), playerIds: [guest.id] });
  assert.equal(created.status, 201, JSON.stringify(created.data));
  const id = created.data.id;
  // Before anyone confirms, start and end are already known to everyone.
  const unconfirmed = (await call(guest, 'GET', '/session/' + id)).data;
  assert.equal(unconfirmed.status, 'Upcoming');
  assert.equal(Date.parse(unconfirmed.autoCloseAt) - Date.parse(unconfirmed.scheduledStartDate), 24 * HOUR, 'end known without confirmed guests');
  assert.equal(Date.parse(unconfirmed.latestCloseAt) - Date.parse(unconfirmed.scheduledStartDate), 48 * HOUR);
  assert.equal((await call(guest, 'POST', `/session/${id}/invites/respond`, { accept: true })).status, 200);
  const waiting = (await call(organizer, 'GET', '/session/' + id)).data;
  assert.equal(waiting.status, 'Upcoming');
  assert.equal(waiting.autoCloseAt, unconfirmed.autoCloseAt, 'accepting does not move the end');
  assert.equal((await call(organizer, 'PUT', `/session/${id}/close-time`, { closesAt: new Date(Date.now() + 5 * HOUR).toISOString() })).status, 409, 'not before it starts');
  await sleep(Math.max(0, scheduled - Date.now()) + 2000);
  const running = (await call(organizer, 'GET', '/session/' + id)).data;
  assert.equal(running.status, 'Active');
  const start = Date.parse(running.scheduledStartDate);
  assert.equal(Date.parse(running.latestCloseAt) - start, 48 * HOUR);
  assert.notEqual(Date.parse(running.autoCloseAt), Date.parse(running.effectiveDeadline), 'independent of the response deadline');
  pass('new session starts at its scheduled time and ends 24 h later; the response deadline is separate');

  const target = new Date(start + 30 * HOUR).toISOString();
  assert.equal((await call(guest, 'PUT', `/session/${id}/close-time`, { closesAt: target })).status, 403, 'guest cannot change the end');
  assert.equal((await call(organizer, 'PUT', `/session/${id}/close-time`, { closesAt: new Date(start + 49 * HOUR).toISOString() })).status, 400, 'beyond 48 h');
  assert.equal((await call(organizer, 'PUT', `/session/${id}/close-time`, { closesAt: new Date(Date.now() - HOUR).toISOString() })).status, 400, 'past');
  const extended = await call(organizer, 'PUT', `/session/${id}/close-time`, { closesAt: target });
  assert.equal(extended.status, 200);
  const reread = (await call(organizer, 'GET', '/session/' + id)).data;
  assert.equal(Date.parse(reread.autoCloseAt), Date.parse(target));
  assert.ok(reread.closeTimeChosen && reread.status === 'Active');
  const seenByGuest = (await call(guest, 'GET', '/session/' + id)).data;
  assert.equal(Date.parse(seenByGuest.autoCloseAt), Date.parse(target));
  pass('organizer extends to 30 h (persisted, visible to the guest); guest, past and beyond 48 h are refused');

  const keep = Object.fromEntries((await mine()).filter(s => s.id === id || s.closedAutomatically).map(s => [s.id, [s.status, s.endDate, s.autoCloseAt, s.latestCloseAt]]));
  fs.writeFileSync(state, JSON.stringify({ sessions: keep }, null, 1));
  console.log(`Left running for the iPhone route: "${running.name}" (ends ${new Date(target).toISOString()}). Restart the API and run with --recheck.`);
})().catch(error => { console.error('FAIL', error.message); process.exit(1); });
