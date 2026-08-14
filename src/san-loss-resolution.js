"use strict";

/* v1.6.3 SAN Loss Resolution
 * The browser already owns the SAN roll and SAN-loss amount. This module owns
 * the immediate CoC7 shock resolution that follows a single SAN-loss event.
 * Indefinite insanity is deliberately deferred until a reliable starting-SAN
 * baseline and cumulative-loss window exist.
 */
const SAN_LOSS_RESOLUTION_VERSION="1.0";
const SAN_LOSS_RESOLUTION_AUTHORITY="browser_coc_sanity";
const SAN_LOSS_RESOLUTION_HISTORY_LIMIT=40;
const SAN_TEMPORARY_INSANITY_THRESHOLD=5;
const SAN_BOUT_TABLE={
  1:{type:"amnesia",label:"失忆"},
  2:{type:"psychosomatic_disability",label:"心因性障碍"},
  3:{type:"violence",label:"暴力倾向"},
  4:{type:"paranoia",label:"偏执"},
  5:{type:"significant_person",label:"重要之人"},
  6:{type:"faint",label:"昏厥"},
  7:{type:"flee_in_panic",label:"惊恐逃离"},
  8:{type:"hysterics",label:"歇斯底里／情绪爆发"},
  9:{type:"phobia",label:"恐惧症"},
  10:{type:"mania",label:"躁狂症"}
};

function sanityStateDefault(character=state.character){
  const current=Math.max(0,Number(character?.san||0));
  return{version:SAN_LOSS_RESOLUTION_VERSION,authority:SAN_LOSS_RESOLUTION_AUTHORITY,baselineSan:current,baselineSource:"legacy_current",indefiniteTrackingReady:false,temporary:null,history:[]}
}
function sanityStateSnapshot(character=state.character){
  if(!character||character.system!=="coc7")return null;const existing=isPlainObject(character.sanityState)?character.sanityState:{},base=sanityStateDefault(character),candidate=Number(existing.baselineSan),baseline=Number.isFinite(candidate)?clamp(Math.floor(candidate),0,99):base.baselineSan;
  return{version:SAN_LOSS_RESOLUTION_VERSION,authority:SAN_LOSS_RESOLUTION_AUTHORITY,baselineSan:baseline,baselineSource:asString(existing.baselineSource,40)||base.baselineSource,indefiniteTrackingReady:existing.indefiniteTrackingReady===true,temporary:isPlainObject(existing.temporary)?deepClone(existing.temporary):null,history:Array.isArray(existing.history)?existing.history.slice(-SAN_LOSS_RESOLUTION_HISTORY_LIMIT).map(deepClone):[]}
}
function normalizeSanityState(character=state.character,{newCharacter=false}={}){
  if(!character||character.system!=="coc7")return null;character.sanityState=sanityStateSnapshot(character);if(newCharacter){const current=Math.max(0,Number(character.san||0));character.sanityState.baselineSan=current;character.sanityState.baselineSource="creation";character.sanityState.indefiniteTrackingReady=false}return character.sanityState
}
function sanBoutDefinition(roll){const value=clamp(Math.floor(Number(roll)||1),1,10);return{roll:value,...SAN_BOUT_TABLE[value]}}
function sanResolutionRollD10(roller=randomInt){return clamp(Math.floor(Number(roller(1,10))||1),1,10)}
function sanResolutionRollD100(roller=randomInt){return clamp(Math.floor(Number(roller(1,100))||1),1,100)}
function buildSanLossResolution(record,{roller=randomInt}={}){
  const loss=Math.max(0,Math.floor(Number(record?.sanLoss?.amount||0))),intTarget=clamp(Math.floor(Number(state.character?.attributes?.int||0)),1,100),majorShock=loss>=SAN_TEMPORARY_INSANITY_THRESHOLD,momentaryReactionRequired=loss>0;let intCheck=null,temporaryInsanity=null;
  if(majorShock){const roll=sanResolutionRollD100(roller),success=roll<=intTarget;intCheck={roll,target:intTarget,success};if(success){const durationHours=sanResolutionRollD10(roller),boutRoll=sanResolutionRollD10(roller),boutDurationRounds=sanResolutionRollD10(roller);temporaryInsanity={active:true,durationHours,bout:{...sanBoutDefinition(boutRoll),durationRounds:boutDurationRounds},expiryManaged:false,manifestationAuthority:"ai_narration_within_browser_selected_type"}}}
  return{version:SAN_LOSS_RESOLUTION_VERSION,authority:SAN_LOSS_RESOLUTION_AUTHORITY,recordId:record?.id||null,sanLoss:loss,momentaryReactionRequired,majorShock,intCheck,temporaryInsanity,indefiniteInsanity:{evaluated:false,reason:"starting_san_and_cumulative_loss_window_not_authoritative_in_v1.6.3"},immutable:true,createdAt:nowIso()}
}
function ensureSanLossResolution(record,{roller=randomInt}={}){
  if(!record||record.system!=="coc7"||record.type!=="san"||record.skipped)return null;if(isPlainObject(record.sanResolution)&&record.sanResolution.version===SAN_LOSS_RESOLUTION_VERSION)return record.sanResolution;
  const sanity=normalizeSanityState(state.character),resolution=buildSanLossResolution(record,{roller});record.sanResolution=deepClone(resolution);if(resolution.temporaryInsanity){sanity.temporary={...deepClone(resolution.temporaryInsanity),sourceRecordId:record.id,startedAt:nowIso()}}sanity.history.push(deepClone(resolution));if(sanity.history.length>SAN_LOSS_RESOLUTION_HISTORY_LIMIT)sanity.history.splice(0,sanity.history.length-SAN_LOSS_RESOLUTION_HISTORY_LIMIT);return record.sanResolution
}
function sanResolutionContext(record=null){
  const sanity=state.character?.system==="coc7"?sanityStateSnapshot(state.character):null,resolution=record?.system==="coc7"&&record?.type==="san"?record.sanResolution||null:null;
  return{version:SAN_LOSS_RESOLUTION_VERSION,authority:SAN_LOSS_RESOLUTION_AUTHORITY,temporaryInsanityThreshold:SAN_TEMPORARY_INSANITY_THRESHOLD,boutTable:deepClone(SAN_BOUT_TABLE),current:resolution?deepClone(resolution):null,temporary:sanity?.temporary?deepClone(sanity.temporary):null,indefiniteInsanity:{implemented:false,reason:"requires authoritative starting-SAN baseline and cumulative-loss window"},policy:"browser_resolves_single_san_shock_ai_narrates_selected_result"}
}

