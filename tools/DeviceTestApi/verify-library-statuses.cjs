// DeviceTests only: adding a game with status Wishlist (3) or Played (2) is stored and read back with that status.
// A fresh person (disposable.cjs) adds two entries and removes them again; the person is removed at the end. Never the iPhone accounts.
if (require.main !== module) return; // Importing a verifier never runs it: only `node <file>` does (see docs/testes-macos-docker.md).
const assert=require('node:assert/strict');
const {run}=require('./disposable.cjs');
const base='http://127.0.0.1:5099';
run({label:'libstatus',people:[{role:'membro'}]},async world=>{
 const call=async(u,method,route,body)=>{const r=await fetch(base+'/MeepleBoard'+route,{method,headers:{Authorization:`Bearer ${u.token}`,'Content-Type':'application/json'},body:body&&JSON.stringify(body)});const t=await r.text();return {status:r.status,data:t?JSON.parse(t):null};};
 const user=await world.login('membro');
 const catalog=(await call(user,'GET','/game/suggestions?query=Meeple&limit=10')).data.map(g=>({gameId:g.id,name:g.name}));
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
}).catch(e=>{console.error('✗',e.message);process.exit(1);});
