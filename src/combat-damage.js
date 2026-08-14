"use strict";

/* v1.6.9 Combat Damage Authority
 * Consumes v1.6.8 browser-owned melee damageDisposition and resolves a conservative
 * non-impaling melee damage profile: weapon dice + STR/SIZ damage bonus - fixed armor.
 * Firearms, impaling, maneuvers and variable armor remain explicitly deferred.
 */
const COMBAT_DAMAGE_VERSION="1.0";
const COMBAT_DAMAGE_AUTHORITY="browser_coc_combat_damage";
const COMBAT_DAMAGE_HISTORY_LIMIT=120;
const COMBAT_DEFAULT_WEAPON=Object.freeze({id:"unarmed",label:"徒手/拳脚",damage:"1d3",addsDamageBonus:true,mode:"melee_non_impaling"});

function combatDamageBonusProfile(str,siz){
  const sum=Math.max(2,Math.floor(Number(str)||0)+Math.floor(Number(siz)||0));
  if(sum<=64)return{sum,kind:"flat",value:-2,expression:"-2",max:-2};
  if(sum<=84)return{sum,kind:"flat",value:-1,expression:"-1",max:-1};
  if(sum<=124)return{sum,kind:"flat",value:0,expression:"0",max:0};
  if(sum<=164)return{sum,kind:"dice",count:1,faces:4,expression:"1d4",max:4};
  if(sum<=204)return{sum,kind:"dice",count:1,faces:6,expression:"1d6",max:6};
  const count=2+Math.floor((sum-205)/80);return{sum,kind:"dice",count,faces:6,expression:`${count}d6`,max:count*6}
}
function combatDamageNormalizeWeapon(raw=COMBAT_DEFAULT_WEAPON){
  const source=isPlainObject(raw)?raw:COMBAT_DEFAULT_WEAPON,mode=asString(source.mode,60)||"melee_non_impaling";if(mode!=="melee_non_impaling")throw new Error("v1.6.9 仅支持非穿刺近战伤害；枪械/Impale 尚未启用");
  const damage=asString(source.damage,32)||"1d3",parsed=parseDiceExpression(damage);if(!parsed.ok)throw new Error(`武器伤害骰无效：${parsed.error}`);
  return{id:asString(source.id,80)||"weapon",label:asString(source.label,80)||"近战武器",damage:parsed.value.text,addsDamageBonus:source.addsDamageBonus!==false,mode}
}
function combatDamageMaxDiceExpression(expression){const parsed=parseDiceExpression(expression);if(!parsed.ok)throw new Error(parsed.error);const {count,faces,modifier}=parsed.value;return count*faces+modifier}
function combatDamageRollWeapon(expression,roller=null){
  const parsed=parseDiceExpression(expression);if(!parsed.ok)throw new Error(parsed.error);const {count,faces,modifier}=parsed.value,rawRolls=[];for(let i=0;i<count;i++)rawRolls.push(typeof roller==="function"?clamp(Math.floor(Number(roller(faces,i))||1),1,faces):randomInt(1,faces));return{expression:parsed.value.text,rawRolls,modifier,total:rawRolls.reduce((a,b)=>a+b,0)+modifier}
}
function combatDamageRollBonus(profile,roller=null){
  if(profile.kind==="flat")return{expression:profile.expression,rawRolls:[],modifier:profile.value,total:profile.value};const rawRolls=[];for(let i=0;i<profile.count;i++)rawRolls.push(typeof roller==="function"?clamp(Math.floor(Number(roller(profile.faces,i))||1),1,profile.faces):randomInt(1,profile.faces));return{expression:profile.expression,rawRolls,modifier:0,total:rawRolls.reduce((a,b)=>a+b,0)}
}
function combatDamagePlayerLoadout(){
  if(state.character?.system!=="coc7")return null;const raw=isPlainObject(state.character.combatLoadout)?state.character.combatLoadout:{},weapon=combatDamageNormalizeWeapon(raw.weapon||COMBAT_DEFAULT_WEAPON),armor=clamp(Math.floor(Number(raw.armor)||0),0,99);return{version:COMBAT_DAMAGE_VERSION,authority:COMBAT_DAMAGE_AUTHORITY,weapon,armor}
}
function combatDamageSetPlayerLoadout({weapon,armor=0}={}){
  if(state.character?.system!=="coc7")throw new Error("仅 CoC7 角色支持战斗伤害配置");if(combatIsActive())throw new Error("Combat Mode 进行中不能更换玩家伤害配置");if(activeAbortController||state.runtime?.activeRequestId)throw new Error("AI 请求进行中，不能修改战斗伤害配置");const normalized={version:COMBAT_DAMAGE_VERSION,authority:COMBAT_DAMAGE_AUTHORITY,weapon:combatDamageNormalizeWeapon(weapon||COMBAT_DEFAULT_WEAPON),armor:clamp(Math.floor(Number(armor)||0),0,99)};state.character.combatLoadout=normalized;bumpRevision();renderAll();return deepClone(normalized)
}
function combatDamageEnhancePlayer(participant){
  const loadout=combatDamagePlayerLoadout(),attrs=state.character?.attributes||{};return{...participant,str:Math.max(1,Math.floor(Number(attrs.str)||1)),siz:Math.max(1,Math.floor(Number(attrs.siz)||1)),hp:clamp(Math.floor(Number(state.character.hp)||0),0,Math.max(1,Math.floor(Number(state.character.maxHp)||1))),maxHp:Math.max(1,Math.floor(Number(state.character.maxHp)||1)),armor:loadout.armor,weapon:deepClone(loadout.weapon),damageBonus:combatDamageBonusProfile(attrs.str,attrs.siz)}
}
function combatDamageEnhanceOpponent(participant,raw={}){
  const str=clamp(Math.floor(Number(raw.str)||50),1,999),siz=clamp(Math.floor(Number(raw.siz)||50),1,999),maxHp=clamp(Math.floor(Number(raw.maxHp)||10),1,999),hp=clamp(Math.floor(Number(raw.hp??maxHp)||maxHp),0,maxHp),armor=clamp(Math.floor(Number(raw.armor)||0),0,99),weapon=combatDamageNormalizeWeapon(raw.weapon||{...COMBAT_DEFAULT_WEAPON,id:"opponent-unarmed",label:"对手徒手"});return{...participant,str,siz,hp,maxHp,armor,weapon,damageBonus:combatDamageBonusProfile(str,siz),defeated:hp<=0,active:participant.active!==false&&hp>0}
}
function combatDamageResolveAmount({owner,mode,weaponRoller=null,bonusRoller=null}={}){
  if(!owner)throw new Error("缺少伤害来源参与者");const weapon=combatDamageNormalizeWeapon(owner.weapon||COMBAT_DEFAULT_WEAPON),bonus=weapon.addsDamageBonus!==false?(owner.damageBonus||combatDamageBonusProfile(owner.str,owner.siz)):{sum:Number(owner.str||0)+Number(owner.siz||0),kind:"flat",value:0,expression:"0",max:0};
  if(mode==="initiator_extreme_eligible"){
    const weaponMax=combatDamageMaxDiceExpression(weapon.damage),bonusMax=Number(bonus.max||0),gross=Math.max(0,weaponMax+bonusMax);return{mode,weapon,weaponResult:{expression:weapon.damage,rawRolls:[],modifier:0,total:weaponMax,maximized:true},damageBonusResult:{expression:bonus.expression,rawRolls:[],modifier:bonus.kind==="flat"?Number(bonus.value||0):0,total:bonusMax,maximized:true},grossDamage:gross}
  }
  const weaponResult=combatDamageRollWeapon(weapon.damage,weaponRoller),damageBonusResult=weapon.addsDamageBonus!==false?combatDamageRollBonus(bonus,bonusRoller):{expression:"0",rawRolls:[],modifier:0,total:0},grossDamage=Math.max(0,Number(weaponResult.total||0)+Number(damageBonusResult.total||0));return{mode:mode||"regular",weapon,weaponResult,damageBonusResult,grossDamage}
}
function combatDamageUpdateHistory(exchange){
  const combat=state.campaign.combat;if(!combat)return;combat.lastExchange=deepClone(exchange);const index=(combat.history||[]).findIndex(item=>item?.id===exchange.id);if(index>=0)combat.history[index]=deepClone(exchange)
}
function combatDamageRepairAfterDefeat(targetId){
  const combat=state.campaign.combat;if(!combat?.active)return;const target=combatParticipant(targetId);if(target)target.active=false;combat.order=(combat.order||[]).filter(id=>id!==targetId);
  if(target?.side==="opposition"&&!(combat.participants||[]).some(item=>item.side==="opposition"&&item.active!==false&&Number(item.hp||0)>0)){combat.active=false;combat.endedAt=nowIso();combat.endReason="opposition_defeated";return}
  if(target?.side==="player"){combat.active=false;combat.endedAt=nowIso();combat.endReason="player_dead";return}
  if(!combat.order.length){combat.active=false;combat.endedAt=nowIso();combat.endReason="no_active_participants";return}
  const nextUnacted=combat.order.findIndex(id=>Number(combat.actionCounts?.[id]||0)===0);if(nextUnacted>=0)combat.turnIndex=nextUnacted;else combatRoundWrap({})
}
function combatDamageApplyDisposition(exchange,{weaponRoller=null,bonusRoller=null}={}){
  if(!exchange?.damageDisposition?.pending)return{applied:false,reason:"no_pending_damage",exchange:deepClone(exchange)};const disposition=exchange.damageDisposition,owner=combatParticipant(disposition.ownerId),target=combatParticipant(disposition.targetId);if(!owner||!target)throw new Error("待伤害的战斗参与者不存在");if(target.active===false||Number(target.hp||0)<=0)return{applied:false,reason:"target_already_defeated",exchange:deepClone(exchange)};
  const rolled=combatDamageResolveAmount({owner,mode:disposition.mode,weaponRoller,bonusRoller}),armor=clamp(Math.floor(Number(target.armor)||0),0,99),netDamage=Math.max(0,rolled.grossDamage-armor),hpBefore=target.id==="player"?Number(state.character.hp||0):Number(target.hp||0),maxHp=target.id==="player"?Number(state.character.maxHp||1):Number(target.maxHp||1),hpAfter=clamp(hpBefore-netDamage,0,maxHp),at=nowIso();let hpDamageState=null;
  if(target.id==="player"){
    state.character.hp=hpAfter;if(netDamage>0)hpDamageState=hpDamageApplyEvent({eventKey:`combat:${exchange.id}`,source:"combat_damage",sourceId:exchange.id,index:0,damage:netDamage,hpBefore,hpAfter,maxHp,reason:`${owner.label} 的 ${rolled.weapon.label}`});target.hp=state.character.hp;target.maxHp=state.character.maxHp;if(state.character.healthState?.dead?.active)combatDamageRepairAfterDefeat("player")
  }else{target.hp=hpAfter;if(hpAfter<=0){target.defeated=true;target.defeatedAt=at;combatDamageRepairAfterDefeat(target.id)}}
  disposition.pending=false;disposition.hpCommitted=true;disposition.resolvedAt=at;disposition.result={authority:COMBAT_DAMAGE_AUTHORITY,weapon:deepClone(rolled.weapon),weaponResult:deepClone(rolled.weaponResult),damageBonusResult:deepClone(rolled.damageBonusResult),impaling:Boolean(rolled.impaling),impaleExtraResult:rolled.impaleExtraResult?deepClone(rolled.impaleExtraResult):null,grossDamage:rolled.grossDamage,armor,netDamage,hpBefore,hpAfter,targetDefeated:hpAfter<=0,hpDamageState:hpDamageState?deepClone(hpDamageState.state||null):null};exchange.damageDisposition=disposition;exchange.damageResult=deepClone(disposition.result);combatDamageUpdateHistory(exchange);bumpRevision();addLog("combat",`${owner.label} → ${target.label}：${rolled.grossDamage} 伤害 - ${armor} 护甲 = ${netDamage}，HP ${hpBefore} → ${hpAfter}`,{});renderAll();return{applied:true,exchange:deepClone(exchange),result:deepClone(disposition.result),state:combatSnapshot()}
}
function combatDamageContext(){
  const combat=combatSnapshot(),player=combatPlayerParticipant?combatDamageEnhancePlayer({...combatPlayerParticipant()}):null;return{version:COMBAT_DAMAGE_VERSION,authority:COMBAT_DAMAGE_AUTHORITY,combatActive:combat.active,playerLoadout:player?{weapon:player.weapon,armor:player.armor,damageBonus:player.damageBonus}:null,lastDamage:combat.lastExchange?.damageResult?deepClone(combat.lastExchange.damageResult):null,policy:"browser_consumes_damage_disposition_then_rolls_non_impaling_melee_damage_and_fixed_armor",deferred:["firearms","impaling","variable_armor","fighting_maneuvers","npc_major_wound"],aiAuthority:"cannot_roll_or_commit_combat_damage"}
}

