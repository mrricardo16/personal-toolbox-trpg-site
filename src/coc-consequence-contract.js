"use strict";

/* v1.6.1 Mechanical Consequence Contract
 * Browser-owned check outcomes may authorize mechanical consequences.
 * AI may describe/propose; punitive canonical effects require browser evidence.
 * BLOCK UNSAFE STATE, NOT PLAYER ACTION.
 */
const COC_CONSEQUENCE_CONTRACT_VERSION="1.0";
const COC_CONSEQUENCE_AUTHORITY="browser_coc_consequence";
const COC_CONSEQUENCE_HISTORY_LIMIT=40;

function cocConsequenceFindRecord(recordId){
  return (state.checkRecords||[]).find(record=>record?.id===recordId)||null
}
function cocConsequenceAuthoredCheck(record){
  if(!record||record.system!=="coc7"||record.origin!=="node"||!record.sourceNodeId||!record.sourceCheckId)return null;
  const node=allScenarioNodes().find(item=>item.id===record.sourceNodeId);if(!node)return null;
  const definition=[...(node.mandatoryChecks||[]),...(node.optionalChecks||[])].find(item=>item.id===record.sourceCheckId);if(!definition||definition.system!=="coc7")return null;
  if(record.type&&definition.type&&record.type!==definition.type)return null;
  if(record.skillId&&definition.skillId&&record.skillId!==definition.skillId)return null;
  return definition
}
function cocConsequenceEffectIdentity(raw){
  const op=raw?.operation||"";
  const fields={revealClue:"clueId",updateClue:"clueId",removeItem:"itemId",updateItemQuantity:"itemId",removeStatus:"statusId",updateNpc:"npcId",resolveLead:"leadId",resolveQuestion:"questionId",advanceClock:"clockId",resolveClock:"clockId"};
  const field=fields[op];if(field)return `${op}:${asString(raw?.[field],120)}`;
  if(op==="setScenarioFlag"||op==="clearScenarioFlag")return `${op}:${asString(raw?.flag,120)}`;
  if(op==="adjustResource")return `${op}:${asString(raw?.resource,120)}`;
  return op
}
function cocConsequenceTrustedEffect(raw,record){
  const effect=deepClone(raw||{});if(effect.operation==="revealClue"&&!effect.sourceCheckRecordId)effect.sourceCheckRecordId=record.id;return effect
}
function cocConsequenceAuthoredEffects(record){
  if(!record||record.skipped||record.visibility==="secret")return[];const definition=cocConsequenceAuthoredCheck(record);if(!definition)return[];
  const source=record.result===true?definition.successStateChanges:definition.failureStateChanges;
  return(Array.isArray(source)?source:[]).filter(isPlainObject).map(effect=>cocConsequenceTrustedEffect(effect,record))
}
function cocConsequenceFailureForwardTension(parsed,record){
  if(!record||record.system!=="coc7"||record.result===true&&!record.skipped)return 0;let required=0;
  for(const change of parsed?.stateChanges||[]){
    if(change?.operation!=="revealClue")continue;const routeId=asString(change.sourceRouteId,120);if(!routeId)continue;
    const found=findScenarioClue(asString(change.clueId,120));if(!found)continue;
    const route=(found.clue.acquisitionRoutes||[]).map(normalizeAcquisitionRoute).find(item=>item.id===routeId);if(!route||route.type!=="failure_forward")continue;
    if(route.checkId&&route.checkId!==record.sourceCheckId)continue;
    const base=Math.max(1,Number(route.cost?.tension||1)),amount=record.rank==="fumble"?Math.max(2,base):base;required=Math.max(required,amount)
  }
  return required
}
function cocConsequenceItemQuantityIsLoss(change){
  if(change?.operation!=="updateItemQuantity")return false;const item=(state.items||[]).find(entry=>entry.id===change.itemId);if(!item)return false;return Number(change.quantity)<Number(item.quantity||0)
}
function cocConsequenceUnauthorizedPunitive(change,channel){
  const op=change?.operation;if(channel==="state"){
    if(op==="adjustHp")return Number(change.amount)<0;
    if(op==="adjustSan")return true;
    if(op==="adjustResource")return Number(change.amount)<0;
    if(op==="removeItem")return true;
    if(op==="updateItemQuantity")return cocConsequenceItemQuantityIsLoss(change);
    return false
  }
  if(op==="adjustTension")return true;
  if(op==="addThreat")return true;
  if(op==="advanceClock")return Number(change.amount??change.by??change.delta??1)>0;
  if(op==="adjustProgress")return Number(change.amount)<0;
  return false
}
function cocConsequenceStripAuthoredDuplicates(list,effects){
  const ids=new Set(effects.map(cocConsequenceEffectIdentity).filter(Boolean));return(list||[]).filter(change=>!ids.has(cocConsequenceEffectIdentity(change)))
}
function cocConsequenceNeutralizeNarrative(value,stripped){
  if(!stripped?.length)return asString(value,12000);const text=asString(value,12000),mechanical=/(?:\bHP\b|生命值|\bSAN\b|理智值|资源|张力|威胁时钟|时钟).{0,24}(?:[+＋\-－−]\s*\d+|减少|损失|扣除|下降|增加|上升|推进)|(?:受到|承受|失去).{0,12}\d+.{0,8}(?:点)?(?:伤害|生命|理智|SAN)/iu;
  const chunks=text.match(/[^。！？!?]+[。！？!?]?/gu)||[text],kept=chunks.filter(chunk=>!mechanical.test(chunk)),safe=kept.join("").trim();
  return[safe,"本次检定的实际机械代价以浏览器已确认的结果为准，没有额外扣减。"].filter(Boolean).join("\n")
}
function cocConsequencePrepareParsed(parsed,record){
  ensureCocOutcomeContract(record);const out=deepClone(parsed),authoredEffects=cocConsequenceAuthoredEffects(record),stripped=[];
  out.stateChanges=cocConsequenceStripAuthoredDuplicates(out.stateChanges||[],authoredEffects);
  out.campaignChanges=cocConsequenceStripAuthoredDuplicates(out.campaignChanges||[],authoredEffects);
  out.stateChanges=out.stateChanges.filter(change=>{if(!cocConsequenceUnauthorizedPunitive(change,"state"))return true;stripped.push({channel:"state",operation:change.operation,reason:"unauthorized_ai_punitive_consequence"});return false});
  out.campaignChanges=out.campaignChanges.filter(change=>{if(!cocConsequenceUnauthorizedPunitive(change,"campaign"))return true;stripped.push({channel:"campaign",operation:change.operation,reason:"unauthorized_ai_punitive_consequence"});return false});
  const injected=[];
  for(const effect of authoredEffects){const copy=cocConsequenceTrustedEffect(effect,record),channel=ALLOWED_CAMPAIGN_OPERATIONS.has(copy.operation)?"campaign":"state";(channel==="campaign"?out.campaignChanges:out.stateChanges).push(copy);injected.push({channel,effect:deepClone(copy),source:"authored_node_check"})}
  const failureForwardTension=cocConsequenceFailureForwardTension(out,record);if(failureForwardTension>0){out.campaignChanges.push({operation:"adjustTension",amount:failureForwardTension,reason:"失败前进的浏览器规则代价"});injected.push({channel:"campaign",effect:{operation:"adjustTension",amount:failureForwardTension},source:"failure_forward_route"})}
  if(stripped.length)out.narrative=cocConsequenceNeutralizeNarrative(out.narrative,stripped);
  const sanLoss=record.type==="san"&&record.sanLoss?{amount:Number(record.sanLoss.amount||0),expression:asString(record.sanLoss.expression,30),alreadyApplied:true}:null;
  const contract={version:COC_CONSEQUENCE_CONTRACT_VERSION,authority:COC_CONSEQUENCE_AUTHORITY,recordId:record.id||null,outcomeContractId:record.outcomeContract?.contractId||null,immutable:true,policy:"block_unsafe_state_not_player_action",sanLoss,authoredCheckId:cocConsequenceAuthoredCheck(record)?.id||null,failureForwardTension,stripped,injected,createdAt:nowIso()};
  return{parsed:out,contract}
}
function cocConsequenceContext(record=null){
  const sanLoss=record?.type==="san"&&record?.sanLoss?{amount:Number(record.sanLoss.amount||0),expression:asString(record.sanLoss.expression,30),alreadyApplied:true}:null;
  return{version:COC_CONSEQUENCE_CONTRACT_VERSION,authority:COC_CONSEQUENCE_AUTHORITY,policy:"punitive_effects_require_browser_evidence",sanLoss,aiMayNotInvent:["hp_loss","san_adjustment","resource_loss","item_loss","tension_change","new_threat","clock_advance","progress_loss"],failureForwardCostAuthority:"browser_authored_route",authoredEffectAuthority:"browser_scenario_definition"}
}
function ensureCocConsequenceRuntime(){
  state.runtime.cocConsequenceContract=isPlainObject(state.runtime.cocConsequenceContract)?state.runtime.cocConsequenceContract:{};const runtime=state.runtime.cocConsequenceContract;runtime.version=COC_CONSEQUENCE_CONTRACT_VERSION;runtime.last=runtime.last||null;runtime.history=Array.isArray(runtime.history)?runtime.history:[];return runtime
}

