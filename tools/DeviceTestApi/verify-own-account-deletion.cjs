// DeviceTests only: deleting one's own account against real SQL (docs/proposta-eliminacao-conta.md in the app).
//   node tools/DeviceTestApi/verify-own-account-deletion.cjs
// Fresh people are created for each run (AccountDeletionFixture), with two accounts that stay («conta-fica-a/b»):
// P deletes in the app (password, new campaign manager), Q through support (single-use link). Checks what is deleted,
// anonymised, neutralised or kept; storage and push failures; repeated and simultaneous requests; report retention.
// Everything created is removed at the end and the game average returns to where it was. Never touches the iPhone
// walkthrough accounts or sessions; never prints credentials or tokens.
if (require.main !== module) return; // Importing a verifier never runs it: only `node <file>` does (see docs/testes-macos-docker.md).
const { run } = require('./disposable.cjs');
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../..'), base = 'http://127.0.0.1:5099';
run({ label: 'owndeletion', people: [{ role: 'admin', admin: true }] }, async world => {
  const health = await (await fetch(base + '/device-test/health')).json();
  assert.deepEqual(health, { environment: 'DeviceTests', database: 'MeepleBoard_DeviceTests', externalDelivery: false });
  const pass = name => console.log('PASS ' + name);
  const fixture = async (method, route, body) => {
    const r = await fetch(`${base}/device-test/account-deletion${route}`, { method, headers: { 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined });
    const text = await r.text(); return { status: r.status, data: text ? JSON.parse(text) : null };
  };
  const login = async (email, password, name) => {
    const r = await fetch(base + '/MeepleBoard/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email, password, deviceInfo: 'verify-own-account-deletion' }) });
    return r.status === 200 ? { name, token: (await r.json()).token } : { name, status: r.status };
  };
  const call = async (u, method, route, body, expected) => {
    const headers = { Authorization: `Bearer ${u.token}` };
    if (body && !(body instanceof FormData)) headers['Content-Type'] = 'application/json';
    const r = await fetch(base + '/MeepleBoard' + route, { method, headers, body: body instanceof FormData ? body : body ? JSON.stringify(body) : undefined });
    const text = await r.text();
    if (expected) assert.equal(r.status, expected, `${u.name} ${method} ${route} → ${r.status} ${text.slice(0, 300)}`);
    return { status: r.status, data: text && r.headers.get('content-type')?.includes('json') ? JSON.parse(text) : text };
  };
  const stay = JSON.parse(fs.readFileSync(path.join(root, '.device-tests/account-deletion-accounts.json'), 'utf8'));
  const adminAccount = world.people.admin; // an administrator of this run only (disposable.cjs), never the walkthrough's
  const people = (await fixture('POST', '/people')).data;
  // Only what this run creates is cleaned at the end (other scenarios, such as the iPhone validation, stay).
  const runPeople = people.people.map(x => x.id);
  // The fixture people of this run are removed also when a check fails.
  let cleaned = false;
  try {
  const [pInfo, qInfo] = [people.people.find(x => x.role === 'p'), people.people.find(x => x.role === 'q')];
  const A = { ...(await login(stay[0].Email, stay[0].Password, 'A')), id: stay[0].Id };
  const B = { ...(await login(stay[1].Email, stay[1].Password, 'B')), id: stay[1].Id };
  const P = { ...(await login(pInfo.email, pInfo.password, 'P')), id: pInfo.id };
  const Q = { ...(await login(qInfo.email, qInfo.password, 'Q')), id: qInfo.id };
  const admin = { ...(await login(adminAccount.Email, adminAccount.Password, 'admin')), id: adminAccount.Id };
  for (const u of [A, B, P, Q, admin]) assert.ok(u.token, 'login ' + u.name);
  const st = (await fixture('POST', `/structure/${P.id}`)).data;
  const game = (await call(P, 'GET', `/game/${st.gameId}`, null, 200)).data;
  const averages = async () => { const g = (await call(A, 'GET', `/game/${game.id}`, null, 200)).data; return [g.meepleBoardScore, g.meepleBoardRatingsCount]; };
  const initial = await averages();
  const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN5kAAAAASUVORK5CYII=', 'base64');
  const photo = async (u, matchId) => { const form = new FormData(); form.append('file', new Blob([png], { type: 'image/png' }), 'fixture.png'); await call(u, 'POST', `/campaigns/matches/${matchId}/journal/photos`, form, 200); };
  const match = (u, body) => call(u, 'POST', '/matches', { gameId: game.id, gameName: game.name, matchDate: new Date(Date.now() - 3600e3).toISOString(), durationInMinutes: 40, durationMethod: 'Measured', ...body }, 201).then(r => r.data);
  pass('fresh people P and Q, friends of A and B; sessions and campaigns around P (scheduled, running, ended; with a new manager, without one, only P\'s)');

  // P's activity: shared matches (P's and A's), a solo match, diaries with photos, publications, reports, a suggestion, a library entry.
  const m1 = await match(P, { isSoloGame: false, playerIds: [P.id, A.id], winnerId: P.id, playerScores: [{ userId: P.id, score: 10 }, { userId: A.id, score: 8 }], scoreSummary: 'P ganhou na casa do P', location: 'Rua do P', tags: 'tag-de-P', personalRating: 6 });
  const m2 = await match(P, { isSoloGame: true, playerIds: [P.id], scoreSummary: 'Solo do P', personalRating: 7 });
  const m3 = await match(A, { isSoloGame: false, playerIds: [A.id, P.id], winnerId: A.id, scoreSummary: 'Texto escrito por A', location: 'Casa de A', personalRating: 8 });
  await call(A, 'PUT', `/campaigns/matches/${m1.id}/journal`, { personalRating: 9, notes: 'nota de A' }, 200);
  await call(P, 'PUT', `/campaigns/matches/${m3.id}/journal`, { personalRating: 5, notes: 'nota de P' }, 200);
  await photo(P, m1.id); await photo(P, m3.id);
  await call(P, 'PUT', `/campaigns/matches/${m1.id}/journal/publication`, { showRating: true, comment: 'Publicação de P', publishComment: true }, 200);
  await call(A, 'PUT', `/campaigns/matches/${m3.id}/journal/publication`, { showRating: true, comment: 'Publicação de A', publishComment: true }, 200);
  const ofA = (await call(A, 'GET', `/game/${game.id}/experiences?limit=50`, null, 200)).data.items.find(e => e.comment === 'Publicação de A');
  await call(P, 'POST', `/experiences/${ofA.id}/reports`, { reason: 'Abusive', detail: 'Detalhe escrito por P' }, 204);
  const stamp = Date.now();
  const resource = { kind: 'Rules', printCategory: null, language: 'pt', title: 'Recurso de verificação', author: null, platform: 'Teste', compatibility: null, isOfficial: false, isFree: null };
  const r1 = (await call(admin, 'POST', `/admin/game/${game.id}/resources`, { ...resource, url: `https://example.org/meepleboard-device-test/account-deletion/${stamp}/a` })).data;
  const r2 = (await call(P, 'POST', `/game/${game.id}/resources/suggestions`, { ...resource, title: 'Sugestão de P', url: `https://example.org/meepleboard-device-test/account-deletion/${stamp}/b` }, 201)).data;
  await call(P, 'POST', `/game-resources/${r1.id}/reports`, { reason: 'BrokenLink', comment: 'Comentário de P' }, 201);
  const resourceReport = (await call(admin, 'GET', '/admin/game-resources/reports', null, 200)).data.find(r => r.resource?.id === r1.id || r.resourceId === r1.id);
  await call(admin, 'POST', `/admin/game-resources/reports/${resourceReport.id ?? resourceReport.reportId}/resolve`, null, 204);
  await call(P, 'POST', `/users/${P.id}/games`, { gameId: game.id, gameName: game.name, status: 1 }, 201);
  await fixture('POST', `/encounter?campaign=${st.campaigns.transfer}&match=${m1.id}&title=${encodeURIComponent('Mesa do P')}`);
  const ids = { Users: [P.id, Q.id, A.id], Sessions: Object.values(st.sessions), Campaigns: Object.values(st.campaigns), Matches: [m1.id, m2.id, m3.id], Reporters: [P.id, Q.id], Resources: [r1.id, r2.id], Photos: [] };
  let state = (await fixture('POST', '/state', ids)).data;
  const pPhotos = state.matches.flatMap(m => m.journals.filter(j => j.userId === P.id).flatMap(j => j.photoUrls));
  assert.equal(pPhotos.length, 2);
  pass('P\'s activity: a shared match P created and one A created, a solo match, diaries with two photos, publications, an open report, a resolved resource report, a suggestion, a library entry, an encounter title');

  // The confirmation screen's summary.
  const summary = (await call(P, 'GET', '/users/me/deletion-summary', null, 200)).data;
  assert.equal(summary.scheduledSessions, 1); assert.equal(summary.runningSessions, 1); assert.equal(summary.campaignsOnlyYours, 1);
  const withMembers = Object.fromEntries(summary.campaignsWithMembers.map(c => [c.id, c]));
  assert.deepEqual(withMembers[st.campaigns.transfer].members.map(m => m.id).sort(), [A.id, B.id].sort());
  assert.equal(withMembers[st.campaigns.transfer].hasNotes, true);
  // Succession order: accepted longest ago first (B 120 min, A 60 min); a tie falls back to a fixed order.
  assert.deepEqual(withMembers[st.campaigns.auto].members.map(m => m.id), [B.id, A.id]); assert.equal(withMembers[st.campaigns.auto].defaultManagerId, B.id);
  assert.equal(withMembers[st.campaigns.transfer].defaultManagerId, B.id, 'B would take over if P chose nobody');
  const tieDefault = withMembers[st.campaigns.tie].defaultManagerId;
  assert.ok([A.id, B.id].includes(tieDefault));
  assert.equal((await call(P, 'GET', '/users/me/deletion-summary', null, 200)).data.campaignsWithMembers.find(c => c.id === st.campaigns.tie).defaultManagerId, tieDefault, 'tie-break is deterministic');
  assert.equal(withMembers[st.campaigns.managedByP].isAuthor, false); assert.equal(withMembers[st.campaigns.managedByP].defaultManagerId, A.id);
  assert.equal(withMembers[st.campaigns.transfer].isAuthor, true);
  pass('deletion summary: sessions; four managed campaigns with their successor shown (by seniority, deterministic on a tie), authorship flagged, notes flagged; one campaign only P\'s');

  // Refusals change nothing.
  await call(P, 'DELETE', '/users/me', { password: 'errada-1A' }, 400);
  await call(P, 'DELETE', '/users/me', { password: pInfo.password, campaignTransfers: [{ campaignId: st.campaigns.transfer, newManagerId: Q.id }] }, 400);
  await call(P, 'DELETE', '/users/me', { password: pInfo.password, campaignTransfers: [{ campaignId: st.campaigns.ofA, newManagerId: A.id }] }, 400);
  state = (await fixture('POST', '/state', ids)).data;
  assert.equal(state.users.find(u => u.id === P.id).deletedAt, null); assert.equal(state.matches.length, 3);
  pass('wrong password (400), a new manager who is not an active member (400) or a campaign P did not create (400): nothing changed');

  // Deletion in the app, with storage failing twice and the guests' push failing; three requests at once.
  const before = (await fixture('GET', '/recorded')).data;
  await fixture('POST', '/fail?photos=2&push=true');
  const body = { password: pInfo.password, campaignTransfers: [{ campaignId: st.campaigns.transfer, newManagerId: A.id }] };
  const statuses = (await Promise.all([0, 1, 2].map(() => call(P, 'DELETE', '/users/me', body)))).map(r => r.status);
  assert.ok(statuses.includes(204) && statuses.every(s => [204, 400, 401, 404].includes(s)), 'simultaneous requests: ' + statuses);
  await call(P, 'GET', '/users/me', null, 401);
  await call(P, 'DELETE', '/users/me', body, 401);
  assert.equal((await login(pInfo.email, pInfo.password)).status !== undefined, true, 'P can no longer sign in');
  const after = (await fixture('GET', '/recorded')).data;
  assert.equal(after.jobs.length, before.jobs.length, 'two storage failures were retried without a job');
  assert.equal(after.pushBulk, before.pushBulk, 'the push failed (simulated) and the deletion went on');
  pass(`deleted in the app (simultaneous requests: ${statuses.join(', ')}); the old token and a repeat get 401; storage failing twice was retried; the failing push did not stop it`);

  state = (await fixture('POST', '/state', { ...ids, Photos: pPhotos })).data;
  const user = state.users.find(u => u.id === P.id);
  assert.ok(user.deletedAt && !user.email && !user.lastActiveAt && /^deleted:[0-9a-f]{8}$/.test(user.userName));
  assert.deepEqual(state.photoFiles, [false, false], 'P\'s photos are gone from storage');
  const session = key => state.sessions.find(s => s.id === st.sessions[key]);
  assert.equal(session('scheduled').isCancelled, true); assert.match(session('scheduled').name, /^deleted:session:\d{4}-\d{2}-\d{2}$/); assert.equal(session('scheduled').location, null);
  assert.ok(session('running').endDate && !session('running').isCancelled); assert.match(session('running').name, /^deleted:session:/); assert.equal(session('running').location, null);
  assert.match(session('closed').name, /^deleted:session:/); assert.equal(session('closed').location, null);
  assert.equal(session('ofA').name, 'Sessão de A'); assert.equal(session('ofA').location, 'Casa de A'); assert.equal(session('ofA').players, 2);
  const cancelled = (await call(A, 'GET', `/session/${st.sessions.scheduled}`, null, 200)).data;
  assert.equal(cancelled.status, 'Cancelled');
  assert.ok(!((await call(A, 'GET', '/session/mine', null, 200)).data || []).some(s => s.id === st.sessions.scheduled), 'a cancelled session leaves the lists');
  pass('sessions: the scheduled one is cancelled (A sees «Cancelled», out of the lists), the running one ended, both and the ended one with a neutral title and no place; A\'s session keeps its text and the anonymised P');

  const campaign = key => state.campaigns.find(c => c.id === st.campaigns[key]);
  assert.equal(campaign('transfer').creatorId, A.id); assert.deepEqual(campaign('transfer').creators, [P.id], 'P stays the author');
  assert.equal(campaign('transfer').name, 'deleted:campaign'); assert.equal(campaign('transfer').notes, 'Notas que A escreveu');
  assert.equal(campaign('transfer').matches.find(m => m.matchId === m1.id).sessionTitle, null);
  assert.equal(campaign('auto').creatorId, B.id); assert.deepEqual(campaign('auto').creators, [P.id]);
  assert.equal(campaign('tie').creatorId, tieDefault); assert.deepEqual(campaign('tie').creators, [P.id]);
  assert.equal(campaign('alone'), undefined);
  assert.equal(campaign('managedByP').creatorId, A.id); assert.deepEqual(campaign('managedByP').creators, [A.id]); assert.equal(campaign('managedByP').name, 'Campanha escrita por A');
  assert.equal(campaign('ofA').name, 'Campanha de A'); assert.equal(campaign('ofA').notes, 'Notas de A');
  const pushTexts = (await fixture('GET', '/recorded')).data.pushTexts;
  assert.ok(pushTexts.filter(t => t.includes('Passaste a gerir')).length >= 1, 'the new manager with notifications is told');
  assert.ok(!pushTexts.some(t => t.includes('deleted:')), 'no technical marker in any notification: ' + pushTexts.filter(t => t.includes('deleted:')).join(' | '));
  pass('campaigns: chosen successor A, automatic successor B (longest-standing), the tie resolved as announced; P stays the author everywhere, the campaign written by A keeps A\'s name and authorship; P\'s own one is gone; the new manager is told, with no marker');

  // Management after the succession: only the manager; complete/abandon only from «Ativa»; matches out by who played or manages.
  await call(B, 'POST', `/campaigns/${st.campaigns.transfer}/complete`, null, 403);
  await call(B, 'DELETE', `/campaigns/${st.campaigns.transfer}/members/${A.id}`, null, 403);
  await call(B, 'DELETE', `/campaigns/${st.campaigns.transfer}/matches/${m1.id}`, null, 403);
  await call(A, 'DELETE', `/campaigns/${st.campaigns.transfer}/members/${P.id}`, null, 409);
  await call(A, 'DELETE', `/campaigns/${st.campaigns.transfer}/matches/${m1.id}`, null, 204);
  await call(A, 'POST', `/campaigns/${st.campaigns.transfer}/complete`, null, 204);
  await call(A, 'POST', `/campaigns/${st.campaigns.transfer}/abandon`, null, 409);
  await call(A, 'DELETE', `/campaigns/${st.campaigns.auto}/members/${B.id}`, null, 403);
  await call(B, 'DELETE', `/campaigns/${st.campaigns.auto}/members/${A.id}`, null, 204);
  await call(B, 'POST', `/campaigns/${st.campaigns.auto}/members/leave`, null, 409);
  pass('management: B (member) cannot complete, remove members or take out a match it did not play (403); A (manager) cannot remove the original author (409), takes out the match it played, completes, then cannot abandon a completed campaign (409); B manages its campaign and cannot leave it (409)');

  const m = id => state.matches.find(x => x.id === id);
  assert.equal(m(m2.id), undefined, 'the solo match is gone');
  assert.equal(m(m1.id).location, null); assert.equal(m(m1.id).scoreSummary, null); assert.equal(m(m1.id).tags, null);
  assert.equal(m(m1.id).durationInMinutes, 40); assert.deepEqual(m(m1.id).players.map(p => p.score).sort(), [10, 8].sort());
  assert.deepEqual(m(m1.id).journals.map(j => [j.userId, j.personalRating, j.notes]), [[A.id, 9, 'nota de A']]);
  assert.equal(m(m3.id).scoreSummary, 'Texto escrito por A'); assert.equal(m(m3.id).location, 'Casa de A');
  assert.deepEqual(m(m3.id).journals.map(j => j.userId), [A.id]);
  await call(A, 'PUT', `/campaigns/matches/${m1.id}/journal`, { personalRating: 9, notes: 'nota de A, depois' }, 200);
  const detail = (await call(A, 'GET', `/matches/${m1.id}`, null, 200)).data;
  await call(A, 'PUT', `/matches/${m1.id}`, { ...detail, scoreSummary: 'A tenta mudar' }, 403);
  pass('matches: the solo one is gone; in P\'s shared match P\'s place, text and tags go, scores and duration stay, A\'s diary stays and A can still rate and note; common data is read-only; A\'s match keeps A\'s text');

  const report = state.reports.find(r => r.reporterId === P.id);
  assert.equal(report.open, true); assert.equal(report.detail, 'Detalhe escrito por P');
  assert.equal(state.resourceReports.find(r => r.userId === P.id).comment, null);
  await call(A, 'GET', '/admin/experiences/reported', null, 403);
  await call(admin, 'POST', `/admin/experiences/${ofA.id}/keep`, { reason: null }, 204);
  state = (await fixture('POST', '/state', ids)).data;
  assert.equal(state.reports.find(r => r.reporterId === P.id).detail, null);
  assert.equal(state.resources.find(r => r.id === r2.id).suggestedById, null);
  assert.equal(state.libraries, 0, 'library');
  pass('reports: P\'s open report keeps its text for administrators only and loses it when resolved; the resolved resource report lost it at once; P\'s suggestion stays without P');

  // Through support: a single-use link sent to the account's own address; storage failing beyond the retries.
  const qCampaign = (await call(Q, 'POST', '/campaigns', { name: 'Campanha de Q', gameId: game.id, notes: null })).data;
  await call(Q, 'POST', `/campaigns/${qCampaign.id}/members/invite`, { userId: A.id }); await call(A, 'POST', `/campaigns/${qCampaign.id}/members/respond`, true);
  const q1 = await match(Q, { isSoloGame: true, playerIds: [Q.id], personalRating: 7 });
  await photo(Q, q1.id);
  await call(Q, 'POST', `/experiences/${ofA.id}/reports`, { reason: 'Spam', detail: 'Detalhe escrito por Q' }, 204);
  const qPhotos = (await fixture('POST', '/state', { ...ids, Matches: [q1.id] })).data.matches[0].journals[0].photoUrls;
  const outbox = path.join(root, '.device-tests/outbox');
  const mails = () => fs.existsSync(outbox) ? fs.readdirSync(outbox).map(f => JSON.parse(fs.readFileSync(path.join(outbox, f), 'utf8'))) : [];
  const count = kind => mails().filter(x => x.kind === kind).length;
  const [links, done] = [count('account-deletion'), count('account-deleted')];
  await call(A, 'POST', '/admin/account-deletion/requests', { email: qInfo.email }, 403);
  const unknown = await call(admin, 'POST', '/admin/account-deletion/requests', { email: `ninguem-${stamp}@meepleboard.test` }, 202);
  const known = await call(admin, 'POST', '/admin/account-deletion/requests', { email: qInfo.email }, 202);
  assert.equal(unknown.data.message, known.data.message, 'the answer never says whether the account exists');
  const sent = mails().filter(x => x.kind === 'account-deletion');
  assert.equal(sent.length, links + 1);
  const link = sent.find(x => x.recipient === qInfo.email && x.link.includes(Q.id)).link;
  const page = await fetch(link); const html = await page.text();
  assert.equal(page.status, 200); assert.match(html, /<form method="post"/);
  assert.ok((await login(qInfo.email, qInfo.password)).token, 'opening the link deletes nothing');
  await fixture('POST', '/fail?photos=5&push=false');
  const url = new URL(link);
  const form = new URLSearchParams({ u: url.searchParams.get('u'), t: url.searchParams.get('t') });
  const confirm = await fetch(url.origin + url.pathname, { method: 'POST', body: form });
  assert.equal(confirm.status, 200); assert.match(await confirm.text(), /Conta eliminada/);
  assert.equal(count('account-deleted'), done + 1); assert.ok(mails().some(x => x.kind === 'account-deleted' && x.recipient === qInfo.email));
  const again = await fetch(url.origin + url.pathname, { method: 'POST', body: form });
  assert.equal(again.status, 400); assert.equal(count('account-deleted'), done + 1);
  assert.equal((await fetch(link)).status, 400);
  assert.ok(!(await login(qInfo.email, qInfo.password)).token, 'Q can no longer sign in');
  const recorded = (await fixture('GET', '/recorded')).data.jobs;
  assert.ok(recorded.some(j => j === 'PhotoDeletionJob:' + qPhotos[0]), 'storage failing beyond the retries leaves a job with the reference only');
  await fixture('POST', '/fail?photos=0&push=false');
  const qState = (await fixture('POST', '/state', { ...ids, Campaigns: [qCampaign.id] })).data.campaigns[0];
  assert.equal(qState.creatorId, A.id); assert.deepEqual(qState.creators, [Q.id]); assert.equal(qState.name, 'deleted:campaign');
  pass('through support: the same rule — the only active member A takes over automatically; Q stays the author');
  pass('through support: same answer for any address; the link goes to the account\'s address; opening it deletes nothing; POST deletes and confirms by email; the used link changes nothing; storage still failing leaves a retry job');

  // Retention: an open report by a deleted account keeps its text at most until the limit (job step, run now for the fixture).
  state = (await fixture('POST', '/state', ids)).data;
  assert.equal(state.reports.find(r => r.reporterId === Q.id).detail, 'Detalhe escrito por Q');
  const cleared = (await fixture('POST', `/report-retention?deletedBefore=${encodeURIComponent(new Date().toISOString())}`)).data.cleared;
  state = (await fixture('POST', '/state', ids)).data;
  assert.ok(cleared >= 1); const qReport = state.reports.find(r => r.reporterId === Q.id);
  assert.equal(qReport.detail, null); assert.equal(qReport.open, true); assert.equal(qReport.reason, 'Spam');
  pass('retention: past the limit, an open report by a deleted account loses its text and keeps its reason for moderation');

  // A successor leaving at the very moment the manager deletes the account: one of them wins, never a campaign whose
  // manager has left or been deleted.
  const outcomes = [];
  // The leave starts 0, 150 and 400 ms after the deletion request, so both orders get a chance to happen.
  for (const delay of [0, 150, 400]) {
    const created = (await fixture('POST', '/people')).data.people; runPeople.push(...created.map(x => x.id));
    const fresh = created.find(x => x.role === 'p');
    const R = { ...(await login(fresh.email, fresh.password, 'R')), id: fresh.id };
    const c = (await call(R, 'POST', '/campaigns', { name: 'Corrida de sucessão', gameId: game.id, notes: null })).data;
    for (const u of [A, B]) { await call(R, 'POST', `/campaigns/${c.id}/members/invite`, { userId: u.id }); await call(u, 'POST', `/campaigns/${c.id}/members/respond`, true); }
    const [del, leave] = await Promise.all([
      call(R, 'DELETE', '/users/me', { password: fresh.password, campaignTransfers: [{ campaignId: c.id, newManagerId: A.id }] }),
      new Promise(res => setTimeout(res, delay)).then(() => call(A, 'POST', `/campaigns/${c.id}/members/leave`))]);
    const after = (await fixture('POST', '/state', { ...ids, Users: [R.id], Campaigns: [c.id] })).data;
    const cs = after.campaigns[0], r = after.users[0];
    if (del.status === 204) { assert.equal(leave.status, 409, 'A left after becoming manager'); assert.equal(cs.creatorId, A.id); assert.ok(r.deletedAt); outcomes.push('deletion first'); }
    else { assert.equal(del.status, 400); assert.equal(leave.status, 204); assert.equal(cs.creatorId, R.id); assert.equal(r.deletedAt, null); outcomes.push('leave first');
      await call(R, 'DELETE', '/users/me', { password: fresh.password }, 204);
      const again = (await fixture('POST', '/state', { ...ids, Users: [R.id], Campaigns: [c.id] })).data.campaigns[0];
      assert.equal(again.creatorId, B.id, 'then B, the only active member left, takes over'); }
  }
  pass(`successor leaving while the manager deletes (${outcomes.join(', ')}): the deletion with A as successor and A's leave never both happen; a refused choice changes nothing`);

  // Clean up: resources, then everything the fixture made; the game average is back.
  await call(admin, 'POST', `/admin/experiences/${ofA.id}/keep`, { reason: null }, 204);
  for (const r of [r1, r2]) await call(admin, 'DELETE', `/admin/game-resources/${r.id}`, null, 204);
  cleaned = true; assert.equal((await fixture('DELETE', `/fixture?people=${runPeople.join(',')}`)).status, 204);
  assert.deepEqual(await averages(), initial, 'the game average is back to where it was');
  pass('clean-up: resources, matches, sessions and campaigns of the fixture removed; the game average is back to where it was');
  } finally {
    if (!cleaned) { await fixture('DELETE', `/fixture?people=${runPeople.join(',')}`); console.log('CLEAN fixture people of this run removed after a failure'); }
  }
}).catch(error => { console.error('FAIL', error.message); process.exit(1); });
