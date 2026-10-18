// DeviceTests only, read-only: a snapshot of the iPhone walkthrough data (accounts in accounts.json: autor, participante,
// alheio, membro), to prove later that a verifier or a change did not touch it — by content, not just counts.
//   node tools/DeviceTestApi/roteiro-snapshot.cjs save      → .device-tests/roteiro-snapshot.json (git-ignored)
//   node tools/DeviceTestApi/roteiro-snapshot.cjs compare   → differences by section, exit 1 if any
// Only sign-in (POST) and GET requests. Never prints credentials, tokens, notes or other private text (notes are hashed).
// Time-dependent fields (a session's computed status, its end once the automatic close passes) are compared apart and
// reported without failing.
if (require.main !== module) return; // Importing a verifier never runs it: only `node <file>` does (see docs/testes-macos-docker.md).
const fs = require('node:fs'), path = require('node:path'), crypto = require('node:crypto');
const root = path.resolve(__dirname, '../..'), base = 'http://127.0.0.1:5099/MeepleBoard', file = path.join(root, '.device-tests/roteiro-snapshot.json');
const hash = text => text == null ? null : crypto.createHash('sha256').update(String(text)).digest('hex').slice(0, 12);
const sortBy = (list, key) => [...list].sort((a, b) => String(a[key]).localeCompare(String(b[key])));
(async () => {
  const mode = process.argv[2];
  if (!['save', 'compare'].includes(mode)) { console.log('usage: roteiro-snapshot.cjs save|compare'); process.exit(2); }
  const health = await (await fetch('http://127.0.0.1:5099/device-test/health')).json();
  if (health.database !== 'MeepleBoard_DeviceTests') throw new Error('not DeviceTests');
  const accounts = JSON.parse(fs.readFileSync(path.join(root, '.device-tests/accounts.json'), 'utf8')).filter(a => /^(autor|participante|alheio|membro)@/.test(a.Email));
  const stable = {}, timeDependent = {}, matchIds = new Set();
  for (const account of accounts) {
    const name = account.Email.split('@')[0];
    const { token } = await (await fetch(base + '/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: account.Email, password: account.Password, deviceInfo: 'roteiro-snapshot' }) })).json();
    // Some lists answer 204 or an empty body when there is nothing: read as empty.
    const get = async route => { const r = await fetch(base + route, { headers: { Authorization: 'Bearer ' + token } }); const text = await r.text(); return r.ok ? (text ? JSON.parse(text) : []) : { status: r.status }; };
    const friends = (await get('/friendships')).map?.(f => f.id ?? f.userId).sort() ?? [];
    const library = sortBy(((await get(`/users/${account.Id}/games`)) || []).map?.(l => ({ game: l.gameId, status: l.status, price: l.pricePaid ?? null })) ?? [], 'game');
    const sessions = [];
    for (const s of (await get('/session/mine')) || []) {
      const d = await get(`/session/${s.id}`);
      sessions.push({ id: s.id, name: d.name, location: d.location ?? null, organizer: d.organizerId, scheduled: d.scheduledStartDate, deadline: d.responseDeadline ?? null,
        plannedGame: d.plannedGameId ?? null, players: sortBy((d.players || []).map(p => ({ user: p.userId, status: p.status })), 'user'), matches: (d.matches || []).map(m => m.id).sort() });
      timeDependent[`${name}:session:${s.id}`] = { status: d.status, end: d.endDate ?? null, auto: d.closedAutomatically ?? null };
    }
    const campaigns = [];
    for (const c of (await get('/campaigns/mine')) || []) {
      const d = await get(`/campaigns/${c.id}`);
      campaigns.push({ id: c.id, name: d.name, notes: hash(d.notes), manager: d.creatorId, status: d.status,
        members: sortBy((d.members || []).map(m => ({ user: m.userId, author: m.isCreator, status: m.status })), 'user'),
        encounters: sortBy((d.matches || []).map(m => ({ match: m.matchId, number: m.sessionNumber ?? null, title: m.sessionTitle ?? null })), 'match') });
    }
    let ids = [];
    for (let offset = 0; ; offset += 100) {
      const page = await get(`/statistics/me/matches?start=2017-01-02&endExclusive=2027-01-01&timeZone=Europe%2FLisbon&metric=all&offset=${offset}&limit=100`);
      ids.push(...page.items.map(i => i.id)); if (ids.length >= page.total) break;
    }
    ids.forEach(id => matchIds.add(id));
    stable[name] = { friends, library, sessions: sortBy(sessions, 'id'), campaigns: sortBy(campaigns, 'id'), matches: ids.sort() };
    stable[name].token = token;
  }
  // Each match once: shared data and every participant's diary (ratings, tags, photo count; notes hashed).
  const reader = stable[accounts[0].Email.split('@')[0]].token, tokens = Object.values(stable).map(s => s.token);
  const matches = {};
  for (const id of [...matchIds].sort()) {
    let detail = null, journal = null;
    for (const token of [reader, ...tokens]) {
      const r = await fetch(`${base}/matches/${id}`, { headers: { Authorization: 'Bearer ' + token } });
      if (r.ok) { detail = await r.json(); const j = await fetch(`${base}/campaigns/matches/${id}/journal`, { headers: { Authorization: 'Bearer ' + token } }); const jt = await j.text(); journal = j.ok && jt ? JSON.parse(jt) : []; break; }
    }
    matches[id] = detail && { game: detail.gameId, date: detail.matchDate, mode: detail.gameMode ?? null, result: detail.result ?? null, winner: detail.winnerId ?? null,
      duration: detail.durationInMinutes ?? null, location: detail.location ?? null, summary: detail.scoreSummary ?? null, session: detail.gameSessionId ?? null,
      players: sortBy((detail.players || []).map(p => ({ user: p.userId, score: p.score ?? null, outcome: p.outcome ?? null })), 'user'),
      diaries: sortBy((journal || []).map(e => ({ user: e.userId, rating: e.personalRating ?? null, notes: hash(e.notes), tags: e.tags ?? null, photos: (e.photoUrls || []).length })), 'user') };
  }
  for (const s of Object.values(stable)) delete s.token;
  const snapshot = { takenAt: new Date().toISOString(), accounts: stable, matches, timeDependent };
  if (mode === 'save') {
    fs.writeFileSync(file, JSON.stringify(snapshot, null, 1), { mode: 0o600 });
    console.log(`saved: ${Object.keys(stable).length} accounts, ${Object.keys(matches).length} matches, ${Object.values(stable).reduce((n, s) => n + s.sessions.length, 0)} session views, ${Object.values(stable).reduce((n, s) => n + s.campaigns.length, 0)} campaign views`);
    return;
  }
  const before = JSON.parse(fs.readFileSync(file, 'utf8'));
  const differences = [];
  const compare = (label, a, b) => { if (JSON.stringify(a) !== JSON.stringify(b)) differences.push(label); };
  for (const name of new Set([...Object.keys(before.accounts), ...Object.keys(stable)]))
    for (const section of ['friends', 'library', 'sessions', 'campaigns', 'matches']) compare(`${name}.${section}`, before.accounts[name]?.[section], stable[name]?.[section]);
  for (const id of new Set([...Object.keys(before.matches), ...Object.keys(matches)])) compare(`match ${id}`, before.matches[id], matches[id]);
  const timed = Object.keys({ ...before.timeDependent, ...timeDependent }).filter(k => JSON.stringify(before.timeDependent[k]) !== JSON.stringify(timeDependent[k]));
  console.log(`compared with the snapshot of ${before.takenAt}: ${differences.length} differences` + (differences.length ? '\n  ' + differences.join('\n  ') : ''));
  if (timed.length) console.log(`time-dependent changes (not failures): ${timed.length}\n  ` + timed.map(k => `${k}: ${JSON.stringify(before.timeDependent[k])} → ${JSON.stringify(timeDependent[k])}`).join('\n  '));
  process.exit(differences.length ? 1 : 0);
})().catch(error => { console.error('FAIL', error.message); process.exit(1); });
