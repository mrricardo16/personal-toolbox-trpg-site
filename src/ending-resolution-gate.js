"use strict";

/* v1.5.12 Ending / Resolution Gate
 * AI may propose an ending; browser-owned canonical state decides whether it may commit.
 * BLOCK UNSAFE STATE, NOT PLAYER ACTION.
 */
const ENDING_RESOLUTION_GATE_VERSION="1.0";
const ENDING_GATE_CLOCK_STATES=new Set(["active","triggered","resolved"]);
const ENDING_GATE_SEMANTIC_KINDS=new Set(["DISCOVERY","ACCESS","SOCIAL","THREAT","RESOLUTION"]);

function normalizeEndingGateClockRequirement(raw,index=0){
  const item=isPlainObject(raw)?raw:{};
  return{clockId:asString(item.clockId,120)||`clock-${index+1}`,state:asString(item.state,40)||"resolved"}
}
function normalizeEndingGateDefinition(raw){
  const ending=isPlainObject(raw)?raw:{};
  return{
    ...deepClone(ending),
    requiredFlags:listStrings(ending.requiredFlags,16,120),
    forbiddenFlags:listStrings(ending.forbiddenFlags,16,120),
    requiredClueIds:listStrings(ending.requiredClueIds,24,120),
    requiresAnyClueIds:listStrings(ending.requiresAnyClueIds,24,120),
    requiredResolvedLeadIds:listStrings(ending.requiredResolvedLeadIds,16,120),
    requiredResolvedQuestionIds:listStrings(ending.requiredResolvedQuestionIds,16,120),
    requiredNodeIds:listStrings(ending.requiredNodeIds,16,120),
    requiredSemanticKinds:listStrings(ending.requiredSemanticKinds,8,40),
    requiredClockStates:Array.isArray(ending.requiredClockStates)?ending.requiredClockStates.map(normalizeEndingGateClockRequirement).slice(0,16):[],
    requireNoActiveThreats:Boolean(ending.requireNoActiveThreats),
    minClues:clamp(Number(ending.minClues||0),0,50),
    outcomeRequirements:isPlainObject(ending.outcomeRequirements)?deepClone(ending.outcomeRequirements):null
  }
}
function endingGateSemanticKinds(campaign=state.campaign){
  const semantics=campaign?.progressSemantics||{},events=[...(Array.isArray(semantics.history)?semantics.history:[]),semantics.last].filter(Boolean),set=new Set();
  for(const event of events)for(const kind of event?.kinds||[])if(ENDING_GATE_SEMANTIC_KINDS.has(kind))set.add(kind);
  return set
}
function endingGateClockMatches(clock,required){
  if(!clock)return false;
  if(required==="resolved")return Boolean(clock.resolved);
  if(required==="triggered")return Boolean(clock.triggered);
  if(required==="active")return Boolean(clock.active)&&!clock.triggered&&!clock.resolved;
  return false
}
function endingGateEvaluate(ending,campaign=state.campaign,clues=state.clues){
  const e=normalizeEndingGateDefinition(ending),missing=[],flags=campaign?.flags||{},outcomes={...defaultOutcomes(),...(campaign?.outcomes||{})},clueIds=new Set((clues||[]).map(clue=>clue?.id).filter(Boolean)),leads=new Map((campaign?.activeLeads||[]).map(item=>[item.id,item])),questions=new Map((campaign?.unresolvedQuestions||[]).map(item=>[item.id,item])),clocks=new Map((campaign?.directorState?.clocks||[]).map(clock=>[clock.id,clock])),semanticKinds=endingGateSemanticKinds(campaign);
  if(e.alwaysAvailable)return{version:ENDING_RESOLUTION_GATE_VERSION,endingId:e.id||null,ready:true,alwaysAvailable:true,missing:[],checkedAt:nowIso()};
  for(const flag of e.requiredFlags)if(flags[flag]!==true)missing.push({code:"required_flag",id:flag});
  for(const flag of e.forbiddenFlags)if(flags[flag]===true)missing.push({code:"forbidden_flag",id:flag});
  if(e.minClues>(clues||[]).length)missing.push({code:"min_clues",required:e.minClues,actual:(clues||[]).length});
  for(const clueId of e.requiredClueIds)if(!clueIds.has(clueId))missing.push({code:"required_clue",id:clueId});
  if(e.requiresAnyClueIds.length&&!e.requiresAnyClueIds.some(id=>clueIds.has(id)))missing.push({code:"any_clue",ids:e.requiresAnyClueIds});
  for(const leadId of e.requiredResolvedLeadIds)if(leads.get(leadId)?.status!=="resolved")missing.push({code:"resolved_lead",id:leadId});
  for(const questionId of e.requiredResolvedQuestionIds)if(questions.get(questionId)?.status!=="resolved")missing.push({code:"resolved_question",id:questionId});
  if(e.requiredNodeIds.length&&!e.requiredNodeIds.includes(campaign?.currentNodeId))missing.push({code:"required_node",ids:e.requiredNodeIds,actual:campaign?.currentNodeId||null});
  for(const requirement of e.requiredClockStates){const clock=clocks.get(requirement.clockId);if(!endingGateClockMatches(clock,requirement.state))missing.push({code:"clock_state",id:requirement.clockId,state:requirement.state});}
  if(e.requireNoActiveThreats&&(campaign?.directorState?.activeThreats||[]).length)missing.push({code:"active_threats",count:(campaign.directorState.activeThreats||[]).length});
  for(const kind of e.requiredSemanticKinds)if(!ENDING_GATE_SEMANTIC_KINDS.has(kind))missing.push({code:"semantic_invalid",kind});else if(!semanticKinds.has(kind))missing.push({code:"semantic",kind});
  if(e.outcomeRequirements)for(const [key,value] of Object.entries(e.outcomeRequirements)){const allowed=Array.isArray(value)?value:[value];if(!allowed.includes(outcomes[key]))missing.push({code:"outcome",key,allowed,actual:outcomes[key]})}
  return{version:ENDING_RESOLUTION_GATE_VERSION,endingId:e.id||null,ready:missing.length===0,alwaysAvailable:false,missing,checkedAt:nowIso()}
}
function endingGateMissingLabel(item){
  const map={required_flag:"缺少必要剧情旗标",forbidden_flag:"存在禁止剧情旗标",min_clues:"线索数量不足",required_clue:"缺少必要线索",any_clue:"缺少任一可接受线索",resolved_lead:"调查方向尚未解决",resolved_question:"核心问题尚未解决",required_node:"尚未到达允许收束的节点",clock_state:"威胁时钟状态未满足",active_threats:"仍有活动威胁",semantic:"缺少浏览器确认的进展语义",semantic_invalid:"结局声明了非法进展语义",outcome:"案件结果状态未满足"};
  return map[item?.code]||"结局条件尚未满足"
}
function endingGateContext(campaign=state.campaign,clues=state.clues){
  return{version:ENDING_RESOLUTION_GATE_VERSION,authority:"browser_canonical_resolution",endings:(state.scenario?.endings||[]).map(ending=>{const gate=endingGateEvaluate(ending,campaign,clues);return{id:ending.id,title:ending.playerTitle||ending.title,ready:gate.ready,alwaysAvailable:gate.alwaysAvailable,missing:gate.missing.map(item=>({code:item.code,label:endingGateMissingLabel(item)}))}})}
}
function neutralizePrematureEndingNarrative(value){
  const text=asString(value,12000);if(!text)return text;
  const finality=/(?:调查|案件|故事|本案|旅程).{0,12}(?:结束|终结|告一段落|至此结束)|(?:进入|迎来|达成|确定).{0,8}(?:结局|终局)|(?:最终|彻底).{0,8}(?:胜利|失败|结局)/u;
  const chunks=text.match(/[^。！？!?]+[。！？!?]?/gu)||[text],kept=chunks.filter(chunk=>!finality.test(chunk)),removed=kept.length!==chunks.length;
  if(!removed)return text;
  const safe=kept.join("").trim();return[safe,"当前行动中已经通过浏览器校验的结果仍然生效，但案件尚未满足正式收束条件。"].filter(Boolean).join("\n")
}
function ensureEndingGateRuntime(){
  state.runtime.endingResolutionGate=isPlainObject(state.runtime.endingResolutionGate)?state.runtime.endingResolutionGate:{};
  state.runtime.endingResolutionGate.version=ENDING_RESOLUTION_GATE_VERSION;
  state.runtime.endingResolutionGate.lastRecovery=state.runtime.endingResolutionGate.lastRecovery||null;
  state.runtime.endingResolutionGate.lastConfirmed=state.runtime.endingResolutionGate.lastConfirmed||null;
  return state.runtime.endingResolutionGate
}

