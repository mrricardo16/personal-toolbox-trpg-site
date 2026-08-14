/* v1.5.10 Authored Threat Clock：剧本作者定义条件，浏览器决定推进；AI 不直接拥有 authored clock authority。 */
const AUTHORED_THREAT_CLOCK_VERSION="1.0";
const AUTHORED_CLOCK_RULE_EVENTS=Object.freeze(["stall","semantic","flag","clue","node","tension","turn"]);
const AUTHORED_CLOCK_MAX_RULES=24;
const AUTHORED_CLOCK_MAX_ADVANCE_PER_EVALUATION=3;
const AUTHORED_CLOCK_NON_MODEL_SEMANTIC_SOURCES=new Set(["authored_threat_clock","authored_threat_clock_resolution","legacy_pacing"]);

function authoredClockList(director=state.campaign?.directorState){return Array.isArray(director?.clocks)?director.clocks.filter(clock=>clock?.authored===true):[]}
function authoredClockById(clockId,director=state.campaign?.directorState){const id=asString(clockId,120).trim();return authoredClockList(director).find(clock=>clock.id===id)||null}
function normalizeAuthoredClockRule(raw,index=0,{mode="advance"}={}){
  const source=isPlainObject(raw)?raw:{},event=AUTHORED_CLOCK_RULE_EVENTS.includes(source.event)?source.event:"",kinds=(Array.isArray(source.kinds)?source.kinds:[source.kind]).filter(kind=>PROGRESS_SEMANTIC_TYPES.includes(kind)),nodeIds=listStrings(source.nodeIds?.length?source.nodeIds:(source.nodeId?[source.nodeId]:[]),30,120);
  return{id:asString(source.id,120).trim()||`${mode}-rule-${index+1}`,event,amount:mode==="advance"?clamp(Number(source.amount||1),1,10):0,once:source.once!==false,cooldownTurns:clamp(Number(source.cooldownTurns||0),0,100),atLeast:clamp(Number(source.atLeast??source.turns??1),1,1000),kinds,flag:asString(source.flag,120).trim(),equals:source.equals===undefined?true:deepClone(source.equals),clueId:asString(source.clueId,120).trim(),nodeIds}
}
function normalizeAuthoredClockState(raw){const source=isPlainObject(raw)?raw:{};return{ruleFires:isPlainObject(source.ruleFires)?deepClone(source.ruleFires):{},lastSemanticEventId:asString(source.lastSemanticEventId,160)||null,lastEvaluationTurn:Number(source.lastEvaluationTurn??-1)}}
const __authoredBaseNormalizeThreatClock=normalizeThreatClock;
normalizeThreatClock=function(raw,index=0){
  const source=isPlainObject(raw)?raw:{},base=__authoredBaseNormalizeThreatClock(raw,index),authored=source.authored===true||Array.isArray(source.advanceRules)||Array.isArray(source.resolveRules);
  if(!authored)return base;
  return{...base,authored:true,authority:"browser_authored_rules",maxAdvancePerEvaluation:clamp(Number(source.maxAdvancePerEvaluation||1),1,AUTHORED_CLOCK_MAX_ADVANCE_PER_EVALUATION),advanceRules:(Array.isArray(source.advanceRules)?source.advanceRules:[]).slice(0,AUTHORED_CLOCK_MAX_RULES).map((rule,i)=>normalizeAuthoredClockRule(rule,i,{mode:"advance"})),resolveRules:(Array.isArray(source.resolveRules)?source.resolveRules:[]).slice(0,AUTHORED_CLOCK_MAX_RULES).map((rule,i)=>normalizeAuthoredClockRule(rule,i,{mode:"resolve"})),authoredState:normalizeAuthoredClockState(source.authoredState)}
};

