// DeviceTests only: statistics against real SQL on dated matches (statistics-dataset.cjs) of fresh people (disposable.cjs),
// removed at the end, also on failure. Never the iPhone accounts; credentials stay in memory.
if (require.main !== module) return; // Importing a verifier never runs it: only `node <file>` does (see docs/testes-macos-docker.md).
const assert=require('node:assert/strict');
const {run,quartet}=require('./disposable.cjs'),dataset=require('./statistics-dataset.cjs');
const base='http://127.0.0.1:5099';
async function call(route,token,method='GET',body,expected=200){
 const r=await fetch(base+route,{method,headers:{...(token?{Authorization:'Bearer '+token}:{}),...(body?{'Content-Type':'application/json'}:{})},body:body?JSON.stringify(body):undefined});
 assert.equal(r.status,expected,method+' '+route+' HTTP '+r.status);const text=await r.text();return text?JSON.parse(text):null;
}
const route=(q,suffix='')=>'/MeepleBoard/statistics/me'+suffix+'?'+new URLSearchParams(q);
run({label:'statistics',...quartet()},async world=>{
 const health=await call('/device-test/health');assert.deepEqual(health,{environment:'DeviceTests',database:'MeepleBoard_DeviceTests',externalDelivery:false});
 const tokenFor=async(a)=>(await call('/MeepleBoard/auth/login',null,'POST',{email:a.Email,password:a.Password,deviceInfo:'MeepleBoard Mobile App'})).token;
 const author=world.people.autor,peer=world.people.participante,third=world.people.membro,outsider=world.people.alheio;
 const token=await tokenFor(author);
 const games=await call('/MeepleBoard/game/suggestions?query=Meeple&limit=10',token);
 const comp=games.find(g=>g.name==='Meeple Teste Competitivo'),solo=games.find(g=>g.name==='Meeple Teste Solo'),coop=games.find(g=>g.name==='Meeple Teste Cooperativo');assert.ok(comp&&solo&&coop);
 const query={start:'2024-10-27',endExclusive:'2024-10-28',timeZone:'Europe/Lisbon'};
 await dataset.statisticsCases({id:author.Id,token},{id:peer.Id},{id:third.Id});
 const summary=await call(route(query),token);
 assert.equal(summary.matches,8);assert.equal(summary.distinctGames,3);assert.equal(summary.recordedMinutes,90);assert.equal(summary.matchesWithDuration,5);assert.equal(summary.matchesWithoutDuration,3);
 assert.deepEqual(summary.results,{wins:2,losses:2,draws:2,known:6,withoutResult:2,legacy:1,winRate:33.33});assert.equal(summary.averagePersonalRating,4.06);assert.equal(summary.ratedMatches,8);
 assert.equal(summary.evolution.reduce((n,b)=>n+b.matches,0),8);
 const expected={all:8,games:8,duration:5,'missing-duration':3,ratings:8,known:6,wins:2,losses:2,draws:2,undefined:2,legacy:1};
 for(const [metric,n]of Object.entries(expected)){
  const list=await call(route({...query,metric},'/matches'),token);assert.equal(list.total,n,metric);assert.equal(list.items.length,n);
  for(const row of list.items)assert.ok(!('players'in row)&&!('notes'in row)&&!('userId'in row)&&!('location'in row));
 }
 const page1=await call(route({...query,offset:'0',limit:'3'},'/matches'),token),page2=await call(route({...query,offset:'3',limit:'3'},'/matches'),token);
 assert.equal(new Set([...page1.items,...page2.items].map(x=>x.id)).size,6);
 const mode=await call(route({...query,mode:'SOLO'}),token);assert.equal(mode.matches,3);assert.deepEqual(mode.results,{wins:0,losses:1,draws:1,known:2,withoutResult:1,legacy:0,winRate:0});
 const game=await call(route({...query,gameId:comp.id}),token);assert.equal(game.matches,4);
 const both=await call(route({...query,gameId:comp.id,mode:'COMPETITIVE'}),token);assert.equal(both.matches,3);assert.equal(both.results.legacy,0);
 const peerToken=await tokenFor(peer);const peerSummary=await call(route(query),peerToken);assert.equal(peerSummary.matches,5);assert.equal(peerSummary.ratedMatches,0);assert.equal(peerSummary.averagePersonalRating,null);
 const thirdSummary=await call(route(query),await tokenFor(third));assert.equal(thirdSummary.results.losses,3);assert.equal(thirdSummary.results.draws,0);
 const outsiderToken=await tokenFor(outsider);const empty=await call(route(query),outsiderToken);assert.equal(empty.matches,0);assert.equal(empty.recordedMinutes,null);assert.equal(empty.results.winRate,null);
 await call(route(query),null,'GET',null,401);
 await call(route({...query,mode:'invalid'}),token,'GET',null,400);await call(route({...query,timeZone:'Invalid/Zone'}),token,'GET',null,400);
 await call(route({...query,endExclusive:query.start}),token,'GET',null,400);await call(route({...query,metric:'invalid'},'/matches'),token,'GET',null,400);
 // An arbitrary userId parameter cannot expand the authenticated scope.
 const forged=await call(route({...query,userId:author.Id}),outsiderToken);assert.equal(forged.matches,0);
 const text=JSON.stringify(summary);assert.ok(!text.includes('STATISTICS_TEST_PRIVATE_AUTHOR'));assert.ok(!text.includes(author.Email));assert.ok(!text.includes(peer.Email));
 console.log('PASS DeviceTests statistics: 8 matches / 3 games; 2 wins, 2 losses, 2 draws; 2 without known result; 90 min from 5/8; own rating 4.06 from 8/8.');
 console.log('PASS SQL-backed API: shared win, partial draw, Solo, cooperative, zero/negative scores, rating zero/half points, legacy, local DST boundary and exclusive end; filters/supporting rows/pagination; own-only diary, outsider isolation and no private fields.');
}).catch(e=>{console.error(e.message);process.exitCode=1;});