/* Extend legacy ending condition semantics without invalidating old authored endings. */
endingConditionMatches=function(ending,campaign=state.campaign,clues=state.clues){return endingGateEvaluate(ending,campaign,clues).ready};
availableEndings=function(campaign=state.campaign,clues=state.clues){return(state.scenario?.endings||[]).filter(ending=>endingGateEvaluate(ending,campaign,clues).ready).sort((a,b)=>Number(b.priority||0)-Number(a.priority||0))};
validateEndingProposal=function(proposal,campaignView=state.campaign,cluesView=state.clues){
  if(!proposal)return null;if(!isPlainObject(proposal)||hasDangerousKeys(proposal))throw protocolError("ENDING_PROPOSAL_INVALID","endingProposal 格式非法");
  const endingId=asString(proposal.endingId,120),ending=(state.scenario?.endings||[]).find(item=>item.id===endingId);if(!ending)throw protocolError("ENDING_PROPOSAL_UNKNOWN","提议的结局不存在",{endingId});
  const gate=endingGateEvaluate(ending,campaignView,cluesView);if(!gate.ready)throw protocolError("ENDING_GATE_NOT_READY","当前 canonical state 尚未满足该结局条件",{endingId,title:ending.playerTitle||ending.title,missing:deepClone(gate.missing)});
  return{endingId,title:ending.playerTitle||ending.title,reason:asString(proposal.reason,500),ending:deepClone(ending),gate:deepClone(gate)}
};

