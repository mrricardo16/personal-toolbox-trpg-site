"use strict";

/* v1.6.2 Failure-Forward Cost Engine
 * Authored clue routes declare a fixed cost bundle; browser normalizes and applies it.
 * AI may choose/narrate an eligible route, but cannot choose or enlarge its mechanical cost.
 */
const FAILURE_FORWARD_COST_ENGINE_VERSION="1.0";
const FAILURE_FORWARD_COST_AUTHORITY="browser_authored_failure_forward";
const FAILURE_FORWARD_COST_HISTORY_LIMIT=40;
const FAILURE_FORWARD_COST_KEYS=new Set(["tension","hp","san","progress","resources"]);

function failureForwardCostScalar(value,key,max){
  if(value===undefined||value===null)return null;const number=Number(value);if(!Number.isInteger(number)||number<0||number>max)throw new Error(`failure_forward cost.${key} 必须是 0-${max} 的整数`);return number
}
function failureForwardCostResources(raw){
  if(raw===undefined||raw===null)return{};if(!isPlainObject(raw))throw new Error("failure_forward cost.resources 必须是对象");const out={};for(const [rawId,rawAmount] of Object.entries(raw)){const id=asString(rawId,80).trim(),amount=Number(rawAmount);if(!id)throw new Error("failure_forward cost.resources 存在空资源 ID");if(!Number.isInteger(amount)||amount<=0||amount>999)throw new Error(`failure_forward cost.resources.${id} 必须是 1-999 的整数`);out[id]=amount}return out
}
function normalizeFailureForwardCost(rawCost){
  const cost=isPlainObject(rawCost)?rawCost:{};for(const key of Object.keys(cost))if(!FAILURE_FORWARD_COST_KEYS.has(key))throw new Error(`failure_forward cost 包含未知字段：${key}`);
  const tensionRaw=failureForwardCostScalar(cost.tension,"tension",6),hp=failureForwardCostScalar(cost.hp,"hp",100)??0,san=failureForwardCostScalar(cost.san,"san",100)??0,progress=failureForwardCostScalar(cost.progress,"progress",10)??0,resources=failureForwardCostResources(cost.resources);
  const tension=tensionRaw===null?1:tensionRaw,meaningful=tension>0||hp>0||san>0||progress>0||Object.keys(resources).length>0;if(!meaningful)throw new Error("failure_forward cost 至少需要一项正成本；如需非张力成本，可显式设置 tension:0 并声明其它成本");
  return{tension,hp,san,progress,resources,tensionExplicit:cost.tension!==undefined}
}
function failureForwardCostIntegrityIssues(scenario){
  const issues=[];for(const node of allScenarioNodes(scenario))for(const clue of node.clues||[])for(const route of clue.acquisitionRoutes||[]){if(route?.type!=="failure_forward")continue;try{normalizeFailureForwardCost(route.cost)}catch(error){issues.push(caseIntegrityIssue("ERROR","FAILURE_FORWARD_COST_INVALID",`线索“${clue.name||clue.id}”的失败前进路线 ${route.id||"未命名"} 成本无效：${error.message}`,{nodeId:node.id,clueId:clue.id,routeId:route.id||""}))}}
  return issues
}
function failureForwardCostRouteEntries(parsed,record){
  if(!record||record.system!=="coc7"||record.skipped||record.result===true)return[];const entries=[],seen=new Set();for(const change of parsed?.stateChanges||[]){if(change?.operation!=="revealClue")continue;const clueId=asString(change.clueId,120),routeId=asString(change.sourceRouteId,120);if(!clueId||!routeId)continue;if(change.sourceCheckRecordId&&change.sourceCheckRecordId!==record.id)continue;const found=findScenarioClue(clueId);if(!found)continue;const route=(found.clue.acquisitionRoutes||[]).map(normalizeAcquisitionRoute).find(item=>item.id===routeId);if(!route||route.type!=="failure_forward")continue;if(route.checkId&&route.checkId!==record.sourceCheckId)continue;const key=`${clueId}:${routeId}`;if(seen.has(key))continue;seen.add(key);entries.push({clueId,clueName:asString(found.clue.name,160),routeId,checkId:route.checkId||null,cost:normalizeFailureForwardCost(route.cost)})}return entries
}
function failureForwardCostMerge(target,source){target.tension+=source.tension;target.hp+=source.hp;target.san+=source.san;target.progress+=source.progress;for(const [id,amount] of Object.entries(source.resources||{}))target.resources[id]=(target.resources[id]||0)+amount;return target}
function failureForwardCostAuthoredTotals(routes,record){
  const totals={tension:0,hp:0,san:0,progress:0,resources:{}};for(const entry of routes){const effective=deepClone(entry.cost);if(effective.tension>0&&record?.rank==="fumble")effective.tension=Math.max(2,effective.tension);entry.effectiveCost=deepClone(effective);failureForwardCostMerge(totals,effective)}return totals
}
function failureForwardCostAppliedTotals(authored){
  const director=state.campaign?.directorState||{},currentTension=Math.max(0,Number(director.tension||1)),maxTension=Math.max(currentTension,Number(director.maxTension||6)),currentProgress=Math.max(0,Number(director.progress||0)),currentHp=Math.max(0,Number(state.character?.hp||0)),currentSan=Math.max(0,Number(state.character?.san||0)),resources={};for(const [id,amount] of Object.entries(authored.resources||{})){const current=Math.max(0,Number(state.resources?.[id]||0));resources[id]=Math.min(amount,current)}
  return{tension:Math.min(authored.tension,Math.max(0,maxTension-currentTension)),hp:Math.min(authored.hp,Math.max(0,currentHp-1)),san:Math.min(authored.san,currentSan),progress:Math.min(authored.progress,currentProgress,10),resources}
}
function failureForwardCostPlan(parsed,record){
  const routes=failureForwardCostRouteEntries(parsed,record),authoredTotals=failureForwardCostAuthoredTotals(routes,record),totals=failureForwardCostAppliedTotals(authoredTotals),effects=[];
  if(totals.hp>0)effects.push({channel:"state",effect:{operation:"adjustHp",amount:-totals.hp,reason:"失败前进的作者成本"}});if(totals.san>0)effects.push({channel:"state",effect:{operation:"adjustSan",amount:-totals.san,reason:"失败前进的作者成本"}});for(const [resource,amount] of Object.entries(totals.resources))if(amount>0)effects.push({channel:"state",effect:{operation:"adjustResource",resource,amount:-amount,reason:"失败前进的作者成本"}});if(totals.tension>0)effects.push({channel:"campaign",effect:{operation:"adjustTension",amount:totals.tension,reason:"失败前进的作者成本"}});if(totals.progress>0)effects.push({channel:"campaign",effect:{operation:"adjustProgress",amount:-totals.progress,reason:"失败前进的作者成本"}});
  return{version:FAILURE_FORWARD_COST_ENGINE_VERSION,authority:FAILURE_FORWARD_COST_AUTHORITY,recordId:record?.id||null,outcomeContractId:record?.outcomeContract?.contractId||null,immutable:true,policy:"authored_cost_browser_applied",routes:deepClone(routes),authoredTotals,totals,effects,createdAt:nowIso()}
}
function failureForwardCostContext(){
  const routes=[];for(const node of allScenarioNodes())for(const clue of node.clues||[])for(const route of clue.acquisitionRoutes||[]){if(route?.type!=="failure_forward")continue;try{routes.push({clueId:clue.id,routeId:route.id||null,checkId:route.checkId||null,cost:normalizeFailureForwardCost(route.cost)})}catch{}}
  return{version:FAILURE_FORWARD_COST_ENGINE_VERSION,authority:FAILURE_FORWARD_COST_AUTHORITY,policy:"author_declares_browser_applies",supportedCosts:[...FAILURE_FORWARD_COST_KEYS],routes:routes.slice(0,60),fumblePolicy:"if authored tension > 0, fumble tension is at least 2",hpPolicy:"failure-forward HP cost is nonlethal and cannot reduce HP below 1",aggregationPolicy:"dedupe same clue+route, aggregate authored costs once, then clamp to current canonical availability"}
}
function ensureFailureForwardCostRuntime(){state.runtime.failureForwardCostEngine=isPlainObject(state.runtime.failureForwardCostEngine)?state.runtime.failureForwardCostEngine:{};const runtime=state.runtime.failureForwardCostEngine;runtime.version=FAILURE_FORWARD_COST_ENGINE_VERSION;runtime.last=runtime.last||null;runtime.history=Array.isArray(runtime.history)?runtime.history:[];return runtime}