function authoredClockScenarioIds(scenario){const nodes=new Set(),clues=new Set();for(const node of allScenarioNodes(scenario)){if(node?.id)nodes.add(node.id);for(const clue of node?.clues||[])if(clue?.id)clues.add(clue.id)}return{nodes,clues}}
function validateAuthoredClockRule(raw,{mode,clockId,index,ids}){
  const errors=[],label=`威胁时钟 ${clockId} 的 ${mode==="advance"?"推进":"解决"}规则 #${index+1}`;if(!isPlainObject(raw)){errors.push(`${label} 必须是对象`);return errors}const event=asString(raw.event,40).trim();if(!AUTHORED_CLOCK_RULE_EVENTS.includes(event))errors.push(`${label} event 无效：${event||"未提供"}`);
  if(mode==="advance"&&(!Number.isFinite(Number(raw.amount??1))||Number(raw.amount??1)<=0))errors.push(`${label} amount 必须为正数`);
  if(["stall","tension","turn"].includes(event)&&(!Number.isFinite(Number(raw.atLeast??raw.turns))||Number(raw.atLeast??raw.turns)<1))errors.push(`${label} 缺少有效 atLeast/turns`);
  if(event==="semantic"){const kinds=(Array.isArray(raw.kinds)?raw.kinds:[raw.kind]).filter(Boolean);if(!kinds.length||kinds.some(kind=>!PROGRESS_SEMANTIC_TYPES.includes(kind)))errors.push(`${label} 必须提供有效 semantic kinds`)}
  if(event==="flag"&&!asString(raw.flag,120).trim())errors.push(`${label} 缺少 flag`);
  if(event==="clue"){const clueId=asString(raw.clueId,120).trim();if(!clueId)errors.push(`${label} 缺少 clueId`);else if(!ids.clues.has(clueId))errors.push(`${label} 引用了不存在的 clueId：${clueId}`)}
  if(event==="node"){const nodeIds=(Array.isArray(raw.nodeIds)?raw.nodeIds:(raw.nodeId?[raw.nodeId]:[])).map(x=>asString(x,120).trim()).filter(Boolean);if(!nodeIds.length)errors.push(`${label} 缺少 nodeId/nodeIds`);for(const nodeId of nodeIds)if(!ids.nodes.has(nodeId))errors.push(`${label} 引用了不存在的 nodeId：${nodeId}`)}
  return errors
}
function validateAuthoredThreatClockDefinitions(scenario){
  const clocks=scenario?.director?.threatClocks;if(clocks===undefined)return[];if(!Array.isArray(clocks))return["director.threatClocks 必须是数组"];const errors=[],ids=authoredClockScenarioIds(scenario),clockIds=new Set();
  clocks.forEach((raw,index)=>{if(!isPlainObject(raw)){errors.push(`威胁时钟 #${index+1} 必须是对象`);return}const id=asString(raw.id,120).trim();if(!id)errors.push(`威胁时钟 #${index+1} 缺少 id`);else if(clockIds.has(id))errors.push(`威胁时钟 ID 重复：${id}`);else clockIds.add(id);const authored=raw.authored===true||Array.isArray(raw.advanceRules)||Array.isArray(raw.resolveRules);if(!authored)return;if(!Number.isFinite(Number(raw.max))||Number(raw.max)<1||Number(raw.max)>20)errors.push(`威胁时钟 ${id||index+1} 的 max 必须在 1..20`);const advance=Array.isArray(raw.advanceRules)?raw.advanceRules:[],resolve=Array.isArray(raw.resolveRules)?raw.resolveRules:[];if(!advance.length&&!resolve.length)errors.push(`authored 威胁时钟 ${id||index+1} 至少需要一条 advanceRules 或 resolveRules`);if(advance.length>AUTHORED_CLOCK_MAX_RULES||resolve.length>AUTHORED_CLOCK_MAX_RULES)errors.push(`威胁时钟 ${id||index+1} 的规则数量超过 ${AUTHORED_CLOCK_MAX_RULES}`);const ruleIds=new Set();for(const [mode,rules] of [["advance",advance],["resolve",resolve]])rules.forEach((rule,ruleIndex)=>{const rid=asString(rule?.id,120).trim()||`${mode}-rule-${ruleIndex+1}`;if(ruleIds.has(rid))errors.push(`威胁时钟 ${id||index+1} 的规则 ID 重复：${rid}`);else ruleIds.add(rid);errors.push(...validateAuthoredClockRule(rule,{mode,clockId:id||String(index+1),index:ruleIndex,ids}))})});return errors
}

