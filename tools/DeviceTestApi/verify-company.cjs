// Guarded synthetic SQL/API checks. No credentials, tokens or private text are printed.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'../..'),base='http://127.0.0.1:5099';
async function call(url,token,method='GET',body,status=200){const r=await fetch(base+url,{method,headers:{...(token?{Authorization:`Bearer ${token}`}:{ }),...(body?{'Content-Type':'application/json'}:{})},body:body?JSON.stringify(body):undefined});assert.ok(Array.isArray(status)?status.includes(r.status):r.status===status,`${method} ${url.split('?')[0]} returned ${r.status}`);const text=await r.text();return text?JSON.parse(text):null;}
const route=(path,q)=>`/MeepleBoard/statistics/me/${path}?${new URLSearchParams(q)}`;
(async()=>{
 assert.deepEqual(await call('/device-test/health'),{environment:'DeviceTests',database:'MeepleBoard_DeviceTests',externalDelivery:false});
 const accounts=JSON.parse(fs.readFileSync(path.join(root,'.device-tests/accounts.json'),'utf8'));
 const account=name=>accounts.find(a=>a.Email===name+'@meepleboard.test');const a=account('autor'),b=account('participante'),c=account('membro'),outsider=account('alheio');
 const login=async user=>(await call('/MeepleBoard/auth/login',null,'POST',{email:user.Email,password:user.Password,deviceInfo:'MeepleBoard Mobile App'})).token;
 const token=await login(a),peerToken=await login(b),outToken=await login(outsider);
 const q={start:'2024-10-27',endExclusive:'2024-10-28',timeZone:'Europe/Lisbon'};
 const paired=await call(route('company',{...q,friendId:b.Id}),token);
 assert.equal(paired.matches,5);assert.equal(paired.results.myWins,1);assert.equal(paired.results.friendWins,2);assert.equal(paired.results.sharedWins,1);assert.equal(paired.results.draws,1);assert.equal(paired.results.teamWins,1);assert.equal(paired.results.known,4);assert.equal(paired.results.unknown,1);
 for(const [metric,n] of Object.entries({'pair-all':5,'my-win':1,'friend-win':2,'shared-win':1,'pair-draw':1,'draw-together':1,'team-win':1,'pair-known':4,'unknown-pair':1,'paired-scores':3})) {
  const list=await call(route('matches',{...q,friendId:b.Id,metric}),token);assert.equal(list.total,n,metric);
 }
 const third=await call(route('company',{...q,friendId:c.Id}),token);assert.equal(third.matches,3);assert.equal(third.results.myOnlyWins,1);assert.equal(third.results.draws,1);assert.equal(third.results.drawsTogether,0);assert.equal(third.results.otherWins,1);
 await call(route('company',{...q,friendId:c.Id}),peerToken,'GET',null,403);
 await call(route('matches',{...q,friendId:c.Id,metric:'pair-all'}),peerToken,'GET',null,403);
 await call(route('company',{...q,friendId:b.Id}),outToken,'GET',null,403);
 await call(route('company',{...q,friendId:b.Id}),null,'GET',null,401);
 const comp=paired.comparisons.find(g=>g.mode==='COMPETITIVE');assert.equal(comp.completeScores,3);assert.ok(comp.scores.some(s=>s.mine===0&&s.friend===0));assert.ok(comp.scores.some(s=>s.mine===-5));
 const game=await call(route('company',{...q,friendId:b.Id,gameId:comp.gameId,mode:'COMPETITIVE'}),token);assert.equal(game.matches,3);assert.equal(game.results.unknown,0);
 const explore=await call(route('explore',q),token);assert.equal(explore.games.find(g=>g.mode==='COMPETITIVE').minimum,-5);assert.equal(explore.games.find(g=>g.mode==='COMPETITIVE').maximum,17);assert.equal(explore.games.find(g=>g.mode==='SOLO').minimum,-5);assert.equal(explore.games.find(g=>g.mode==='SOLO').maximum,0);
 assert.equal(explore.games.find(g=>g.mode==='COOPERATIVE').averageRating,0);
 const record=await call(route('matches',{...q,metric:'scores',gameId:comp.gameId,mode:'COMPETITIVE',scoreValue:'-5'}),token);assert.equal(record.total,1);assert.equal(record.items[0].score,-5);
 const own=await call(route('company',{...q,userId:a.Id}),outToken);assert.ok(own.companions.every(x=>x.matches===0));
 const games=await call('/MeepleBoard/game/suggestions?query=Meeple&limit=10',token),coop=games.find(g=>g.name==='Meeple Teste Cooperativo'),solo=games.find(g=>g.name==='Meeple Teste Solo');
 const fixturePath=path.join(root,'.device-tests/company-fixture.json');const fixture=fs.existsSync(fixturePath)?JSON.parse(fs.readFileSync(fixturePath,'utf8')):{matches:{}};
 for(const [index,result] of ['Loss','Draw','Undefined'].entries()){
  if(fixture.matches[result])continue;
  const saved=await call('/MeepleBoard/matches',token,'POST',{gameId:coop.id,gameName:coop.name,gameMode:'COOPERATIVE',result,playerIds:[a.Id,b.Id],resultPlayerIds:[],isSoloGame:false,matchDate:`2024-10-28T12:0${index}:00Z`,personalRating:0,scoresEnabled:false,notes:'COMPANY_AUTHOR_PRIVATE'},201);fixture.matches[result]=saved.id;
  fs.writeFileSync(fixturePath,JSON.stringify(fixture,null,2),{mode:0o600});
 }
 if(!fixture.privatePeer){const saved=await call('/MeepleBoard/matches',peerToken,'POST',{gameId:solo.id,gameName:solo.name,gameMode:'SOLO',result:'Win',playerIds:[b.Id],resultPlayerIds:[],isSoloGame:true,matchDate:'2024-10-28T13:00:00Z',personalRating:7.5,scoresEnabled:true,playerScores:[{userId:b.Id,score:-5}],notes:'COMPANY_PEER_PRIVATE'},201);fixture.privatePeer=saved.id;fs.writeFileSync(fixturePath,JSON.stringify(fixture,null,2),{mode:0o600});}
 if(!fixture.sharedOther){const saved=await call('/MeepleBoard/matches',token,'POST',{gameId:comp.gameId,gameName:comp.name,gameMode:'COMPETITIVE',result:'Win',sharedVictoryAllowed:true,playerIds:[a.Id,b.Id,c.Id],resultPlayerIds:[a.Id,c.Id],isSoloGame:false,matchDate:'2024-10-28T12:03:00Z',personalRating:0,scoresEnabled:true,playerScores:[{userId:a.Id,score:-5},{userId:b.Id,score:0},{userId:c.Id,score:17}]},201);fixture.sharedOther=saved.id;fs.writeFileSync(fixturePath,JSON.stringify(fixture,null,2),{mode:0o600});}
 const otherShared=await call(route('company',{start:'2024-10-28',endExclusive:'2024-10-29',timeZone:q.timeZone,mode:'COMPETITIVE',friendId:b.Id}),token);assert.equal(otherShared.results.myWins,1);assert.equal(otherShared.results.otherSharedWins,1);assert.equal(otherShared.results.otherWins,0);
 const team=await call(route('company',{start:'2024-10-28',endExclusive:'2024-10-29',timeZone:q.timeZone,mode:'COOPERATIVE',friendId:b.Id}),token);assert.equal(team.matches,3);assert.equal(team.results.teamLosses,1);assert.equal(team.results.teamDraws,1);assert.equal(team.results.unknown,1);
 const undefinedTeam=await call(route('matches',{start:'2024-10-28',endExclusive:'2024-10-29',timeZone:q.timeZone,mode:'COOPERATIVE',friendId:b.Id,metric:'unknown-pair'}),token);assert.equal(undefinedTeam.total,1);assert.equal(undefinedTeam.items[0].friendOutcome,'Undefined');
 const year=await call(route('year',{year:'2024',timeZone:q.timeZone}),token);
 const summary=await call('/MeepleBoard/statistics/me?'+new URLSearchParams({start:'2024-01-01',endExclusive:'2025-01-01',timeZone:q.timeZone}),token);
 assert.equal(year.summary.matches,summary.matches);assert.equal(year.summary.results.known,summary.results.known);
 const january=await call(route('year',{year:'2023',timeZone:q.timeZone}),token);assert.equal(january.summary.matches,0);assert.equal(january.mostFrequentCompanyMatches,null);
 const zeroRate=year.bestWinRate;assert.ok(zeroRate.every(g=>g.known>=5));
 const privateText=JSON.stringify({paired,third,explore,year,team});for(const secret of [a.Email,b.Email,'COMPANY_PEER_PRIVATE','COMPANY_AUTHOR_PRIVATE',fixture.privatePeer])assert.ok(!privateText.includes(secret));
 assert.ok(!('companions' in year));assert.ok(!JSON.stringify(year).includes(b.Id));
 for(const g of year.firstRecordedGames)assert.ok(g.gameId);
 await call(route('company',{...q,friendId:b.Id,mode:'not-mode'}),token,'GET',null,400);await call(route('year',{year:'0',timeZone:q.timeZone}),token,'GET',null,400);
 const currentYear=String(new Date().getFullYear());
 const currentQuery={start:currentYear+'-01-01',endExclusive:String(Number(currentYear)+1)+'-01-01',timeZone:'UTC'};
 let library=(await call(`/MeepleBoard/users/${a.Id}/games`,token,'GET',null,[200,204]))??[];
 if(!library.some(l=>l.gameId===comp.gameId)){
  await call(`/MeepleBoard/users/${a.Id}/games`,token,'POST',{gameId:comp.gameId,gameName:comp.name,status:1,pricePaid:0},201);
  library=await call(`/MeepleBoard/users/${a.Id}/games`,token);
 }

 const expectedEntries=(library??[]).filter(l=>new Date(l.addedAt)>=new Date(currentQuery.start+'T00:00:00Z')&&new Date(l.addedAt)<new Date(currentQuery.endExclusive+'T00:00:00Z'));
 const currentExplore=await call(route('explore',currentQuery),token);
 assert.equal(currentExplore.collection.length,expectedEntries.length);
 for(const l of currentExplore.collection){const stored=expectedEntries.find(e=>e.id===l.entryId);assert.ok(stored);assert.equal(l.pricePaid,stored.pricePaid);}
 const currentRecap=await call(route('year',{year:currentYear,timeZone:'UTC'}),token);
 assert.ok(!currentRecap.firstRecordedGames.some(g=>g.gameId===comp.gameId));
 assert.ok(currentRecap.mostPlayed.every(g=>g.matches===Math.max(...currentRecap.summary.games.map(x=>x.matches))));
 const denied=await call(route('explore',{...currentQuery,userId:a.Id}),outToken);
 assert.ok(denied.collection.every(l=>!expectedEntries.some(e=>e.id===l.entryId)));
 console.log('PASS SQL/API company: shared and individual wins, partial draws, another winner, team loss/draw/undefined, zero/negative scores, samples, filters and drilldown.');
 console.log('PASS accepted-friend and participant authorization; separate friend history, notes/ratings and identities excluded from annual payload; records/own ratings/year summaries use shared principal services.');
})().catch(e=>{console.error(e.message);process.exitCode=1;});
