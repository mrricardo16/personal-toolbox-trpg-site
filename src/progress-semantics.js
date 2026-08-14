/* v1.5.9 Progress Semantics：只描述已提交 canonical consequences，不授予 AI 新状态权限。 */
const PROGRESS_SEMANTICS_VERSION="1.0";
const PROGRESS_SEMANTIC_TYPES=Object.freeze(["NONE","DISCOVERY","ACCESS","SOCIAL","THREAT","RESOLUTION"]);
const PROGRESS_SEMANTIC_HISTORY_LIMIT=80;
const PROGRESS_SEMANTIC_PRIORITY=Object.freeze(["RESOLUTION","THREAT","DISCOVERY","ACCESS","SOCIAL"]);

function defaultProgressSemanticsState(){return{version:PROGRESS_SEMANTICS_VERSION,last:null,history:[]}}
function ensureProgressSemanticsState(campaign=state.campaign){
  if(!campaign)return defaultProgressSemanticsState();
  const current=isPlainObject(campaign.progressSemantics)?campaign.progressSemantics:{};
  const history=Array.isArray(current.history)?current.history.filter(item=>isPlainObject(item)).slice(-PROGRESS_SEMANTIC_HISTORY_LIMIT):[];
  campaign.progressSemantics={version:PROGRESS_SEMANTICS_VERSION,last:isPlainObject(current.last)?current.last:null,history};
  return campaign.progressSemantics
}
function progressSemanticObjectMap(items,project){
  const out={};for(const item of Array.isArray(items)?items:[]){const id=asString(item?.id,160);if(!id)continue;out[id]=project(item)}return out
}
function progressSemanticStable(value){if(Array.isArray(value))return value.map(progressSemanticStable);if(!isPlainObject(value))return value;const out={};for(const key of Object.keys(value).sort())out[key]=progressSemanticStable(value[key]);return out}
function progressSemanticEqual(a,b){return JSON.stringify(progressSemanticStable(a))===JSON.stringify(progressSemanticStable(b))}
function progressSemanticSnapshot(){
  const campaign=state.campaign||{},director=campaign.directorState||{},character=state.character||{};
  return{
    nodeId:campaign.currentNodeId||null,location:campaign.currentLocation||"",
    clues:progressSemanticObjectMap(state.clues,item=>({revealed:item.revealed!==false,name:asString(item.name,240),description:asString(item.description,1000),playerDescription:asString(item.playerDescription,1000),discoveryQuality:item.discoveryQuality||null,insight:deepClone(item.insight??null)})),
    items:progressSemanticObjectMap(state.items,item=>({name:asString(item.name,240),quantity:Number(item.quantity??1)})),
    npcs:progressSemanticObjectMap(state.npcs,item=>{const continuity=isPlainObject(item.continuity)?item.continuity:{};return{name:asString(item.name,240),attitude:asString(item.attitude,240),description:asString(item.description,1200),claims:Array.isArray(continuity.claims)?deepClone(continuity.claims):[],relationship:deepClone(continuity.relationship??null),currentIntent:deepClone(continuity.currentIntent??null)}}),
    leads:progressSemanticObjectMap(campaign.activeLeads,item=>({status:item.status||null,text:asString(item.text,600)})),
    questions:progressSemanticObjectMap(campaign.unresolvedQuestions,item=>({status:item.status||null,text:asString(item.text,600)})),
    revealedTruths:deepClone(Array.isArray(director.revealedTruths)?director.revealedTruths:[]),
    activeThreats:deepClone(Array.isArray(director.activeThreats)?director.activeThreats:[]),
    tension:Number(director.tension||0),
    clocks:progressSemanticObjectMap(director.clocks,item=>({name:asString(item.name,240),current:Number(item.current||0),max:Number(item.max||0),active:item.active!==false,triggered:Boolean(item.triggered),resolved:Boolean(item.resolved)})),
    outcomes:deepClone(campaign.outcomes||{}),ending:campaign.ending?{id:campaign.ending.id||null,title:campaign.ending.title||null}:null,
    hp:Number(character.hp??0),san:Number(character.san??0)
  }
}
function progressSemanticMapChanged(beforeMap,afterMap){const ids=new Set([...Object.keys(beforeMap||{}),...Object.keys(afterMap||{})]);const changed=[];for(const id of ids)if(!progressSemanticEqual(beforeMap?.[id],afterMap?.[id]))changed.push(id);return changed}
function progressSemanticMapAdded(beforeMap,afterMap){return Object.keys(afterMap||{}).filter(id=>!Object.prototype.hasOwnProperty.call(beforeMap||{},id))}
function progressSemanticStatusResolved(beforeMap,afterMap){const resolved=[];for(const [id,after] of Object.entries(afterMap||{})){const before=beforeMap?.[id];if(!before)continue;const wasResolved=["resolved","closed","done"].includes(String(before.status||"").toLowerCase()),isResolved=["resolved","closed","done"].includes(String(after.status||"").toLowerCase());if(!wasResolved&&isResolved)resolved.push(id)}return resolved}
function deriveProgressSemantics(before,after,{source="unknown",requestId=null}={}){
  const kinds=new Set(),evidence=[];const add=(kind,code,detail={})=>{kinds.add(kind);evidence.push({kind,code,...deepClone(detail)})};
  const clueAdded=progressSemanticMapAdded(before.clues,after.clues),clueChanged=progressSemanticMapChanged(before.clues,after.clues).filter(id=>!clueAdded.includes(id));
  if(clueAdded.length)add("DISCOVERY","clue_added",{ids:clueAdded});if(clueChanged.length)add("DISCOVERY","clue_changed",{ids:clueChanged});
  if(!progressSemanticEqual(before.revealedTruths,after.revealedTruths)&&after.revealedTruths.length>=before.revealedTruths.length)add("DISCOVERY","truth_revealed",{before:before.revealedTruths.length,after:after.revealedTruths.length});
  const resolvedLeads=progressSemanticStatusResolved(before.leads,after.leads),resolvedQuestions=progressSemanticStatusResolved(before.questions,after.questions);if(resolvedLeads.length)add("DISCOVERY","lead_resolved",{ids:resolvedLeads});if(resolvedQuestions.length)add("DISCOVERY","question_resolved",{ids:resolvedQuestions});

  if(before.nodeId!==after.nodeId)add("ACCESS","node_changed",{from:before.nodeId,to:after.nodeId});
  const itemAdded=progressSemanticMapAdded(before.items,after.items),itemIncreased=[];for(const [id,item] of Object.entries(after.items||{})){const prior=before.items?.[id];if(prior&&Number(item.quantity||0)>Number(prior.quantity||0))itemIncreased.push(id)}if(itemAdded.length)add("ACCESS","item_acquired",{ids:itemAdded});if(itemIncreased.length)add("ACCESS","item_quantity_increased",{ids:itemIncreased});

  const npcChanged=progressSemanticMapChanged(before.npcs,after.npcs);if(npcChanged.length)add("SOCIAL","npc_canonical_changed",{ids:npcChanged});

  if(after.tension>before.tension)add("THREAT","tension_increased",{before:before.tension,after:after.tension});
  if(after.hp<before.hp)add("THREAT","hp_decreased",{before:before.hp,after:after.hp});if(after.san<before.san)add("THREAT","san_decreased",{before:before.san,after:after.san});
  const beforeThreatCount=Array.isArray(before.activeThreats)?before.activeThreats.length:0,afterThreatCount=Array.isArray(after.activeThreats)?after.activeThreats.length:0;if(afterThreatCount>beforeThreatCount)add("THREAT","active_threat_added",{before:beforeThreatCount,after:afterThreatCount});
  for(const [id,clock] of Object.entries(after.clocks||{})){const prior=before.clocks?.[id];if(!prior)continue;if(Number(clock.current||0)>Number(prior.current||0))add("THREAT","clock_advanced",{id,before:prior.current,after:clock.current});if(!prior.triggered&&clock.triggered)add("THREAT","clock_triggered",{id})}

  if(!progressSemanticEqual(before.outcomes,after.outcomes))add("RESOLUTION","outcome_changed",{});if(!before.ending&&after.ending)add("RESOLUTION","ending_committed",{id:after.ending.id});
  if(afterThreatCount<beforeThreatCount)add("RESOLUTION","active_threat_removed",{before:beforeThreatCount,after:afterThreatCount});for(const [id,clock] of Object.entries(after.clocks||{})){const prior=before.clocks?.[id];if(prior&&!prior.resolved&&clock.resolved)add("RESOLUTION","clock_resolved",{id})}

  const ordered=PROGRESS_SEMANTIC_TYPES.filter(type=>kinds.has(type)),primary=PROGRESS_SEMANTIC_PRIORITY.find(type=>kinds.has(type))||"NONE";if(!ordered.length)ordered.push("NONE");
  return{version:PROGRESS_SEMANTICS_VERSION,primary,kinds:ordered,evidence:evidence.slice(0,24),source,requestId:requestId||null}
}
function recordProgressSemantics(before,after,{source="unknown",requestId=null,recordNone=true}={}){
  const semantic=deriveProgressSemantics(before,after,{source,requestId});if(semantic.primary==="NONE"&&!recordNone)return null;const store=ensureProgressSemanticsState();const director=state.campaign?.directorState||{};const event={id:uid("semantic"),...semantic,turn:Number(director.totalTurns||0),at:nowIso()};store.last=deepClone(event);store.history.push(deepClone(event));if(store.history.length>PROGRESS_SEMANTIC_HISTORY_LIMIT)store.history.splice(0,store.history.length-PROGRESS_SEMANTIC_HISTORY_LIMIT);state.runtime.lastProgressSemantic=deepClone(event);addLog("progress_semantics",`Canonical consequence：${event.primary}${event.kinds.length>1?` [${event.kinds.join(",")}]`:""}`,{requestId,secret:true});return event
}
function progressSemanticsPublicContext(){const store=ensureProgressSemanticsState();return{version:store.version,last:store.last?{primary:store.last.primary,kinds:deepClone(store.last.kinds),evidence:deepClone(store.last.evidence),turn:store.last.turn,source:store.last.source}:null,recent:store.history.slice(-8).map(item=>({primary:item.primary,kinds:deepClone(item.kinds),turn:item.turn,source:item.source}))}}