/* Extend combat participants without changing v1.6.8 opposed semantics. */
const __combatDamagePlayerParticipant=combatPlayerParticipant;
combatPlayerParticipant=function(){return combatDamageEnhancePlayer(__combatDamagePlayerParticipant())};
const __combatDamageNormalizeOpponent=combatNormalizeOpponent;
combatNormalizeOpponent=function(raw,index=0){return combatDamageEnhanceOpponent(__combatDamageNormalizeOpponent(raw,index),raw)};

/* Resolve pending damage immediately after the browser-owned opposed exchange. This
 * happens after v1.6.8 advances the turn, then repairs order if the target is defeated. */
const __combatDamageResolveMelee=combatResolveMelee;
combatResolveMelee=function(options={}){const result=__combatDamageResolveMelee(options);if(result?.exchange?.damageDisposition?.pending){const damage=combatDamageApplyDisposition(result.exchange,{weaponRoller:options.weaponRoller,bonusRoller:options.bonusRoller});result.exchange=damage.exchange;result.damage=damage.result;result.state=combatSnapshot()}return result};

const __combatDamageBuildCocCharacter=buildCocCharacter;
buildCocCharacter=function(form){const character=__combatDamageBuildCocCharacter(form);character.combatLoadout={version:COMBAT_DAMAGE_VERSION,authority:COMBAT_DAMAGE_AUTHORITY,weapon:deepClone(COMBAT_DEFAULT_WEAPON),armor:0};return character};
const __combatDamageNormalizeLoadedState=normalizeLoadedState;
normalizeLoadedState=function(raw){const loaded=__combatDamageNormalizeLoadedState(raw);if(loaded.character?.system==="coc7"){try{const weapon=combatDamageNormalizeWeapon(loaded.character.combatLoadout?.weapon||COMBAT_DEFAULT_WEAPON),armor=clamp(Math.floor(Number(loaded.character.combatLoadout?.armor)||0),0,99);loaded.character.combatLoadout={version:COMBAT_DAMAGE_VERSION,authority:COMBAT_DAMAGE_AUTHORITY,weapon,armor}}catch{loaded.character.combatLoadout={version:COMBAT_DAMAGE_VERSION,authority:COMBAT_DAMAGE_AUTHORITY,weapon:deepClone(COMBAT_DEFAULT_WEAPON),armor:0}}}return loaded};

