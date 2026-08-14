"use strict";

/* v1.6.5 HP Damage State / Major Wound
 * Browser-owned CoC7 single-damage-event classification.
 * Each trusted negative adjustHp operation is one damage event; separate operations
 * are never aggregated into a synthetic major wound.
 * This stage records major wound, unconsciousness, dying, and instant death.
 * It deliberately does not invent First Aid/Medicine recovery or round-based dying
 * CON checks yet.
 */
const HP_DAMAGE_STATE_VERSION="1.0";
const HP_DAMAGE_STATE_AUTHORITY="browser_coc_health";
const HP_DAMAGE_EVENT_LIMIT=80;

function hpMajorWoundThreshold(maxHp){return Math.max(1,Math.ceil(Math.max(1,Number(maxHp)||1)/2))}
function normalizeHpCondition(raw,kind){
  if(!isPlainObject(raw)||raw.active!==true)return null;
  return{active:true,kind,authority:HP_DAMAGE_STATE_AUTHORITY,sourceEventKey:asString(raw.sourceEventKey,180)||null,at:asString(raw.at,80)||nowIso(),reason:asString(raw.reason,160)||kind,...(kind==="dying"?{stabilized:raw.stabilized===true,roundChecksManaged:false}:{})}
}
function hpDamageStateSnapshot(character=state.character){
  if(character?.system!=="coc7")return null;const raw=isPlainObject(character.healthState)?character.healthState:{};
  const history=Array.isArray(raw.history)?raw.history.slice(-HP_DAMAGE_EVENT_LIMIT).map(deepClone):[];
  const hp=clamp(Math.floor(Number(character.hp)||0),0,Math.max(1,Math.floor(Number(character.maxHp)||1)));
  let unconscious=normalizeHpCondition(raw.unconscious,"unconscious");
  if(hp===0&&!unconscious&&!normalizeHpCondition(raw.dead,"dead"))unconscious={active:true,kind:"unconscious",authority:HP_DAMAGE_STATE_AUTHORITY,sourceEventKey:null,at:nowIso(),reason:"legacy_zero_hp"};
  return{version:HP_DAMAGE_STATE_VERSION,authority:HP_DAMAGE_STATE_AUTHORITY,majorWound:normalizeHpCondition(raw.majorWound,"major_wound"),unconscious,dying:normalizeHpCondition(raw.dying,"dying"),dead:normalizeHpCondition(raw.dead,"dead"),lastDamageEvent:isPlainObject(raw.lastDamageEvent)?deepClone(raw.lastDamageEvent):null,history}
}
function normalizeHpDamageState(character=state.character){
  if(character?.system!=="coc7")return null;const snapshot=hpDamageStateSnapshot(character);character.healthState=snapshot;return character.healthState
}
function hpDamageEventKey(source,sourceId,index=0){return `${asString(source,40)||"hp_damage"}:${asString(sourceId,120)||"unknown"}:${Math.max(0,Math.floor(Number(index)||0))}`}
function hpDamageExtractEvents(changes,{source="canonical_transaction",sourceId=null,startHp=state.character?.hp,maxHp=state.character?.maxHp}={}){
  const cap=Math.max(1,Math.floor(Number(maxHp)||1));let hp=clamp(Math.floor(Number(startHp)||0),0,cap),index=0;const events=[];
  for(const change of Array.isArray(changes)?changes:[]){if(change?.operation!=="adjustHp")continue;const amount=clamp(Number(change.amount??change.by??change.delta??0),-999,999);if(!Number.isFinite(amount))continue;const before=hp,after=clamp(before+amount,0,cap);if(amount<0)events.push({eventKey:hpDamageEventKey(source,sourceId,index),source:asString(source,40)||"canonical_transaction",sourceId:asString(sourceId,120)||null,index,damage:Math.abs(amount),hpBefore:before,hpAfter:after,maxHp:cap,reason:asString(change.reason,300)});hp=after;index++}
  return events
}
function hpDamageApplyEvent(raw,{roller=()=>randomInt(1,100)}={}){
  if(state.character?.system!=="coc7")return{tracked:false,reason:"not_coc7"};const health=normalizeHpDamageState(state.character),eventKey=asString(raw?.eventKey,180)||uid("hp_damage_event");const existing=health.history.find(item=>item.eventKey===eventKey);if(existing)return{tracked:true,deduped:true,event:deepClone(existing),state:hpDamageStateSnapshot(state.character)};
  if(health.dead?.active)return{tracked:false,reason:"already_dead",state:hpDamageStateSnapshot(state.character)};
  const maxHp=Math.max(1,Math.floor(Number(raw?.maxHp??state.character.maxHp)||1)),damage=Math.max(0,Number(raw?.damage)||0),hpBefore=clamp(Math.floor(Number(raw?.hpBefore??state.character.hp)||0),0,maxHp),hpAfter=clamp(Math.floor(Number(raw?.hpAfter??state.character.hp)||0),0,maxHp),threshold=hpMajorWoundThreshold(maxHp),instantDeath=damage>=maxHp,majorWoundEvent=!instantDeath&&damage>=threshold,at=nowIso();let conCheck=null;
  if(majorWoundEvent){const target=clamp(Math.floor(Number(state.character.attributes?.con)||0),0,100),roll=clamp(Math.floor(Number(roller())||1),1,100);conCheck={roll,target,success:roll<=target}}
  const event={eventKey,source:asString(raw?.source,40)||"hp_damage",sourceId:asString(raw?.sourceId,120)||null,index:Math.max(0,Math.floor(Number(raw?.index)||0)),damage,hpBefore,hpAfter,maxHp,majorWoundThreshold:threshold,majorWound:majorWoundEvent,instantDeath,conCheck,reason:asString(raw?.reason,300),at};
  if(instantDeath){health.dead={active:true,kind:"dead",authority:HP_DAMAGE_STATE_AUTHORITY,sourceEventKey:eventKey,at,reason:"single_damage_at_least_max_hp"};health.dying=null;health.unconscious=null}
  else{
    if(majorWoundEvent)health.majorWound={active:true,kind:"major_wound",authority:HP_DAMAGE_STATE_AUTHORITY,sourceEventKey:eventKey,at,reason:"single_damage_at_least_half_max_hp"};
    if(majorWoundEvent&&conCheck&&!conCheck.success)health.unconscious={active:true,kind:"unconscious",authority:HP_DAMAGE_STATE_AUTHORITY,sourceEventKey:eventKey,at,reason:"major_wound_con_failure"};
    if(hpAfter===0){health.unconscious={active:true,kind:"unconscious",authority:HP_DAMAGE_STATE_AUTHORITY,sourceEventKey:eventKey,at,reason:"zero_hp"};if(health.majorWound?.active)health.dying={active:true,kind:"dying",authority:HP_DAMAGE_STATE_AUTHORITY,sourceEventKey:eventKey,at,reason:"zero_hp_with_major_wound",stabilized:false,roundChecksManaged:false}}
  }
  health.lastDamageEvent=deepClone(event);health.history.push(deepClone(event));if(health.history.length>HP_DAMAGE_EVENT_LIMIT)health.history.splice(0,health.history.length-HP_DAMAGE_EVENT_LIMIT);state.character.healthState=health;
  return{tracked:true,deduped:false,event:deepClone(event),state:hpDamageStateSnapshot(state.character)}
}
function hpDamageStateContext(){
  if(state.character?.system!=="coc7")return null;return{version:HP_DAMAGE_STATE_VERSION,authority:HP_DAMAGE_STATE_AUTHORITY,state:hpDamageStateSnapshot(state.character),policy:"each_trusted_negative_adjustHp_is_one_damage_event_browser_derives_major_wound_unconscious_dying_death",deferred:["first_aid_stabilization","medicine_recovery","major_wound_healing","dying_round_con_checks"]}
}
function hpDamageApplyEvents(events,meta={}){let changed=false,last=null;for(const event of events||[]){const result=hpDamageApplyEvent(event,meta);if(result.tracked&&!result.deduped){changed=true;last=result}}return{changed,last,state:hpDamageStateSnapshot(state.character)}}

