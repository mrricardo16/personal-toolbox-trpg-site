"use strict";

/* v1.6.8 Combat Round / Melee Opposed Contract
 * Browser-owned CoC7 combat mode, DEX order and close-combat opposed resolution.
 * This stage deliberately does NOT generate weapon/armor damage. A winning exchange
 * records a damage disposition for the next rule layer; AI cannot directly adjust HP.
 */
const COMBAT_OPPOSED_VERSION="1.0";
const COMBAT_OPPOSED_AUTHORITY="browser_coc_combat";
const COMBAT_HISTORY_LIMIT=120;
const COMBAT_PARTICIPANT_LIMIT=24;
const COMBAT_SUCCESS_LEVEL=Object.freeze({fumble:0,failure:0,regular:1,hard:2,extreme:3,critical:4});

function combatSkillValue(character,id){
  if(character?.system!=="coc7")return 0;
  if(Array.isArray(character.skills))return clamp(Math.floor(Number(character.skills.find(item=>item?.id===id)?.value)||0),0,100);
  return clamp(Math.floor(Number(character.skills?.[id])||0),0,100)
}
function combatPlayerParticipant(){
  if(state.character?.system!=="coc7")throw new Error("仅 CoC7 角色支持 Combat Mode");
  return{id:"player",label:asString(state.character.name,80)||"调查员",kind:"investigator",side:"player",dex:clamp(Math.floor(Number(state.character.attributes?.dex)||0),1,100),fighting:clamp(combatSkillValue(state.character,"fighting_brawl"),1,100),dodge:clamp(combatSkillValue(state.character,"dodge"),1,100),responsePreference:"player_choice",responseAllowance:1,active:true}
}
function combatNormalizeOpponent(raw,index=0){
  if(!isPlainObject(raw))throw new Error("战斗对手参数无效");
  const label=asString(raw.label||raw.name,80)||`对手 ${index+1}`,dex=Number(raw.dex),fighting=Number(raw.fighting),dodge=Number(raw.dodge);
  for(const [name,value] of [["DEX",dex],["Fighting",fighting],["Dodge",dodge]])if(!Number.isFinite(value)||value<1||value>100)throw new Error(`${label} 的 ${name} 必须在 1 到 100 之间`);
  const preference=raw.responsePreference==="dodge"?"dodge":"fight_back";
  return{id:asString(raw.id,80)||`opponent-${index+1}`,label,kind:"opponent",side:"opposition",dex:clamp(Math.floor(dex),1,100),fighting:clamp(Math.floor(fighting),1,100),dodge:clamp(Math.floor(dodge),1,100),responsePreference:preference,responseAllowance:clamp(Math.floor(Number(raw.responseAllowance)||1),1,10),active:raw.active!==false}
}
function combatOrder(participants){return participants.map((participant,index)=>({...participant,__order:index})).sort((a,b)=>Number(b.dex)-Number(a.dex)||a.__order-b.__order).map(item=>{const copy={...item};delete copy.__order;return copy.id})}
function combatEmpty(){return{version:COMBAT_OPPOSED_VERSION,authority:COMBAT_OPPOSED_AUTHORITY,active:false,round:0,turnIndex:0,order:[],participants:[],responseCounts:{},actionCounts:{},history:[],lastExchange:null,startedAt:null,endedAt:null,endReason:null,playerDyingObservedRound:null,lastAutoDyingCheckRound:null}}
function combatNormalizeState(){
  state.campaign=state.campaign||{};const raw=isPlainObject(state.campaign.combat)?state.campaign.combat:combatEmpty();
  const participants=Array.isArray(raw.participants)?raw.participants.slice(0,COMBAT_PARTICIPANT_LIMIT).filter(isPlainObject).map((item,index)=>item.kind==="investigator"?{...combatPlayerParticipant(),...deepClone(item),id:"player",kind:"investigator",side:"player"}:combatNormalizeOpponent(item,index)):[];
  const ids=new Set(participants.map(item=>item.id)),order=Array.isArray(raw.order)?raw.order.filter(id=>ids.has(id)):[];for(const id of combatOrder(participants))if(!order.includes(id))order.push(id);
  const normalized={version:COMBAT_OPPOSED_VERSION,authority:COMBAT_OPPOSED_AUTHORITY,active:raw.active===true&&participants.length>=2,round:Math.max(0,Math.floor(Number(raw.round)||0)),turnIndex:Math.max(0,Math.floor(Number(raw.turnIndex)||0)),order,participants,responseCounts:isPlainObject(raw.responseCounts)?deepClone(raw.responseCounts):{},actionCounts:isPlainObject(raw.actionCounts)?deepClone(raw.actionCounts):{},history:Array.isArray(raw.history)?raw.history.slice(-COMBAT_HISTORY_LIMIT).filter(isPlainObject).map(deepClone):[],lastExchange:isPlainObject(raw.lastExchange)?deepClone(raw.lastExchange):null,startedAt:asString(raw.startedAt,80)||null,endedAt:asString(raw.endedAt,80)||null,endReason:asString(raw.endReason,120)||null,playerDyingObservedRound:raw.playerDyingObservedRound==null?null:Math.max(1,Math.floor(Number(raw.playerDyingObservedRound)||1)),lastAutoDyingCheckRound:raw.lastAutoDyingCheckRound==null?null:Math.max(1,Math.floor(Number(raw.lastAutoDyingCheckRound)||1))};
  if(normalized.active){normalized.round=Math.max(1,normalized.round);normalized.turnIndex=normalized.order.length?normalized.turnIndex%normalized.order.length:0}else normalized.turnIndex=0;
  state.campaign.combat=normalized;return normalized
}
function combatSnapshot(){
  const combat=isPlainObject(state.campaign?.combat)?state.campaign.combat:combatEmpty(),participants=Array.isArray(combat.participants)?combat.participants.map(deepClone):[],currentId=combat.active&&combat.order?.length?combat.order[combat.turnIndex%combat.order.length]:null;
  return{version:COMBAT_OPPOSED_VERSION,authority:COMBAT_OPPOSED_AUTHORITY,active:Boolean(combat.active),round:Number(combat.round||0),turnIndex:Number(combat.turnIndex||0),currentActorId:currentId,order:Array.isArray(combat.order)?[...combat.order]:[],participants,responseCounts:deepClone(combat.responseCounts||{}),actionCounts:deepClone(combat.actionCounts||{}),lastExchange:deepClone(combat.lastExchange||null),startedAt:combat.startedAt||null,endedAt:combat.endedAt||null,endReason:combat.endReason||null,playerDyingObservedRound:combat.playerDyingObservedRound??null,lastAutoDyingCheckRound:combat.lastAutoDyingCheckRound??null}
}
function combatIsActive(){return Boolean(state.campaign?.combat?.active)}
function combatParticipant(id){return(state.campaign?.combat?.participants||[]).find(item=>item.id===id)||null}
function combatCurrentActor(){const combat=state.campaign?.combat;if(!combat?.active||!combat.order?.length)return null;return combatParticipant(combat.order[combat.turnIndex%combat.order.length])}
function combatAssertMutable(){if(activeAbortController||state.runtime?.activeRequestId)throw new Error("AI 请求进行中，不能同时推进战斗状态")}
function combatStart({opponents=[]}={}){
  combatAssertMutable();if(state.character?.system!=="coc7")throw new Error("仅 CoC7 角色支持 Combat Mode");if(combatIsActive())throw new Error("当前已经处于 Combat Mode");if(state.character.healthState?.dead?.active)throw new Error("角色已死亡，不能开始新的战斗");
  if(!Array.isArray(opponents)||opponents.length<1)throw new Error("至少需要 1 名战斗对手");if(opponents.length>=COMBAT_PARTICIPANT_LIMIT)throw new Error("战斗参与者数量超过上限");const player=combatPlayerParticipant(),normalized=opponents.map(combatNormalizeOpponent),ids=new Set([player.id]);for(const opponent of normalized){if(ids.has(opponent.id))throw new Error(`战斗参与者 ID 重复：${opponent.id}`);ids.add(opponent.id)}
  const participants=[player,...normalized],order=combatOrder(participants),at=nowIso();state.campaign.combat={...combatEmpty(),active:true,round:1,turnIndex:0,order,participants,responseCounts:Object.fromEntries(participants.map(item=>[item.id,0])),actionCounts:Object.fromEntries(participants.map(item=>[item.id,0])),startedAt:at,history:[]};
  if(combatDyingActuallyActive())state.campaign.combat.playerDyingObservedRound=1;bumpRevision();addLog("combat",`Combat Mode 开始：${order.map(id=>combatParticipant(id)?.label||id).join(" → ")}`,{});renderAll();return combatSnapshot()
}
function combatEnd(reason="manual_end"){
  combatAssertMutable();const combat=combatNormalizeState();if(!combat.active)throw new Error("当前没有进行中的 Combat Mode");combat.active=false;combat.endedAt=nowIso();combat.endReason=asString(reason,120)||"manual_end";bumpRevision();addLog("combat",`Combat Mode 结束：${combat.endReason}`,{});renderAll();return combatSnapshot()
}
function combatRankLevel(rank){return COMBAT_SUCCESS_LEVEL[rank]??0}
function combatRankSuccess(rank){return combatRankLevel(rank)>=1}
function combatRoll(target,{bonusDice=0,penaltyDice=0,roller=null}={}){
  const normalized=clamp(Math.floor(Number(target)||0),1,100);if(typeof roller==="function"){const total=clamp(Math.floor(Number(roller())||1),1,100),rank=cocRank(total,normalized);return{expression:"1d100",rawRolls:[total],modifier:0,total,target:normalized,difficulty:"regular",difficultyTarget:normalized,rank,result:cocDifficultyPass(rank,"regular"),bonusDice,penaltyDice,deterministicRoller:true}}
  const result=rollCocPercentile({target:normalized,difficulty:"regular",bonusDice,penaltyDice});return{...result,bonusDice,penaltyDice,deterministicRoller:false}
}
function combatResolveRanks(attackerRoll,defenderRoll,response){
  const attackerSuccess=combatRankSuccess(attackerRoll.rank),defenderSuccess=combatRankSuccess(defenderRoll.rank),a=combatRankLevel(attackerRoll.rank),d=combatRankLevel(defenderRoll.rank);
  if(response==="dodge"){
    if(attackerSuccess&&a>d)return{outcome:"attacker_hits",winner:"attacker",loser:"defender",damageBy:"attacker",damageMode:["extreme","critical"].includes(attackerRoll.rank)?"initiator_extreme_eligible":"regular"};
    return{outcome:attackerSuccess||defenderSuccess?"defender_dodges":"both_fail_no_damage",winner:defenderSuccess?"defender":null,loser:null,damageBy:null,damageMode:null}
  }
  if(defenderSuccess&&d>a)return{outcome:"defender_fights_back",winner:"defender",loser:"attacker",damageBy:"defender",damageMode:"fight_back_regular_cap"};
  if(attackerSuccess)return{outcome:"attacker_hits",winner:"attacker",loser:"defender",damageBy:"attacker",damageMode:["extreme","critical"].includes(attackerRoll.rank)?"initiator_extreme_eligible":"regular"};
  return{outcome:"both_fail_no_damage",winner:null,loser:null,damageBy:null,damageMode:null}
}
function combatObservePlayerDying(){const combat=state.campaign?.combat;if(!combat?.active)return;if(healthStabilizationCanAdvanceDying(state.character)&&combat.playerDyingObservedRound==null)combat.playerDyingObservedRound=combat.round;if(!healthStabilizationCanAdvanceDying(state.character))combat.playerDyingObservedRound=null}
function combatRoundWrap({dyingRoller=null}={}){
  const combat=state.campaign.combat,finishedRound=combat.round;combatObservePlayerDying();let dyingResult=null;
  if(healthStabilizationCanAdvanceDying(state.character)&&combat.playerDyingObservedRound!=null&&finishedRound>combat.playerDyingObservedRound&&combat.lastAutoDyingCheckRound!==finishedRound){dyingResult=healthStabilizationResolveDyingRound({roller:typeof dyingRoller==="function"?dyingRoller:()=>randomInt(1,100),sourceId:`combat-round-${finishedRound}`});combat.lastAutoDyingCheckRound=finishedRound;if(dyingResult.state?.dead?.active){combat.active=false;combat.endedAt=nowIso();combat.endReason="player_dead";return{wrapped:true,finishedRound,dyingResult,ended:true}}}
  combat.round=finishedRound+1;combat.turnIndex=0;combat.responseCounts=Object.fromEntries((combat.participants||[]).map(item=>[item.id,0]));combat.actionCounts=Object.fromEntries((combat.participants||[]).map(item=>[item.id,0]));combatObservePlayerDying();return{wrapped:true,finishedRound,dyingResult,ended:false}
}
function combatAdvanceTurn({actionKind="pass",dyingRoller=null}={}){
  const combat=combatNormalizeState();if(!combat.active)throw new Error("当前没有进行中的 Combat Mode");const actor=combatCurrentActor();if(!actor)throw new Error("当前战斗行动者不存在");combat.actionCounts[actor.id]=(combat.actionCounts[actor.id]||0)+1;const entry={id:uid("combat-action"),kind:"turn_action",authority:COMBAT_OPPOSED_AUTHORITY,round:combat.round,actorId:actor.id,actionKind:asString(actionKind,80)||"pass",at:nowIso()};combat.history.push(entry);if(combat.history.length>COMBAT_HISTORY_LIMIT)combat.history.splice(0,combat.history.length-COMBAT_HISTORY_LIMIT);combatObservePlayerDying();combat.turnIndex+=1;let wrap=null;if(combat.turnIndex>=combat.order.length)wrap=combatRoundWrap({dyingRoller});bumpRevision();renderAll();return{entry:deepClone(entry),wrap:deepClone(wrap),state:combatSnapshot()}
}
function combatResolveMelee({defenderId,response,attackerRoller=null,defenderRoller=null,dyingRoller=null}={}){
  combatAssertMutable();const combat=combatNormalizeState();if(!combat.active)throw new Error("当前没有进行中的 Combat Mode");const attacker=combatCurrentActor(),defender=combatParticipant(asString(defenderId,80));if(!attacker||!defender||defender.active===false)throw new Error("近战参与者无效");if(attacker.id===defender.id)throw new Error("不能攻击自己");if(attacker.side===defender.side)throw new Error("当前近战仅支持敌对双方");const normalizedResponse=response==="dodge"?"dodge":response==="fight_back"?"fight_back":null;if(!normalizedResponse)throw new Error("防守反应必须是 dodge 或 fight_back");
  const responseCount=Math.max(0,Math.floor(Number(combat.responseCounts[defender.id])||0)),allowance=Math.max(1,Math.floor(Number(defender.responseAllowance)||1)),outnumberedBonus=responseCount>=allowance?1:0,attackerRoll=combatRoll(attacker.fighting,{bonusDice:outnumberedBonus,roller:attackerRoller}),defenderTarget=normalizedResponse==="dodge"?defender.dodge:defender.fighting,defenderRoll=combatRoll(defenderTarget,{roller:defenderRoller}),resolved=combatResolveRanks(attackerRoll,defenderRoll,normalizedResponse),at=nowIso();
  const exchange={id:uid("combat-exchange"),kind:"melee_opposed",authority:COMBAT_OPPOSED_AUTHORITY,round:combat.round,turnIndex:combat.turnIndex,attackerId:attacker.id,attackerLabel:attacker.label,defenderId:defender.id,defenderLabel:defender.label,response:normalizedResponse,attackerRoll,defenderRoll,outnumberedBonus,responseCountBefore:responseCount,responseAllowance:allowance,outcome:resolved.outcome,winnerId:resolved.winner==="attacker"?attacker.id:resolved.winner==="defender"?defender.id:null,damageDisposition:resolved.damageBy?{pending:true,ownerId:resolved.damageBy==="attacker"?attacker.id:defender.id,targetId:resolved.damageBy==="attacker"?defender.id:attacker.id,mode:resolved.damageMode,authority:"deferred_weapon_damage_engine",hpCommitted:false}:null,at};
  combat.responseCounts[defender.id]=responseCount+1;combat.lastExchange=deepClone(exchange);combat.history.push(deepClone(exchange));if(combat.history.length>COMBAT_HISTORY_LIMIT)combat.history.splice(0,combat.history.length-COMBAT_HISTORY_LIMIT);addLog("combat",`${attacker.label} vs ${defender.label}：${exchange.outcome}`,{});const turn=combatAdvanceTurn({actionKind:"melee_attack",dyingRoller});return{exchange:deepClone(exchange),turn,state:combatSnapshot()}
}
function combatPassTurn({dyingRoller=null}={}){combatAssertMutable();return combatAdvanceTurn({actionKind:"pass",dyingRoller})}
function combatContext(){const snapshot=combatSnapshot();return{version:COMBAT_OPPOSED_VERSION,authority:COMBAT_OPPOSED_AUTHORITY,state:snapshot,policy:"combat_round_order_and_melee_opposed_outcomes_are_browser_owned",rules:{initiative:"descending_DEX_stable_tie_order",dodge:"attacker_must_achieve_strictly_higher_success_level_equal_level_dodge_avoids",fightBack:"defender_must_achieve_strictly_higher_success_level_equal_level_initiator_wins",fightBackDamage:"regular_damage_cap_even_on_extreme",outnumbered:"after_response_allowance_subsequent_melee_attacks_gain_one_bonus_die",round:"one_significant_action_per_participant_then_explicit_browser_round_wrap",dying:"when_combat_round_engine_is_active_dying_CON_auto_checks_begin_at_end_of_round_after_the_round_dying_was_first_observed"},damageAuthority:"deferred_browser_weapon_damage_engine",aiAuthority:"narrative_only_cannot_roll_resolve_or_apply_combat_hp"}}