/* Keep the old route proof, but remove its tension side effect. v1.6.2 applies the full cost exactly once. */
const __failureForwardValidateClueAcquisition=validateClueAcquisition;
validateClueAcquisition=function(raw,clue,validationContext={}){
  const routeId=asString(raw?.sourceRouteId,120),route=(clue?.acquisitionRoutes||[]).map(normalizeAcquisitionRoute).find(item=>item.id===routeId);if(!route||route.type!=="failure_forward")return __failureForwardValidateClueAcquisition(raw,clue,validationContext);
  const had=Object.prototype.hasOwnProperty.call(validationContext,"failureForwardTension"),previous=validationContext.failureForwardTension;let authorization;
  try{authorization=__failureForwardValidateClueAcquisition(raw,clue,validationContext)}finally{if(had)validationContext.failureForwardTension=previous;else delete validationContext.failureForwardTension}
  return{...authorization,tensionCost:0,costManagedBy:FAILURE_FORWARD_COST_AUTHORITY}
};

/* Replace v1.6.1's tension-only compatibility path with the full authored bundle. */
const __failureForwardLegacyTension=cocConsequenceFailureForwardTension;
cocConsequenceFailureForwardTension=function(){return 0};
const __failureForwardCocPrepareParsed=cocConsequencePrepareParsed;
cocConsequencePrepareParsed=function(parsed,record){const governed=__failureForwardCocPrepareParsed(parsed,record),plan=failureForwardCostPlan(governed.parsed,record);for(const item of plan.effects){const copy=deepClone(item.effect);(item.channel==="campaign"?governed.parsed.campaignChanges:governed.parsed.stateChanges).push(copy);governed.contract.injected.push({channel:item.channel,effect:deepClone(copy),source:"failure_forward_cost_engine"})}governed.contract.failureForwardTension=plan.totals.tension;governed.contract.failureForwardCostContract=deepClone(plan);return governed};