function authoredClockLatestExternalSemantic(){const history=ensureProgressSemanticsState()?.history||[];for(let i=history.length-1;i>=0;i--){const event=history[i];if(event?.id&&!AUTHORED_CLOCK_NON_MODEL_SEMANTIC_SOURCES.has(event.source))return event}return null}
function authoredClockRuleState(clock,rule){clock.authoredState=normalizeAuthoredClockState(clock.authoredState);const existing=clock.authoredState.ruleFires[rule.id];return isPlainObject(existing)?existing:{count:0,lastTurn:-1,lastSemanticEventId:null}}
function authoredClockRuleCanFire(clock,rule,semantic){const status=authoredClockRuleState(clock,rule),turn=Number(state.campaign?.directorState?.totalTurns||0);if(rule.once&&Number(status.count||0)>0)return false;if(rule.cooldownTurns>0&&Number(status.lastTurn??-1)>=0&&turn-Number(status.lastTurn)<rule.cooldownTurns)return false;if(rule.event==="semantic"&&semantic?.id&&status.lastSemanticEventId===semantic.id)return false;return true}
function authoredClockRuleMatches(rule,{semantic,stalled,turn,tension}){
  if(rule.event==="stall")return stalled>=rule.atLeast;
  if(rule.event==="semantic")return Boolean(semantic&&rule.kinds.some(kind=>semantic.kinds?.includes(kind)));
  if(rule.event==="flag")return progressSemanticEqual(state.campaign?.flags?.[rule.flag],rule.equals);
  if(rule.event==="clue")return state.clues.some(clue=>clue.id===rule.clueId&&clue.revealed!==false);
  if(rule.event==="node")return rule.nodeIds.includes(state.campaign?.currentNodeId);
  if(rule.event==="tension")return tension>=rule.atLeast;
  if(rule.event==="turn")return turn>=rule.atLeast;
  return false
}
function markAuthoredClockRuleFire(clock,rule,semantic){clock.authoredState=normalizeAuthoredClockState(clock.authoredState);const turn=Number(state.campaign?.directorState?.totalTurns||0),status=authoredClockRuleState(clock,rule);clock.authoredState.ruleFires[rule.id]={count:Number(status.count||0)+1,lastTurn:turn,lastSemanticEventId:rule.event==="semantic"?(semantic?.id||null):(status.lastSemanticEventId||null)}}
function authoredClockEvaluationContext(){const director=state.campaign.directorState,semantic=authoredClockLatestExternalSemantic();return{semantic,stalled:Math.max(0,Number(director.sceneTurns||0)-Number(director.lastProgressTurn||0)),turn:Number(director.totalTurns||0),tension:Number(director.tension||0)}}
function evaluateAuthoredThreatClocks(){
  const director=state.campaign.directorState={...defaultDirectorState(),...(state.campaign.directorState||{})};normalizeDirectorClocks(director);const clockIds=authoredClockList(director).map(clock=>clock.id);if(!clockIds.length)return{changed:false,events:[]};const before=progressSemanticSnapshot(),context=authoredClockEvaluationContext(),events=[];
  if(Number(director.authoredClockLastEvaluationTurn??-1)===context.turn)return{changed:false,events:[],skipped:"already_evaluated"};director.authoredClockLastEvaluationTurn=context.turn;
  for(const clockId of clockIds){let clock=authoredClockById(clockId,director);if(!clock)continue;clock.authoredState=normalizeAuthoredClockState(clock.authoredState);const semantic=context.semantic,semanticForClock=semantic?.id&&clock.authoredState.lastSemanticEventId===semantic.id?null:semantic,ruleContext={...context,semantic:semanticForClock};
    if(!clock.resolved){const resolution=clock.resolveRules.find(rule=>authoredClockRuleCanFire(clock,rule,ruleContext.semantic)&&authoredClockRuleMatches(rule,ruleContext));if(resolution){const wasResolved=clock.resolved,resolvedClock=resolveThreatClockInDirector(director,clock.id);clock=resolvedClock;markAuthoredClockRuleFire(clock,resolution,ruleContext.semantic);if(!wasResolved)events.push({clockId:clock.id,kind:"resolved",ruleId:resolution.id,description:`威胁时钟“${clock.name}”按剧本规则解除`})}}
    clock=authoredClockById(clockId,director)||clock;
    if(!clock.resolved&&!clock.triggered&&clock.active!==false){let budget=clamp(Number(clock.maxAdvancePerEvaluation||1),1,AUTHORED_CLOCK_MAX_ADVANCE_PER_EVALUATION);for(const rule of clock.advanceRules){if(budget<=0)break;clock=authoredClockById(clockId,director)||clock;if(!authoredClockRuleCanFire(clock,rule,ruleContext.semantic)||!authoredClockRuleMatches(rule,ruleContext))continue;const amount=Math.min(budget,clamp(Number(rule.amount||1),1,10)),result=advanceThreatClockInDirector(director,{clockId:clock.id,amount,reason:`authored:${rule.id}`});clock=result.clock;budget-=amount;markAuthoredClockRuleFire(clock,rule,ruleContext.semantic);events.push({clockId:clock.id,kind:result.triggeredNow?"triggered":"advanced",ruleId:rule.id,amount,before:result.before,after:result.clock.current,description:result.description});if(result.triggeredNow)break}}
    clock=authoredClockById(clockId,director)||clock;if(semantic?.id)clock.authoredState.lastSemanticEventId=semantic.id;clock.authoredState.lastEvaluationTurn=context.turn
  }
  if(!events.length)return{changed:false,events:[]};const triggered=events.find(event=>event.kind==="triggered"),resolved=events.find(event=>event.kind==="resolved"),event={id:uid("director-event"),kind:triggered?"clock_triggered":resolved?"authored_clock_resolved":"authored_clock_advanced",turn:context.turn,reason:events.map(item=>`${item.clockId}:${item.ruleId}`).join(", "),events:deepClone(events)};director.pendingPressure=event;state.runtime.pendingDirectorEvent=event;bumpRevision();const after=progressSemanticSnapshot(),semanticEvent=recordProgressSemantics(before,after,{source:"authored_threat_clock",requestId:null,recordNone:false});addLog("director",`Authored Threat Clock：${events.map(item=>item.description).join("；")}`,{secret:true});return{changed:true,events,semantic:semanticEvent}
}


