/* ????????????????????? */
const $ = (selector,root=document)=>root.querySelector(selector);
const $$ = (selector,root=document)=>Array.from(root.querySelectorAll(selector));
const nowIso = ()=>new Date().toISOString();
const uid = prefix => `${prefix}_${Date.now().toString(36)}_${randomInt(100000,999999).toString(36)}`;
const clamp = (n,min,max)=>Math.min(max,Math.max(min,n));
const deepClone = value => JSON.parse(JSON.stringify(value));
const asString = (v,max=2000)=>typeof v==="string"?v.slice(0,max):"";
const isPlainObject = v => {if(!v||typeof v!=="object"||Array.isArray(v))return false;const proto=Object.getPrototypeOf(v);return proto===null||Object.getPrototypeOf(proto)===null;};
function memoryStorage(){const map=new Map();return{getItem:key=>map.has(String(key))?map.get(String(key)):null,setItem:(key,value)=>map.set(String(key),String(value)),removeItem:key=>map.delete(String(key)),clear:()=>map.clear(),key:index=>Array.from(map.keys())[index]??null,get length(){return map.size}}}
function safeBrowserStorage(name){try{const storage=window[name],probe=`__trpg_probe_${name}`;storage.setItem(probe,"1");storage.removeItem(probe);return storage}catch{return memoryStorage()}}
const APP_LOCAL_STORAGE=safeBrowserStorage("localStorage"),APP_SESSION_STORAGE=safeBrowserStorage("sessionStorage");
function normalizeEnum(value){return String(value??"").trim().toLowerCase()}
function normalizeApiUrl(value){
  let url;try{url=new URL(String(value||"").trim())}catch{throw new Error("API 地址不是有效 URL")}
  const local=["localhost","127.0.0.1","::1"].includes(url.hostname);
  if(url.username||url.password)throw new Error("API 地址不得包含用户名或密码");
  if(url.protocol!=="https:"&&!(url.protocol==="http:"&&local))throw new Error("API 地址必须使用 HTTPS；仅 localhost 允许 HTTP");
  url.hash="";return url.toString()
}
function apiStorageHost(apiUrl){return new URL(normalizeApiUrl(apiUrl)).origin.toLowerCase()}
function apiKeyStorageKey(apiUrl){return STORAGE_API_KEY_PREFIX+encodeURIComponent(apiStorageHost(apiUrl))}
function stripApiTransportConfig(config){const source=isPlainObject(config)?deepClone(config):{};for(const key of API_TRANSPORT_CONFIG_KEYS)delete source[key];delete source.apiKey;delete source.persistKey;return source}
function pickApiTransportConfig(config){const source=isPlainObject(config)?config:{};return{apiUrl:normalizeApiUrl(source.apiUrl||DEFAULT_CONFIG.apiUrl),model:normalizeConfiguredModel(source.model||DEFAULT_CONFIG.model),temperature:clamp(Number(source.temperature??RECOMMENDED_TEMPERATURE),0,2),timeoutMs:clamp(Number(source.timeoutMs||DEFAULT_CONFIG.timeoutMs),5000,180000)}}
function readApiPreferences(){try{const raw=APP_LOCAL_STORAGE.getItem(STORAGE_PREFS_KEY)||APP_LOCAL_STORAGE.getItem(STORAGE_PREFS_KEY_LEGACY);if(!raw)return pickApiTransportConfig(DEFAULT_CONFIG);const parsed=JSON.parse(raw),safe=pickApiTransportConfig({...DEFAULT_CONFIG,...(isPlainObject(parsed)?parsed:{})});APP_LOCAL_STORAGE.setItem(STORAGE_PREFS_KEY,JSON.stringify(safe));APP_LOCAL_STORAGE.removeItem(STORAGE_PREFS_KEY_LEGACY);return safe}catch{return pickApiTransportConfig(DEFAULT_CONFIG)}}
function writeApiPreferences(config){const safe=pickApiTransportConfig(config);APP_LOCAL_STORAGE.setItem(STORAGE_PREFS_KEY,JSON.stringify(safe));APP_LOCAL_STORAGE.removeItem(STORAGE_PREFS_KEY_LEGACY);return safe}
function applyStoredApiPreferences(config){return{...(isPlainObject(config)?config:{}),...readApiPreferences()}}
function hasPersistedApiKey(apiUrl=state?.config?.apiUrl||DEFAULT_CONFIG.apiUrl){try{return Boolean(APP_LOCAL_STORAGE.getItem(apiKeyStorageKey(apiUrl)))}catch{return false}}
function clearApiKeyForUrl(apiUrl){try{const key=apiKeyStorageKey(apiUrl);APP_LOCAL_STORAGE.removeItem(key);APP_SESSION_STORAGE.removeItem(key)}catch{}}
function escapeHtml(value){return String(value??"").replace(/[&<>"']/g,ch=>({"&":"&amp;","<":"&lt;",">":"&gt;","\"":"&quot;","'":"&#39;"}[ch]));}
function formatTime(iso){try{return new Date(iso).toLocaleString("zh-CN",{hour12:false});}catch{return iso||"";}}
function normalizeConfiguredModel(model){const value=asString(model,200).trim();if(value==="deepseek-chat"||value==="deepseek-reasoner")return "deepseek-v4-flash";return value||"deepseek-v4-flash"}
function modelPresetValue(model){const value=normalizeConfiguredModel(model);return OFFICIAL_MODEL_OPTIONS.some(item=>item.value===value)?value:"custom"}
function temperaturePresetValue(value){const n=Number(value);for(const preset of [0.3,0.45,0.65])if(Math.abs(n-preset)<0.001)return String(preset);return "custom"}
function readConfiguredModel(form){const preset=String(form.get("modelPreset")||"");if(preset&&preset!=="custom")return normalizeConfiguredModel(preset);const custom=asString(form.get("modelCustom"),200).trim();if(!custom)throw new Error("自定义模型名称不能为空");return custom}
function readConfiguredTemperature(form){const preset=String(form.get("temperaturePreset")||"");const value=preset&&preset!=="custom"?Number(preset):Number(form.get("temperatureCustom"));if(!Number.isFinite(value))throw new Error("温度必须是有效数字");return clamp(value,0,2)}
function progressMarker(campaign=state.campaign,clues=state.clues){const d=campaign?.directorState||{};return JSON.stringify([clues?.length||0,Number(d.progress||0),(campaign?.activeLeads||[]).filter(x=>x.status==="resolved").length,(campaign?.unresolvedQuestions||[]).filter(x=>x.status==="resolved").length,(d.revealedTruths||[]).length,(d.activeThreats||[]).length,Object.values(campaign?.outcomes||{}).join("|")])}
function locationSignatureText(node){return [node?.title,node?.background,...(node?.goals||[]),...(node?.visibleDetails||[])].filter(Boolean).join(" ")}
function locationSignatureTokens(value){const source=String(value||"").toLowerCase().replace(/[^\p{L}\p{N}]+/gu,"");const tokens=new Set();for(const term of SPATIAL_GENERIC_TERMS)if(source.includes(term))tokens.add(term);for(let i=0;i<source.length-1&&tokens.size<160;i++)tokens.add(source.slice(i,i+2));return tokens}
function setSimilarity(a,b){const aa=a instanceof Set?a:locationSignatureTokens(a),bb=b instanceof Set?b:locationSignatureTokens(b);if(!aa.size||!bb.size)return 0;let common=0;for(const item of aa)if(bb.has(item))common++;return common/(aa.size+bb.size-common)}
function genericSpatialPattern(value){const text=String(value||"");return SPATIAL_GENERIC_TERMS.filter(term=>text.includes(term)).join(">")}
function narrativeImpliesLocationTransition(value){const text=String(value||"").replace(/\s+/g," ");if(!text)return false;const patterns=[/你(?:推开|拉开).{0,18}(?:门).{0,14}(?:走进|进入|来到|踏入|抵达)/u,/你(?:穿过|越过).{0,18}(?:门|走廊|楼梯|地道|通道).{0,18}(?:来到|进入|抵达|踏入|下到)/u,/你(?:来到|进入|走进|踏入|抵达)(?:了)?(?:另一|下一|新的|更深的|一间|一条).{0,24}(?:房间|走廊|地道|地下室|大厅|仓库|密室|通道)/u,/你(?:顺着|沿着).{0,18}(?:楼梯|地道|走廊|通道).{0,20}(?:下去|前进|来到|进入|抵达)/u];return patterns.some(pattern=>pattern.test(text))}
function spatialLoopPattern(value){const text=String(value||"");const sequence=[];for(const term of SPATIAL_GENERIC_TERMS){let index=text.indexOf(term);while(index>=0){sequence.push({term,index});index=text.indexOf(term,index+term.length)}}sequence.sort((a,b)=>a.index-b.index);return sequence.map(item=>item.term).slice(0,10).join(">")}
function recentSpatialLoopDetected(narrative){const pattern=spatialLoopPattern(narrative);if(!pattern||pattern.split(">").length<3)return false;const recent=state.messages.filter(m=>m.role==="ai").slice(-5).map(m=>spatialLoopPattern(m.content)).filter(Boolean);return recent.filter(item=>item===pattern||setSimilarity(item,pattern)>=0.7).length>=2}
const LOCATION_EFFECT_TYPES=new Set(["stay","transition_proposal","blocked","searched","returned","uncertain"]);
function normalizeLocationEffect(raw){if(raw===null||raw===undefined)return{type:"stay",targetNodeId:null};if(!isPlainObject(raw)||hasDangerousKeys(raw))throw protocolError("LOCATION_EFFECT_INVALID","locationEffect 必须是对象");const type=normalizeEnum(asString(raw.type,40));if(!LOCATION_EFFECT_TYPES.has(type))throw protocolError("LOCATION_EFFECT_INVALID",`locationEffect.type 不受支持：${type||"未提供"}`);return{type,targetNodeId:type==="transition_proposal"?(asString(raw.targetNodeId,120).trim()||null):null}}
function transactionHasMeaningfulProgress(parsed){const meaningfulState=new Set(["addClue","revealClue","addNpc"]),meaningfulCampaign=new Set(["addLead","resolveLead","resolveQuestion","addThreat","removeThreat","addRevealedTruth","setOutcome","advanceClock","resolveClock"]);if((parsed.stateChanges||[]).some(x=>meaningfulState.has(x.operation)))return true;if((parsed.campaignChanges||[]).some(x=>meaningfulCampaign.has(x.operation)||(x.operation==="adjustProgress"&&Number(x.amount)>0)))return true;return false}
function validateLocationContinuity(parsed,proposal,meaningfulProgress){
  const effect=parsed.locationEffect||{type:"stay",targetNodeId:null},implies=narrativeImpliesLocationTransition(parsed.narrative),setLocation=parsed.stateChanges?.find(change=>change.operation==="setLocation"),nonTransition=["stay","blocked","searched","returned","uncertain"].includes(effect.type);
  if(effect.type==="transition_proposal"&&!proposal)throw protocolError("LOCATION_REPAIR_REQUIRED","地点转换缺少可验证目标，需要进行内部地点校正");
  if(nonTransition&&proposal)throw protocolError("LOCATION_EFFECT_CONFLICT",`locationEffect=${effect.type} 时不得同时提交 nodeProposal`);
  if(proposal&&effect.targetNodeId&&proposal.targetNodeId&&effect.targetNodeId!==proposal.targetNodeId)throw protocolError("LOCATION_EFFECT_CONFLICT","locationEffect 与 nodeProposal 的目标节点不一致");
  if(effect.type==="stay"&&implies&&!proposal)throw protocolError("LOCATION_REPAIR_REQUIRED","叙事宣布进入新地点，但地点结果仍为 stay，需要进行内部地点校正");
  if(setLocation){if(!proposal)throw protocolError("LOCATION_SET_REQUIRES_NODE","地点变更必须通过 nodeProposal，不能单独使用 setLocation");if(asString(setLocation.location,120)!==proposal.title)throw protocolError("LOCATION_SET_CONFLICT","setLocation 必须与节点提议目标一致")}
  if(recentSpatialLoopDetected(parsed.narrative)&&!meaningfulProgress)throw protocolError("SPATIAL_LOOP_DETECTED","检测到连续重复的空间循环；本轮需要返回已有地点、让环境产生反应或结束无效搜索")
}
function temporaryNodeSimilarity(raw){const text=[raw?.title,raw?.background,raw?.purpose,...(raw?.novelElements||[])].filter(Boolean).join(" "),tokens=locationSignatureTokens(text),history=state.campaign.navigation?.recentLocationSignatures||[];let max=0;for(const item of history.slice(-6))max=Math.max(max,setSimilarity(tokens,new Set(item.tokens||[])));return max}
function updateNavigationAfterMeaningfulProgress(){const nav=state.campaign.navigation={...defaultNavigationState(),...(state.campaign.navigation||{})};nav.consecutiveSpatialMoves=0;nav.lastMeaningfulNodeId=state.campaign.currentNodeId;nav.lastProgressMarker=progressMarker()}
function recordNodeTransition(found,{temporary=false,meaningfulProgress=false,reason="",initial=false}={}){const nav=state.campaign.navigation={...defaultNavigationState(),...(state.campaign.navigation||{})},previous=state.campaign.currentNodeId,marker=progressMarker(),meaningful=initial||meaningfulProgress||nav.lastProgressMarker!==marker;if(previous&&previous!==found.node.id){nav.transitionHistory.push({id:uid("transition"),fromNodeId:previous,toNodeId:found.node.id,at:nowIso(),temporary:Boolean(temporary),meaningfulProgress:meaningful,reason:asString(reason,300)});if(nav.transitionHistory.length>100)nav.transitionHistory.splice(0,nav.transitionHistory.length-100);nav.consecutiveSpatialMoves=meaningful?0:Number(nav.consecutiveSpatialMoves||0)+1}if(!nav.visitedNodeIds.includes(found.node.id))nav.visitedNodeIds.push(found.node.id);const tokens=Array.from(locationSignatureTokens(locationSignatureText(found.node)));nav.recentLocationSignatures.push({nodeId:found.node.id,title:found.node.title,tokens,temporary:Boolean(temporary),at:nowIso()});if(nav.recentLocationSignatures.length>12)nav.recentLocationSignatures.splice(0,nav.recentLocationSignatures.length-12);if(temporary){nav.temporaryNodeCount=Number(nav.temporaryNodeCount||0)+1;nav.temporaryNodeCountByScene[found.scene.id]=Number(nav.temporaryNodeCountByScene[found.scene.id]||0)+1}if(meaningful)nav.lastMeaningfulNodeId=found.node.id;nav.lastProgressMarker=marker;nav.lastConfirmedNodeId=found.node.id}

