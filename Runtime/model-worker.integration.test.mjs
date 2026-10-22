// Failure-path checks use synthetic credentials and an in-memory network substitute.
// The timeout test intentionally waits for the real 30-second service deadline.
import test from 'node:test';
import assert from 'node:assert/strict';
import { ModelService } from './model-worker.mjs';
const command={provider:'deepseek-api',model:'deepseek-flash',messages:[{Role:'peer',Text:'固定测试'}]};
test('真实30秒截止时间取消网络等待，不启动替代服务',{timeout:40000},async()=>{
 const original=globalThis.fetch;let calls=0,aborted=false;
 globalThis.fetch=async(_url,{signal})=>{
  calls++;
  return new Promise((resolve,reject)=>{
   signal.addEventListener('abort',()=>{aborted=true;reject(signal.reason);},{once:true});
  });
 };
 const keepAlive=setInterval(()=>{},1000);
 try{
  const service=new ModelService(()=>{});service.credentials['deepseek-api']={key:'synthetic-no-network'};
  const start=performance.now();await assert.rejects(service.recommend(command));
  const elapsed=performance.now()-start;
  assert.ok(aborted);assert.equal(calls,1);assert.ok(elapsed>=29500&&elapsed<35000,`deadline ${elapsed}ms`);
  console.log(JSON.stringify({timeoutElapsedMs:Math.round(elapsed),actualNetworkCalls:0}));
 }finally{clearInterval(keepAlive);globalThis.fetch=original;}
});
test('认证、额度、限流和权限错误不自动切换服务',async()=>{
 const original=globalThis.fetch;
 try{
  for(const [status,code] of [[401,'AUTH_REQUIRED'],[402,'QUOTA'],[403,'ACCESS_DENIED'],[429,'RATE_LIMIT']]){
   let calls=0;
   globalThis.fetch=async url=>{calls++;assert.equal(url,'https://api.deepseek.com/anthropic/v1/messages');return new Response('',{status});};
   const service=new ModelService(()=>{});service.credentials['deepseek-api']={key:'synthetic-no-network'};
   await assert.rejects(service.recommend(command),e=>e.safeCode===code);assert.equal(calls,1);
  }
 }finally{globalThis.fetch=original;}
});
test('退出账号会取消进行中的生成，并等待凭证删除确认',async()=>{
 const original=globalThis.fetch;let aborted=false,credentialEvent;
 globalThis.fetch=async(_url,{signal})=>new Promise((resolve,reject)=>signal.addEventListener('abort',()=>{aborted=true;reject(signal.reason);},{once:true}));
 try{
  const service=new ModelService(event=>credentialEvent=event);
  await service.handle({op:'init',device:'11111111-1111-1111-1111-111111111111',credentials:{'deepseek-api':{key:'synthetic-no-network'}}});
  const generation=service.recommend(command);generation.catch(()=>{});await new Promise(r=>setImmediate(r));
  const logout=service.handle({op:'auth/logout',provider:'deepseek-api'});
  assert.equal(credentialEvent.value,null);assert.ok(aborted);
  await service.handle({op:'vaultAck',id:credentialEvent.id,ok:true});await logout;
  await assert.rejects(generation);assert.equal(service.status().find(s=>s.provider==='deepseek-api').connected,false);
 }finally{globalThis.fetch=original;}
});
