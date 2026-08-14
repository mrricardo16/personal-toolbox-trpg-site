"use strict";

/* v1.6.4 SAN Loss Window / Indefinite Insanity Tracking
 * The browser owns the authoritative starting-SAN window and cumulative SAN loss.
 * A new scenario starts a fresh window; crossing into a new authored chapter starts
 * another window. The one-fifth threshold is derived from that window baseline.
 * This module records the indefinite-insanity condition only; it does not invent
 * recovery timing or an additional bout of madness beyond v1.6.3 single-shock rules.
 */
const SAN_LOSS_WINDOW_VERSION="1.0";
const SAN_LOSS_WINDOW_AUTHORITY="browser_coc_sanity_window";
const SAN_LOSS_WINDOW_EVENT_LIMIT=80;

function sanIndefiniteThreshold(baselineSan){return Math.max(1,Math.floor(Math.max(0,Number(baselineSan)||0)/5))}
function normalizeSanLossWindow(raw,character=state.character){
  if(!isPlainObject(raw)||raw.authoritative!==true)return null;
  const baseline=clamp(Math.floor(Number(raw.baselineSan ?? character?.san ?? 0)),0,99),threshold=sanIndefiniteThreshold(baseline),events=Array.isArray(raw.events)?raw.events.slice(-SAN_LOSS_WINDOW_EVENT_LIMIT).map(deepClone):[];
  return{version:SAN_LOSS_WINDOW_VERSION,authority:SAN_LOSS_WINDOW_AUTHORITY,authoritative:true,id:asString(raw.id,120)||uid("san_window"),baselineSan:baseline,threshold,cumulativeLoss:Math.max(0,Math.floor(Number(raw.cumulativeLoss)||0)),source:asString(raw.source,40)||"unknown",sourceId:asString(raw.sourceId,160)||null,label:asString(raw.label,240)||"",startedAt:asString(raw.startedAt,80)||nowIso(),triggered:raw.triggered===true,triggeredAt:asString(raw.triggeredAt,80)||null,triggerEventKey:asString(raw.triggerEventKey,180)||null,lastEventAt:asString(raw.lastEventAt,80)||null,events}
}
function normalizeIndefiniteInsanity(raw){
  if(!isPlainObject(raw)||raw.active!==true)return null;
  return{active:true,authority:SAN_LOSS_WINDOW_AUTHORITY,sourceWindowId:asString(raw.sourceWindowId,120)||null,threshold:Math.max(1,Math.floor(Number(raw.threshold)||1)),cumulativeLossAtTrigger:Math.max(0,Math.floor(Number(raw.cumulativeLossAtTrigger)||0)),triggeredAt:asString(raw.triggeredAt,80)||nowIso(),triggerEventKey:asString(raw.triggerEventKey,180)||null,recoveryManaged:false}
}
function makeSanLossWindow(character=state.character,{source="manual",sourceId=null,label=""}={}){
  const baseline=clamp(Math.floor(Number(character?.san||0)),0,99);
  return{version:SAN_LOSS_WINDOW_VERSION,authority:SAN_LOSS_WINDOW_AUTHORITY,authoritative:true,id:uid("san_window"),baselineSan:baseline,threshold:sanIndefiniteThreshold(baseline),cumulativeLoss:0,source:asString(source,40)||"manual",sourceId:asString(sourceId,160)||null,label:asString(label,240),startedAt:nowIso(),triggered:false,triggeredAt:null,triggerEventKey:null,lastEventAt:null,events:[]}
}

/* Extend v1.6.3 sanity snapshots without mutating on read. */
const __sanWindowSanityStateSnapshot=sanityStateSnapshot;
sanityStateSnapshot=function(character=state.character){
  const snapshot=__sanWindowSanityStateSnapshot(character);if(!snapshot)return null;const existing=isPlainObject(character?.sanityState)?character.sanityState:{};
  snapshot.lossWindow=normalizeSanLossWindow(existing.lossWindow,character);snapshot.indefinite=normalizeIndefiniteInsanity(existing.indefinite);snapshot.indefiniteTrackingReady=Boolean(snapshot.lossWindow?.authoritative);return snapshot
};