/* New investigators receive an explicit empty browser-owned health state. */
const __hpDamageBuildCocCharacter=buildCocCharacter;
buildCocCharacter=function(form){const character=__hpDamageBuildCocCharacter(form);character.healthState={version:HP_DAMAGE_STATE_VERSION,authority:HP_DAMAGE_STATE_AUTHORITY,majorWound:null,unconscious:null,dying:null,dead:null,lastDamageEvent:null,history:[]};return character};

/* Old Schema 8 saves are normalized conservatively: zero HP implies unconsciousness,
 * but major wound/dying/death are never reconstructed without damage-event evidence. */
const __hpDamageNormalizeLoadedState=normalizeLoadedState;
normalizeLoadedState=function(raw){const loaded=__hpDamageNormalizeLoadedState(raw);if(loaded.character?.system==="coc7")normalizeHpDamageState(loaded.character);return loaded};

/* The returned transaction.parsed already reflects inner v1.6.1/v1.6.2 guards, so
 * unauthorized AI damage stripped by those modules is never classified here. */
const __hpDamagePrepareAiTransaction=prepareAiTransaction;
prepareAiTransaction=function(parsed,options={}){const transaction=__hpDamagePrepareAiTransaction(parsed,options);if(state.character?.system==="coc7")transaction.hpDamageEvents=hpDamageExtractEvents(transaction.parsed?.stateChanges,{source:"canonical_transaction",sourceId:state.runtime?.activeRequestId||null,startHp:state.character.hp,maxHp:state.character.maxHp});return transaction};