const __combatBuildCocCharacter=buildCocCharacter;
buildCocCharacter=function(form){const character=__combatBuildCocCharacter(form);return character};
const __combatNormalizeLoadedState=normalizeLoadedState;
normalizeLoadedState=function(raw){const loaded=__combatNormalizeLoadedState(raw);if(loaded.config?.system==="coc7"){const previousState=state;try{state=loaded;combatNormalizeState()}finally{state=previousState}}return loaded};

/* While a browser Combat Mode is active, HP damage/healing is not accepted from AI.
 * This stage has not yet authorized weapon/armor damage; safe unrelated effects remain. */
const __combatPrepareAiTransaction=prepareAiTransaction;
prepareAiTransaction=function(parsed,options={}){if(!combatIsActive())return __combatPrepareAiTransaction(parsed,options);const out=deepClone(parsed);let stripped=0;if(Array.isArray(out.stateChanges))out.stateChanges=out.stateChanges.filter(change=>{if(change?.operation==="adjustHp"){stripped++;return false}return true});const transaction=__combatPrepareAiTransaction(out,options);if(stripped)transaction.combatOpposedRecovery={version:COMBAT_OPPOSED_VERSION,reason:"combat_hp_requires_browser_damage_authority",stripped};return transaction};

/* During real Combat Mode, v1.6.6's manual dying-round button is suppressed because
 * the combat round wrapper now owns the end-of-round survival timing. */
