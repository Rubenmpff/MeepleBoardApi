// DeviceTests only: blocks, reports, suspension and the PT/EN filter against real SQL and real tokens
// (docs/proposta-bloqueio-comunidade.md in the app).
//   node tools/DeviceTestApi/verify-community.cjs
// Fresh people each run (disposable.cjs): A, B, C, D with friendships A–B, A–C, B–C, A–D, and an administrator of their
// own. Every rule is called through the HTTP endpoints, with IDs sent directly where the app would never send them, and
// blocks checked in both directions. Everything created is removed at the end, accounts included, also on failure.
// Never touches the iPhone walkthrough; never prints credentials or tokens.
if (require.main !== module) return; // Importing a verifier never runs it: only `node <file>` does (see docs/testes-macos-docker.md).
const assert = require('node:assert/strict');
const { run } = require('./disposable.cjs');
const base = 'http://127.0.0.1:5099';
run({ label: 'community', people: [{ role: 'a' }, { role: 'b' }, { role: 'c' }, { role: 'd' }, { role: 'admin', admin: true }],
  friends: [['a', 'b'], ['a', 'c'], ['b', 'c'], ['a', 'd']] }, async world => {
  const health = await (await fetch(base + '/device-test/health')).json();
  assert.deepEqual(health, { environment: 'DeviceTests', database: 'MeepleBoard_DeviceTests', externalDelivery: false });
  const pass = name => console.log('PASS ' + name);
  const fixture = async (method, route, body) => {
    const r = await fetch(`${base}/device-test/disposable${route}`, { method, headers: { 'Content-Type': 'application/json' }, body: body ? JSON.stringify(body) : undefined });
    const text = await r.text(); return { status: r.status, data: text ? JSON.parse(text) : null };
  };
  const auth = async (route, body) => {
    const r = await fetch(base + '/MeepleBoard/auth/' + route, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    const text = await r.text(); return { status: r.status, data: text ? JSON.parse(text) : null };
  };
  const login = async (who) => {
    const r = await auth('login', { email: who.email, password: who.password, deviceInfo: 'verify-community' });
    assert.equal(r.status, 200, `login ${who.name} → ${r.status}`);
    who.token = r.data.token; who.refresh = r.data.refreshToken; return who;
  };
  const call = async (u, method, route, body, expected) => {
    const headers = { Authorization: `Bearer ${u.token}` };
    if (body !== undefined && !(body instanceof FormData)) headers['Content-Type'] = 'application/json';
    const r = await fetch(base + '/MeepleBoard' + route, { method, headers, body: body instanceof FormData ? body : body !== undefined ? JSON.stringify(body) : undefined });
    const text = await r.text();
    if (expected) assert.equal(r.status, expected, `${u.name} ${method} ${route} → ${r.status} ${text.slice(0, 300)}`);
    return { status: r.status, data: text && r.headers.get('content-type')?.includes('json') ? JSON.parse(text) : text };
  };

  const made = { stamp: world.stamp };
  const [A, B, C, D, admin] = ['a', 'b', 'c', 'd', 'admin'].map(role => ({ name: role.toUpperCase(), ...world.people[role] }));
  const adminAccount = { Id: admin.id };
  const ids = [A, B, C, D].map(u => u.id);
  {
    for (const u of [A, B, C, D, admin]) await login(u);
    const competitive = (await call(A, 'GET', '/game/search?name=' + encodeURIComponent('Meeple Teste Competitivo'), undefined, 200)).data;
    const game = { id: competitive.id, name: competitive.name };
    // Token issue times have whole seconds: a sign-in in the same second as a revocation is refused as well.
    const nextSecond = () => new Promise(resolve => setTimeout(resolve, 1100));
    const state = async () => (await fixture('POST', '/state', ids)).data;
    const match = (u, body, expected = 201) => call(u, 'POST', '/matches', { gameId: game.id, gameName: game.name, matchDate: new Date(Date.now() - 3600e3).toISOString(),
      isSoloGame: false, personalRating: 7, ...body }, expected);
    const found = async (u, other) => (await call(u, 'GET', `/friendships/search?query=${encodeURIComponent(other.userName)}`, undefined, 200)).data.some(x => x.id === other.id);
    const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN5kAAAAASUVORK5CYII=', 'base64');
    pass('fresh people A, B, C, D (friends A–B, A–C, B–C, A–D); signed in; the administrator only moderates them');

    // ── Before any block ────────────────────────────────────────────────────────────────────────────────────────────
    assert.ok(await found(B, C) && await found(C, B), 'B and C find each other');
    await call(B, 'GET', `/friendships/profile/${C.id}`, undefined, 200);
    const shared = (await match(A, { playerIds: [A.id, B.id, C.id], winnerId: A.id })).data; // before the block: stays shared
    const m1 = (await match(A, { playerIds: [A.id, B.id], winnerId: B.id })).data;
    await call(B, 'GET', `/friendships/${C.id}/matches`, undefined, 200);
    // Outside sessions, every other player must be an accepted friend of whoever registers, even with IDs sent directly.
    const notFriends = await match(B, { playerIds: [B.id, D.id], winnerId: B.id }, 409);
    assert.match(notFriends.data.message, /amigos/);
    pass('outside sessions: only accepted friends of whoever registers (B with D refused, IDs sent directly)');

    // ── Sessions and campaigns where the block will land ───────────────────────────────────────────────────────────
    const s1 = (await call(A, 'POST', '/session', { name: 'Sessão com convites', playerIds: [B.id, C.id] })).data;
    await call(B, 'POST', `/session/${s1.id}/invites/respond`, { accept: true }, 200);
    // Inside a session, accepted participants play together without friendship (B and D are not friends).
    const s2 = (await call(A, 'POST', '/session', { name: 'Sessão sem amizade', playerIds: [B.id, D.id] })).data;
    await call(B, 'POST', `/session/${s2.id}/invites/respond`, { accept: true }, 200);
    await call(D, 'POST', `/session/${s2.id}/invites/respond`, { accept: true }, 200);
    await match(B, { gameSessionId: s2.id, playerIds: [B.id, D.id], winnerId: D.id });
    pass('inside a session: accepted participants without friendship (B with D) are allowed');
    // Campaign encounter: accepted members of the campaign, with or without friendship.
    const k = (await call(A, 'POST', '/campaigns', { name: 'Campanha da comunidade', gameId: game.id, notes: 'Notas partilhadas' }, 201)).data;
    for (const u of [B, D]) { await call(A, 'POST', `/campaigns/${k.id}/members/invite`, { userId: u.id }, 204); await call(u, 'POST', `/campaigns/${k.id}/members/respond`, true, 204); }
    const encounter = (await match(B, { campaignId: k.id, playerIds: [B.id, D.id], winnerId: B.id })).data;
    await call(B, 'POST', `/campaigns/${k.id}/matches`, { matchId: encounter.id, sessionNumber: 1, sessionTitle: 'Primeiro encontro' }, 204);
    await match(C, { campaignId: k.id, playerIds: [C.id, A.id], winnerId: C.id }, 403); // C is not a member
    await match(B, { campaignId: k.id, playerIds: [B.id, C.id], winnerId: B.id }, 409); // C is not a member
    // A campaignId alone is not enough: the server reads who is an accepted member now.
    const k2 = (await call(A, 'POST', '/campaigns', { name: 'Campanha com convite pendente', gameId: game.id }, 201)).data;
    await call(A, 'POST', `/campaigns/${k2.id}/members/invite`, { userId: D.id }, 204);                      // D invited, not accepted
    await match(A, { campaignId: k2.id, playerIds: [A.id, D.id], winnerId: A.id }, 409);                   // a pending member does not play, even as A's friend
    await match(D, { campaignId: k2.id, playerIds: [D.id, A.id], winnerId: D.id }, 403);                   // a pending member does not register
    await match(A, { campaignId: k2.id, playerIds: [A.id, B.id], winnerId: A.id }, 409);                   // with a campaignId, friendship does not replace membership
    await match(B, { campaignId: k2.id, playerIds: [B.id, D.id], winnerId: B.id }, 403);                   // B is not a member of k2
    await match(B, { campaignId: '00000000-0000-0000-0000-000000000001', playerIds: [B.id, D.id], winnerId: B.id }, 404);
    await call(B, 'POST', `/campaigns/${k.id}/members/invite`, { userId: D.id }, 409); // already a member
    await call(D, 'POST', `/campaigns/${k.id}/members/invite`, { userId: C.id }, 400); // C is not D's friend
    pass('campaign encounter: members play without friendship; non-members refused; invitations only to the inviter\'s friends');

    // ── A photo in B's diary of m1, seen by A ───────────────────────────────────────────────────────────────────────
    const form = new FormData(); form.append('file', new Blob([png], { type: 'image/png' }), 'fixture.png');
    const bEntry = (await call(B, 'POST', `/campaigns/matches/${m1.id}/journal/photos`, form, 200)).data;
    const [, entryId, key] = bEntry.photoUrls[0].match(/photos\/([0-9a-f-]+)\/([0-9a-f]+)$/);
    const photoRoute = `/campaigns/matches/${m1.id}/journal/photos/${entryId}/${key}`;
    await call(A, 'GET', photoRoute, undefined, 200);
    await call(C, 'POST', `${photoRoute}/reports`, { reason: 'Inappropriate' }, 403);                 // C did not play m1
    await call(B, 'POST', `${photoRoute}/reports`, { reason: 'Inappropriate' }, 409);                 // own photo
    await call(A, 'POST', `/campaigns/matches/${shared.id}/journal/photos/${entryId}/${key}/reports`, { reason: 'Inappropriate' }, 404); // wrong match
    await call(A, 'POST', `${photoRoute.replace(key, '0'.repeat(64))}/reports`, { reason: 'Inappropriate' }, 404);              // unknown key
    await call(A, 'POST', `${photoRoute}/reports`, { reason: 'Banana' }, 400);
    await call(admin, 'GET', `/admin/moderation/photos/${entryId}/${key}`, undefined, 404);          // not reported yet: closed
    await call(A, 'POST', `${photoRoute}/reports`, { reason: 'PersonalData', detail: 'Aparece a minha morada' }, 204);
    await call(A, 'POST', `${photoRoute}/reports`, { reason: 'PersonalData' }, 409);
    await call(A, 'GET', `/admin/moderation/photos/${entryId}/${key}`, undefined, 403);              // administrators only
    const listed = (await call(admin, 'GET', '/admin/moderation/photos', undefined, 200)).data.find(p => p.journalEntryId === entryId);
    assert.ok(listed && listed.present && listed.reasons.PersonalData === 1 && !JSON.stringify(listed).includes('cloudinary'), 'listed by key, never by stored address');
    await call(admin, 'GET', `/admin/moderation/photos/${entryId}/${key}`, undefined, 200);
    assert.equal((await call(B, 'GET', `/campaigns/matches/${m1.id}/journal`, undefined, 200)).data.find(e => e.userId === B.id).photoUrls.length, 1, 'a report removes nothing');
    pass('photo reports: only a participant, never one\'s own, the right match and key, once while open; the administrator sees it only while reported');

    // ── B blocks C ──────────────────────────────────────────────────────────────────────────────────────────────────
    await call(B, 'POST', `/community/blocks/${C.id}`, undefined, 204);
    let st = await state();
    assert.deepEqual(st.blocks, [{ blockerId: B.id, blockedId: C.id }]);
    assert.ok(!st.friendships.some(f => [f.userAId, f.userBId].sort().join() === [B.id, C.id].sort().join()), 'the friendship between B and C is gone');
    for (const [x, y] of [[B, C], [C, B]]) {
      assert.ok(!await found(x, y), `${x.name} does not find ${y.name}`);
      await call(x, 'GET', `/friendships/profile/${y.id}`, undefined, 404);
      await call(x, 'GET', `/users/${y.id}/games`, undefined, 404);
      await call(x, 'GET', `/users/${y.id}/played-games`, undefined, 404);
      await call(x, 'GET', `/friendships/${y.id}/matches`, undefined, 403);
      await call(x, 'GET', `/friendships/${y.id}/matches/game/${game.id}`, undefined, 403);
      await call(x, 'POST', `/friendships/request/${y.id}`, undefined, 409);
      assert.ok(!(await call(x, 'GET', '/friendships', undefined, 200)).data.some(f => f.friendId === y.id), 'not friends');
    }
    // Company statistics with the other person: the same answer as for anyone who is not a friend.
    const company = await call(C, 'GET', `/statistics/me/company?start=2017-01-02&endExclusive=2027-01-01&timeZone=Europe%2FLisbon&friendId=${B.id}`);
    assert.ok([403, 404].includes(company.status), 'no pair comparison: ' + company.status);
    // Third parties are not affected.
    assert.ok(await found(A, B) && await found(A, C));
    await call(A, 'GET', `/friendships/profile/${C.id}`, undefined, 200);
    // Shared history stays: names, results and matches; each one opens what they already could.
    for (const u of [B, C]) {
      const detail = (await call(u, 'GET', `/matches/${shared.id}`, undefined, 200)).data;
      assert.deepEqual(detail.players.map(p => p.userId).sort(), [A.id, B.id, C.id].sort());
      await call(u, 'GET', `/campaigns/matches/${shared.id}/journal`, undefined, 200);
    }
    await call(B, 'GET', `/session/${s1.id}`, undefined, 200);
    pass('B blocks C: in both directions no search, profile, collection, played games, shared views, requests or friendship; shared matches and sessions stay open');

    // Sessions: C's pending invitation (B accepted) leaves the actionable list; accepting is refused; nobody is removed.
    assert.ok(!(await call(C, 'GET', '/session/mine', undefined, 200)).data.some(s => s.id === s1.id), 'left C\'s list');
    const cView = (await call(C, 'GET', `/session/${s1.id}`, undefined, 200)).data;
    assert.equal(cView.inviteAvailable, false);
    const refused = await call(C, 'POST', `/session/${s1.id}/invites/respond`, { accept: true }, 409);
    assert.equal(refused.data.message, 'Não é possível aceitar este convite.');
    const bView = (await call(B, 'GET', `/session/${s1.id}`, undefined, 200)).data;
    assert.ok(bView.inviteAvailable && bView.status !== 'Cancelled' && [1, 'Accepted'].includes(bView.players.find(p => p.userId === B.id).status), 'B and the session unchanged');
    assert.ok((await call(A, 'GET', '/session/mine', undefined, 200)).data.some(s => s.id === s1.id), 'the organizer still sees it');
    // A new session: a block between any two invited together is refused, whoever organises.
    const together = await call(A, 'POST', '/session', { name: 'Noite com B e C', playerIds: [B.id, C.id] });
    assert.equal(together.status, 400); assert.equal(together.data.message, 'Não é possível convidar esta pessoa.');
    const s3 = (await call(A, 'POST', '/session', { name: 'Noite com B', playerIds: [B.id] })).data;
    const invite = await call(A, 'POST', `/session/${s3.id}/players`, { userId: C.id });
    assert.equal(invite.status, 400); assert.equal(invite.data.message, 'Não é possível convidar esta pessoa.');
    pass('sessions: the pending invitation leaves the list with a neutral state, accepting refused; no session cancelled, nobody removed; new invitations refused');

    // Campaigns: inviting C where B is a member is refused (A is C's friend).
    const cInvite = await call(A, 'POST', `/campaigns/${k.id}/members/invite`, { userId: C.id }, 400);
    assert.equal(cInvite.data.message, 'Não é possível convidar esta pessoa.');
    // Matches: refused whoever registers (A is friends with both), also when adding a player later.
    for (const players of [[A.id, B.id, C.id], [A.id, C.id, B.id]])
      assert.equal((await match(A, { playerIds: players, winnerId: A.id }, 409)).data.message, 'Não é possível registar a partida com estes jogadores.');
    await call(A, 'POST', `/matches/${m1.id}/players`, { userId: C.id }, 409);
    // Members of the same campaign with a block between them: refused with the campaignId too.
    await call(B, 'POST', `/community/blocks/${D.id}`, undefined, 204);
    assert.equal((await match(B, { campaignId: k.id, playerIds: [B.id, D.id], winnerId: B.id }, 409)).data.message, 'Não é possível registar a partida com estes jogadores.');
    await call(B, 'DELETE', `/community/blocks/${D.id}`, undefined, 204);
    await match(C, { gameSessionId: s1.id, playerIds: [C.id, B.id], winnerId: C.id }, 409); // the block is checked first
    // Joining an earlier match to a campaign widens nothing and invites nobody.
    await call(A, 'POST', `/campaigns/${k.id}/matches`, { matchId: shared.id, sessionNumber: 2, sessionTitle: 'Encontro antigo' }, 204);
    const kAfter = (await call(A, 'GET', `/campaigns/${k.id}`, undefined, 200)).data;
    assert.ok(!kAfter.members.some(m => m.userId === C.id), 'no invitation to C');
    await call(C, 'GET', `/campaigns/${k.id}`, undefined, 403);
    pass('campaigns and matches: invitation and new matches refused with the block, whoever registers or adds; an earlier match joins without widening access');

    // Publications on the game page: neither sees the other's, others see both.
    await call(B, 'PUT', `/campaigns/matches/${shared.id}/journal`, { personalRating: 8 }, 200);
    await call(C, 'PUT', `/campaigns/matches/${shared.id}/journal`, { personalRating: 6 }, 200);
    await call(B, 'PUT', `/campaigns/matches/${shared.id}/journal/publication`, { showRating: true, comment: 'Gostei muito da disputa', publishComment: true }, 200);
    await call(C, 'PUT', `/campaigns/matches/${shared.id}/journal/publication`, { showRating: true, comment: 'Boa partida em Barcelona', publishComment: true }, 200);
    const authors = async u => (await call(u, 'GET', `/game/${game.id}/experiences?limit=50`, undefined, 200)).data;
    const [ofB, ofC, ofA] = [await authors(B), await authors(C), await authors(A)];
    assert.ok(!ofB.items.some(i => i.authorName === C.userName) && ofB.items.some(i => i.authorName === B.userName), 'B does not see C');
    assert.ok(!ofC.items.some(i => i.authorName === B.userName) && ofC.items.some(i => i.authorName === C.userName), 'C does not see B');
    assert.ok(ofA.items.some(i => i.authorName === B.userName) && ofA.items.some(i => i.authorName === C.userName), 'A sees both');
    assert.equal(ofA.total - ofB.total, 1, 'the count leaves out exactly the other one');
    pass('publications: hidden between B and C (list and count); A sees both');

    // Each one removes only their own block; unblocking does not restore the friendship; the other's block stays.
    await call(C, 'DELETE', `/community/blocks/${B.id}`, undefined, 404);
    await call(C, 'POST', `/community/blocks/${B.id}`, undefined, 204);
    await call(B, 'DELETE', `/community/blocks/${C.id}`, undefined, 204);
    st = await state();
    assert.deepEqual(st.blocks, [{ blockerId: C.id, blockedId: B.id }], 'C\'s block stays');
    await call(B, 'GET', `/friendships/profile/${C.id}`, undefined, 404);
    await call(C, 'DELETE', `/community/blocks/${B.id}`, undefined, 204);
    assert.deepEqual((await state()).blocks, []);
    assert.ok(!(await call(B, 'GET', '/friendships', undefined, 200)).data.some(f => f.friendId === C.id), 'friendship not restored');
    assert.equal((await call(B, 'GET', '/community/blocks', undefined, 200)).data.length, 0);
    assert.ok(await found(B, C));
    assert.equal((await call(C, 'GET', `/session/${s1.id}`, undefined, 200)).data.inviteAvailable, true, 'the invitation is available again');
    pass('unblocking: each removes only their own; the other person\'s block stays; the friendship does not come back');

    // ── Reports of people ───────────────────────────────────────────────────────────────────────────────────────────
    await call(A, 'POST', `/community/people/${D.id}/reports`, { reason: 'Harassment', detail: 'Mensagens insistentes' }, 204);
    await call(A, 'POST', `/community/people/${D.id}/reports`, { reason: 'Spam' }, 409);
    await call(A, 'POST', `/community/people/${A.id}/reports`, { reason: 'Spam' }, 400);
    await call(B, 'POST', `/community/people/${D.id}/reports`, { reason: 'Nope' }, 400);
    await call(A, 'GET', '/admin/moderation/people', undefined, 403);
    const person = (await call(admin, 'GET', '/admin/moderation/people', undefined, 200)).data.find(p => p.userId === D.id);
    assert.ok(person && person.reasons.Harassment === 1 && !person.suspended, 'reported, not suspended');
    assert.ok(!JSON.stringify(person).includes(A.id), 'who reported is not in the list');
    await call(D, 'GET', '/friendships', undefined, 200);
    pass('person reports: a known reason, once while open, never oneself; administrators only; nothing automatic');

    // ── Suspension ──────────────────────────────────────────────────────────────────────────────────────────────────
    const before = { token: D.token, refresh: D.refresh };
    await call(admin, 'POST', `/admin/moderation/people/${D.id}/suspend`, { days: 7, reason: 'Assédio confirmado' }, 204);
    let du = (await state()).users.find(u => u.id === D.id);
    assert.ok(du.suspendedAt && du.suspendedUntil && du.tokensRevokedAt && du.refreshTokens === 0, 'suspended for 7 days; refresh tokens gone');
    assert.equal((await state()).userReports.find(r => r.reportedUserId === D.id).outcome, 'suspended');
    await call(D, 'GET', '/friendships', undefined, 401);
    assert.equal((await auth('refresh-token', { refreshToken: before.refresh })).status, 401);
    let signIn = await auth('login', { email: D.email, password: D.password, deviceInfo: 'verify-community' });
    assert.equal(signIn.status, 403); assert.equal(signIn.data.code, 'account_suspended'); assert.ok(signIn.data.suspendedUntil);
    assert.ok(!JSON.stringify(signIn.data).includes('Assédio'), 'the reason is not shown at sign-in');
    pass('suspension (7 days): sign-in refused with the end date, refresh refused, tokens already issued refused (401)');
    // Lifting: a new sign-in works; tokens from before the suspension never work again.
    await call(admin, 'POST', `/admin/moderation/people/${D.id}/lift-suspension`, undefined, 204);
    await nextSecond();
    await login(D);
    await call(D, 'GET', '/friendships', undefined, 200);
    await call({ name: 'D (old token)', token: before.token }, 'GET', '/friendships', undefined, 401);
    assert.equal((await auth('refresh-token', { refreshToken: before.refresh })).status, 401);
    pass('lifting: a new sign-in works; the access and refresh tokens from before the suspension stay refused');
    // Until review, then the end of a period: the same; the session from before the suspension never comes back.
    const lifted = { token: D.token, refresh: D.refresh };
    await call(admin, 'POST', `/admin/moderation/people/${D.id}/suspend`, { days: null, reason: 'Até revisão' }, 204);
    signIn = await auth('login', { email: D.email, password: D.password, deviceInfo: 'verify-community' });
    assert.equal(signIn.status, 403); assert.equal(signIn.data.suspendedUntil, null);
    assert.ok((await call(admin, 'GET', '/admin/moderation/people', undefined, 200)).data.some(p => p.userId === D.id && p.suspended), 'listed while suspended, without open reports');
    await call(admin, 'POST', `/admin/moderation/people/${D.id}/lift-suspension`, undefined, 204);
    await nextSecond();
    await login(D);
    const mid = { token: D.token, refresh: D.refresh };
    await call(admin, 'POST', `/admin/moderation/people/${D.id}/suspend`, { days: 7, reason: 'Período' }, 204);
    assert.equal((await fixture('POST', `/expire-suspension/${D.id}`)).status, 204);
    await nextSecond();
    await login(D);
    await call(D, 'GET', '/friendships', undefined, 200);
    for (const old of [lifted, mid]) {
      await call({ name: 'D (earlier token)', token: old.token }, 'GET', '/friendships', undefined, 401);
      assert.equal((await auth('refresh-token', { refreshToken: old.refresh })).status, 401);
    }
    await call(admin, 'POST', `/admin/moderation/people/${D.id}/suspend`, { days: 3, reason: 'x' }, 400);
    await call(admin, 'POST', `/admin/moderation/people/${D.id}/suspend`, { days: 7, reason: ' ' }, 400);
    await call(admin, 'POST', `/admin/moderation/people/${adminAccount.Id}/suspend`, { days: 7, reason: 'x' }, 400);
    await call(A, 'POST', `/admin/moderation/people/${D.id}/suspend`, { days: 7, reason: 'x' }, 403);
    // The account, matches and history stay.
    await call(D, 'GET', `/campaigns/${k.id}`, undefined, 200);
    pass('until review and end of a period: a new sign-in is required; earlier access and refresh tokens never work again; account and history kept');

    // ── Photo moderation: keep, then remove (manual) ───────────────────────────────────────────────────────────────
    await call(admin, 'POST', `/admin/moderation/photos/${entryId}/${key}/keep`, undefined, 204);
    await call(admin, 'GET', `/admin/moderation/photos/${entryId}/${key}`, undefined, 404);
    await call(A, 'POST', `${photoRoute}/reports`, { reason: 'Inappropriate' }, 204);
    await call(admin, 'POST', `/admin/moderation/photos/${entryId}/${key}/remove`, undefined, 204);
    assert.equal((await call(B, 'GET', `/campaigns/matches/${m1.id}/journal`, undefined, 200)).data.find(e => e.userId === B.id).photoUrls.length, 0, 'removed by decision');
    assert.ok((await state()).photoReports.every(r => !r.open), 'resolved');
    pass('photo moderation: «Manter» resolves and closes access; «Remover fotografia» takes it out of the diary');

    // ── Filter PT/EN ────────────────────────────────────────────────────────────────────────────────────────────────
    const filtered = 'Este texto tem termos que não são permitidos. Revê-o antes de guardar.';
    const refusedText = async (promise) => { const r = await promise; assert.equal(r.status, 400, JSON.stringify(r.data)); assert.equal(r.data.message ?? r.data.errors?.[0], filtered); };
    await refusedText(match(A, { playerIds: [A.id, B.id], winnerId: A.id, location: 'Casa do caralho' }, 400));
    await refusedText(match(A, { playerIds: [A.id, B.id], winnerId: A.id, scoreSummary: 'Ganhei, fuck you' }, 400));
    await refusedText(call(A, 'POST', '/session', { name: 'Noite do filho da puta', playerIds: [B.id] }));
    await refusedText(call(A, 'POST', '/campaigns', { name: 'Campanha', gameId: game.id, notes: 'vai-te foder' }));
    await refusedText(call(A, 'PUT', `/campaigns/${k.id}`, { name: 'Cabrão', notes: null }));
    await refusedText(call(A, 'POST', `/campaigns/${k.id}/matches`, { matchId: m1.id, sessionNumber: 3, sessionTitle: 'Kill yourself' }));
    await refusedText(call(A, 'PUT', `/campaigns/matches/${m1.id}/journal`, { tags: 'bitch' }));
    await refusedText(call(A, 'PUT', `/campaigns/matches/${m1.id}/journal/publication`, { showRating: true, comment: 'Que merda, caralho', publishComment: true }));
    const resource = { kind: 'Rules', printCategory: null, language: 'pt', author: null, platform: 'Teste', compatibility: null, isOfficial: false, isFree: null };
    await refusedText(call(A, 'POST', `/game/${game.id}/resources/suggestions`, { ...resource, title: 'Regras, motherfucker', url: `${'https://example.org/meepleboard-device-test/community/'}${made.stamp}/a` }));
    const registration = await auth('register', { userName: `Puta-${made.stamp}`, email: `comunidade-registo-${made.stamp}@meepleboard.test`, password: 'aA1!' + made.stamp + 'xyzXYZ123', isMobile: true });
    assert.equal(registration.status, 400); assert.ok(JSON.stringify(registration.data).includes(filtered), 'registration refused with the neutral message');
    pass('filter: refused in match place and summary, session name, campaign notes, name and encounter title, shared tags, publication, resource title, user name');
    // Private notes are never filtered; legitimate words pass; URLs are never filtered nor changed.
    await call(A, 'PUT', `/campaigns/matches/${m1.id}/journal`, { notes: 'Nota privada: que caralho de jogada' }, 200);
    await call(A, 'PUT', `/campaigns/matches/${m1.id}/journal/publication`, { showRating: false, comment: 'Rascunho: que caralho', publishComment: false }, 200);
    await match(A, { playerIds: [A.id, B.id], winnerId: A.id, location: 'Disputa no computador em Barcelona', scoreSummary: 'Classic: Scunthorpe vs Essex' });
    await call(A, 'POST', '/session', { name: 'Assassin\'s Creed e uma cabra', playerIds: [B.id] }, 201);
    const url = `https://example.org/meepleboard-device-test/community/${made.stamp}/fuck-cunt-kys`;
    const suggestion = (await call(A, 'POST', `/game/${game.id}/resources/suggestions`, { ...resource, title: 'Regras em computador', url }, 201)).data;
    assert.equal(suggestion.url, url, 'the URL is kept as sent');
    pass('filter: private notes and unpublished drafts are not filtered; legitimate PT/EN words pass; resource URLs are never filtered nor changed');

    // ── «Bloquear autor» on a publication: by the publication's id (the list never carries the author's id) ───────
    const pubOfB = (await authors(A)).items.find(i => i.authorName === B.userName);
    const ownPub = (await authors(B)).items.find(i => i.authorName === B.userName);
    await call(B, 'POST', `/community/blocks/experiences/${ownPub.id}`, undefined, 400);
    await call(A, 'POST', `/community/blocks/experiences/${'0'.repeat(8)}-0000-0000-0000-${'0'.repeat(12)}`, undefined, 404);
    await call(A, 'POST', `/community/blocks/experiences/${pubOfB.id}`, undefined, 204);
    assert.ok((await state()).blocks.some(b => b.blockerId === A.id && b.blockedId === B.id), 'A blocked the author');
    assert.ok(!(await authors(A)).items.some(i => i.authorName === B.userName), 'and no longer sees the publication');
    await call(A, 'DELETE', `/community/blocks/${B.id}`, undefined, 204);
    pass('«Bloquear autor»: by the publication, never one\'s own; the publication disappears for whoever blocked');

    // ── Deleting an account removes the blocks it made and received; open reports about it close ────────────────────
    await call(A, 'POST', `/community/blocks/${D.id}`, undefined, 204);
    await call(D, 'POST', `/community/blocks/${C.id}`, undefined, 204);
    await call(B, 'POST', `/community/people/${D.id}/reports`, { reason: 'Spam', detail: 'Texto sobre D' }, 204);
    assert.equal((await state()).blocks.length, 2);
    await call(D, 'DELETE', '/users/me', { password: D.password }, 204);
    st = await state();
    assert.equal(st.blocks.length, 0, 'blocks made and received are gone');
    assert.ok(st.userReports.filter(r => r.reportedUserId === D.id).every(r => !r.open && !r.hasDetail), 'reports about D closed and without text, also the resolved ones');
    assert.equal(st.users.find(u => u.id === D.id).suspensionReason, null);
    pass('account deletion: the blocks it made and received go; reports about it close and lose their text');
  }
}).catch(error => { console.error('FAIL', error.message); process.exit(1); });
