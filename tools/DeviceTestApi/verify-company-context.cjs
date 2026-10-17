// Read-only checks against the existing synthetic DeviceTests fixtures.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'../..'),base='http://127.0.0.1:5099';
async function call(route,token,expected=200){const r=await fetch(base+route,{headers:token?{Authorization:`Bearer ${token}`}:{}});assert.equal(r.status,expected,'Unexpected HTTP status');return r.status===200?r.json():null;}
const route=(kind,q)=>`/MeepleBoard/statistics/me/${kind}?${new URLSearchParams(q)}`;
(async()=>{
 assert.deepEqual(await call('/device-test/health'),{environment:'DeviceTests',database:'MeepleBoard_DeviceTests',externalDelivery:false});
 const accounts=JSON.parse(fs.readFileSync(path.join(root,'.device-tests/accounts.json'),'utf8'));
 const account=name=>accounts.find(a=>a.Email===name+'@meepleboard.test');const a=account('autor'),b=account('participante'),outsider=account('alheio');
 const login=async u=>{const r=await fetch(base+'/MeepleBoard/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({email:u.Email,password:u.Password,deviceInfo:'MeepleBoard Mobile App'})});assert.equal(r.status,200);return (await r.json()).token;};
 const token=await login(a),outToken=await login(outsider),q={start:'2024-10-27',endExclusive:'2024-10-28',timeZone:'Europe/Lisbon',friendId:b.Id};
 const all=await call(route('company',q),token),list=await call(route('matches',{...q,metric:'pair-all',limit:'100'}),token);
 assert.equal(all.matches,list.total);assert.ok(list.total<100);
 const evidence=[];for(const row of list.items){const match=await call('/MeepleBoard/matches/'+row.id,token);const ids=new Set(match.players.map(p=>p.userId));assert.ok(ids.has(a.Id)&&ids.has(b.Id));evidence.push({id:row.id,count:ids.size});}
 assert.ok(evidence.some(r=>r.count===2)&&evidence.some(r=>r.count>=3),'Existing test fixtures must include both contexts');
 assert.deepEqual(all.contextCounts,{all:evidence.length,onlyTwo:evidence.filter(r=>r.count===2).length,group:evidence.filter(r=>r.count>=3).length,unclassified:0});
 for(const groupContext of ['all','only-two','group']){
  const report=await call(route('company',{...q,groupContext}),token),matches=await call(route('matches',{...q,groupContext,metric:'pair-all',limit:'100'}),token);
  const expected=evidence.filter(r=>groupContext==='all'||(groupContext==='only-two'?r.count===2:r.count>=3));
  assert.equal(report.groupContext,groupContext);assert.equal(report.matches,expected.length);assert.equal(report.results.known+report.results.unknown,report.matches);
  assert.deepEqual(matches.items.map(r=>r.id).sort(),expected.map(r=>r.id).sort());assert.deepEqual(report.contextCounts,all.contextCounts);
  for(const metric of ['pair-known','unknown-pair']){const support=await call(route('matches',{...q,groupContext,metric}),token);assert.equal(support.total,report.results[metric==='pair-known'?'known':'unknown']);}
  assert.equal(report.evolution.reduce((n,b)=>n+b.matches,0),report.matches);assert.equal(report.games.reduce((n,g)=>n+g.matches,0),report.matches);
  await call(route('company',{...q,groupContext}),outToken,403);await call(route('matches',{...q,groupContext,metric:'pair-all'}),outToken,403);
  assert.ok(!JSON.stringify(report).includes(a.Email));assert.ok(!matches.items.some(r=>'notes'in r||'personalNotes'in r));
 }
 const competitive=all.comparisons.find(g=>g.mode==='COMPETITIVE');assert.ok(competitive);
 for(const groupContext of ['only-two','group']){
  const filtered={...q,gameId:competitive.gameId,mode:'COMPETITIVE',groupContext};
  const report=await call(route('company',filtered),token),support=await call(route('matches',{...filtered,metric:'pair-all'}),token);
  assert.equal(report.matches,support.total);assert.ok(support.items.every(r=>r.gameId===competitive.gameId&&r.gameMode==='COMPETITIVE'));
  assert.ok(report.comparisons.every(g=>g.gameId===competitive.gameId&&g.mode==='COMPETITIVE'));
  const page=await call(route('matches',{...q,groupContext,metric:'pair-all',offset:'0',limit:'1'}),token);assert.equal(page.items.length,1);
  assert.ok(evidence.some(e=>e.id===page.items[0].id&&(groupContext==='only-two'?e.count===2:e.count>=3)));
 }
 const only=await call(route('company',{...q,groupContext:'only-two'}),token);assert.ok(only.results.unknown>0,'Legacy unknown results must remain in two-player sample');
 await call(route('company',{...q,groupContext:'invalid'}),token,400);await call(route('matches',{...q,groupContext:'invalid',metric:'pair-all'}),token,400);await call(route('company',q),null,401);
 console.log('PASS SQL/API: distinct registered participants, both context counts, filtered evidence/results/evolution/games, unknown historical results, authorization and no private notes.');
})().catch(e=>{console.error(e.message);process.exitCode=1;});