const __combatHealthCanAdvanceDying=healthStabilizationCanAdvanceDying;
healthStabilizationCanAdvanceDying=function(character=state.character){if(combatIsActive())return false;return __combatHealthCanAdvanceDying(character)};
function combatDyingActuallyActive(){return __combatHealthCanAdvanceDying(state.character)}
/* Rebind internal observer to the pre-suppression rule. */
combatObservePlayerDying=function(){const combat=state.campaign?.combat;if(!combat?.active)return;if(combatDyingActuallyActive()&&combat.playerDyingObservedRound==null)combat.playerDyingObservedRound=combat.round;if(!combatDyingActuallyActive())combat.playerDyingObservedRound=null};
const __combatRoundWrapBase=combatRoundWrap;
combatRoundWrap=function({dyingRoller=null}={}){const combat=state.campaign.combat,finishedRound=combat.round;combatObservePlayerDying();let dyingResult=null;if(combatDyingActuallyActive()&&combat.playerDyingObservedRound!=null&&finishedRound>combat.playerDyingObservedRound&&combat.lastAutoDyingCheckRound!==finishedRound){dyingResult=healthStabilizationResolveDyingRound({roller:typeof dyingRoller==="function"?dyingRoller:()=>randomInt(1,100),sourceId:`combat-round-${finishedRound}`});combat.lastAutoDyingCheckRound=finishedRound;if(dyingResult.state?.dead?.active){combat.active=false;combat.endedAt=nowIso();combat.endReason="player_dead";return{wrapped:true,finishedRound,dyingResult,ended:true}}}combat.round=finishedRound+1;combat.turnIndex=0;combat.responseCounts=Object.fromEntries((combat.participants||[]).map(item=>[item.id,0]));combat.actionCounts=Object.fromEntries((combat.participants||[]).map(item=>[item.id,0]));combatObservePlayerDying();return{wrapped:true,finishedRound,dyingResult,ended:false}};