/* A premature known ending is recoverable: strip only the unsafe ending result, then re-run the normal transaction validator. */
const __endingGatePrepareAiTransaction=prepareAiTransaction;
prepareAiTransaction=function(parsed,options={}){
  try{return __endingGatePrepareAiTransaction(parsed,options)}catch(error){
    if(error?.code!=="ENDING_GATE_NOT_READY")throw error;
    const recovered=deepClone(parsed),details=isPlainObject(error.details)?deepClone(error.details):{};recovered.endingProposal=null;recovered.narrative=neutralizePrematureEndingNarrative(recovered.narrative);recovered.endingResolutionRecovery={recovered:true,endingId:details.endingId||null,missing:details.missing||[],at:nowIso()};
    const transaction=__endingGatePrepareAiTransaction(recovered,options);transaction.endingResolutionRecovery=deepClone(recovered.endingResolutionRecovery);return transaction
  }
};

const __endingGateCommitAiTransaction=commitAiTransaction;
commitAiTransaction=function(transaction,requestId){const result=__endingGateCommitAiTransaction(transaction,requestId),recovery=transaction?.endingResolutionRecovery||transaction?.parsed?.endingResolutionRecovery;if(recovery){const runtime=ensureEndingGateRuntime();runtime.lastRecovery={...deepClone(recovery),requestId:requestId||null};addLog("ending_gate",`结局提议未满足 canonical gate，已仅剥离结局提交：${recovery.endingId||"unknown"}`,{requestId,secret:true})}return result};

/* Every actual ending commit is re-evaluated at commit time. */
const __endingGateApplyEnding=applyEnding;
applyEnding=function(ending,reason=""){
  const endingId=asString(ending?.id,120),canonical=(state.scenario?.endings||[]).find(item=>item.id===endingId);if(!canonical)throw protocolError("ENDING_PROPOSAL_UNKNOWN","结局不存在或已不属于当前案件",{endingId});
  const gate=endingGateEvaluate(canonical,state.campaign,state.clues);if(!gate.ready)throw protocolError("ENDING_GATE_NOT_READY","当前状态已变化，结局条件不再满足",{endingId,missing:deepClone(gate.missing)});
  const result=__endingGateApplyEnding(canonical,reason),runtime=ensureEndingGateRuntime();runtime.lastConfirmed={endingId,at:nowIso(),gate:deepClone(gate)};return result
};
confirmEndingProposal=function(){
  const proposal=state.runtime.pendingEndingProposal;if(!proposal)throw new Error("没有待确认的结局");const canonical=(state.scenario?.endings||[]).find(item=>item.id===proposal.endingId||item.id===proposal.ending?.id);
  const gate=canonical?endingGateEvaluate(canonical,state.campaign,state.clues):{ready:false,missing:[{code:"unknown_ending"}]};
  if(!canonical||!gate.ready){state.runtime.pendingEndingProposal=null;setPhase("awaiting_player_action",{force:true});bumpRevision();addLog("ending_gate",`确认结局时条件已变化，未提交：${proposal.endingId||proposal.ending?.id||"unknown"}`,{secret:true});addMessage("system","结局条件已经发生变化，本次没有提交正式结局；你可以继续调查或重新考虑收束。",{kind:"continuity"});renderAll();return false}
  applyEnding(canonical,proposal.reason);return true
};