function evaluateAuthoredThreatClockResolutions({source="post_commit"}={}){
  const director=state.campaign?.directorState;if(!director)return{changed:false,events:[]};normalizeDirectorClocks(director);const clockIds=authoredClockList(director).map(clock=>clock.id);if(!clockIds.length)return{changed:false,events:[]};const before=progressSemanticSnapshot(),context=authoredClockEvaluationContext(),events=[];
  for(const clockId of clockIds){let clock=authoredClockById(clockId,director);if(!clock||clock.resolved)continue;clock.authoredState=normalizeAuthoredClockState(clock.authoredState);const semantic=context.semantic,semanticForClock=semantic?.id&&clock.authoredState.lastSemanticEventId===semantic.id?null:semantic,ruleContext={...context,semantic:semanticForClock};const resolution=clock.resolveRules.find(rule=>authoredClockRuleCanFire(clock,rule,ruleContext.semantic)&&authoredClockRuleMatches(rule,ruleContext));if(!resolution)continue;clock=resolveThreatClockInDirector(director,clock.id);markAuthoredClockRuleFire(clock,resolution,ruleContext.semantic);events.push({clockId:clock.id,kind:"resolved",ruleId:resolution.id,description:`威胁时钟“${clock.name}”按剧本规则解除`})
  }
  if(!events.length)return{changed:false,events:[]};const event={id:uid("director-event"),kind:"authored_clock_resolved",turn:context.turn,reason:events.map(item=>`${item.clockId}:${item.ruleId}`).join(", "),events:deepClone(events),source};director.pendingPressure=event;state.runtime.pendingDirectorEvent=event;bumpRevision();const semanticEvent=recordProgressSemantics(before,progressSemanticSnapshot(),{source:"authored_threat_clock_resolution",requestId:null,recordNone:false});addLog("director",`Authored Threat Clock post-commit：${events.map(item=>item.description).join("；")}`,{secret:true});return{changed:true,events,semantic:semanticEvent}
}