/* Tell continuation AI what it may narrate before it replies. */
const __cocConsequenceCheckOutcomeGuidance=checkOutcomeGuidance;
checkOutcomeGuidance=function(record){const guidance=__cocConsequenceCheckOutcomeGuidance(record);if(record?.system!=="coc7")return guidance;return{...guidance,consequencePolicy:cocConsequenceContext(record),prohibited:[...(guidance.prohibited||[]),"do_not_invent_punitive_mechanical_consequences","do_not_apply_san_loss_twice"]}};

/* Final consequence authorization happens immediately before canonical transaction preparation. */
const __cocConsequencePrepareAiTransaction=prepareAiTransaction;
prepareAiTransaction=function(parsed,options={}){
  const recordId=asString(options?.currentCheckRecordId,120),record=recordId?cocConsequenceFindRecord(recordId):null;if(!record||record.system!=="coc7")return __cocConsequencePrepareAiTransaction(parsed,options);
  const governed=cocConsequencePrepareParsed(parsed,record),transaction=__cocConsequencePrepareAiTransaction(governed.parsed,options);transaction.cocConsequenceContract=deepClone(governed.contract);return transaction
};

const __cocConsequenceCommitAiTransaction=commitAiTransaction;
commitAiTransaction=function(transaction,requestId){const result=__cocConsequenceCommitAiTransaction(transaction,requestId),contract=transaction?.cocConsequenceContract;if(contract){const runtime=ensureCocConsequenceRuntime();runtime.last={...deepClone(contract),requestId:requestId||null,committedAt:nowIso()};runtime.history.push(deepClone(runtime.last));if(runtime.history.length>COC_CONSEQUENCE_HISTORY_LIMIT)runtime.history.splice(0,runtime.history.length-COC_CONSEQUENCE_HISTORY_LIMIT);const record=cocConsequenceFindRecord(contract.recordId);if(record)record.consequenceContract=deepClone(contract);if(contract.stripped?.length)addLog("coc_consequence",`已剥离 ${contract.stripped.length} 项未经浏览器授权的检定机械代价`,{requestId,secret:true});if(contract.injected?.length)addLog("coc_consequence",`浏览器应用 ${contract.injected.length} 项已授权检定后果`,{requestId,secret:true})}return result};