/* Static authored-reference checks compose with Case Integrity. */
const __endingGateValidateCaseIntegrity=validateCaseIntegrity;
validateCaseIntegrity=function(scenario){
  const report=__endingGateValidateCaseIntegrity(scenario),issues=[...(report.issues||[])],nodes=new Set(allScenarioNodes(scenario).map(node=>node.id)),clues=new Set(allScenarioNodes(scenario).flatMap(node=>(node.clues||[]).map(clue=>clue.id))),clocks=new Set((scenario?.director?.threatClocks||[]).map(clock=>clock.id)),leadIds=new Set((scenario?.initialLeads||[]).map(item=>item.id)),questionIds=new Set((scenario?.initialQuestions||[]).map(item=>item.id));
  for(const raw of scenario?.endings||[]){const ending=normalizeEndingGateDefinition(raw),title=ending.playerTitle||ending.title||ending.id||"未命名结局";
    for(const kind of listStrings(raw?.requiredSemanticKinds,8,40))if(!ENDING_GATE_SEMANTIC_KINDS.has(kind))issues.push(caseIntegrityIssue("ERROR","ENDING_GATE_SEMANTIC_INVALID",`结局“${title}”声明了非法 Progress Semantic：${kind}。`,{endingId:ending.id,kind}));
    for(const requirement of Array.isArray(raw?.requiredClockStates)?raw.requiredClockStates:[]){const stateName=asString(requirement?.state,40)||"resolved";if(!ENDING_GATE_CLOCK_STATES.has(stateName))issues.push(caseIntegrityIssue("ERROR","ENDING_GATE_CLOCK_STATE_INVALID",`结局“${title}”声明了非法威胁时钟状态：${stateName}。`,{endingId:ending.id,clockId:asString(requirement?.clockId,120),state:stateName}))}
    for(const nodeId of ending.requiredNodeIds)if(!nodes.has(nodeId))issues.push(caseIntegrityIssue("ERROR","ENDING_GATE_NODE_MISSING",`结局“${title}”要求不存在节点 ${nodeId}。`,{endingId:ending.id,nodeId}));
    for(const requirement of ending.requiredClockStates)if(!clocks.has(requirement.clockId))issues.push(caseIntegrityIssue("ERROR","ENDING_GATE_CLOCK_MISSING",`结局“${title}”要求不存在威胁时钟 ${requirement.clockId}。`,{endingId:ending.id,clockId:requirement.clockId}));
    for(const clueId of ending.requiredClueIds)if(!clues.has(clueId))issues.push(caseIntegrityIssue("WARN","ENDING_GATE_CLUE_SOURCE_UNPROVEN",`结局“${title}”要求线索 ${clueId}，静态剧本中未找到；运行时合法动态线索仍可满足。`,{endingId:ending.id,clueId}));
    for(const leadId of ending.requiredResolvedLeadIds)if(!leadIds.has(leadId))issues.push(caseIntegrityIssue("INFO","ENDING_GATE_LEAD_SOURCE_UNPROVEN",`结局“${title}”要求解决调查方向 ${leadId}，静态初始方向中未找到。`,{endingId:ending.id,leadId}));
    for(const questionId of ending.requiredResolvedQuestionIds)if(!questionIds.has(questionId))issues.push(caseIntegrityIssue("INFO","ENDING_GATE_QUESTION_SOURCE_UNPROVEN",`结局“${title}”要求解决问题 ${questionId}，静态初始问题中未找到。`,{endingId:ending.id,questionId}));
  }
  const counts=caseIntegritySummary(issues);return{...report,issues,counts,blocking:counts.errors>0}
};

const __endingGateBuildRequestPayload=buildRequestPayload;
buildRequestPayload=function(stage,requestId,baseRevision,extra={}){const payload=__endingGateBuildRequestPayload(stage,requestId,baseRevision,extra);payload.endingResolutionGate=endingGateContext();return payload};
const __endingGateBuildSystemPrompt=buildSystemPrompt;
buildSystemPrompt=function(){return __endingGateBuildSystemPrompt()+`\n23. 结局只可由 endingProposal 提议，最终是否可收束由页面的 Ending / Resolution Gate 根据 canonical state 决定。不得因为叙事感觉“差不多结束了”就宣布案件结束；gate 未满足时继续正常互动。`};
const __endingGateBuildUserPrompt=buildUserPrompt;
buildUserPrompt=function(payload){return __endingGateBuildUserPrompt(payload)+`\n结局门禁：${JSON.stringify(payload.endingResolutionGate||endingGateContext())}\n只有 ready=true 的 endingId 才可作为 endingProposal；没有 ready 结局时 endingProposal 必须为 null。`};
const __endingGateBuildDiagnosticPackage=buildDiagnosticPackage;
buildDiagnosticPackage=function(options={}){const pkg=__endingGateBuildDiagnosticPackage(options);pkg.endingResolutionGate={...endingGateContext(),runtime:deepClone(ensureEndingGateRuntime())};return pkg};
const __endingGateSanitizeRuntimeAfterLoad=sanitizeRuntimeAfterLoad;
sanitizeRuntimeAfterLoad=function(){const result=__endingGateSanitizeRuntimeAfterLoad();ensureEndingGateRuntime();return result};
ensureEndingGateRuntime();