/* Initialize new CoC investigators with an explicit future baseline marker. */
const __sanBuildCocCharacter=buildCocCharacter;
buildCocCharacter=function(form){const character=__sanBuildCocCharacter(form);normalizeSanityState(character,{newCharacter:true});return character};

/* Old Schema 8 saves lazily gain the new field without pretending the legacy current SAN is an authoritative indefinite-insanity baseline. */
const __sanNormalizeLoadedState=normalizeLoadedState;
normalizeLoadedState=function(raw){const loaded=__sanNormalizeLoadedState(raw);if(loaded.character?.system==="coc7")normalizeSanityState(loaded.character);return loaded};

/* The SAN amount has already been applied before requestContinuation. Resolve the single-shock chain exactly once before the continuation request samples baseRevision/payload. */
const __sanRequestContinuation=requestContinuation;
requestContinuation=async function(record,options={}){if(record?.system==="coc7"&&record?.type==="san"&&!record.skipped&&!record.sanResolution){ensureSanLossResolution(record);bumpRevision();addLog("san_resolution",record.sanResolution.temporaryInsanity?`SAN 冲击触发临时疯狂：INT ${record.sanResolution.intCheck.roll}/${record.sanResolution.intCheck.target}，${record.sanResolution.temporaryInsanity.durationHours} 小时`:`SAN 冲击已由浏览器完成后续判定`,{requestId:record.requestId,secret:false})}return __sanRequestContinuation(record,options)};

const __sanCheckOutcomeGuidance=checkOutcomeGuidance;
checkOutcomeGuidance=function(record){const guidance=__sanCheckOutcomeGuidance(record);if(record?.system!=="coc7"||record?.type!=="san")return guidance;return{...guidance,sanResolution:deepClone(record.sanResolution||null),sanResolutionPolicy:{authority:SAN_LOSS_RESOLUTION_AUTHORITY,rule:"SAN loss、INT shock check、temporary insanity、bout type/duration 均为浏览器不可修改结果。AI 只可在该结果内叙述反应，不得改判、重骰、追加 SAN loss 或宣称 v1.6.3 已判定不定期疯狂。"}}};

const __sanBuildRequestPayload=buildRequestPayload;
buildRequestPayload=function(stage,requestId,baseRevision,extra={}){const payload=__sanBuildRequestPayload(stage,requestId,baseRevision,extra),record=extra?.checkRecord?.system==="coc7"&&extra.checkRecord?.type==="san"?extra.checkRecord:null;payload.sanLossResolution=sanResolutionContext(record);return payload};
const __sanBuildSystemPrompt=buildSystemPrompt;
buildSystemPrompt=function(){return `${__sanBuildSystemPrompt()}\n26. SAN Loss Resolution：单次 SAN 损失后的 INT 冲击检定、临时疯狂持续时间与疯狂发作类型/轮数由浏览器决定。你只能叙述浏览器给出的 immutable sanResolution；不得重骰、改判、重复扣 SAN，不能自行宣告不定期疯狂。本规则受限时仍必须继续正常玩家交互。`};
const __sanBuildUserPrompt=buildUserPrompt;
buildUserPrompt=function(payload){return `${__sanBuildUserPrompt(payload)}\nSAN 冲击权威：${JSON.stringify(payload.sanLossResolution||sanResolutionContext())}`};

if(typeof buildDiagnosticPackage==="function"){
  const __sanBuildDiagnosticPackage=buildDiagnosticPackage;
  buildDiagnosticPackage=function(options={}){const pack=__sanBuildDiagnosticPackage(options),sanity=state.character?.system==="coc7"?sanityStateSnapshot(state.character):null;pack.sanLossResolution={...sanResolutionContext(),state:sanity?deepClone(sanity):null};return pack}
}
