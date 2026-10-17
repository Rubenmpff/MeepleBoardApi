// DeviceTests only: adding a game with status Wishlist (3) or Played (2) is stored and read back with that status.
// Uses the fictitious "membro" account, creates two entries and removes them again, leaving its library as found.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'../..'),base='http://127.0.0.1:5099';
(async()=>{
 const health=await (await fetch(base+'/device-test/health')).json();
 assert.deepEqual(health,{environment:'DeviceTests',database:'MeepleBoard_DeviceTests',externalDelivery:false});
 const accounts=JSON.parse(fs.readFileSync(path.join(root,'.device-tests/accounts.json'),'utf8'));
 const login=async email=>{const a=accounts.find(x=>x.Email===email);const r=await fetch(base+'/MeepleBoard/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({email:a.Email,password:a.Password,deviceInfo:'verify-library-statuses'})});assert.equal(r.status,200);return {id:a.Id,token:(await r.json()).token};};
 const call=async(u,method,route,body)=>{const r=await fetch(base+'/MeepleBoard'+route,{method,headers:{Authorization:`Bearer ${u.token}`,'Content-Type':'application/json'},body:body&&JSON.stringify(body)});const t=await r.text();return {status:r.status,data:t?JSON.parse(t):null};};
 const reader=await login('autor@meepleboard.test'),user=await login('membro@meepleboard.test');
 const games=(await call(reader,'GET','/statistics/me/explore?start=2020-01-01&endExclusive=2029-01-01&timeZone=Europe%2FLisbon')).data.games;
 const catalog=[...new Map(games.map(g=>[g.gameId,g])).values()];
 const library=async()=>(await call(user,"GET",`/users/${user.id}/games`)).data??[]; // 204 when empty
 const before=await library();
 const free=catalog.filter(g=>!before.some(e=>(e.gameId??e.game?.id)===g.gameId));
 assert.ok(free.length>=2,'membro needs two catalog games outside its library');
 const plan=[{game:free[0],status:3,label:'Wishlist'},{game:free[1],status:2,label:'Played'}];
 const created=[];
 try{
  for(const p of plan){const r=await call(user,'POST',`/users/${user.id}/games`,{gameId:p.game.gameId,gameName:p.game.name,status:p.status});assert.ok(r.status>=200&&r.status<300,`add ${p.label}: HTTP ${r.status}`);created.push(p);}
  const after=await library();
  for(const p of plan){const e=after.find(x=>(x.gameId??x.game?.id)===p.game.gameId);assert.ok(e,`${p.label} entry missing`);
   const status=e.status;assert.ok(status===p.status||String(status).toLowerCase()===p.label.toLowerCase(),`${p.label}: read back ${JSON.stringify(status)}`);
   assert.equal(e.pricePaid??null,null);console.log(`✓ ${p.label}: stored and read back as ${JSON.stringify(status)} (${Object.keys(e).join(', ')})`);}
 } finally {
  for(const p of created){const r=await call(user,'DELETE',`/users/${user.id}/games/${p.game.gameId}`);console.log(`  cleanup ${p.label}: HTTP ${r.status}`);}
  const restored=await library();assert.equal(restored.length,before.length,'library restored to its initial size');
 }
 console.log('Library statuses verified; membro library restored.');
})().catch(e=>{console.error('✗',e.message);process.exit(1);});
