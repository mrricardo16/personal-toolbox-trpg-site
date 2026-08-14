"use strict";

/* v1.6.6 Health Stabilization / Dying Round Checks
 * Browser-owned CoC7 First Aid and dying survival mechanics.
 * Chat turns are deliberately NOT treated as combat rounds. Dying CON checks only
 * advance through an explicit local browser action so time authority stays honest.
 */
const HEALTH_STABILIZATION_VERSION="1.0";
const HEALTH_STABILIZATION_AUTHORITY="browser_coc_health_stabilization";
const HEALTH_TREATMENT_HISTORY_LIMIT=60;
const HEALTH_DYING_CHECK_LIMIT=40;

function healthStabilizationCondition(raw,kind){
  if(!isPlainObject(raw)||raw.active!==true)return null;
  return{active:true,kind,authority:HEALTH_STABILIZATION_AUTHORITY,sourceId:asString(raw.sourceId,180)||null,at:asString(raw.at,80)||nowIso(),reason:asString(raw.reason,180)||kind,firstAidTarget:raw.firstAidTarget!=null&&Number.isFinite(Number(raw.firstAidTarget))?clamp(Math.floor(Number(raw.firstAidTarget)),1,100):null,firstAidRoll:raw.firstAidRoll!=null&&Number.isFinite(Number(raw.firstAidRoll))?clamp(Math.floor(Number(raw.firstAidRoll)),1,100):null}
}
function healthStabilizationTrimHistory(value,limit){return Array.isArray(value)?value.slice(-limit).filter(isPlainObject).map(deepClone):[]}
function healthStabilizationFirstAidSkill(character=state.character){
  if(character?.system!=="coc7")return 0;
  if(Array.isArray(character.skills))return clamp(Math.floor(Number(character.skills.find(item=>item?.id==="first_aid")?.value)||0),0,100);
  return clamp(Math.floor(Number(character.skills?.first_aid)||0),0,100)
}

/* Preserve v1.6.6 fields whenever v1.6.5 normalizes healthState. */
const __healthStabilizationHpDamageStateSnapshot=hpDamageStateSnapshot;
hpDamageStateSnapshot=function(character=state.character){
  const raw=isPlainObject(character?.healthState)?character.healthState:{},base=__healthStabilizationHpDamageStateSnapshot(character);if(!base)return base;
  if(base.dying){const rawDying=isPlainObject(raw.dying)?raw.dying:{};base.dying.stabilized=rawDying.stabilized===true;base.dying.roundChecksManaged=rawDying.roundChecksManaged===true;base.dying.checks=healthStabilizationTrimHistory(rawDying.checks,HEALTH_DYING_CHECK_LIMIT);base.dying.nextRoundOrdinal=Math.max(1,Math.floor(Number(rawDying.nextRoundOrdinal)||base.dying.checks.length+1))}
  base.stabilized=healthStabilizationCondition(raw.stabilized,"stabilized");
  base.treatmentHistory=healthStabilizationTrimHistory(raw.treatmentHistory,HEALTH_TREATMENT_HISTORY_LIMIT);
  return base
};
const __healthStabilizationNormalizeHpDamageState=normalizeHpDamageState;
normalizeHpDamageState=function(character=state.character){if(character?.system!=="coc7")return __healthStabilizationNormalizeHpDamageState(character);const snapshot=hpDamageStateSnapshot(character);character.healthState=snapshot;return character.healthState};

const __healthStabilizationBuildCocCharacter=buildCocCharacter;
buildCocCharacter=function(form){const character=__healthStabilizationBuildCocCharacter(form);character.healthState=isPlainObject(character.healthState)?character.healthState:{};character.healthState.stabilized=null;character.healthState.treatmentHistory=[];return character};