const __failureForwardPrepareAiTransaction=prepareAiTransaction;
prepareAiTransaction=function(parsed,options={}){const transaction=__failureForwardPrepareAiTransaction(parsed,options),contract=transaction?.cocConsequenceContract?.failureForwardCostContract;if(contract)transaction.failureForwardCostContract=deepClone(contract);return transaction};
const __failureForwardCommitAiTransaction=commitAiTransaction;
commitAiTransaction=function(transaction,requestId){const result=__failureForwardCommitAiTransaction(transaction,requestId),contract=transaction?.failureForwardCostContract;if(contract?.routes?.length){const runtime=ensureFailureForwardCostRuntime();runtime.last={...deepClone(contract),requestId:requestId||null,committedAt:nowIso()};runtime.history.push(deepClone(runtime.last));if(runtime.history.length>FAILURE_FORWARD_COST_HISTORY_LIMIT)runtime.history.splice(0,runtime.history.length-FAILURE_FORWARD_COST_HISTORY_LIMIT);const record=cocConsequenceFindRecord(contract.recordId);if(record)record.failureForwardCostContract=deepClone(contract);addLog("failure_forward_cost",`浏览器按 ${contract.routes.length} 条 authored failure-forward route 应用固定成本`,{requestId,secret:true})}return result};

const __failureForwardValidateCaseIntegrity=validateCaseIntegrity;
validateCaseIntegrity=function(scenario){const report=__failureForwardValidateCaseIntegrity(scenario),extra=failureForwardCostIntegrityIssues(scenario);if(extra.length){report.issues.push(...extra);report.counts=caseIntegritySummary(report.issues);report.blocking=report.counts.errors>0}return report};

const __failureForwardBuildRequestPayload=buildRequestPayload;
buildRequestPayload=function(stage,requestId,baseRevision,extra={}){const payload=__failureForwardBuildRequestPayload(stage,requestId,baseRevision,extra);payload.failureForwardCostEngine=failureForwardCostContext();return payload};
const __failureForwardCheckOutcomeGuidance=checkOutcomeGuidance;
checkOutcomeGuidance=function(record){const guidance=__failureForwardCheckOutcomeGuidance(record);if(record?.system!=="coc7")return guidance;return{...guidance,failureForwardCostPolicy:{authority:FAILURE_FORWARD_COST_AUTHORITY,rule:"若使用 failure_forward route，只能采用 payload 中该 route 的 authored cost；浏览器会实际应用，禁止增减或另造代价。"}}};
const __failureForwardBuildSystemPrompt=buildSystemPrompt;
buildSystemPrompt=function(){return `${__failureForwardBuildSystemPrompt()}\n25. Failure-Forward Cost Engine：failure_forward 的机械成本只来自剧本作者的 route.cost，浏览器会统一执行 tension / hp / san / progress / resources 成本。你不能自行增加、减少、替换成本，也不能因为成本受限而拒绝玩家正常交互。`};
const __failureForwardBuildUserPrompt=buildUserPrompt;
buildUserPrompt=function(payload){return `${__failureForwardBuildUserPrompt(payload)}\n失败前进成本权威：${JSON.stringify(payload.failureForwardCostEngine||failureForwardCostContext())}`};

if(typeof buildDiagnosticPackage==="function"){
  const __failureForwardBuildDiagnosticPackage=buildDiagnosticPackage;
  buildDiagnosticPackage=function(options={}){const pack=__failureForwardBuildDiagnosticPackage(options),includeSecrets=Boolean(options?.includeSecrets),runtime=ensureFailureForwardCostRuntime(),visible=entry=>{const record=cocConsequenceFindRecord(entry?.recordId);return includeSecrets||record?.visibility!=="secret"};pack.failureForwardCostEngine={...failureForwardCostContext(),last:runtime.last&&visible(runtime.last)?deepClone(runtime.last):null,history:runtime.history.filter(visible).slice(-12).map(deepClone)};return pack}
}
