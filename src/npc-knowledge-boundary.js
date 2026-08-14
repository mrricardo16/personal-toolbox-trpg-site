/* v1.5.11 NPC Knowledge Boundary：NPC 只能依据作者声明与浏览器确认的知识来源发言。 */
const NPC_KNOWLEDGE_BOUNDARY_VERSION="1.0";
const NPC_KNOWLEDGE_MAX_FACTS=80;
const NPC_KNOWLEDGE_MAX_ALIASES=12;
const NPC_KNOWLEDGE_MAX_KNOWN_IDS=80;
const NPC_KNOWLEDGE_DISCLOSURE_ACTION=/(?:告诉|说明|解释|出示|展示|转述|读给|递给|交给|给.{0,12}看|让.{0,12}看)/u;
const NPC_KNOWLEDGE_NEGATION=/(?:不知道|不清楚|不了解|没听说|没有听说|从未听说|无法确认|不能确认|并不知情|并不知道|否认知道|不记得|想不起来)/u;
const NPC_KNOWLEDGE_SPEECH=/(?:说|说道|告诉|承认|坦白|交代|解释|指出|透露|证实|表示|回答|低声|告诉你|记得|知道|见过|听说)/u;

function npcKnowledgeUniqueIds(values,limit=NPC_KNOWLEDGE_MAX_KNOWN_IDS){const result=[];for(const value of Array.isArray(values)?values:[]){const id=asString(value,120).trim();if(id&&!result.includes(id))result.push(id);if(result.length>=limit)break}return result}
function npcKnowledgeNormalizeText(value){return String(value||"").toLowerCase().replace(/[\s，。！？；：、,.!?;:\-—_（）()【】“”"'《》〈〉]/gu,"")}
function normalizeNpcKnowledgeFact(raw,index=0){const source=isPlainObject(raw)?raw:{},text=asString(source.text,1000).trim(),aliases=listStrings(source.aliases,NPC_KNOWLEDGE_MAX_ALIASES,200),terms=[];for(const term of [text,...aliases]){const clean=asString(term,200).trim();if(clean&&npcKnowledgeNormalizeText(clean).length>=2&&!terms.includes(clean))terms.push(clean)}return{id:asString(source.id,120).trim()||`knowledge-fact-${index+1}`,text,aliases:terms.slice(0,NPC_KNOWLEDGE_MAX_ALIASES),knownBy:npcKnowledgeUniqueIds(source.knownBy,40),learnableFromClueIds:npcKnowledgeUniqueIds(source.learnableFromClueIds,30)}}
function npcKnowledgeFacts(scenario=state.scenario){const raw=scenario?.director?.knowledgeFacts;return Array.isArray(raw)?raw.map(normalizeNpcKnowledgeFact).slice(0,NPC_KNOWLEDGE_MAX_FACTS):[]}
function npcKnowledgeFactById(factId,scenario=state.scenario){const id=asString(factId,120).trim();return npcKnowledgeFacts(scenario).find(fact=>fact.id===id)||null}
function npcKnowledgeAllNpcIds(scenario){const ids=new Set();for(const node of allScenarioNodes(scenario))for(const npc of node.npcs||[])if(npc?.id)ids.add(npc.id);return ids}
function validateNpcKnowledgeDefinitions(scenario){const raw=scenario?.director?.knowledgeFacts;if(raw===undefined)return[];if(!Array.isArray(raw))return["director.knowledgeFacts 必须是数组"];const errors=[];if(raw.length>NPC_KNOWLEDGE_MAX_FACTS)errors.push(`director.knowledgeFacts 超过 ${NPC_KNOWLEDGE_MAX_FACTS} 条上限`);const npcIds=npcKnowledgeAllNpcIds(scenario),clueIds=new Set(allScenarioNodes(scenario).flatMap(node=>(node.clues||[]).map(clue=>clue.id).filter(Boolean))),factIds=new Set(),termOwners=new Map();raw.slice(0,NPC_KNOWLEDGE_MAX_FACTS).forEach((item,index)=>{if(!isPlainObject(item)){errors.push(`NPC 知识事实 #${index+1} 必须是对象`);return}const fact=normalizeNpcKnowledgeFact(item,index);if(!asString(item.id,120).trim())errors.push(`NPC 知识事实 #${index+1} 缺少 id`);else if(factIds.has(fact.id))errors.push(`NPC 知识事实 ID 重复：${fact.id}`);else factIds.add(fact.id);if(!fact.text)errors.push(`NPC 知识事实 ${fact.id} 缺少 text`);if(!fact.aliases.length)errors.push(`NPC 知识事实 ${fact.id} 至少需要一个可识别文本/alias`);for(const npcId of fact.knownBy)if(!npcIds.has(npcId))errors.push(`NPC 知识事实 ${fact.id} 的 knownBy 引用了不存在 NPC：${npcId}`);for(const clueId of fact.learnableFromClueIds)if(!clueIds.has(clueId))errors.push(`NPC 知识事实 ${fact.id} 的 learnableFromClueIds 引用了不存在 clue：${clueId}`);for(const term of fact.aliases){const key=npcKnowledgeNormalizeText(term);if(key.length<2)continue;const owner=termOwners.get(key);if(owner&&owner!==fact.id)errors.push(`NPC 知识事实 alias 冲突：${term} 同时属于 ${owner} / ${fact.id}`);else termOwners.set(key,fact.id)}});return errors}


const __npcKnowledgeBaseNormalizeDirectorSituation=normalizeDirectorSituation;
normalizeDirectorSituation=function(raw){
  const source=isPlainObject(raw)?raw:{},normalized=__npcKnowledgeBaseNormalizeDirectorSituation(raw);
  normalized.knowledgeFacts=Array.isArray(source.knowledgeFacts)?source.knowledgeFacts.map(normalizeNpcKnowledgeFact).slice(0,NPC_KNOWLEDGE_MAX_FACTS):[];
  return normalized
};

function npcKnowledgeInitialFactIds(npcId){const id=asString(npcId,120).trim();return npcKnowledgeFacts().filter(fact=>fact.knownBy.includes(id)).map(fact=>fact.id)}
const __npcKnowledgeBaseEnsureNpcContinuity=ensureNpcContinuity;
ensureNpcContinuity=function(npc){const source=isPlainObject(npc?.continuity)?deepClone(npc.continuity):{},continuity=__npcKnowledgeBaseEnsureNpcContinuity(npc);continuity.knownFactIds=npcKnowledgeUniqueIds([...(source.knownFactIds||[]),...npcKnowledgeInitialFactIds(npc?.id)]);continuity.knownClueIds=npcKnowledgeUniqueIds(source.knownClueIds||[]);return continuity};
const __npcKnowledgeBaseApplyNpcContinuityPatch=applyNpcContinuityPatch;
applyNpcContinuityPatch=function(npc,raw={}){const updated=__npcKnowledgeBaseApplyNpcContinuityPatch(npc,raw),continuity=ensureNpcContinuity(updated);if(raw?.__npcKnowledgeValidated===NPC_KNOWLEDGE_BOUNDARY_VERSION){continuity.knownFactIds=npcKnowledgeUniqueIds([...(continuity.knownFactIds||[]),...(raw.knowledgeFactIds||[])]);continuity.knownClueIds=npcKnowledgeUniqueIds([...(continuity.knownClueIds||[]),...(raw.knowledgeClueIds||[])])}return updated};

function npcKnowledgeNpcAliases(npc){const name=asString(npc?.name,160).trim(),aliases=[name];if(name.length>=2){aliases.push(name.slice(0,Math.min(3,name.length)));aliases.push(name.slice(-2))}return [...new Set(aliases.filter(value=>npcKnowledgeNormalizeText(value).length>=2))]}
function npcKnowledgeTextMatchesFact(value,fact){const hay=npcKnowledgeNormalizeText(value);if(!hay)return false;return fact.aliases.some(term=>{const needle=npcKnowledgeNormalizeText(term);return needle.length>=2&&hay.includes(needle)})}
function npcKnowledgeNegativeNearFact(value,fact){const text=String(value||"");for(const term of fact.aliases){const index=text.indexOf(term);if(index<0)continue;const window=text.slice(Math.max(0,index-30),Math.min(text.length,index+term.length+30));if(NPC_KNOWLEDGE_NEGATION.test(window))return true}return false}
function npcKnowledgeUnauthorizedFactsInText(value,npc,additionalFactIds=[]){if(!value)return[];const known=new Set([...(ensureNpcContinuity(npc).knownFactIds||[]),...additionalFactIds]);return npcKnowledgeFacts().filter(fact=>!known.has(fact.id)&&npcKnowledgeTextMatchesFact(value,fact)&&!npcKnowledgeNegativeNearFact(value,fact))}
function npcKnowledgeCurrentAction(){const envelope=state.runtime?.lastContextEnvelope;return asString(envelope?.playerAction??envelope?.playerActionGuard?.originalAction,4000)}
function npcKnowledgeActionTargetsNpc(action,npc){const hay=npcKnowledgeNormalizeText(action);return npcKnowledgeNpcAliases(npc).some(alias=>hay.includes(npcKnowledgeNormalizeText(alias)))}
function npcKnowledgeActionMentionsClue(action,clue){const hay=npcKnowledgeNormalizeText(action);for(const term of [clue?.name,clue?.playerDescription,clue?.description]){const needle=npcKnowledgeNormalizeText(term);if(needle.length>=2&&hay.includes(needle))return true}return false}
function npcKnowledgeCanLearnClue(npc,clueId,action=npcKnowledgeCurrentAction()){const clue=(state.clues||[]).find(item=>item.id===clueId&&item.revealed!==false);return Boolean(clue&&NPC_KNOWLEDGE_DISCLOSURE_ACTION.test(action)&&npcKnowledgeActionTargetsNpc(action,npc)&&npcKnowledgeActionMentionsClue(action,clue))}
function npcKnowledgeCanLearnFact(npc,factId,action=npcKnowledgeCurrentAction()){const fact=npcKnowledgeFactById(factId);if(!fact)return false;if(ensureNpcContinuity(npc).knownFactIds.includes(factId))return true;if(!NPC_KNOWLEDGE_DISCLOSURE_ACTION.test(action)||!npcKnowledgeActionTargetsNpc(action,npc))return false;return fact.learnableFromClueIds.some(clueId=>npcKnowledgeCanLearnClue(npc,clueId,action))}
function npcKnowledgeSanitizeLearnIds(change,npc){const requestedClues=npcKnowledgeUniqueIds(change.learnClueIds||[],30),requestedFacts=npcKnowledgeUniqueIds(change.learnFactIds||[],30),allowedClues=requestedClues.filter(id=>npcKnowledgeCanLearnClue(npc,id)),allowedFacts=requestedFacts.filter(id=>npcKnowledgeCanLearnFact(npc,id));delete change.learnClueIds;delete change.learnFactIds;if(allowedClues.length||allowedFacts.length){change.knowledgeClueIds=allowedClues;change.knowledgeFactIds=allowedFacts;change.__npcKnowledgeValidated=NPC_KNOWLEDGE_BOUNDARY_VERSION}return{allowedClues,allowedFacts,blockedClues:requestedClues.filter(id=>!allowedClues.includes(id)),blockedFacts:requestedFacts.filter(id=>!allowedFacts.includes(id))}}
function npcKnowledgeOperationHasEffect(change){if(!change)return false;return ["description","attitude","claim","relationship","currentIntent","lastInteraction"].some(key=>change[key]!==undefined)||(change.knowledgeClueIds||[]).length||(change.knowledgeFactIds||[]).length}
function npcKnowledgeSanitizeNpcChange(change,violations){if(!["updateNpc","addNpc"].includes(change?.operation))return change;const npcId=asString(change.npcId,120).trim(),existing=(state.npcs||[]).find(item=>item.id===npcId),npc=existing||{id:npcId,name:asString(change.name,160),continuity:{claims:[],knownFactIds:[],knownClueIds:[]}},learn=npcKnowledgeSanitizeLearnIds(change,npc),additional=learn.allowedFacts;if(learn.blockedClues.length||learn.blockedFacts.length)violations.push({type:"invalid_learning",npcId,blockedClueIds:learn.blockedClues,blockedFactIds:learn.blockedFacts});for(const field of ["claim","lastInteraction","description"]){if(change[field]===undefined)continue;const facts=npcKnowledgeUnauthorizedFactsInText(change[field],npc,additional);if(!facts.length)continue;violations.push({type:"unauthorized_state_knowledge",npcId,field,factIds:facts.map(fact=>fact.id)});delete change[field]}return npcKnowledgeOperationHasEffect(change)?change:null}

const __npcKnowledgeBaseSanitizeNpcChange=npcKnowledgeSanitizeNpcChange;
npcKnowledgeSanitizeNpcChange=function(change,violations){
  if(change&&["updateNpc","addNpc"].includes(change.operation)){
    delete change.knowledgeClueIds;
    delete change.knowledgeFactIds;
    delete change.__npcKnowledgeValidated;
  }
  return __npcKnowledgeBaseSanitizeNpcChange(change,violations)
};

function npcKnowledgeNarrativeSegmentLeaks(segment,npc,fact,additionalFactIds=[]){const known=new Set([...(ensureNpcContinuity(npc).knownFactIds||[]),...additionalFactIds]);if(known.has(fact.id)||!npcKnowledgeTextMatchesFact(segment,fact)||npcKnowledgeNegativeNearFact(segment,fact))return false;const normalized=npcKnowledgeNormalizeText(segment);if(!npcKnowledgeNpcAliases(npc).some(alias=>normalized.includes(npcKnowledgeNormalizeText(alias))))return false;return NPC_KNOWLEDGE_SPEECH.test(segment)}
function npcKnowledgeSplitNarrative(text){return String(text||"").match(/[^。！？\n]+[。！？]?|\n/gu)||[]}
function npcKnowledgeRedactNarrative(narrative,grantsByNpc,violations){const npcs=new Map();for(const npc of state.npcs||[])if(npc?.id)npcs.set(npc.id,npc);for(const npc of getCurrentNode()?.npcs||[])if(npc?.id&&!npcs.has(npc.id))npcs.set(npc.id,npc);const facts=npcKnowledgeFacts(),replaced=new Set();return npcKnowledgeSplitNarrative(narrative).map(segment=>{for(const npc of npcs.values()){const additional=grantsByNpc.get(npc.id)||[];for(const fact of facts){if(!npcKnowledgeNarrativeSegmentLeaks(segment,npc,fact,additional))continue;violations.push({type:"unauthorized_narrative_knowledge",npcId:npc.id,factIds:[fact.id]});const key=npc.id+"|"+fact.id;if(replaced.has(key))return"";replaced.add(key);return `${npc.name||"对方"}没有提供能够确认这项信息的说法。`}}return segment}).join("").trim()}
function npcKnowledgeBoundaryApply(out){if(!npcKnowledgeFacts().length)return out;const result=deepClone(out),violations=[],grantsByNpc=new Map(),next=[];for(const raw of result.stateChanges||[]){const change=deepClone(raw),sanitized=npcKnowledgeSanitizeNpcChange(change,violations);if(!sanitized)continue;if(["updateNpc","addNpc"].includes(sanitized.operation)&&sanitized.npcId){grantsByNpc.set(sanitized.npcId,[...(sanitized.knowledgeFactIds||[])])}next.push(sanitized)}result.stateChanges=next;const redacted=npcKnowledgeRedactNarrative(result.narrative,grantsByNpc,violations);if(redacted!==String(result.narrative||""))result.narrative=redacted||"对方没有提供超出其知识范围的可确认信息，但交互仍可继续。";if(violations.length)result.npcKnowledgeRecovery={recovered:true,version:NPC_KNOWLEDGE_BOUNDARY_VERSION,policy:"block_unsafe_state_not_player_action",violations:deepClone(violations)};return result}


function npcKnowledgeTraditionalNpcPatchEffect(change){return ["description","attitude","claim","relationship","currentIntent","lastInteraction"].some(key=>change?.[key]!==undefined)}
const __npcKnowledgeBasePrepareStateChanges=prepareStateChanges;
prepareStateChanges=function(changes,campaignChanges=[],validationContext={}){
  const source=Array.isArray(changes)?changes:[],knowledgeOnly=[];
  const routed=source.filter(change=>{
    const trusted=change?.operation==="updateNpc"&&change?.__npcKnowledgeValidated===NPC_KNOWLEDGE_BOUNDARY_VERSION;
    const hasKnowledge=(change?.knowledgeClueIds||[]).length||(change?.knowledgeFactIds||[]).length;
    if(trusted&&hasKnowledge&&!npcKnowledgeTraditionalNpcPatchEffect(change)){knowledgeOnly.push(deepClone(change));return false}
    return true
  });
  const prepared=__npcKnowledgeBasePrepareStateChanges(routed,campaignChanges,validationContext);
  for(const change of knowledgeOnly){
    const npc=prepared.draft.npcs.find(item=>item.id===change.npcId);
    if(!npc)throw protocolError("STATE_CHANGE_PARAMETER_INVALID","NPC 不存在："+change.npcId);
    applyNpcContinuityPatch(npc,change);
    prepared.summaries.push("NPC 知识更新："+(npc.name||npc.id));
    prepared.count=Number(prepared.count||0)+1;
    prepared.stateCount=Number(prepared.stateCount||0)+1
  }
  return prepared
};

function npcKnowledgeContext(){const facts=npcKnowledgeFacts(),entries=[];const merged=new Map();for(const npc of getCurrentNode()?.npcs||[])if(npc?.id)merged.set(npc.id,deepClone(npc));for(const npc of state.npcs||[])if(npc?.id)merged.set(npc.id,{...(merged.get(npc.id)||{}),...deepClone(npc)});for(const npc of merged.values()){const continuity=ensureNpcContinuity(npc),known=new Set(continuity.knownFactIds||[]);entries.push({npcId:npc.id,name:npc.name||npc.id,knownFactIds:[...known],knownClueIds:[...(continuity.knownClueIds||[])],allowedFacts:facts.filter(fact=>known.has(fact.id)).map(fact=>({id:fact.id,text:fact.text})),forbiddenFacts:facts.filter(fact=>!known.has(fact.id)).map(fact=>({id:fact.id,text:fact.text}))})}return{version:NPC_KNOWLEDGE_BOUNDARY_VERSION,authority:"browser_validated_npc_knowledge",facts:facts.map(fact=>({id:fact.id,text:fact.text,knownBy:fact.knownBy,learnableFromClueIds:fact.learnableFromClueIds})),npcs:entries}}

const __npcKnowledgeBaseValidateAiResponse=validateAiResponse;
validateAiResponse=function(obj,meta){return npcKnowledgeBoundaryApply(__npcKnowledgeBaseValidateAiResponse(obj,meta))};
const __npcKnowledgeBaseBuildSystemPrompt=buildSystemPrompt;
buildSystemPrompt=function(){return __npcKnowledgeBaseBuildSystemPrompt()+"\n30. NPC Knowledge Boundary：NPC 不是全知视角。director.knowledgeFacts / npcKnowledgeBoundary 中明确标记的受保护事实，只能由 knownFactIds 已授权的 NPC 以事实口吻说出；禁止因为 KP 知道真相就让 NPC 自动知道。\n31. 玩家可以把自己已确认的线索告诉 NPC。仅当本轮玩家明确出示/转述已揭示线索时，updateNpc 才可使用 learnClueIds；若该线索是某个 authored knowledgeFact 的 learnableFromClueIds 来源，可同时使用 learnFactIds。不要凭空填写 learnFactIds。\n32. NPC 不知道某事实时可以明确说不知道、拒绝回答、撒谎、猜测或表现异常；知识边界只限制把未知秘密当作已知事实泄露，不能阻止正常社交互动。"};
const __npcKnowledgeBaseBuildRequestPayload=buildRequestPayload;
buildRequestPayload=function(stage,requestId,baseRevision,extra={}){const payload=__npcKnowledgeBaseBuildRequestPayload(stage,requestId,baseRevision,extra);payload.npcKnowledgeBoundary=npcKnowledgeContext();return payload};
const __npcKnowledgeBaseBuildUserPrompt=buildUserPrompt;
buildUserPrompt=function(payload){return __npcKnowledgeBaseBuildUserPrompt(payload)+"\nNPC 知识更新字段：updateNpc 可选 learnClueIds / learnFactIds；它们只用于记录本轮玩家真实告知给该 NPC 的已授权知识，页面会验证来源。"};
const __npcKnowledgeBaseActivateScenario=activateScenario;
activateScenario=function(inputScenario){const errors=validateNpcKnowledgeDefinitions(inputScenario);if(errors.length)throw new Error(`NPC Knowledge Boundary 配置无效：${errors.join("；")}`);const result=__npcKnowledgeBaseActivateScenario(inputScenario);for(const npc of state.npcs||[])ensureNpcContinuity(npc);return result};
const __npcKnowledgeBaseSanitizeRuntimeAfterLoad=sanitizeRuntimeAfterLoad;
sanitizeRuntimeAfterLoad=function(...args){const result=__npcKnowledgeBaseSanitizeRuntimeAfterLoad(...args);for(const npc of state.npcs||[])ensureNpcContinuity(npc);return result};
const __npcKnowledgeBaseBuildDiagnosticPackage=buildDiagnosticPackage;
buildDiagnosticPackage=function(options={}){const doc=__npcKnowledgeBaseBuildDiagnosticPackage(options);return{...doc,npcKnowledgeBoundary:npcKnowledgeContext()}};