function beginSanLossWindow({source="manual",sourceId=null,label=""}={}){
  if(state.character?.system!=="coc7")return null;const sanity=normalizeSanityState(state.character);sanity.lossWindow=makeSanLossWindow(state.character,{source,sourceId,label});sanity.indefiniteTrackingReady=true;return sanity.lossWindow
}
function sanLossWindowEventKey(source,sourceId){const a=asString(source,40)||"san_loss",b=asString(sourceId,140);return b?`${a}:${b}`:uid("san_loss_event")}
function registerSanLossInWindow(amount,{source="san_loss",sourceId=null,reason=""}={}){
  if(state.character?.system!=="coc7")return{tracked:false,reason:"not_coc7"};const loss=Math.max(0,Math.floor(Number(amount)||0));if(loss<=0)return{tracked:false,reason:"no_loss"};const sanity=normalizeSanityState(state.character),window=sanity.lossWindow;if(!window?.authoritative)return{tracked:false,reason:"no_authoritative_window"};
  const eventKey=sanLossWindowEventKey(source,sourceId),existing=(window.events||[]).find(item=>item.eventKey===eventKey);if(existing)return{tracked:true,deduped:true,event:deepClone(existing),triggeredNow:false,indefinite:deepClone(sanity.indefinite||null)};
  const before=Number(window.cumulativeLoss||0),after=before+loss,at=nowIso(),triggeredNow=!window.triggered&&after>=window.threshold;window.cumulativeLoss=after;window.lastEventAt=at;const event={eventKey,source:asString(source,40)||"san_loss",sourceId:asString(sourceId,140)||null,reason:asString(reason,300),loss,before,after,at,triggeredNow};window.events.push(event);if(window.events.length>SAN_LOSS_WINDOW_EVENT_LIMIT)window.events.splice(0,window.events.length-SAN_LOSS_WINDOW_EVENT_LIMIT);
  if(triggeredNow){window.triggered=true;window.triggeredAt=at;window.triggerEventKey=eventKey;sanity.indefinite={active:true,authority:SAN_LOSS_WINDOW_AUTHORITY,sourceWindowId:window.id,threshold:window.threshold,cumulativeLossAtTrigger:after,triggeredAt:at,triggerEventKey:eventKey,recoveryManaged:false}}
  return{tracked:true,deduped:false,event:deepClone(event),triggeredNow,indefinite:deepClone(sanity.indefinite||null)}
}
function sanLossWindowContext(){
  if(state.character?.system!=="coc7")return null;const sanity=sanityStateSnapshot(state.character),window=sanity?.lossWindow||null;
  return{version:SAN_LOSS_WINDOW_VERSION,authority:SAN_LOSS_WINDOW_AUTHORITY,trackingReady:Boolean(window?.authoritative),window:window?deepClone(window):null,indefinite:sanity?.indefinite?deepClone(sanity.indefinite):null,policy:"browser_tracks_authoritative_starting_san_window_and_one_fifth_threshold_ai_only_narrates"}
}

/* Scenario activation is an authoritative window start. */
const __sanWindowActivateScenario=activateScenario;
activateScenario=function(inputScenario){const result=__sanWindowActivateScenario(inputScenario);if(state.character?.system==="coc7"){const window=beginSanLossWindow({source:"scenario_start",sourceId:state.scenario?.id||null,label:state.scenario?.title||""});bumpRevision();addLog("san_window",`SAN 累计窗口开始：${window.baselineSan}，不定期疯狂阈值 ${window.threshold}`);renderAll()}return result};

/* If character creation happens after a scenario was selected, create the same authoritative scenario window on the returned character. */
const __sanWindowBuildCocCharacter=buildCocCharacter;
buildCocCharacter=function(form){const character=__sanWindowBuildCocCharacter(form);if(state.scenario){const existing=isPlainObject(character.sanityState)?character.sanityState:{};existing.lossWindow=makeSanLossWindow(character,{source:"scenario_start",sourceId:state.scenario?.id||null,label:state.scenario?.title||""});existing.indefiniteTrackingReady=true;character.sanityState=existing}return character};

/* Crossing an authored chapter boundary starts a fresh baseline window. */
const __sanWindowEnterNode=enterNode;
enterNode=function(nodeId,options={}){const previousChapter=state.campaign?.currentChapterId||null,result=__sanWindowEnterNode(nodeId,options),nextChapter=state.campaign?.currentChapterId||null;if(state.character?.system==="coc7"&&previousChapter&&nextChapter&&previousChapter!==nextChapter){const window=beginSanLossWindow({source:"chapter_start",sourceId:nextChapter,label:nextChapter});bumpRevision();addLog("san_window",`章节切换，SAN 累计窗口重置：${window.baselineSan}，阈值 ${window.threshold}`);renderAll()}return result};

/* v1.6.3 SAN checks are already numerically applied before continuation; register their actual loss exactly once. */
const __sanWindowEnsureSanLossResolution=ensureSanLossResolution;
ensureSanLossResolution=function(record,options={}){const resolution=__sanWindowEnsureSanLossResolution(record,options);if(!resolution||!record||record.skipped)return resolution;if(!isPlainObject(record.sanLossWindow)){const tracked=registerSanLossInWindow(resolution.sanLoss,{source:"san_check",sourceId:record.id||record.requestId||null,reason:record.reason||record.purpose||""});record.sanLossWindow=deepClone(tracked)}return resolution};

/* Other trusted canonical SAN losses (for example failure-forward authored costs) are captured from the committed delta. */
const __sanWindowCommitAiTransaction=commitAiTransaction;
commitAiTransaction=function(transaction,requestId){const before=state.character?.system==="coc7"?Number(state.character.san||0):null,result=__sanWindowCommitAiTransaction(transaction,requestId),after=state.character?.system==="coc7"?Number(state.character.san||0):null;if(Number.isFinite(before)&&Number.isFinite(after)&&after<before){const tracked=registerSanLossInWindow(before-after,{source:"canonical_transaction",sourceId:requestId||null,reason:"trusted canonical SAN decrease"});if(tracked.triggeredNow){bumpRevision();addLog("san_indefinite",`累计 SAN 损失达到 ${tracked.indefinite.threshold}，浏览器标记不定期疯狂`,{requestId});renderAll()}}return result};

/* Add the cumulative-window result to the existing SAN context, payload and diagnostics by dynamic composition. */
const __sanWindowResolutionContext=sanResolutionContext;
sanResolutionContext=function(record=null){return{...__sanWindowResolutionContext(record),lossWindow:sanLossWindowContext()}};
const __sanWindowBuildSystemPrompt=buildSystemPrompt;
buildSystemPrompt=function(){return `${__sanWindowBuildSystemPrompt()}\n27. SAN Loss Window：Starting SAN 基线、累计 SAN 损失、1/5 不定期疯狂阈值和 indefinite condition 均由浏览器维护。只能叙述 payload 中的 browser-owned lossWindow/indefinite；不得重置窗口、篡改累计值、提前宣告或解除不定期疯狂。窗口规则受限时仍必须继续玩家正常交互。`};