function toast(message,type="ok",duration=3200){
  const el=document.createElement("div");el.className=`toast ${type}`;el.textContent=message;$("#toastWrap").appendChild(el);
  setTimeout(()=>el.remove(),duration);
}
function hasDangerousKeys(value){
  if(!value||typeof value!=="object")return false;
  for(const key of Object.keys(value)){if(DANGEROUS_KEYS.has(key))return true;if(hasDangerousKeys(value[key]))return true;}
  return false;
}
function safeJsonParse(text){try{return {ok:true,value:JSON.parse(text)}}catch(error){return {ok:false,error}}}
function extractFirstJsonObject(text){
  const s=String(text||"").trim().replace(/^```(?:json)?\s*/i,"").replace(/\s*```$/,"");
  let depth=0,start=-1,inString=false,escape=false;
  for(let i=0;i<s.length;i++){
    const c=s[i];
    if(inString){if(escape){escape=false}else if(c==="\\"){escape=true}else if(c==='"'){inString=false}continue;}
    if(c==='"'){inString=true;continue}if(c==="{"){if(depth===0)start=i;depth++}else if(c==="}"){depth--;if(depth===0&&start>=0)return s.slice(start,i+1)}
  }
  return s;
}
function downloadText(filename,text,type="application/json"){
  const blob=new Blob([text],{type:`${type};charset=utf-8`});const url=URL.createObjectURL(blob);const a=document.createElement("a");
  a.href=url;a.download=filename;document.body.appendChild(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),1000);
}
function randomInt(min,max){
  min=Math.ceil(min);max=Math.floor(max);const range=max-min+1;if(range<=0)throw new Error("随机数范围无效");
  const limit=Math.floor(0x100000000/range)*range;const arr=new Uint32Array(1);let x;
  do{crypto.getRandomValues(arr);x=arr[0]}while(x>=limit);return min+(x%range);
}

