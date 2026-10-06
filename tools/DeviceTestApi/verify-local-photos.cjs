// Real HTTP/JWT + isolated SQL journal records + local files. Never contacts Cloudinary.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const base = 'http://127.0.0.1:5099';
const accounts = JSON.parse(fs.readFileSync(path.resolve(__dirname, '../../.device-tests/accounts.json'), 'utf8'));
async function request(method, url, token, body, expected = 200) {
  const headers = token ? { Authorization: 'Bearer ' + token } : {};
  if (body && !(body instanceof FormData)) { headers['Content-Type'] = 'application/json'; body = JSON.stringify(body); }
  const response = await fetch(base + url, { method, headers, body });
  assert.equal(response.status, expected, method + ' ' + url + ': unexpected HTTP status');
  return response;
}
(async () => {
  const tokens = [];
  for (const a of accounts) tokens.push((await (await request('POST', '/MeepleBoard/auth/login', null, { email: a.Email, password: a.Password, rememberMe: false })).json()).token);
  const suggestions = await (await request('GET', '/MeepleBoard/game/suggestions?query=Meeple&limit=10')).json();
  assert.ok(suggestions.length >= 3); console.log('PASS real SQL catalogue search returns synthetic games');
  const game = suggestions.find(g => g.name.includes('Competitivo'));
  const match = await (await request('POST', '/MeepleBoard/matches', tokens[0], { gameId: game.id, gameName: game.name, matchDate: new Date(Date.now() - 60000).toISOString(), isSoloGame: true, personalRating: 7.5, playerIds: [accounts[0].Id, accounts[1].Id] }, 201)).json();
  const route = '/MeepleBoard/campaigns/matches/' + match.id + '/journal/photos';
  const png = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aN5kAAAAASUVORK5CYII=', 'base64');
  const form = new FormData(); form.append('file', new Blob([png], { type: 'image/png' }), 'fixture.png');
  const entry = await (await request('POST', route, tokens[0], form)).json();
  const photo = entry.photoUrls[0]; assert.ok(photo.startsWith('/MeepleBoard/')); assert.ok(!photo.includes('cloudinary'));
  console.log('PASS photo upload persists protected reference using local storage');
  for (const token of tokens.slice(0, 2)) {
    const response = await request('GET', photo, token); assert.ok(response.headers.get('cache-control').includes('no-store'));
    assert.deepEqual(Buffer.from(await response.arrayBuffer()), png);
  }
  console.log('PASS participants can read authorized local photo bytes');
  await request('GET', photo, null, null, 401);
  await request('GET', photo, tokens[2], null, 403);
  await request('GET', photo, tokens[3], null, 403);
  console.log('PASS visitor, outsider and nonparticipant cannot read image');
  await request('DELETE', route + '?photoUrl=' + encodeURIComponent(photo), tokens[1], null, 404);
  await request('DELETE', route + '?photoUrl=' + encodeURIComponent(photo), tokens[0]);
  await request('GET', photo, tokens[0], null, 404);
  console.log('PASS only author removes local photograph; URL becomes unavailable');
  console.log('5 live SQL/HTTP catalogue/photo scenarios passed; production storage untouched.');
})().catch(error => { console.error(error.message); process.exitCode = 1; });
