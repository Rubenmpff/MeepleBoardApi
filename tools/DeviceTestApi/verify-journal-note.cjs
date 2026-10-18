// DeviceTests only: the private note through the diary and back to the match page (requested 18/10/2026).
//   node tools/DeviceTestApi/verify-journal-note.cjs
// Fresh people (disposable.cjs, removed at the end, also on failure): A writes, B plays the same match. The match page and the
// diary both read GET …/journal (and the match); these are exactly the requests the app makes when it opens or comes back.
// - saving a note: the next read (coming back to the match page) has the new text; B never receives it;
// - erasing it and saving: the next read has no note (the field is omitted, as for any empty note);
// - opening the diary and leaving without saving (only reads, no PUT): the previous note stays;
// - the closed rating is never changed by any of this.
if (require.main !== module) return; // Importing a verifier never runs it: only `node <file>` does (see docs/testes-macos-docker.md).
const assert = require('node:assert/strict');
const { run } = require('./disposable.cjs');
const base = 'http://127.0.0.1:5099';
const pass = text => console.log('PASS ' + text);

run({ label: 'journalnote', people: [{ role: 'a' }, { role: 'b' }], friends: [['a', 'b']] }, async world => {
  const [A, B] = await Promise.all(['a', 'b'].map(r => world.login(r)));
  const call = async (u, method, route, body, expected) => {
    const r = await fetch(base + '/MeepleBoard' + route, { method, headers: { Authorization: 'Bearer ' + u.token, ...(body ? { 'Content-Type': 'application/json' } : {}) }, body: body ? JSON.stringify(body) : undefined });
    const text = await r.text();
    assert.equal(r.status, expected, `${method} ${route.replace(/[0-9a-f-]{36}/g, '{id}')} → ${r.status} ${text.slice(0, 160)}`);
    return text ? JSON.parse(text) : null;
  };
  const game = await call(A, 'GET', '/game/search?name=Wingspan', null, 200);
  const match = await call(A, 'POST', '/matches', { gameId: game.id, gameName: game.name, matchDate: new Date().toISOString(), isSoloGame: false, winnerId: A.id, playerIds: [A.id, B.id], personalRating: 6.5 }, 201);
  const route = `/campaigns/matches/${match.id}/journal`;
  // What the match page and the diary read when they open or come back.
  const comeBack = async u => { await call(u, 'GET', `/matches/${match.id}`, null, 200); await call(u, 'GET', `${route}/publication`, null, 200);
    return (await call(u, 'GET', route, null, 200)).find(e => e.userId === A.id); };
  const save = notes => call(A, 'PUT', route, { personalRating: 6.5, notes, tags: null }, 200);

  let mine = await comeBack(A);
  assert.equal(mine.notes, undefined, 'no note yet'); assert.equal(mine.personalRating, 6.5);

  await save('Primeira nota privada');
  mine = await comeBack(A);
  assert.equal(mine.notes, 'Primeira nota privada', 'the match page shows the saved note when you come back');
  const seenByB = await comeBack(B);
  assert.ok(!('notes' in seenByB), 'the other player never receives it');
  pass('saving a note: coming back to the match page reads the new text; the other player never receives it');

  // Opening the diary and leaving without saving makes only reads.
  mine = await comeBack(A);
  assert.equal(mine.notes, 'Primeira nota privada', 'leaving without saving keeps the previous note');
  pass('opening the diary and leaving without saving (reads only, no PUT): the previous note stays');

  await save('Segunda versão');
  assert.equal((await comeBack(A)).notes, 'Segunda versão');
  await save(null);
  mine = await comeBack(A);
  assert.ok(!('notes' in mine), 'an erased note is gone (the field is omitted)');
  assert.equal(mine.personalRating, 6.5, 'the closed rating is unchanged');
  pass('editing and then erasing the note: coming back reads the change each time; the rating stays 6,5');
}).catch(error => { console.error('FAIL', error.message); process.exit(1); });