const __progressActivateScenario=activateScenario;
activateScenario=function(...args){const result=__progressActivateScenario(...args);state.campaign.progressSemantics=defaultProgressSemanticsState();state.runtime.lastProgressSemantic=null;return result};
const __progressSanitizeRuntimeAfterLoad=sanitizeRuntimeAfterLoad;
sanitizeRuntimeAfterLoad=function(...args){const result=__progressSanitizeRuntimeAfterLoad(...args);ensureProgressSemanticsState();state.runtime.lastProgressSemantic=deepClone(state.campaign.progressSemantics.last);return result};

const __progressCommitAiTransaction=commitAiTransaction;
commitAiTransaction=function(transaction,requestId){const before=progressSemanticSnapshot(),result=__progressCommitAiTransaction(transaction,requestId),after=progressSemanticSnapshot(),semantic=recordProgressSemantics(before,after,{source:"ai_transaction",requestId,recordNone:true});if(result&&typeof result==="object")result.progressSemantic=deepClone(semantic);return result};
const __progressCommitPreparedChanges=commitPreparedChanges;
commitPreparedChanges=function(prepared,requestId){const before=progressSemanticSnapshot(),result=__progressCommitPreparedChanges(prepared,requestId),after=progressSemanticSnapshot();recordProgressSemantics(before,after,{source:"prepared_changes",requestId,recordNone:false});return result};
const __progressApplySecretCheckOutcome=applySecretCheckOutcome;
applySecretCheckOutcome=function(check,record){const before=progressSemanticSnapshot(),result=__progressApplySecretCheckOutcome(check,record),after=progressSemanticSnapshot();recordProgressSemantics(before,after,{source:"secret_check",requestId:check?.requestId||record?.requestId||null,recordNone:false});return result};
const __progressEnterNode=enterNode;
enterNode=function(nodeId,options={}){const before=progressSemanticSnapshot(),result=__progressEnterNode(nodeId,options),after=progressSemanticSnapshot();recordProgressSemantics(before,after,{source:"node_transition",requestId:null,recordNone:false});return result};
const __progressApplyEnding=applyEnding;
applyEnding=function(ending,reason=""){const before=progressSemanticSnapshot(),result=__progressApplyEnding(ending,reason),after=progressSemanticSnapshot();recordProgressSemantics(before,after,{source:"ending",requestId:null,recordNone:false});return result};

const __progressSafeContextCoreState=safeContextCoreState;
safeContextCoreState=function(){const base=__progressSafeContextCoreState();return{...base,progressSemantics:progressSemanticsPublicContext()}};
const __progressWorldContinuityContext=worldContinuityContext;
worldContinuityContext=function(options={}){const base=__progressWorldContinuityContext(options);return{...base,progressSemantics:progressSemanticsPublicContext()}};
const __progressBuildDiagnosticPackage=buildDiagnosticPackage;
buildDiagnosticPackage=function(options={}){const doc=__progressBuildDiagnosticPackage(options);return{...doc,progressSemantics:deepClone(ensureProgressSemanticsState())}};