const __combatBuildRequestPayload=buildRequestPayload;
buildRequestPayload=function(stage,requestId,baseRevision,extra={}){const payload=__combatBuildRequestPayload(stage,requestId,baseRevision,extra);payload.cocCombatRound=combatContext();return payload};
const __combatBuildSystemPrompt=buildSystemPrompt;
buildSystemPrompt=function(){return `${__combatBuildSystemPrompt()}\n31. Combat Round / Melee Opposed：Combat Mode、DEX 顺序、近战双方骰点、Dodge/Fight Back 成功等级比较与 outnumbered bonus 均由浏览器裁决。AI 不得自行宣告命中、闪避/反击胜负或使用 adjustHp 结算战斗伤害。本阶段浏览器只产生 damageDisposition，武器/护甲伤害由后续规则层处理；Combat Mode 外普通叙事仍正常进行。`};
if(typeof buildDiagnosticPackage==="function"){const __combatBuildDiagnosticPackage=buildDiagnosticPackage;buildDiagnosticPackage=function(options={}){const pack=__combatBuildDiagnosticPackage(options);pack.cocCombatRound=combatContext();return pack}}

function combatControlsHtml(){
  if(state.character?.system!=="coc7")return"";const combat=combatSnapshot();if(!combat.active)return`<div class="section" id="combatRoundSection"><h3>Combat Mode</h3><div class="muted small">v1.6.8 先结算 DEX 顺序与近战 opposed；武器/护甲伤害下一规则层接入。</div><div class="field"><label>对手名称</label><input id="combatOpponentLabel" value="敌对目标"></div><div class="grid three"><div class="field"><label>DEX</label><input id="combatOpponentDex" type="number" min="1" max="100" value="50"></div><div class="field"><label>Fighting %</label><input id="combatOpponentFight" type="number" min="1" max="100" value="40"></div><div class="field"><label>Dodge %</label><input id="combatOpponentDodge" type="number" min="1" max="100" value="20"></div></div><div class="field"><label>对手默认近战反应</label><select id="combatOpponentResponse"><option value="fight_back">Fight Back</option><option value="dodge">Dodge</option></select></div><button id="combatStartBtn" class="btn" type="button">开始 Combat Mode</button></div>`;
  const current=combat.participants.find(item=>item.id===combat.currentActorId),opponents=combat.participants.filter(item=>item.side==="opposition"&&item.active!==false),orderText=combat.order.map(id=>combat.participants.find(item=>item.id===id)?.label||id).join(" → "),last=combat.lastExchange?`<div class="audit" style="margin-top:8px">最近：${escapeHtml(combat.lastExchange.attackerLabel)} vs ${escapeHtml(combat.lastExchange.defenderLabel)} · ${escapeHtml(combat.lastExchange.outcome)}${combat.lastExchange.damageDisposition?` · 待伤害：${escapeHtml(combat.lastExchange.damageDisposition.mode)}`:""}</div>`:"";
  let action="";if(current?.side==="player"){const target=opponents[0];if(target)action=`<div class="small">当前：<strong>${escapeHtml(current.label)}</strong>。对手按预设选择 ${escapeHtml(target.responsePreference)}。</div><button id="combatPlayerAttackBtn" class="btn primary" type="button">攻击 ${escapeHtml(target.label)}</button>`}else if(current?.side==="opposition")action=`<div class="small">当前：<strong>${escapeHtml(current.label)}</strong> 攻击调查员。请选择调查员反应。</div><div class="field"><select id="combatPlayerResponse"><option value="dodge">Dodge</option><option value="fight_back">Fight Back</option></select></div><button id="combatOpponentAttackBtn" class="btn primary" type="button">浏览器结算近战</button>`;
  return`<div class="section" id="combatRoundSection"><h3>Combat Mode · Round ${combat.round}</h3><div class="muted small">顺序：${escapeHtml(orderText)}</div>${action}<button id="combatPassBtn" class="btn" type="button" style="margin-top:6px">当前行动者跳过/执行非攻击动作</button><button id="combatEndBtn" class="btn" type="button" style="margin-top:6px">结束 Combat Mode</button>${last}</div>`
}
function combatBindControls(){
  const start=$("#combatStartBtn");if(start)start.addEventListener("click",()=>{try{combatStart({opponents:[{label:$("#combatOpponentLabel")?.value,dex:Number($("#combatOpponentDex")?.value),fighting:Number($("#combatOpponentFight")?.value),dodge:Number($("#combatOpponentDodge")?.value),responsePreference:$("#combatOpponentResponse")?.value}]})}catch(error){toast(error.message,"error")}});
  const playerAttack=$("#combatPlayerAttackBtn");if(playerAttack)playerAttack.addEventListener("click",()=>{try{const target=combatSnapshot().participants.find(item=>item.side==="opposition"&&item.active!==false);if(!target)throw new Error("没有可攻击的对手");combatResolveMelee({defenderId:target.id,response:target.responsePreference})}catch(error){toast(error.message,"error")}});
  const opponentAttack=$("#combatOpponentAttackBtn");if(opponentAttack)opponentAttack.addEventListener("click",()=>{try{combatResolveMelee({defenderId:"player",response:$("#combatPlayerResponse")?.value})}catch(error){toast(error.message,"error")}});
  const pass=$("#combatPassBtn");if(pass)pass.addEventListener("click",()=>{try{combatPassTurn()}catch(error){toast(error.message,"error")}});const end=$("#combatEndBtn");if(end)end.addEventListener("click",()=>{try{combatEnd("manual_end")}catch(error){toast(error.message,"error")}})
}
if(typeof renderSidebar==="function"){const __combatRenderSidebar=renderSidebar;renderSidebar=function(){__combatRenderSidebar();const sidebar=$("#sidebar"),html=combatControlsHtml();if(sidebar&&html){sidebar.insertAdjacentHTML("beforeend",html);combatBindControls()}}}
