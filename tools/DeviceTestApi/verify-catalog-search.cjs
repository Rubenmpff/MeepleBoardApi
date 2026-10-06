// Uses only the guarded disposable API and synthetic accounts. No external BGG calls.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const base = 'http://127.0.0.1:5099';
async function request(route, token, method = 'GET', body, expected = 200) {
  const r = await fetch(base + route, { method, headers: { ...(token ? { Authorization: 'Bearer ' + token } : {}), ...(body ? { 'Content-Type': 'application/json' } : {}) }, body: body ? JSON.stringify(body) : undefined });
  assert.equal(r.status, expected, method + ' ' + route + ': HTTP status');
  const text = await r.text();
  return text ? JSON.parse(text) : [];
}
(async () => {
  const health = await request('/device-test/health');
  assert.equal(health.environment, 'DeviceTests');
  assert.equal(health.database, 'MeepleBoard_DeviceTests');
  const a = JSON.parse(fs.readFileSync(path.resolve(__dirname, '../../.device-tests/accounts.json'), 'utf8')).find(a => a.Email === 'autor@meepleboard.test');
  const login = await request('/MeepleBoard/auth/login', null, 'POST', { email: a.Email, password: a.Password, deviceInfo: 'MeepleBoard Mobile App' });
  assert.ok(login.success && login.token && login.refreshToken && login.user);
  const token = login.token;
  async function search(query, suffix = '') {
    const rows = await request('/MeepleBoard/game/suggestions?query=' + encodeURIComponent(query) + '&offset=0&limit=10&sort=relevance' + suffix, token);
    assert.ok(Array.isArray(rows));
    console.log('HTTP 200 search ' + query + ': ' + rows.length + ' results');
    return rows;
  }
  const all = await search('Meeple'); assert.equal(all.length, 3);
  for (const name of ['Meeple Teste Competitivo', 'Meeple Teste Cooperativo', 'Meeple Teste Solo']) {
    const rows = await search(name); assert.equal(rows.length, 1); assert.equal(rows[0].name, name);
    const detail = await request('/MeepleBoard/game/' + rows[0].id, token);
    assert.equal(detail.name, name);
  }
  const word = await search('competitivo'); assert.equal(word.length, 1); assert.equal(word[0].bggId, 990001);
  assert.equal((await search('zzzinexistente')).length, 0);
  assert.equal((await search('Catan')).length, 0); // Not seeded; genuine empty success.
  assert.equal((await search('Meeple', '&playerCount=5')).length, 0);
  await request('/MeepleBoard/game/suggestions?query=Meeple&limit=invalid', token, 'GET', undefined, 400);
  console.log('PASS malformed search returns HTTP 400 rather than an empty success');
  // Preserve existing synthetic entry and its price; create one fixture only if absent.
  const route = '/MeepleBoard/users/' + a.Id + '/games';
  let response = await fetch(base + route, { headers: { Authorization: 'Bearer ' + token } });
  assert.ok([200, 204].includes(response.status));
  let library = response.status === 204 ? [] : await response.json();
  const solo = all.find(g => g.bggId === 990003);
  if (!library.some(g => g.gameId === solo.id)) {
    await request(route, token, 'POST', { gameId: solo.id, gameName: solo.name, status: 1, pricePaid: 0 }, 201);
    library = await request(route, token);
    assert.equal(library.find(g => g.gameId === solo.id).pricePaid, 0);
  }
  assert.ok(library.some(g => g.gameName === 'Meeple Teste Solo'));
  const played = await request('/MeepleBoard/users/' + a.Id + '/played-games', token);
  assert.ok(played.some(g => g.gameName === 'Meeple Teste Competitivo'));
  console.log('PASS SQL-backed collection reload: Meeple Teste Solo; played history preserved');
  console.log('PASS catalogue detail access, word-token search, filters and genuine empty responses');
})().catch(e => { console.error(e.message); process.exitCode = 1; });