/* =========================
   骰式与规则引擎
========================= */

function makeInitialState(){
  return {
    appVersion:APP_VERSION,schemaVersion:SCHEMA_VERSION,revision:0,
    runtime:{phase:"setup",activeRequestId:null,requestStartedAt:null,isDirty:false,lastError:null,processedRequestIds:[],connection:"untested",pendingCheck:null,checkQueue:[],pendingSecretResults:[],pendingNodeProposal:null,pendingEndingProposal:null,pendingActionSuggestions:[],pendingPlayerAction:"",lastContinuationPayload:null,failedRequest:null,lastRawAiResponse:null,summaryInProgress:false,improvGenerating:false,improvDraft:null,pendingDirectorEvent:null,checkChainDepth:0,currentResolutionRecordId:null,lastContextEnvelope:null,lastLoreSelection:[],lastTurnImpact:null,lastNarrativeRepetition:null,lastWorldContinuityEvent:null,longSessionDiagnostics:null,hintGenerating:false,lastHintRequestId:null,sceneContinuityWarning:null,turnSnapshot:null,requestRollback:null,lastUndoAt:null,lastApiSelfCheck:null},
    config:deepClone(DEFAULT_CONFIG),character:null,scenario:null,
    campaign:{currentChapterId:null,currentSceneId:null,currentNodeId:null,currentLocation:"",currentTime:"",flags:{},activeLeads:[],unresolvedQuestions:[],triggeredCheckIds:[],processedExposureKeys:[],directorState:defaultDirectorState(),outcomes:defaultOutcomes(),navigation:defaultNavigationState(),hintUsage:defaultHintUsage(),ending:null},
    clues:[],npcs:[],items:[],statuses:[],resources:{},messages:[],logs:[],checkRecords:[],
    context:{rollingSummary:"",recentMessageLimit:12,summaryThreshold:30,lastSummarizedMessageId:null,pinnedFacts:[],directorNote:{tone:"",currentFocus:"",avoid:""},loreCards:[],activeLoreCardIds:[],loreUsage:{}},
    saveMeta:{slotId:null,slotName:"未命名调查",createdAt:null,updatedAt:null},
    ui:{currentView:"setup",sidebarCollapsed:false,chatVisibleCount:100,actionDraft:""}
  };
}
let state=makeInitialState();
let activeAbortController=null;
let autosaveTimer=null;
let apiKeyMemory={host:"",value:""};