function healthStabilizationSnapshot(character=state.character){
  if(character?.system!=="coc7")return null;const health=hpDamageStateSnapshot(character);
  return{version:HEALTH_STABILIZATION_VERSION,authority:HEALTH_STABILIZATION_AUTHORITY,hp:clamp(Math.floor(Number(character.hp)||0),0,Math.max(1,Math.floor(Number(character.maxHp)||1))),maxHp:Math.max(1,Math.floor(Number(character.maxHp)||1)),con:clamp(Math.floor(Number(character.attributes?.con)||0),0,100),firstAidSkill:healthStabilizationFirstAidSkill(character),majorWound:deepClone(health?.majorWound||null),unconscious:deepClone(health?.unconscious||null),dying:deepClone(health?.dying||null),stabilized:deepClone(health?.stabilized||null),dead:deepClone(health?.dead||null),treatmentHistory:healthStabilizationTrimHistory(health?.treatmentHistory,HEALTH_TREATMENT_HISTORY_LIMIT)}
}
function healthStabilizationCanTreat(character=state.character){
  const snapshot=healthStabilizationSnapshot(character);if(!snapshot||snapshot.dead?.active)return false;
  return snapshot.hp<snapshot.maxHp||snapshot.unconscious?.active||snapshot.dying?.active
}
function healthStabilizationCanAdvanceDying(character=state.character){const snapshot=healthStabilizationSnapshot(character);return Boolean(snapshot?.dying?.active&&!snapshot.dying.stabilized&&!snapshot.dead?.active)}
function healthStabilizationRecordTreatment(health,entry){
  health.treatmentHistory=healthStabilizationTrimHistory(health.treatmentHistory,HEALTH_TREATMENT_HISTORY_LIMIT);health.treatmentHistory.push(deepClone(entry));if(health.treatmentHistory.length>HEALTH_TREATMENT_HISTORY_LIMIT)health.treatmentHistory.splice(0,health.treatmentHistory.length-HEALTH_TREATMENT_HISTORY_LIMIT)
}
/* A prior stabilization cannot remain active if a later trusted damage event creates
 * a fresh dying/dead state. This wrapper invalidates only that stale stabilization. */
