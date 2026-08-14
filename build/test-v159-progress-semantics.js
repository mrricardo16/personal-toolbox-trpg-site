"use strict";
const fs=require("fs"),path=require("path"),vm=require("vm"),assert=require("assert"),{webcrypto}=require("crypto"),{TextEncoder,TextDecoder}=require("util");
const root=path.resolve(__dirname,"..");
const files=["scenarios/library.js","state.js","check-engine.js","scenario-engine.js","case-integrity.js","memory.js","ai-protocol.js","player-action-guard.js","interaction-availability.js","saves.js","progress-semantics.js"];
function storage(){const map=new Map();return{getItem:k=>map.has(String(k))?map.get(String(k)):null,setItem:(k,v)=>map.set(String(k),String(v)),removeItem:k=>map.delete(String(k)),clear:()=>map.clear(),key:i=>Array.from(map.keys())[i]??null,get length(){return map.size}}}
const localStorage=storage(),sessionStorage=storage();
function node(){return{className:"",textContent:"",innerHTML:"",value:"",checked:false,disabled:false,style:{},dataset:{},classList:{add(){},remove(){},toggle(){}},appendChild(){},remove(){},click(){},insertAdjacentHTML(){},setAttribute(){},removeAttribute(){},addEventListener(){},querySelector(){return null},querySelectorAll(){return[]},scrollHeight:0,scrollTop:0}}
const sandbox={Object,Array,JSON,Map,Set,console,crypto:webcrypto,TextEncoder,TextDecoder,URL,AbortController,Blob,structuredClone,fetch:async()=>{throw new Error("fetch not expected")},setTimeout:()=>0,clearTimeout(){},setInterval:()=>0,clearInterval(){},window:{localStorage,sessionStorage,addEventListener(){}},document:{addEventListener(){},querySelector(){return null},querySelectorAll(){return[]},createElement:node,body:{appendChild(){}}},navigator:{clipboard:{writeText:async()=>{}}},confirm:()=>false,alert(){},renderAll(){},renderTopbar(){},renderSidebar(){},renderChat(){},renderChatLog(){},renderChatComposer(){},renderSaves(){},toast(){}};
sandbox.globalThis=sandbox;
const source=files.map(file=>fs.readFileSync(path.join(root,"src",file),"utf8")).join("\n\n")+`\n;scheduleAutosave=()=>{};renderAll=()=>{};renderTopbar=()=>{};renderSidebar=()=>{};renderChat=()=>{};renderChatLog=()=>{};renderChatComposer=()=>{};renderSaves=()=>{};toast=()=>{};
function __ready(){state=makeInitialState();state.character={system:"coc7",name:"Semantics",hp:12,maxHp:12,san:65,maxSan:99,luck:50,attributes:{str:55,con:60,siz:60,dex:65,app:50,int:70,pow:65,edu:70},skills:[{id:"spot_hidden",name:"侦查",value:75}]};activateScenario(deepClone(SCENARIO_LIBRARY.find(x=>x.id==="scenario-old-house")));return deepClone(state)}
function __canonical(){return deepClone({character:state.character,campaign:{...state.campaign,progressSemantics:undefined},clues:state.clues,npcs:state.npcs,items:state.items,statuses:state.statuses,resources:state.resources})}
function __mutate(kind){const before=progressSemanticSnapshot();const d=state.campaign.directorState;
 if(kind==="clue")state.clues.push({id:"semantic-clue",name:"新线索",description:"新的可靠事实",revealed:true});
 if(kind==="node")state.campaign.currentNodeId="semantic-node";
 if(kind==="item")state.items.push({id:"semantic-key",name:"钥匙",quantity:1});
 if(kind==="npc"){const npc=state.npcs[0]||{id:"semantic-npc",name:"管家",continuity:{claims:[]}};if(!state.npcs.length)state.npcs.push(npc);npc.continuity=npc.continuity||{claims:[]};npc.continuity.claims=[...(npc.continuity.claims||[]),"他承认昨晚听到钟声"]}
 if(kind==="tension")d.tension=Number(d.tension||0)+1;
 if(kind==="hp")state.character.hp-=2;
 if(kind==="san")state.character.san-=3;
 if(kind==="clock"){d.clocks=[{id:"clock-semantic",name:"威胁",current:2,max:6,active:true,triggered:false,resolved:false}]}
 if(kind==="clockAdvance"){d.clocks=[{id:"clock-semantic",name:"威胁",current:1,max:6,active:true,triggered:false,resolved:false}];const baseline=progressSemanticSnapshot();d.clocks[0].current=2;return deepClone(deriveProgressSemantics(baseline,progressSemanticSnapshot(),{source:"test"}))}
 if(kind==="clockResolve"){d.clocks=[{id:"clock-semantic",name:"威胁",current:4,max:6,active:true,triggered:false,resolved:false}];const baseline=progressSemanticSnapshot();d.clocks[0].resolved=true;d.clocks[0].active=false;return deepClone(deriveProgressSemantics(baseline,progressSemanticSnapshot(),{source:"test"}))}
 if(kind==="outcome")state.campaign.outcomes.truth="partial";
 if(kind==="lead"){state.campaign.activeLeads=[{id:"lead-semantic",text:"确认来客身份",status:"active"}];const baseline=progressSemanticSnapshot();state.campaign.activeLeads[0].status="resolved";return deepClone(deriveProgressSemantics(baseline,progressSemanticSnapshot(),{source:"test"}))}
 return deepClone(deriveProgressSemantics(before,progressSemanticSnapshot(),{source:"test"}))}
function __multi(){const before=progressSemanticSnapshot();state.clues.push({id:"multi-clue",name:"线索",description:"事实",revealed:true});state.character.hp-=1;return deepClone(deriveProgressSemantics(before,progressSemanticSnapshot(),{source:"test"}))}
function __record(kind="none"){const before=progressSemanticSnapshot();if(kind==="clue")state.clues.push({id:uid("clue"),name:"记录线索",description:"记录",revealed:true});return deepClone(recordProgressSemantics(before,progressSemanticSnapshot(),{source:"test",recordNone:true}))}
function __commitNoop(legacyImpact="neutral"){const parsed={decision:"no_check",narrative:"你停留片刻，没有新的确定变化。",stateChanges:[],campaignChanges:[],locationEffect:{type:"stay",targetNodeId:null},nodeProposal:null,endingProposal:null,actionSuggestions:[]};const tx=prepareAiTransaction(parsed);tx.turnImpact=legacyImpact;return deepClone(commitAiTransaction(tx,"semantic-request"))}
function __move(){const target=allScenarioNodes().find(n=>n.id!==state.campaign.currentNodeId);if(!target)throw new Error("no target node");enterNode(target.id,{reason:"semantic test"});return deepClone(ensureProgressSemanticsState().last)}
function __ending(){applyEnding({id:"semantic-ending",title:"调查结束",summary:"测试结束"},"semantic test");return deepClone(ensureProgressSemanticsState().last)}
function __missing(){delete state.campaign.progressSemantics;state.runtime.lastProgressSemantic=null;sanitizeRuntimeAfterLoad();return deepClone({campaign:state.campaign.progressSemantics,runtime:state.runtime.lastProgressSemantic})}
function __context(){return deepClone({core:safeContextCoreState(),world:worldContinuityContext({debug:true}),diagnostic:buildDiagnosticPackage({includeSecrets:false})})}
function __cap(){for(let i=0;i<90;i++){const before=progressSemanticSnapshot();recordProgressSemantics(before,progressSemanticSnapshot(),{source:"cap",recordNone:true})}return deepClone(ensureProgressSemanticsState())}
globalThis.__test={APP_VERSION,SCHEMA_VERSION,AI_PROTOCOL_VERSION,PROGRESS_SEMANTICS_VERSION,PROGRESS_SEMANTIC_TYPES,ready:__ready,canonical:__canonical,mutate:__mutate,multi:__multi,record:__record,commitNoop:__commitNoop,move:__move,ending:__ending,missing:__missing,context:__context,cap:__cap,snapshot:()=>deepClone(state),derive:(a,b)=>deepClone(deriveProgressSemantics(a,b,{source:"external"}))};`;
vm.createContext(sandbox);vm.runInContext(source,sandbox,{filename:"v159-progress-semantics-runtime.js"});
const api=sandbox.__test;let passed=0;function test(name,fn){fn();passed++;console.log(`PASS ${name}`)}
function has(result,kind){return result.kinds.includes(kind)}