const __combatDamageBuildRequestPayload=buildRequestPayload;
buildRequestPayload=function(stage,requestId,baseRevision,extra={}){const payload=__combatDamageBuildRequestPayload(stage,requestId,baseRevision,extra);payload.cocCombatDamage=combatDamageContext();return payload};
const __combatDamageBuildSystemPrompt=buildSystemPrompt;
buildSystemPrompt=function(){return `${__combatDamageBuildSystemPrompt()}\n32. Combat Damage Authority：v1.6.8 的 melee damageDisposition 由浏览器继续结算非穿刺近战武器骰、STR/SIZ Damage Bonus 与固定 Armor；实际玩家 HP 再进入 HP Damage State。AI 不得自行掷/最大化伤害、决定护甲减免或使用 adjustHp 覆盖浏览器结果。枪械、Impale 与特殊护甲尚未启用，不得假装已经实现。`};
if(typeof buildDiagnosticPackage==="function"){const __combatDamageBuildDiagnosticPackage=buildDiagnosticPackage;buildDiagnosticPackage=function(options={}){const pack=__combatDamageBuildDiagnosticPackage(options);pack.cocCombatDamage=combatDamageContext();return pack}}

function combatDamageControlsHtml(){
  if(state.character?.system!=="coc7")return"";const loadout=combatDamagePlayerLoadout(),combat=combatSnapshot(),rows=combat.active?combat.participants.map(item=>`<div class="audit"><strong>${escapeHtml(item.label)}</strong> · HP ${escapeHtml(item.id==="player"?state.character.hp:item.hp)} / ${escapeHtml(item.id==="player"?state.character.maxHp:item.maxHp)} · Armor ${escapeHtml(item.armor||0)} · ${escapeHtml(item.weapon?.label||"-")} ${escapeHtml(item.weapon?.damage||"")} ${item.damageBonus?.expression?`+ DB ${escapeHtml(item.damageBonus.expression)}`:""}</div>`).join(""):"";
  return`<div class="section" id="combatDamageSection"><h3>Combat Damage</h3><div class="muted small">v1.6.9：仅非穿刺近战；枪械/Impale 后续实现。</div>${combat.active?rows:`<div class="field"><label>玩家武器名称</label><input id="combatDamageWeaponLabel" value="${escapeHtml(loadout.weapon.label)}"></div><div class="field"><label>武器伤害骰</label><input id="combatDamageWeaponExpr" value="${escapeHtml(loadout.weapon.damage)}" placeholder="如 1d3 / 1d6+1"></div><label class="row small"><input id="combatDamageAddsDb" type="checkbox" ${loadout.weapon.addsDamageBonus?"checked":""}><span>加入 STR/SIZ Damage Bonus</span></label><div class="field"><label>玩家固定 Armor</label><input id="combatDamageArmor" type="number" min="0" max="99" value="${escapeHtml(loadout.armor)}"></div><button id="combatDamageSaveBtn" class="btn" type="button">保存伤害配置</button>`}</div>`
}
function combatDamageBindControls(){const save=$("#combatDamageSaveBtn");if(save)save.addEventListener("click",()=>{try{combatDamageSetPlayerLoadout({weapon:{id:"player-melee",label:$("#combatDamageWeaponLabel")?.value,damage:$("#combatDamageWeaponExpr")?.value,addsDamageBonus:Boolean($("#combatDamageAddsDb")?.checked),mode:"melee_non_impaling"},armor:Number($("#combatDamageArmor")?.value)})}catch(error){toast(error.message,"error")}})}
if(typeof renderSidebar==="function"){const __combatDamageRenderSidebar=renderSidebar;renderSidebar=function(){__combatDamageRenderSidebar();const sidebar=$("#sidebar"),html=combatDamageControlsHtml();if(sidebar&&html){sidebar.insertAdjacentHTML("beforeend",html);combatDamageBindControls()}}}