const __hpDamageCommitAiTransaction=commitAiTransaction;
commitAiTransaction=function(transaction,requestId){if(Array.isArray(transaction?.hpDamageEvents))for(const event of transaction.hpDamageEvents){event.sourceId=requestId||event.sourceId;event.eventKey=hpDamageEventKey(event.source,event.sourceId,event.index)}const result=__hpDamageCommitAiTransaction(transaction,requestId),applied=hpDamageApplyEvents(transaction?.hpDamageEvents||[]);if(applied.changed){bumpRevision();const health=applied.state;if(health.dead?.active)addLog("hp_damage","浏览器判定单次致命伤害：角色死亡",{requestId});else if(health.dying?.active)addLog("hp_damage","浏览器判定角色进入濒死状态",{requestId});else if(health.majorWound?.active)addLog("hp_damage",`浏览器记录重伤${health.unconscious?.active?"并昏迷":""}`,{requestId});renderAll()}return result};

/* Secret authored checks bypass commitAiTransaction, so capture their trusted HP
 * operations separately without aggregating multiple blows. */
const __hpDamageApplySecretCheckOutcome=applySecretCheckOutcome;
applySecretCheckOutcome=function(check,record){const changes=(record?.result?(check?.successStateChanges||[]):(check?.failureStateChanges||[])),events=state.character?.system==="coc7"?hpDamageExtractEvents(changes,{source:"secret_authored_check",sourceId:record?.id||check?.id||null,startHp:state.character.hp,maxHp:state.character.maxHp}):[];const result=__hpDamageApplySecretCheckOutcome(check,record),applied=hpDamageApplyEvents(events);if(applied.changed){bumpRevision();addLog("hp_damage","暗骰作者伤害已由浏览器更新伤势状态",{requestId:check?.requestId,secret:true});renderAll()}return result};

const __hpDamageBuildRequestPayload=buildRequestPayload;
buildRequestPayload=function(stage,requestId,baseRevision,extra={}){const payload=__hpDamageBuildRequestPayload(stage,requestId,baseRevision,extra);payload.cocHealthDamageState=hpDamageStateContext();return payload};
const __hpDamageBuildSystemPrompt=buildSystemPrompt;
buildSystemPrompt=function(){return `${__hpDamageBuildSystemPrompt()}\n28. HP Damage State：CoC 单次伤害、Major Wound、CON 昏迷、0 HP 与 dying/instant death 均由浏览器从可信 adjustHp 事件裁决。不得把多次小伤合并成一次重伤，不得根据叙事自行宣告/解除 major wound、dying 或 death。即使伤势严重，也不得把正常玩家交互变成技术拒绝。`};

if(typeof buildDiagnosticPackage==="function"){
  const __hpDamageBuildDiagnosticPackage=buildDiagnosticPackage;
  buildDiagnosticPackage=function(options={}){const pack=__hpDamageBuildDiagnosticPackage(options);pack.cocHealthDamageState=hpDamageStateContext();return pack}
}