test("版本不低于 v1.5.9 且 Schema/协议不变",()=>{api.ready();const v=api.APP_VERSION.split(".").map(Number);assert(v[0]>1||v[0]===1&&(v[1]>5||v[1]===5&&v[2]>=9));assert.equal(api.SCHEMA_VERSION,8);assert.equal(api.AI_PROTOCOL_VERSION,"1.3");assert.equal(api.PROGRESS_SEMANTICS_VERSION,"1.0")});
test("语义集合固定为六类",()=>{assert.deepEqual(Array.from(api.PROGRESS_SEMANTIC_TYPES),["NONE","DISCOVERY","ACCESS","SOCIAL","THREAT","RESOLUTION"])});
test("无 canonical 变化合法分类为 NONE",()=>{api.ready();const r=api.record("none");assert.equal(r.primary,"NONE");assert.deepEqual(r.kinds,["NONE"])});
test("新增线索分类为 DISCOVERY",()=>{api.ready();const r=api.mutate("clue");assert.equal(r.primary,"DISCOVERY");assert(has(r,"DISCOVERY"));assert(r.evidence.some(x=>x.code==="clue_added"))});
test("节点实际变化分类为 ACCESS",()=>{api.ready();const r=api.mutate("node");assert.equal(r.primary,"ACCESS");assert(r.evidence.some(x=>x.code==="node_changed"))});
test("获得物品分类为 ACCESS",()=>{api.ready();const r=api.mutate("item");assert.equal(r.primary,"ACCESS");assert(r.evidence.some(x=>x.code==="item_acquired"))});
test("NPC canonical continuity 变化分类为 SOCIAL",()=>{api.ready();const r=api.mutate("npc");assert.equal(r.primary,"SOCIAL");assert(r.evidence.some(x=>x.code==="npc_canonical_changed"))});
test("张力上升分类为 THREAT",()=>{api.ready();const r=api.mutate("tension");assert.equal(r.primary,"THREAT");assert(r.evidence.some(x=>x.code==="tension_increased"))});
test("HP 下降分类为 THREAT",()=>{api.ready();const r=api.mutate("hp");assert.equal(r.primary,"THREAT");assert(r.evidence.some(x=>x.code==="hp_decreased"))});
test("SAN 下降分类为 THREAT",()=>{api.ready();const r=api.mutate("san");assert.equal(r.primary,"THREAT");assert(r.evidence.some(x=>x.code==="san_decreased"))});
test("威胁时钟推进分类为 THREAT",()=>{api.ready();const r=api.mutate("clockAdvance");assert.equal(r.primary,"THREAT");assert(r.evidence.some(x=>x.code==="clock_advanced"))});
test("威胁时钟解决分类为 RESOLUTION",()=>{api.ready();const r=api.mutate("clockResolve");assert.equal(r.primary,"RESOLUTION");assert(r.evidence.some(x=>x.code==="clock_resolved"))});
test("案件 outcome 变化分类为 RESOLUTION",()=>{api.ready();const r=api.mutate("outcome");assert.equal(r.primary,"RESOLUTION");assert(r.evidence.some(x=>x.code==="outcome_changed"))});
test("lead 从 active 到 resolved 属于 DISCOVERY",()=>{api.ready();const r=api.mutate("lead");assert.equal(r.primary,"DISCOVERY");assert(r.evidence.some(x=>x.code==="lead_resolved"))});
test("同轮可保留多语义且 THREAT 优先为 primary",()=>{api.ready();const r=api.multi();assert.equal(r.primary,"THREAT");assert(has(r,"DISCOVERY")&&has(r,"THREAT"))});
test("AI 旧 turnImpact 自报 transition 但无提交变化仍为 NONE",()=>{api.ready();const tx=api.commitNoop("transition");assert.equal(tx.progressSemantic.primary,"NONE");assert.equal(api.snapshot().runtime.lastTurnImpact,"transition")});
test("实际 enterNode 后记录 ACCESS 而不是依赖 proposal",()=>{api.ready();const r=api.move();assert.equal(r.primary,"ACCESS");assert.equal(r.source,"node_transition")});
test("实际 applyEnding 后记录 RESOLUTION",()=>{api.ready();const r=api.ending();assert.equal(r.primary,"RESOLUTION");assert(r.evidence.some(x=>x.code==="ending_committed"))});
test("旧存档缺少字段时可在 Schema 8 下懒初始化",()=>{api.ready();const r=api.missing();assert.equal(r.campaign.version,"1.0");assert.deepEqual(r.campaign.history,[]);assert.equal(r.runtime,null)});
test("语义进入 trueState/worldContinuity/诊断上下文",()=>{api.ready();api.record("clue");const c=api.context();assert.equal(c.core.progressSemantics.last.primary,"DISCOVERY");assert.equal(c.world.progressSemantics.last.primary,"DISCOVERY");assert.equal(c.diagnostic.progressSemantics.last.primary,"DISCOVERY")});
test("语义历史有固定 80 条上限",()=>{api.ready();const store=api.cap();assert.equal(store.history.length,80);assert.equal(store.last.primary,"NONE")});
test("纯分类函数不会反向修改 canonical state",()=>{api.ready();const before=api.canonical();const a=api.snapshot();api.derive(a,a);assert.deepEqual(api.canonical(),before)});
const buildSource=fs.readFileSync(path.join(root,"build/build-single-html.js"),"utf8");
test("构建顺序在 API resilience 后加载 Progress Semantics",()=>{const resilience=buildSource.indexOf('"api-response-resilience.js"'),semantics=buildSource.indexOf('"progress-semantics.js"');assert(resilience>=0&&semantics>resilience)});
console.log(`V159_PROGRESS_SEMANTICS_TESTS:${passed}:PASS`);
