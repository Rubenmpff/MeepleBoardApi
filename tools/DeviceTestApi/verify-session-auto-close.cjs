// DeviceTests only: automatic session end against real SQL through the running test API (port 5099).
//   node tools/DeviceTestApi/verify-session-auto-close.cjs
// Fresh people (disposable.cjs): «autor» organizes, «participante» is the guest; removed at the end, also on failure.
// Sessions already past their end are made by moving a session of these people back in time, then the host's job
// (every minute) ends them. Never the iPhone accounts; never prints credentials or tokens.
if (require.main !== module) return; // Importing a verifier never runs it: only `node <file>` does (see docs/testes-macos-docker.md).
const assert = require('node:assert/strict');
const { run } = require('./disposable.cjs');
const base = 'http://127.0.0.1:5099';
const HOUR = 3600000, sleep = ms => new Promise(r => setTimeout(r, ms));
run({ label: 'autoclose', people: [{ role: 'autor' }, { role: 'participante' }], friends: [['autor', 'participante']] }, async world => {
  const call = async (u, method, route, body) => { const r = await fetch(base + '/MeepleBoard' + route, { method, headers: { Authorization: `Bearer ${u.token}`, 'Content-Type': 'application/json' }, body: body && JSON.stringify(body) }); const t = await r.text(); return { status: r.status, data: t ? JSON.parse(t) : null }; };
  const organizer = await world.login('autor'), guest = await world.login('participante');
  const mine = async () => (await call(organizer, 'GET', '/session/mine')).data;
  const pass = name => console.log('PASS ' + name);
  // Two sessions that ran and are past their end (one with a match), as legacy sessions left active would be.
  const game = (await call(organizer, 'GET', '/game/search?name=' + encodeURIComponent('Meeple Teste Competitivo'))).data;
  const legacy = [];
  for (const [name, withMatch] of [['Sessão antiga com partida', true], ['Sessão antiga sem partida', false]]) {
    const s = (await call(organizer, 'POST', '/session', { name, scheduledStartDate: new Date().toISOString(), playerIds: [guest.id] })).data;
    assert.equal((await call(guest, 'POST', `/session/${s.id}/invites/respond`, { accept: true })).status, 200);
    if (withMatch) assert.equal((await call(organizer, 'POST', '/matches', { gameId: game.id, gameName: game.name, gameSessionId: s.id, matchDate: new Date().toISOString(), isSoloGame: false,
      winnerId: organizer.id, playerIds: [organizer.id, guest.id], personalRating: 7 })).status, 201);
    const started = new Date(Date.now() - 30 * HOUR);
    await world.schedule(s.id, started.toISOString(), new Date(started.getTime() - HOUR).toISOString());
    legacy.push(s.id);
  }
  // The host's job runs every minute.
  let auto = [];
  for (let i = 0; i < 16 && auto.length < legacy.length; i++) { await sleep(5000); auto = (await mine()).filter(s => legacy.includes(s.id) && s.closedAutomatically); }
  assert.equal(auto.length, legacy.length, 'the job ended both sessions');
  for (const s of auto) {
    assert.equal(s.status, 'Closed');
    assert.equal(Date.parse(s.endDate), Date.parse(s.autoCloseAt), 'endDate is the due time: ' + s.name);
    assert.ok(Date.parse(s.autoCloseAt) <= Date.parse(s.latestCloseAt) && Date.parse(s.autoCloseAt) - Date.parse(s.scheduledStartDate) >= 0);
  }
  const kept = (await call(organizer, 'GET', '/session/' + legacy[0])).data;
  assert.equal(kept.matches.length, 1); assert.equal(kept.players.length, 2);
  pass('sessions past their end are ended at their due time by the job; players and matches kept');
  const before = JSON.stringify(auto.map(s => [s.id, s.endDate]));
  await sleep(65000);
  const after = JSON.stringify((await mine()).filter(s => legacy.includes(s.id)).map(s => [s.id, s.endDate]));
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

}).catch(error => { console.error('FAIL', error.message); process.exit(1); });
