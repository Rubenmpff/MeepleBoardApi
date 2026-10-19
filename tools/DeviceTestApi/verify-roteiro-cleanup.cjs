// DeviceTests only: the roteiro clean-up procedure (RoteiroCleanupFixture) on disposable data, before it is ever used on
// the rows verifiers left on the iPhone walkthrough accounts:
//   plan (dependents, refusals, hash) → backup (every column) → removal refused without the hash or after a change →
//   removal → restore (every row back, identical) — and the game average follows each step.
//   node tools/DeviceTestApi/verify-roteiro-cleanup.cjs
// Fresh people (disposable.cjs), removed at the end with the backup file of this run. Never the iPhone accounts.
if (require.main !== module) return; // Importing a verifier never runs it: only `node <file>` does (see docs/testes-macos-docker.md).
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const { run, quartet } = require('./disposable.cjs');
const root = path.resolve(__dirname, '../..'), base = 'http://127.0.0.1:5099';
const cleanup = async (route, body) => {
  const r = await fetch(base + '/device-test/roteiro-cleanup' + route, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  const text = await r.text(); return { status: r.status, data: text ? JSON.parse(text) : null };
};
run({ label: 'cleanupself', ...quartet() }, async world => {
  const [A, P, , M] = await Promise.all(['autor', 'participante', 'alheio', 'membro'].map(r => world.login(r)));
  const call = async (u, method, route, body, expected) => {
    const headers = { Authorization: 'Bearer ' + u.token };
    if (body && !(body instanceof FormData)) headers['Content-Type'] = 'application/json';
    const r = await fetch(base + '/MeepleBoard' + route, { method, headers, body: body instanceof FormData ? body : body && JSON.stringify(body) });
    const t = await r.text(); if (expected) assert.equal(r.status, expected, `${method} ${route} → ${r.status} ${t.slice(0, 200)}`);
    return t && r.headers.get('content-type')?.includes('json') ? JSON.parse(t) : null;
  };
  const pass = name => console.log('PASS ' + name);
  // What a verifier leaves: a session (two guests accepted) with two matches, diaries, a photo, a publication and reports.
  const game = await call(A, 'GET', '/game/search?name=' + encodeURIComponent('Meeple Teste Cooperativo'), null, 200);
  const average = async () => { const g = await call(A, 'GET', `/game/${game.id}`, null, 200); return [g.meepleBoardScore, g.meepleBoardRatingsCount]; };
  const start = await average();
  const s = await call(A, 'POST', '/session', { name: 'Sessão deixada por um verificador', scheduledStartDate: new Date().toISOString(), playerIds: [P.id, M.id] }, 201);
  for (const u of [P, M]) await call(u, 'POST', `/session/${s.id}/invites/respond`, { accept: true }, 200);
  const matches = [];
  for (const rating of [6, 8]) matches.push(await call(A, 'POST', '/matches', { gameId: game.id, gameName: game.name, gameSessionId: s.id, matchDate: new Date().toISOString(), isSoloGame: false,
    gameMode: 'COMPETITIVE', result: 'Undefined', resultPlayerIds: [], playerIds: [A.id, P.id], personalRating: rating, scoreSummary: 'Resultado de verificação' }, 201));
  await call(P, 'PUT', `/campaigns/matches/${matches[0].id}/journal`, { personalRating: 7, notes: 'nota de P' }, 200);
  const form = new FormData(); form.append('file', new Blob([Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN5kAAAAASUVORK5CYII=', 'base64')], { type: 'image/png' }), 'x.png');
  const entry = await call(P, 'POST', `/campaigns/matches/${matches[0].id}/journal/photos`, form, 200);
  await call(A, 'POST', `${entry.photoUrls[0].replace('/MeepleBoard', '')}/reports`, { reason: 'Spam' }, 204);
  await call(P, 'PUT', `/campaigns/matches/${matches[0].id}/journal/publication`, { showRating: true, comment: 'Publicação de verificação', publishComment: true }, 200);
  const pub = (await call(A, 'GET', `/game/${game.id}/experiences?limit=50`, null, 200)).items.find(i => i.comment === 'Publicação de verificação');
  await call(A, 'POST', `/experiences/${pub.id}/reports`, { reason: 'Spam' }, 204);
  const rated = await average();
  const selection = { sessions: [s.id], matches: matches.map(m => m.id) };
  const before = { session: await call(A, 'GET', `/session/${s.id}`, null, 200), match: await call(A, 'GET', `/matches/${matches[0].id}`, null, 200), journal: await call(A, 'GET', `/campaigns/matches/${matches[0].id}/journal`, null, 200) };

  // Plan: every dependent row, by table; refusals when the selection is incomplete.
  const partial = await cleanup('/plan', { sessions: [s.id], matches: [matches[0].id] });
  assert.ok(partial.data.refusals.some(x => x.includes('belongs to a listed session')), 'a match of a listed session must be listed too');
  const plan = (await cleanup('/plan', selection)).data;
  assert.deepEqual(plan.refusals, []);
  assert.deepEqual(plan.counts, { GameSessions: 1, GameSessionPlayers: 3, Matches: 2, MatchPlayers: 4, MatchJournalEntries: 3, MatchExperiencePublications: 1, ExperienceReports: 1, PhotoReports: 1, PublicationPhotos: 0 });
  pass(`plan: 1 session, 3 session players, 2 matches, 4 players, 3 diaries, 1 publication, 2 reports; an incomplete selection is refused (hash ${plan.hash})`);

  // Backup: every column of every row; read-only on SQL.
  const first = `roteiro-cleanup-selftest-${world.stamp}`, name = first + '-b';
  const files = [first, name].map(n => path.join(root, '.device-tests', n + '.json')), file = files[1];
  try {
    const backup = await cleanup('/backup', { ...selection, name: first });
    assert.equal(backup.status, 200, JSON.stringify(backup.data)); assert.equal(backup.data.hash, plan.hash);
    assert.equal((await cleanup('/backup', { ...selection, name: first })).status, 409, 'an existing backup is never overwritten');
    const saved = JSON.parse(fs.readFileSync(files[0], 'utf8'));
    assert.ok(Object.keys(saved.rows.Matches[0]).includes('CreatedAt') && saved.rows.MatchJournalEntries.some(e => e.Notes === 'nota de P'), 'complete rows');
    assert.equal((fs.statSync(files[0]).mode & 0o777).toString(8), '600');
    pass('backup: every column of every affected row, owner-only file, never overwritten');

    // Removal: refused without the right hash; refused after a change; then everything goes and the average follows.
    assert.equal((await cleanup('/remove', { backup: first, confirmHash: 'x' })).status, 409);
    await call(A, 'PUT', `/campaigns/matches/${matches[1].id}/journal`, { notes: 'mudou depois da cópia' }, 200);
    assert.equal((await cleanup('/remove', { backup: first, confirmHash: plan.hash })).status, 409, 'a row changed since the backup');
    // A new copy of the current rows, then the removal with it.
    assert.equal((await cleanup('/backup', { ...selection, name })).status, 200);
    before.journal = await call(A, 'GET', `/campaigns/matches/${matches[0].id}/journal`, null, 200);
    const removed = await cleanup('/remove', { backup: name, confirmHash: plan.hash });
    assert.equal(removed.status, 200, JSON.stringify(removed.data));
    assert.deepEqual(removed.data.removed, plan.counts);
    assert.equal((await fetch(base + `/MeepleBoard/session/${s.id}`, { headers: { Authorization: 'Bearer ' + A.token } })).status, 404);
    assert.deepEqual(await average(), start, 'the game average is back to before');
    pass('removal: refused without the confirmed hash or after a change; then exactly the planned rows go and the average is recalculated');

    // Restore: every row back, identical; the average follows.
    const restored = await cleanup('/restore', { backup: name });
    assert.equal(restored.status, 200, JSON.stringify(restored.data)); assert.deepEqual(restored.data.restored, plan.counts);
    assert.deepEqual(await call(A, 'GET', `/session/${s.id}`, null, 200), before.session);
    assert.deepEqual(await call(A, 'GET', `/matches/${matches[0].id}`, null, 200), before.match);
    assert.deepEqual(await call(A, 'GET', `/campaigns/matches/${matches[0].id}/journal`, null, 200), before.journal);
    assert.equal((await cleanup('/plan', selection)).data.hash, plan.hash);
    assert.deepEqual(await average(), rated, 'the average is back to the rated value');
    pass('restore: every row back with the same content (session, match, diary, photo, publication, reports) and the same hash');
  } finally {
    for (const f of files) if (fs.existsSync(f)) fs.unlinkSync(f);
  }
}).catch(error => { console.error('FAIL', error.message); process.exit(1); });