const __authoredBaseEnterNode=enterNode;
enterNode=function(...args){const result=__authoredBaseEnterNode(...args);evaluateAuthoredThreatClockResolutions({source:"node_transition"});return result};
const __authoredBaseCommitAiTransaction=commitAiTransaction;
commitAiTransaction=function(...args){const result=__authoredBaseCommitAiTransaction(...args);evaluateAuthoredThreatClockResolutions({source:"ai_commit"});return result};
const __authoredBaseApplySecretCheckOutcome=applySecretCheckOutcome;
applySecretCheckOutcome=function(...args){const result=__authoredBaseApplySecretCheckOutcome(...args);evaluateAuthoredThreatClockResolutions({source:"secret_check"});return result};

const __authoredBaseApplyDeterministicPacingBeforeAction=applyDeterministicPacingBeforeAction;
applyDeterministicPacingBeforeAction=function(){
  const director=state.campaign?.directorState;if(!authoredClockList(director).length){const before=progressSemanticSnapshot(),result=__authoredBaseApplyDeterministicPacingBeforeAction(),after=progressSemanticSnapshot();recordProgressSemantics(before,after,{source:"legacy_pacing",requestId:null,recordNone:false});return result}
  return evaluateAuthoredThreatClocks()
};

const __authoredBasePrepareStateChanges=prepareStateChanges;
prepareStateChanges=function(changes,campaignChanges=[],validationContext={}){
  if(!Array.isArray(campaignChanges))return __authoredBasePrepareStateChanges(changes,campaignChanges,validationContext);const blocked=[],filtered=campaignChanges.filter(change=>{const op=change?.operation,id=asString(change?.clockId??change?.id,120).trim();if(["advanceClock","resolveClock"].includes(op)&&id&&authoredClockById(id)){blocked.push({operation:op,clockId:id});return false}return true});const prepared=__authoredBasePrepareStateChanges(changes,filtered,validationContext);if(blocked.length){prepared.authoredClockBlockedOps=deepClone(blocked);addLog("authored_clock_guard",`忽略 ${blocked.length} 项 AI authored clock 越权提议`,{secret:true})}return prepared
};

const __authoredBaseActivateScenario=activateScenario;
activateScenario=function(inputScenario){const errors=validateAuthoredThreatClockDefinitions(inputScenario);if(errors.length)throw new Error(`Authored Threat Clock 配置无效：${errors.join("；")}`);const result=__authoredBaseActivateScenario(inputScenario);normalizeDirectorClocks(state.campaign.directorState);state.runtime.authoredThreatClock={version:AUTHORED_THREAT_CLOCK_VERSION,lastEvaluation:null};return result};
const __authoredBaseSanitizeRuntimeAfterLoad=sanitizeRuntimeAfterLoad;
sanitizeRuntimeAfterLoad=function(...args){const result=__authoredBaseSanitizeRuntimeAfterLoad(...args);normalizeDirectorClocks(state.campaign.directorState);state.runtime.authoredThreatClock={version:AUTHORED_THREAT_CLOCK_VERSION,lastEvaluation:null};return result};

const __authoredBaseBuildDiagnosticPackage=buildDiagnosticPackage;
buildDiagnosticPackage=function(options={}){const doc=__authoredBaseBuildDiagnosticPackage(options);return{...doc,authoredThreatClock:{version:AUTHORED_THREAT_CLOCK_VERSION,clocks:deepClone(authoredClockList()),pendingDirectorEvent:deepClone(state.runtime?.pendingDirectorEvent||null)}}};