// DeviceTests only: fresh people for one verifier run (DisposableFixture). Not a verifier: importing it does nothing.
//   const { run } = require('./disposable.cjs');
//   run({ label: 'x', people: [{ role: 'autor' }, { role: 'participante' }], friends: [['autor', 'participante']] }, async world => { … });
// The people exist only for that run and are removed with everything they made when it ends, also when it fails or is
// interrupted (Ctrl+C); people left by a run that was killed are swept by the next one. Never prints credentials or tokens.
const assert = require('node:assert/strict');
const base = 'http://127.0.0.1:5099';

async function fixture(method, route, body) {
  const r = await fetch(`${base}/device-test/disposable${route}`, { method, headers: { 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined });
  const text = await r.text();
  if (!r.ok) throw new Error(`disposable fixture ${method} ${route.split('?')[0]} → ${r.status} ${text.slice(0, 200)}`);
  return text ? JSON.parse(text) : null;
}

/** The roles of the iPhone walkthrough with its friendships (autor is friends with participante, alheio and membro), but new people. */
const quartet = (extra = {}) => ({
  people: [{ role: 'autor' }, { role: 'participante' }, { role: 'alheio' }, { role: 'membro' }, ...(extra.people || [])],
  friends: [['autor', 'participante'], ['autor', 'alheio'], ['autor', 'membro'], ...(extra.friends || [])],
  pending: extra.pending || [],
});

async function run(spec, body) {
  const health = await (await fetch(base + '/device-test/health')).json();
  assert.deepEqual(health, { environment: 'DeviceTests', database: 'MeepleBoard_DeviceTests', externalDelivery: false });
  const swept = await fixture('POST', '/sweep?olderThanMinutes=30');
  if (swept.removed) console.log(`(removed ${swept.removed} disposable people left by an interrupted run)`);
  const made = await fixture('POST', '/people', { label: spec.label, people: spec.people, friends: spec.friends || [], pending: spec.pending || [] });
  const people = Object.fromEntries(Object.entries(made.people).map(([role, p]) =>
    [role, { role, id: p.id, Id: p.id, email: p.email, Email: p.email, password: p.password, Password: p.password, userName: p.userName }]));
  const ids = Object.values(people).map(p => p.id);
  let cleaned = false;
  const cleanup = async () => {
    if (cleaned) return; cleaned = true;
    const r = await fixture('DELETE', `/fixture?people=${ids.join(',')}`);
    assert.equal(r.removed, ids.length, 'every disposable person removed');
    console.log(`CLEAN removed the ${ids.length} disposable people of this run and everything they made`);
  };
  const interrupted = async () => { try { await cleanup(); } finally { process.exit(130); } };
  process.once('SIGINT', interrupted);
  const world = {
    people, stamp: made.stamp,
    /** In the shape of accounts.json, in the order given (for verifiers written against it). */
    accounts: roles => roles.map(r => people[r]),
    async login(role, deviceInfo = 'verify-' + spec.label) {
      const p = people[role];
      const r = await fetch(base + '/MeepleBoard/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: p.email, password: p.password, deviceInfo }) });
      assert.equal(r.status, 200, 'login ' + role);
      const data = await r.json();
      return { name: role, id: p.id, token: data.token, refresh: data.refreshToken };
    },
    schedule: (sessionId, scheduledStartDate, acceptedAt) => fixture('POST', `/sessions/${sessionId}/schedule?scheduledStartDate=${encodeURIComponent(scheduledStartDate)}${acceptedAt ? '&acceptedAt=' + encodeURIComponent(acceptedAt) : ''}`),
  };
  let failure;
  try { await body(world); } catch (error) { failure = error; }
  finally { process.removeListener('SIGINT', interrupted); await cleanup(); }
  if (failure) throw failure;
}

/** People kept for a manual validation (never swept); remove them with remove(ids). */
async function keep(spec) { return fixture('POST', '/people', { label: spec.label, people: spec.people, friends: spec.friends || [], pending: spec.pending || [], keep: true }); }
const remove = ids => fixture('DELETE', `/fixture?people=${ids.join(',')}`);

module.exports = { run, quartet, keep, remove };