const __healthStabilizationHpDamageApplyEvent=hpDamageApplyEvent;
hpDamageApplyEvent=function(raw,options={}){
  const result=__healthStabilizationHpDamageApplyEvent(raw,options),health=state.character?.healthState;
  if(result?.tracked&&!result.deduped&&(health?.dying?.active||health?.dead?.active)&&health?.stabilized?.active){health.stabilized=null;if(result.state)result.state.stabilized=null}
  return result
};
function healthStabilizationResolveDyingRound({roller=()=>randomInt(1,100),sourceId=null}={}){
  if(state.character?.system!=="coc7")throw new Error("仅 CoC7 角色支持濒死 CON 结算");
  const health=normalizeHpDamageState(state.character);if(health.dead?.active)throw new Error("角色已经死亡，不能继续濒死检定");if(!health.dying?.active||health.dying.stabilized)throw new Error("当前没有待结算的未稳定濒死状态");
  const target=clamp(Math.floor(Number(state.character.attributes?.con)||0),0,100),roll=clamp(Math.floor(Number(roller())||1),1,100),success=roll<=target,checks=healthStabilizationTrimHistory(health.dying.checks,HEALTH_DYING_CHECK_LIMIT),ordinal=checks.length+1,at=nowIso();
  const record={kind:"dying_con",authority:HEALTH_STABILIZATION_AUTHORITY,ordinal,roll,target,success,sourceId:asString(sourceId,180)||null,at};checks.push(record);if(checks.length>HEALTH_DYING_CHECK_LIMIT)checks.splice(0,checks.length-HEALTH_DYING_CHECK_LIMIT);
  health.dying.checks=checks;health.dying.roundChecksManaged=true;health.dying.nextRoundOrdinal=ordinal+1;
  if(!success){health.dead={active:true,kind:"dead",authority:HP_DAMAGE_STATE_AUTHORITY,sourceEventKey:health.dying.sourceEventKey||null,at,reason:"dying_con_failure"};health.dying=null;health.unconscious=null;health.stabilized=null}
  state.character.healthState=health;bumpRevision();addLog("hp_damage",success?`濒死 CON 第 ${ordinal} 次成功：${roll}/${target}`:`濒死 CON 第 ${ordinal} 次失败：${roll}/${target}，角色死亡`,{requestId:sourceId||null});renderAll();
  return{version:HEALTH_STABILIZATION_VERSION,authority:HEALTH_STABILIZATION_AUTHORITY,record:deepClone(record),state:healthStabilizationSnapshot(state.character)}
}
function healthStabilizationResolveFirstAid({target,roller=()=>randomInt(1,100),withinHour=false,sourceId=null}={}){
  if(state.character?.system!=="coc7")throw new Error("仅 CoC7 角色支持急救结算");if(!healthStabilizationCanTreat())throw new Error("当前没有需要急救的玩家角色伤势");if(withinHour!==true)throw new Error("CoC7 急救必须确认仍在受伤后一小时内");
  const normalizedTarget=clamp(Math.floor(Number(target)||0),1,100);if(!Number.isFinite(Number(target))||Number(target)<1||Number(target)>100)throw new Error("救助者急救技能值必须在 1 到 100 之间");
  const health=normalizeHpDamageState(state.character);if(health.dead?.active)throw new Error("死亡状态不能通过急救逆转");const roll=clamp(Math.floor(Number(roller())||1),1,100),success=roll<=normalizedTarget,at=nowIso(),hpBefore=clamp(Math.floor(Number(state.character.hp)||0),0,state.character.maxHp),wasDying=Boolean(health.dying?.active),wasUnconscious=Boolean(health.unconscious?.active);let healedHp=0;
  if(success){const hpAfter=clamp(hpBefore+1,0,state.character.maxHp);healedHp=hpAfter-hpBefore;state.character.hp=hpAfter;if(wasDying){health.stabilized={active:true,kind:"stabilized",authority:HEALTH_STABILIZATION_AUTHORITY,sourceId:asString(sourceId,180)||null,at,reason:"successful_first_aid",firstAidTarget:normalizedTarget,firstAidRoll:roll};health.dying=null}if(wasUnconscious)health.unconscious=null}
  const record={kind:"first_aid",authority:HEALTH_STABILIZATION_AUTHORITY,sourceId:asString(sourceId,180)||null,target:normalizedTarget,roll,success,withinHour:true,hpBefore,hpAfter:state.character.hp,healedHp,wasDying,wasUnconscious,stabilizedDying:Boolean(success&&wasDying),rousedUnconscious:Boolean(success&&wasUnconscious),at};healthStabilizationRecordTreatment(health,record);state.character.healthState=health;bumpRevision();addLog("hp_damage",success?`急救成功：${roll}/${normalizedTarget}${healedHp?"，HP +1":""}${wasDying?"，濒死已稳定":""}${wasUnconscious?"，已唤醒":""}`:`急救失败：${roll}/${normalizedTarget}`,{requestId:sourceId||null});renderAll();
  return{version:HEALTH_STABILIZATION_VERSION,authority:HEALTH_STABILIZATION_AUTHORITY,record:deepClone(record),state:healthStabilizationSnapshot(state.character)}
}
function healthStabilizationContext(){
  return{version:HEALTH_STABILIZATION_VERSION,authority:HEALTH_STABILIZATION_AUTHORITY,state:healthStabilizationSnapshot(state.character),policy:"dying_rounds_and_first_aid_are_explicit_browser_actions_not_chat_turns",rules:{dyingCheck:"at_explicit_end_of_following_round_and_each_round_afterward_CON_failure_means_death",firstAid:"within_one_hour_regular_skill_success_heals_one_hp_can_rouse_unconscious_and_stabilize_dying"},deferred:["medicine_recovery","major_wound_weekly_healing","natural_daily_healing","full_combat_round_engine"]}
}

/* Do not let a First Aid continuation invent player-character healing. The browser
 * health control owns that canonical result; safe narrative/state changes continue. */
