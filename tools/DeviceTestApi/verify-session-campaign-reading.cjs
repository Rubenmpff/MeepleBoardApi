const fs = require('node:fs');
const assert = require('node:assert/strict');
const root = require('node:path').resolve(__dirname, '../..');
const base = 'http://127.0.0.1:5099';
async function call(path, token, options = {}) {
  const r = await fetch(base + path, { ...options, headers: { ...(token ? { Authorization: 'Bearer ' + token } : {}), ...(options.body ? { 'Content-Type': 'application/json' } : {}) } });
  const body = await r.text();
  return { status: r.status, body };
}
(async () => {
  const health = await call('/device-test/health');
  assert.equal(JSON.parse(health.body).database, 'MeepleBoard_DeviceTests');
  const accounts = JSON.parse(fs.readFileSync(root + '/.device-tests/accounts.json', 'utf8'));
  const authorIds = {};
  const inviteFile = root + '/.device-tests/session-invite-friends-fixture.json';
  const invitedSession = fs.existsSync(inviteFile) ? JSON.parse(fs.readFileSync(inviteFile, 'utf8')).sessionId : null;
  const writeFile = root + '/.device-tests/session-write-fixture.json';
  const privateSession = fs.existsSync(writeFile) ? JSON.parse(fs.readFileSync(writeFile, 'utf8')).sessionId : null;
  for (const account of accounts) {
    const login = await call('/MeepleBoard/auth/login', null, { method: 'POST', body: JSON.stringify({ email: account.Email, password: account.Password, deviceInfo: 'MeepleBoard Mobile App' }) });
    assert.equal(login.status, 200, 'Synthetic login');
    const result = JSON.parse(login.body);
    assert.equal(result.success, true);
    assert.ok(result.refreshToken && result.user);
    const token = result.token;
    assert.ok(token);
    const claims = JSON.parse(Buffer.from(token.split('.')[1], 'base64url').toString());
    assert.equal(claims.sub || claims.nameid || claims.id || claims['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'], account.Id);
    console.log('PASS login ' + account.Email);
    for (const path of ['/MeepleBoard/matches/last', '/MeepleBoard/matches/pending-journal']) {
      const response = await call(path, token);
      assert.ok(response.status === 200 || (path.endsWith('/last') && response.status === 404) || (path.endsWith('/pending-journal') && response.status === 204), 'Home data: ' + response.status);
      console.log('PASS home ' + path + ': HTTP ' + response.status);
    }
    for (const resource of ['session', 'campaigns']) {
      const list = await call('/MeepleBoard/' + resource + '/mine', token);
      console.log(resource + ' list HTTP ' + list.status);
      assert.equal(list.status, 200);
      const rows = JSON.parse(list.body);
      assert.ok(Array.isArray(rows));
      if (account === accounts[0]) { assert.ok(rows.length > 0, 'Existing SQL fixtures'); authorIds[resource] = resource === 'session' && privateSession ? privateSession : rows.find(row => row.id !== invitedSession)?.id; assert.ok(authorIds[resource], 'A fixture without outsider membership is required'); }
      if (account === accounts[2]) {
        if (resource === 'campaigns') assert.equal(rows.length, 0, 'Outsider has no campaign membership');
        else {
          assert.ok(!rows.some(row => row.id === authorIds[resource]), 'Private fixture absent from outsider list');
          if (invitedSession) assert.ok(rows.some(row => row.id === invitedSession), 'Explicitly invited fixture is readable');
        }
        assert.equal((await call('/MeepleBoard/' + resource + '/' + authorIds[resource], token)).status, 403);
      }
      for (const row of rows) {
        const detail = await call('/MeepleBoard/' + resource + '/' + row.id, token);
        assert.equal(detail.status, 200, resource + ' detail');
        if (account === accounts[2] && resource === 'session') {
          assert.ok(JSON.parse(detail.body).players.some(player => player.userId === account.Id), 'Every outsider session requires actual membership');
        }
        assert.ok(!detail.body.includes('PRIVATE_SQL_'), 'No private match notes in containers');
      }
      console.log('PASS ' + resource + ': ' + rows.length + ' readable details');
      assert.equal((await call('/MeepleBoard/' + resource + '/mine')).status, 401);
      if (authorIds[resource]) assert.equal((await call('/MeepleBoard/' + resource + '/' + authorIds[resource])).status, 401);
    }
  }
})().catch(e => { console.error(e.message); process.exitCode = 1; });
