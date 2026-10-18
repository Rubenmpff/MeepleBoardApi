// DeviceTests only: the dated matches the statistics and company verifiers read, created for one run's disposable people
// (disposable.cjs quartet: autor, participante, alheio, membro). Not a verifier: importing it does nothing.
// 27/10/2024 (Lisbon): ten cases — shared win, partial draw, loss, Solo, team win, undefined, legacy result, both ends of
// the day. 28/10/2024: the company extras — team loss, draw and undefined, a private Solo of participante and a shared win
// that participante did not share.
const assert = require('node:assert/strict');
const base = 'http://127.0.0.1:5099';

async function post(route, token, body) {
  const r = await fetch(base + route, { method: 'POST', headers: { Authorization: 'Bearer ' + token, 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  const text = await r.text();
  assert.equal(r.status, 201, 'POST ' + route + ' → ' + r.status + ' ' + text.slice(0, 200));
  return JSON.parse(text);
}

async function games(token) {
  const r = await fetch(base + '/MeepleBoard/game/suggestions?query=Meeple&limit=10', { headers: { Authorization: 'Bearer ' + token } });
  const list = await r.json();
  const comp = list.find(g => g.name === 'Meeple Teste Competitivo'), solo = list.find(g => g.name === 'Meeple Teste Solo'), coop = list.find(g => g.name === 'Meeple Teste Cooperativo');
  assert.ok(comp && solo && coop, 'the three fictitious games exist');
  return { comp, solo, coop };
}

/** The ten statistics cases of 27/10/2024, registered by autor. */
async function statisticsCases(author, peer, third) {
  const { comp, solo, coop } = await games(author.token);
  const ids = [author.id, peer.id, third.id];
  const cases = [
    ['shared', comp, 'COMPETITIVE', 'Win', ids, [author.id, peer.id], 30, 0, '2024-10-27T12:00:00Z', [17, 0, -5]],
    ['partial-draw', comp, 'COMPETITIVE', 'Draw', ids, [author.id, peer.id], null, 7.5, '2024-10-27T12:01:00Z', [0, 0, -5]],
    ['competitive-loss', comp, 'COMPETITIVE', 'Win', ids, [peer.id], 0, 10, '2024-10-27T12:02:00Z', [-5, 17, 0]],
    ['solo-loss', solo, 'SOLO', 'Loss', [author.id], [], 15, 7.5, '2024-10-27T12:03:00Z', [-5]],
    ['team-win', coop, 'COOPERATIVE', 'Win', [author.id, peer.id], [], 45, 0, '2024-10-27T12:04:00Z', null],
    ['undefined', solo, 'SOLO', 'Undefined', [author.id], [], null, 0, '2024-10-27T12:05:00Z', [0]],
    ['legacy', comp, null, null, [author.id, peer.id], [], null, 7.5, '2024-10-27T12:06:00Z', null],
    ['start-inclusive', solo, 'SOLO', 'Draw', [author.id], [], 0, 0, '2024-10-26T23:00:00Z', [0]],
    ['before-start', solo, 'SOLO', 'Win', [author.id], [], 99, 10, '2024-10-26T22:59:59Z', [1]],
    ['end-exclusive', solo, 'SOLO', 'Win', [author.id], [], 99, 10, '2024-10-28T00:00:00Z', [1]],
  ];
  const created = {};
  for (const [name, game, mode, result, playerIds, resultPlayerIds, duration, rating, date, scores] of cases) {
    created[name] = (await post('/MeepleBoard/matches', author.token, { gameId: game.id, gameName: game.name, matchDate: date, isSoloGame: mode === 'SOLO', gameMode: mode, result, resultPlayerIds,
      sharedVictoryAllowed: name === 'shared', durationInMinutes: duration, personalRating: rating, playerIds, winnerId: name === 'legacy' ? author.id : null,
      scoresEnabled: scores != null, playerScores: scores?.map((score, i) => ({ userId: playerIds[i], score })), notes: 'STATISTICS_TEST_PRIVATE_AUTHOR', tags: 'statistics-test' })).id;
  }
  return { matches: created, comp, solo, coop };
}

/** The company extras of 28/10/2024. */
async function companyExtras(author, peer, third) {
  const { comp, solo, coop } = await games(author.token);
  const made = {};
  for (const [index, result] of ['Loss', 'Draw', 'Undefined'].entries())
    made[result] = (await post('/MeepleBoard/matches', author.token, { gameId: coop.id, gameName: coop.name, gameMode: 'COOPERATIVE', result, playerIds: [author.id, peer.id], resultPlayerIds: [],
      isSoloGame: false, matchDate: `2024-10-28T12:0${index}:00Z`, personalRating: 0, scoresEnabled: false, notes: 'COMPANY_AUTHOR_PRIVATE' })).id;
  made.privatePeer = (await post('/MeepleBoard/matches', peer.token, { gameId: solo.id, gameName: solo.name, gameMode: 'SOLO', result: 'Win', playerIds: [peer.id], resultPlayerIds: [], isSoloGame: true,
    matchDate: '2024-10-28T13:00:00Z', personalRating: 7.5, scoresEnabled: true, playerScores: [{ userId: peer.id, score: -5 }], notes: 'COMPANY_PEER_PRIVATE' })).id;
  made.sharedOther = (await post('/MeepleBoard/matches', author.token, { gameId: comp.id, gameName: comp.name, gameMode: 'COMPETITIVE', result: 'Win', sharedVictoryAllowed: true,
    playerIds: [author.id, peer.id, third.id], resultPlayerIds: [author.id, third.id], isSoloGame: false, matchDate: '2024-10-28T12:03:00Z', personalRating: 0, scoresEnabled: true,
    playerScores: [{ userId: author.id, score: -5 }, { userId: peer.id, score: 0 }, { userId: third.id, score: 17 }] })).id;
  return made;
}

module.exports = { statisticsCases, companyExtras };