const __healthStabilizationPrepareAiTransaction=prepareAiTransaction;
prepareAiTransaction=function(parsed,options={}){
  const recordId=asString(options?.currentCheckRecordId,120),record=recordId?(state.checkRecords||[]).find(item=>item?.id===recordId):null,out=deepClone(parsed);let stripped=false;
  if(record?.system==="coc7"&&record.skillId==="first_aid"&&Array.isArray(out.stateChanges)){out.stateChanges=out.stateChanges.filter(change=>{if(change?.operation==="adjustHp"&&Number(change.amount)>0){stripped=true;return false}return true})}
  const transaction=__healthStabilizationPrepareAiTransaction(out,options);if(stripped)transaction.healthStabilizationRecovery={version:HEALTH_STABILIZATION_VERSION,reason:"first_aid_player_healing_is_browser_owned",recordId:record.id};return transaction
};

const __healthStabilizationBuildRequestPayload=buildRequestPayload;
buildRequestPayload=function(stage,requestId,baseRevision,extra={}){const payload=__healthStabilizationBuildRequestPayload(stage,requestId,baseRevision,extra);payload.cocHealthStabilization=healthStabilizationContext();return payload};
const __healthStabilizationBuildSystemPrompt=buildSystemPrompt;
buildSystemPrompt=function(){return `${__healthStabilizationBuildSystemPrompt()}\n29. Health Stabilization：CoC dying 的每轮 CON 与 First Aid 稳定/HP+1/唤醒均由浏览器显式结算；聊天回合不自动等同战斗轮。不得用 adjustHp 或叙事自行替玩家角色结算急救，不得自行宣告 dying 已稳定或 CON 失败死亡；严重伤势也不能把正常交互变成技术拒绝。`};
if(typeof buildDiagnosticPackage==="function"){
  const __healthStabilizationBuildDiagnosticPackage=buildDiagnosticPackage;
  buildDiagnosticPackage=function(options={}){const pack=__healthStabilizationBuildDiagnosticPackage(options);pack.cocHealthStabilization=healthStabilizationContext();return pack}
}

function healthStabilizationControlsHtml(){
  const snapshot=healthStabilizationSnapshot();if(!snapshot)return"";if(snapshot.dead?.active)return`<div class="section"><h3>伤势结算</h3><div class="notice warn">角色已死亡。死亡不能通过急救逆转。</div></div>`;
  const needs=healthStabilizationCanTreat(),dying=healthStabilizationCanAdvanceDying(),defaultAid=snapshot.firstAidSkill||"";if(!needs&&!dying)return"";
  return`<div class="section" id="healthStabilizationSection"><h3>伤势结算</h3>${dying?`<div class="notice warn">濒死尚未稳定。CoC7 要求从下一轮结束开始逐轮进行 CON；这里不会把聊天消息自动当作战斗轮。</div><button id="healthDyingRoundBtn" class="btn" type="button">结算下一轮 CON</button>`:""}${needs?`<div class="field" style="margin-top:8px"><label>救助者急救技能值</label><input id="healthFirstAidTarget" type="number" min="1" max="100" value="${escapeHtml(defaultAid)}" placeholder="1-100"></div><label class="row small"><input id="healthFirstAidWithinHour" type="checkbox"><span>确认仍在受伤后 1 小时内</span></label><button id="healthFirstAidBtn" class="btn" type="button">浏览器急救检定</button>`:""}</div>`
}
function healthStabilizationBindControls(){
  const roundBtn=$("#healthDyingRoundBtn");if(roundBtn)roundBtn.addEventListener("click",()=>{try{healthStabilizationResolveDyingRound({sourceId:uid("dying-round")})}catch(error){toast(error.message,"error")}});
  const aidBtn=$("#healthFirstAidBtn");if(aidBtn)aidBtn.addEventListener("click",()=>{try{const input=$("#healthFirstAidTarget"),within=$("#healthFirstAidWithinHour");healthStabilizationResolveFirstAid({target:Number(input?.value),withinHour:Boolean(within?.checked),sourceId:uid("first-aid")})}catch(error){toast(error.message,"error")}})
}
if(typeof renderSidebar==="function"){
  const __healthStabilizationRenderSidebar=renderSidebar;
  renderSidebar=function(){__healthStabilizationRenderSidebar();const sidebar=$("#sidebar"),html=healthStabilizationControlsHtml();if(sidebar&&html){sidebar.insertAdjacentHTML("beforeend",html);healthStabilizationBindControls()}}
}