const __cocConsequenceBuildRequestPayload=buildRequestPayload;
buildRequestPayload=function(stage,requestId,baseRevision,extra={}){const payload=__cocConsequenceBuildRequestPayload(stage,requestId,baseRevision,extra),record=extra?.checkRecord?.system==="coc7"?extra.checkRecord:null;payload.cocConsequenceContract=cocConsequenceContext(record);return payload};
const __cocConsequenceBuildSystemPrompt=buildSystemPrompt;
buildSystemPrompt=function(){return `${__cocConsequenceBuildSystemPrompt()}\n24. Mechanical Consequence Contract：CoC 检定后的额外 HP/SAN/资源/物品损失、张力升级、新威胁、时钟推进等惩罚性机械后果必须有浏览器授权。SAN loss 已由浏览器结算时不得再次 adjustSan；failure-forward 代价由 authored route 决定。未经授权的代价会被剥离，但仍必须继续正常叙事与交互。`};
const __cocConsequenceBuildUserPrompt=buildUserPrompt;
buildUserPrompt=function(payload){return `${__cocConsequenceBuildUserPrompt(payload)}\nCoC 检定后果权威：${JSON.stringify(payload.cocConsequenceContract||cocConsequenceContext())}`};

if(typeof buildDiagnosticPackage==="function"){
  const __cocConsequenceBuildDiagnosticPackage=buildDiagnosticPackage;
  buildDiagnosticPackage=function(options={}){const pack=__cocConsequenceBuildDiagnosticPackage(options),includeSecrets=Boolean(options?.includeSecrets),runtime=ensureCocConsequenceRuntime(),visible=entry=>{const record=cocConsequenceFindRecord(entry?.recordId);return includeSecrets||record?.visibility!=="secret"};pack.cocConsequenceContract={...cocConsequenceContext(),last:runtime.last&&visible(runtime.last)?deepClone(runtime.last):null,history:runtime.history.filter(visible).slice(-12).map(deepClone)};return pack}
}