function setPhase(next,{force=false}={}){
  if(!PHASES.has(next))throw new Error(`未知阶段：${next}`);const current=state.runtime.phase;
  if(!force&&current!==next&&!TRANSITIONS[current]?.has(next))throw new Error(`非法状态转换：${current} → ${next}`);
  state.runtime.phase=next;renderTopbar();renderChatComposer();
}
function bumpRevision(){state.revision+=1;state.runtime.isDirty=true;state.saveMeta.updatedAt=nowIso();scheduleAutosave()}
function addLog(type,description,extra={}){
  state.logs.push({id:uid("log"),time:nowIso(),type,description:asString(description,1000),requestId:extra.requestId||null,revision:state.revision,secret:Boolean(extra.secret)});
  if(state.logs.length>2000)state.logs.splice(0,state.logs.length-2000);
}
function addMessage(role,content,extra={}){
  state.messages.push({id:uid("msg"),role,content:asString(content,12000),time:nowIso(),requestId:extra.requestId||null,kind:extra.kind||null});
  if(state.messages.length>1200)state.messages.splice(0,state.messages.length-1200);bumpRevision();renderChatLog();
}
function getCurrentNode(){
  if(!state.scenario)return null;for(const chapter of state.scenario.chapters||[])for(const scene of chapter.scenes||[])for(const node of scene.nodes||[])if(node.id===state.campaign.currentNodeId)return node;return null;
}
function findNode(id){
  if(!state.scenario)return null;for(const chapter of state.scenario.chapters||[])for(const scene of chapter.scenes||[])for(const node of scene.nodes||[])if(node.id===id)return {chapter,scene,node};return null;
}
function materializeNodeNpcs(node=getCurrentNode(),target=state.npcs){
  if(!node||!Array.isArray(target))return 0;let added=0;
  for(const raw of node.npcs||[]){
    if(!isPlainObject(raw))continue;const id=asString(raw.id,100).trim();if(!id)continue;
    let npc=target.find(item=>item.id===id);
    if(npc){ensureNpcContinuity(npc);continue}
    npc={id,name:asString(raw.name,160).trim()||id,description:asString(raw.description,1000),attitude:asString(raw.attitude,100)};
    if(isPlainObject(raw.continuity))npc.continuity=deepClone(raw.continuity);
    ensureNpcContinuity(npc);target.push(npc);added+=1
  }
  return added
}
function enterNode(nodeId,{initial=false,temporary=false,meaningfulProgress=false,reason=""}={}){
  const found=findNode(nodeId);if(!found)throw new Error("目标节点不存在");recordNodeTransition(found,{initial,temporary,meaningfulProgress,reason});state.campaign.currentChapterId=found.chapter.id;state.campaign.currentSceneId=found.scene.id;state.campaign.currentNodeId=found.node.id;state.campaign.currentLocation=found.node.title;materializeNodeNpcs(found.node,state.npcs);state.runtime.pendingNodeProposal=null;state.runtime.checkQueue=[];state.runtime.checkChainDepth=0;state.runtime.sceneContinuityWarning=null;state.campaign.directorState={...defaultDirectorState(),...(state.campaign.directorState||{}),sceneTurns:0,lastProgressTurn:0};setPhase("awaiting_player_action",{force:true});bumpRevision();addLog("node",`进入节点：${found.node.title}`);addMessage("system",`进入节点：${found.node.title}
${found.node.background||""}`,{kind:"sceneTransition"});renderAll();scheduleNodeChecks("on_enter");
}
function sanitizeRuntimeAfterLoad(){
  const interrupted=["requesting_ai","requesting_ai_continuation","rolling"].includes(state.runtime.phase);
  state.runtime.activeRequestId=null;state.runtime.requestStartedAt=null;state.runtime.pendingCheck=state.runtime.pendingCheck||null;state.runtime.pendingNodeProposal=state.runtime.pendingNodeProposal||null;
  materializeNodeNpcs(getCurrentNode(),state.npcs);
  if(interrupted){state.runtime.phase=state.runtime.pendingCheck?"awaiting_check":"awaiting_player_action";addLog("recovery","上一次操作在页面关闭前未完成，未应用未确认的状态变化。");state.messages.push({id:uid("msg"),role:"system",content:"上一次操作在页面关闭前未完成，未应用未确认的状态变化。",time:nowIso()});}
}

/* =========================
   角色创建
========================= */
const COC_ATTRIBUTE_KEYS=["str","con","siz","dex","app","int","pow","edu"];
const COC_ATTRIBUTE_LABELS={str:"力量 STR",con:"体质 CON",siz:"体型 SIZ",dex:"敏捷 DEX",app:"外貌 APP",int:"智力 INT",pow:"意志 POW",edu:"教育 EDU"};
const COC_SKILL_MAP=Object.fromEntries(COC_SKILL_DEFINITIONS.map(skill=>[skill.id,skill]));
const COC_OCCUPATION_MAP=Object.fromEntries(COC_OCCUPATIONS.map(occupation=>[occupation.id,occupation]));
