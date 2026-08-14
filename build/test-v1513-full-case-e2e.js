"use strict";
const fs=require("fs"),path=require("path"),vm=require("vm"),assert=require("assert"),{webcrypto}=require("crypto"),{TextEncoder,TextDecoder}=require("util");
const root=path.resolve(__dirname,"..");
const files=["scenarios/library.js","state.js","check-engine.js","scenario-engine.js","case-integrity.js","memory.js","ai-protocol.js","player-action-guard.js","interaction-availability.js","saves.js","progress-semantics.js","authored-threat-clock.js","npc-knowledge-boundary.js","ending-resolution-gate.js"];
function storage(){const map=new Map();return{getItem:k=>map.has(String(k))?map.get(String(k)):null,setItem:(k,v)=>map.set(String(k),String(v)),removeItem:k=>map.delete(String(k)),clear:()=>map.clear(),key:i=>Array.from(map.keys())[i]??null,get length(){return map.size}}}
const localStorage=storage(),sessionStorage=storage();
function node(){return{className:"",textContent:"",innerHTML:"",value:"",checked:false,disabled:false,style:{},dataset:{},classList:{add(){},remove(){},toggle(){}},appendChild(){},remove(){},click(){},insertAdjacentHTML(){},setAttribute(){},removeAttribute(){},addEventListener(){},querySelector(){return null},querySelectorAll(){return[]},scrollHeight:0,scrollTop:0}}
const sandbox={Object,Array,JSON,Map,Set,console,crypto:webcrypto,TextEncoder,TextDecoder,URL,AbortController,Blob,structuredClone,fetch:async()=>{throw new Error("fetch not expected")},setTimeout:()=>0,clearTimeout(){},setInterval:()=>0,clearInterval(){},window:{localStorage,sessionStorage,addEventListener(){}},document:{addEventListener(){},querySelector(){return null},querySelectorAll(){return[]},createElement:node,body:{appendChild(){}}},navigator:{clipboard:{writeText:async()=>{}}},confirm:()=>false,alert(){},renderAll(){},renderTopbar(){},renderSidebar(){},renderChat(){},renderChatLog(){},renderChatComposer(){},renderSaves(){},toast(){}};
sandbox.globalThis=sandbox;
const source=files.map(file=>fs.readFileSync(path.join(root,"src",file),"utf8")).join("\n\n")+`\n;scheduleAutosave=()=>{};renderAll=()=>{};renderTopbar=()=>{};renderSidebar=()=>{};renderChat=()=>{};renderChatLog=()=>{};renderChatComposer=()=>{};renderSaves=()=>{};toast=()=>{};
function __character(){return{system:"coc7",name:"E2E Investigator",hp:12,maxHp:12,san:65,maxSan:99,luck:55,attributes:{str:55,con:60,siz:60,dex:65,app:50,int:70,pow:65,edu:70},skills:[{id:"spot_hidden",name:"侦查",value:75},{id:"library_use",name:"图书馆使用",value:70},{id:"persuade",name:"说服",value:65}]}}
function __scenario(){return{id:"scenario-v1513-e2e",title:"v1.5.13 长局验收案",mode:"structured",system:"coc7",metadata:{era:"1920s",difficulty:"test"},briefing:{subtitle:"全链路验收",playerRole:"调查员",knownFacts:["暴雨封路"],caseObjectives:["找出失踪者并带回证据"],opening:"你抵达宅邸。"},director:{knowledgeFacts:[{id:"fact-door",text:"书房后存在地下入口。",aliases:["地下入口","书房后的入口"],knownBy:["npc-butler"],learnableFromClueIds:["clue-blueprint"]},{id:"fact-experiment",text:"地下室正在进行非法实验。",aliases:["地下非法实验","非法实验"],knownBy:["npc-captive"],learnableFromClueIds:["clue-notes"]}],threatClocks:[{id:"clock-storm",name:"暴雨封路",current:0,max:3,consequence:"退路恶化",authored:true,maxAdvancePerEvaluation:1,advanceRules:[{id:"stall-pressure",event:"stall",turns:2,amount:1,once:false,cooldownTurns:2}],resolveRules:[{id:"contained",event:"flag",flag:"threatContained",equals:true,once:true}]}]},initialLeads:[{id:"lead-door",text:"书房后的入口",status:"active"}],initialQuestions:[{id:"question-captive",text:"失踪者是否仍活着？",status:"open"}],endings:[{id:"ending-withdraw",title:"撤离",playerTitle:"撤离",alwaysAvailable:true,priority:1,summary:"你撤离了。"},{id:"ending-solved",title:"案件解决",playerTitle:"带着人证与物证离开",requiredFlags:["survivorRescued","threatContained"],requiredClueIds:["clue-ledger","clue-blueprint","clue-notes"],requiredResolvedQuestionIds:["question-captive"],requiredClockStates:[{clockId:"clock-storm",state:"resolved"}],requiredSemanticKinds:["DISCOVERY","ACCESS","SOCIAL","THREAT"],priority:100,summary:"你救出失踪者并带回证据。"}],chapters:[{id:"chapter",title:"调查",scenes:[{id:"scene",title:"宅邸",nodes:[{id:"node-hall",title:"大厅",background:"管家守在门边。",goals:[],clues:[{id:"clue-ledger",name:"残缺账本",description:"账本记录异常资金。",hidden:true,acquisitionRoutes:[{id:"ledger-auto",type:"automatic"}]}],npcs:[{id:"npc-butler",name:"管家",description:"戒备。",attitude:"戒备"}],optionalChecks:[],mandatoryChecks:[],exits:[{id:"to-study",label:"去书房",targetNodeId:"node-study",condition:null}],keeperNotes:""},{id:"node-study",title:"书房",background:"书架有移动痕迹。",goals:[],clues:[{id:"clue-blueprint",name:"改建图",description:"标出地下入口。",hidden:true,acquisitionRoutes:[{id:"blueprint-auto",type:"automatic"}]}],npcs:[],optionalChecks:[],mandatoryChecks:[],exits:[{id:"to-cellar",label:"进入地下室",targetNodeId:"node-cellar",condition:null}],keeperNotes:""},{id:"node-cellar",title:"地下室",background:"失踪者被困，实验设备仍在运转。",goals:[],clues:[{id:"clue-notes",name:"实验速记",description:"记录非法实验。",hidden:true,acquisitionRoutes:[{id:"notes-auto",type:"automatic"}]}],npcs:[{id:"npc-captive",name:"失踪者",description:"虚弱。",attitude:"信任"}],optionalChecks:[],mandatoryChecks:[],exits:[],keeperNotes:""}]}]}]}}
function __ready(){state=makeInitialState();state.character=__character();activateScenario(__scenario());return deepClone(state)}
function __guard(action){state.runtime.pendingPlayerAction=action;state.runtime.lastContextEnvelope={playerAction:action,playerActionGuard:analyzePlayerActionGuard(action),memory:{loreSelection:[]}}}
function __raw(meta,spec={}){return{protocolVersion:"1.3",requestId:meta.requestId,baseRevision:meta.baseRevision,decision:spec.decision||"no_check",narrative:spec.narrative??"行动得到回应。",check:spec.check||null,stateChanges:deepClone(spec.stateChanges||[]),campaignChanges:deepClone(spec.campaignChanges||[]),locationEffect:deepClone(spec.locationEffect||{type:"stay",targetNodeId:null}),nodeProposal:deepClone(spec.nodeProposal||null),endingProposal:deepClone(spec.endingProposal||null),actionSuggestions:[]}}
let seq=0;
function __turn(action,spec={}){__guard(action);const meta={requestId:"e2e-"+(++seq),baseRevision:state.revision,stage:"action_adjudication"};state.runtime.activeRequestId=meta.requestId;const parsed=validateAiResponse(__raw(meta,spec),meta),tx=prepareAiTransaction(parsed,{});commitAiTransaction(tx,meta.requestId);state.runtime.activeRequestId=null;if(tx.ending)setPhase("awaiting_ending_confirmation",{force:true});else if(tx.proposal)setPhase("awaiting_node_confirmation",{force:true});else setPhase("awaiting_player_action",{force:true});return deepClone({tx,state})}
function __neutral(action="我等一会。",narrative="时间过去了一些，没有新的发现。"){return __turn(action,{narrative})}
function __reveal(clueId,action){const found=findScenarioClue(clueId);if(!found)throw new Error("missing clue "+clueId);const route=(found.clue.acquisitionRoutes||[]).find(x=>x.type==="automatic")||found.clue.acquisitionRoutes?.[0];if(!route)throw new Error("missing route "+clueId);return __turn(action,{narrative:"你获得了一条可持续调查的线索。",stateChanges:[{operation:"revealClue",clueId,sourceRouteId:route.id}]})}
function __move(target,action){__turn(action,{narrative:"你沿已知路线移动。",locationEffect:{type:"transition_proposal",targetNodeId:target},nodeProposal:{targetNodeId:target,reason:"玩家明确移动"}});if(!state.runtime.pendingNodeProposal)throw new Error("node proposal missing");confirmNodeProposal();return deepClone(state)}
function __npc(action,change,narrative="对方回应了你。"){return __turn(action,{narrative,stateChanges:[{operation:"updateNpc",npcId:"npc-butler",...deepClone(change)}]})}
function __flags(flags){return __turn("我根据已经确认的证据处理现场。",{narrative:"已确认的现场处置进入记录。",stateChanges:Object.entries(flags).map(([flag,value])=>({operation:"setScenarioFlag",flag,value}))})}
function __resolveQuestion(){return __turn("我确认失踪者仍然活着。",{narrative:"失踪者的生存状态已经得到系统内证据支持。",campaignChanges:[{operation:"resolveQuestion",questionId:"question-captive"}]})}
function __prematureEnding(){return __turn("我继续调查。",{narrative:"你整理证据，案件至此结束。",endingProposal:{endingId:"ending-solved",reason:"AI 抢跑"}})}
function __proposeEnding(){return __turn("我带着失踪者和证据离开并正式结案。",{narrative:"你准备把案件交给警方并离开现场。",endingProposal:{endingId:"ending-solved",reason:"条件已满足"}})}
function __confirmEnding(){return confirmEndingProposal()}
function __setTurns(sceneTurns,lastProgressTurn,totalTurns=sceneTurns){const d=state.campaign.directorState;d.sceneTurns=sceneTurns;d.lastProgressTurn=lastProgressTurn;d.totalTurns=totalTurns;return deepClone(d)}
function __pace(){return deepClone(applyDeterministicPacingBeforeAction()||null)}
function __gate(id="ending-solved"){const ending=state.scenario.endings.find(x=>x.id===id);return deepClone(endingGateEvaluate(ending))}
function __knowledge(){return deepClone(npcKnowledgeContext())}
function __state(){return deepClone(state)}
function __context(){return deepClone(buildContextSnapshot("继续",{debug:true}))}
function __diagnostic(){return deepClone(buildDiagnosticPackage({includeSecrets:false}))}
function __sanitize(){sanitizeRuntimeAfterLoad();return deepClone(state)}
globalThis.__test={APP_VERSION,SCHEMA_VERSION,AI_PROTOCOL_VERSION,PLAYER_ACTION_GUARD_VERSION,INTERACTION_AVAILABILITY_VERSION,PROGRESS_SEMANTICS_VERSION,AUTHORED_THREAT_CLOCK_VERSION,NPC_KNOWLEDGE_BOUNDARY_VERSION,ENDING_RESOLUTION_GATE_VERSION,ready:__ready,turn:__turn,neutral:__neutral,reveal:__reveal,move:__move,npc:__npc,flags:__flags,resolveQuestion:__resolveQuestion,prematureEnding:__prematureEnding,proposeEnding:__proposeEnding,confirmEnding:__confirmEnding,setTurns:__setTurns,pace:__pace,gate:__gate,knowledge:__knowledge,state:__state,context:__context,diagnostic:__diagnostic,sanitize:__sanitize,normalize:raw=>deepClone(normalizeAiProtocolShape(raw))};`;
vm.createContext(sandbox);vm.runInContext(source,sandbox,{filename:"v1513-full-case-e2e-runtime.js"});
const api=sandbox.__test;let passed=0;
function test(name,fn){fn();passed++;console.log(`PASS ${name}`)}
function current(){return api.state()}

api.ready();
test("版本为 v1.5.13 且 Schema/协议保持稳定",()=>{const v=api.APP_VERSION.split(".").map(Number);assert(v[0]>1||v[0]===1&&(v[1]>5||v[1]===5&&v[2]>=13));assert.equal(api.SCHEMA_VERSION,8);assert.equal(api.AI_PROTOCOL_VERSION,"1.3")});
test("addRevealedTruth description 可窄归一为 text",()=>{const n=api.normalize({protocolVersion:"1.3",stateChanges:[],campaignChanges:[{operation:"addRevealedTruth",description:"已确认事实"}]});assert.equal(n.campaignChanges[0].text,"已确认事实");assert.equal(n.campaignChanges[0].operation,"addRevealedTruth")});
test("核心防御模块全部在同一运行态加载",()=>{assert.equal(api.PLAYER_ACTION_GUARD_VERSION,"1.0");assert.equal(api.INTERACTION_AVAILABILITY_VERSION,"1.0");assert.equal(api.PROGRESS_SEMANTICS_VERSION,"1.0");assert.equal(api.AUTHORED_THREAT_CLOCK_VERSION,"1.0");assert.equal(api.NPC_KNOWLEDGE_BOUNDARY_VERSION,"1.0");assert.equal(api.ENDING_RESOLUTION_GATE_VERSION,"1.0")});
test("完整案件从大厅开始且管家实体化",()=>{const s=current();assert.equal(s.campaign.currentNodeId,"node-hall");assert(s.npcs.some(n=>n.id==="npc-butler"))});
test("管家初始只拥有作者授权的入口知识",()=>{const n=current().npcs.find(n=>n.id==="npc-butler");assert(n.continuity.knownFactIds.includes("fact-door"));assert(!n.continuity.knownFactIds.includes("fact-experiment"))});

api.turn("我已经找到了地下室然后进去拿走所有证据。",{narrative:"你找到了地下室并立即进入。",locationEffect:{type:"transition_proposal",targetNodeId:"node-study"},nodeProposal:{targetNodeId:"node-study",reason:"错误抢跑"},stateChanges:[{operation:"addItem",itemId:"fake-evidence",name:"所有证据",quantity:1}]});
test("玩家完成式多步声明不会直接写入后续结果",()=>{assert.equal(current().campaign.currentNodeId,"node-hall");assert(!current().items.some(x=>x.id==="fake-evidence"));assert(!current().runtime.pendingNodeProposal)});
test("Guard 恢复后仍保持可交互而非 error dead-end",()=>{assert.notEqual(current().runtime.phase,"error");assert.equal(current().runtime.phase,"awaiting_player_action")});

api.neutral("我看看大厅里普通的家具。","大厅里没有出现新的异常。");
test("无收益行动在长局中保持合法",()=>{assert.equal(current().campaign.currentNodeId,"node-hall");assert.notEqual(current().runtime.phase,"error")});
api.reveal("clue-ledger","我检查大厅里留下的账册。");
test("第一条线索通过正式 clue route 进入 canonical state",()=>{assert(current().clues.some(x=>x.id==="clue-ledger"))});
test("线索提交产生 DISCOVERY Progress Semantic",()=>{assert(current().campaign.progressSemantics.history.some(x=>(x.kinds||[]).includes("DISCOVERY")))});

api.npc("我问管家地下到底发生了什么。",{claim:"地下室正在进行非法实验。",relationship:"仍然戒备"},"管家保持戒备，没有给出可验证的新事实。");
test("NPC 未知受保护事实不能写入 claim",()=>{const n=current().npcs.find(n=>n.id==="npc-butler");assert(!n.continuity.claims.some(x=>x.includes("非法实验")));assert.equal(n.continuity.relationship,"仍然戒备")});
test("越权知识被剥离时同回合合法关系变化保留",()=>{assert(current().campaign.progressSemantics.history.some(x=>(x.kinds||[]).includes("SOCIAL")))});

api.npc("我把残缺账本给管家看，告诉他账本里记录的资金问题。",{learnClueIds:["clue-ledger"],lastInteraction:"查看了玩家出示的残缺账本"});
test("玩家明确出示已揭示线索后 NPC 可以获得该 clue 知识",()=>{const n=current().npcs.find(n=>n.id==="npc-butler");assert(n.continuity.knownClueIds.includes("clue-ledger"))});

api.move("node-study","我前往书房继续调查。");
test("合法节点提议经确认后实际进入书房",()=>{assert.equal(current().campaign.currentNodeId,"node-study")});
test("实际地点切换产生 ACCESS Progress Semantic",()=>{assert(current().campaign.progressSemantics.history.some(x=>(x.kinds||[]).includes("ACCESS")))});

const early=api.prematureEnding();
test("条件不足的已知结局只被剥离而不杀死整回合",()=>{assert.equal(current().campaign.ending,null);assert.equal(current().runtime.pendingEndingProposal,null);assert(early.tx.endingResolutionRecovery?.recovered)});
test("提前终局叙事被局部中和且游戏继续",()=>{assert(early.tx.parsed.narrative.includes("尚未满足正式收束条件"));assert.equal(current().runtime.phase,"awaiting_player_action")});

api.setTurns(2,0,2);api.pace();
test("长局停滞达到 authored 阈值后威胁时钟推进",()=>{const c=current().campaign.directorState.clocks.find(x=>x.id==="clock-storm");assert.equal(c.current,1);assert.equal(c.resolved,false)});
test("authored 威胁推进产生 THREAT Progress Semantic",()=>{assert(current().campaign.progressSemantics.history.some(x=>x.source==="authored_threat_clock"&&(x.kinds||[]).includes("THREAT")))});

api.reveal("clue-blueprint","我检查书房里的改建图。");
test("第二条线索进入 canonical state",()=>{assert(current().clues.some(x=>x.id==="clue-blueprint"))});
api.npc("我把改建图拿给管家看，问他书房后的入口。",{learnClueIds:["clue-blueprint"],learnFactIds:["fact-door"],claim:"书房后确实存在地下入口。"},"管家看过图纸后承认书房后确实存在地下入口。");
test("NPC 已授权事实可以在合法来源支持下持续记录",()=>{const n=current().npcs.find(n=>n.id==="npc-butler");assert(n.continuity.knownFactIds.includes("fact-door"));assert(n.continuity.claims.some(x=>x.includes("地下入口")))});

api.move("node-cellar","我沿改建图标出的路线进入地下室。");
test("第二次合法移动进入案件核心节点",()=>{assert.equal(current().campaign.currentNodeId,"node-cellar");assert(current().npcs.some(n=>n.id==="npc-captive"))});
test("失踪者实体化时获得作者声明的实验知识",()=>{const n=current().npcs.find(n=>n.id==="npc-captive");assert(n.continuity.knownFactIds.includes("fact-experiment"))});
api.reveal("clue-notes","我检查实验台上的速记。");
test("第三条核心线索进入 canonical state",()=>{assert.equal(current().clues.filter(x=>["clue-ledger","clue-blueprint","clue-notes"].includes(x.id)).length,3)});

test("结局在救援前仍然不满足 browser gate",()=>{const g=api.gate();assert.equal(g.ready,false);assert(g.missing.some(x=>x.code==="required_flag"&&x.id==="survivorRescued"))});
api.resolveQuestion();
test("确认失踪者生还后 canonical question 被解决",()=>{const q=current().campaign.unresolvedQuestions.find(x=>x.id==="question-captive");assert.equal(q.status,"resolved")});
api.flags({survivorRescued:true,threatContained:true});
test("救援与威胁控制旗标由正常事务提交",()=>{const f=current().campaign.flags;assert.equal(f.survivorRescued,true);assert.equal(f.threatContained,true)});
test("threatContained 提交后 authored clock 同回合由浏览器解决",()=>{const c=current().campaign.directorState.clocks.find(x=>x.id==="clock-storm");assert.equal(c.resolved,true);assert.equal(c.active,false)});
test("威胁时钟正式解决产生 RESOLUTION semantic",()=>{assert(current().campaign.progressSemantics.history.some(x=>x.source==="authored_threat_clock_resolution"&&(x.kinds||[]).includes("RESOLUTION")))});

test("完整 canonical 证据满足后最终结局 gate ready",()=>{const g=api.gate();assert.equal(g.ready,true);assert.equal(g.missing.length,0)});
const proposed=api.proposeEnding();
test("AI 只能形成待确认 ending proposal 而不能直接结束案件",()=>{const s=current();assert.equal(s.campaign.ending,null);assert.equal(s.runtime.phase,"awaiting_ending_confirmation");assert.equal(s.runtime.pendingEndingProposal.endingId,"ending-solved");assert(proposed.tx.ending)});
const confirmed=api.confirmEnding();
test("玩家确认后 browser gate 二次校验并正式提交结局",()=>{const s=current();assert.equal(confirmed,true);assert.equal(s.runtime.phase,"campaign_ended");assert.equal(s.campaign.ending.id,"ending-solved")});
test("最终实际结局提交记录 RESOLUTION Progress Semantic",()=>{const last=current().campaign.progressSemantics.last;assert((last.kinds||[]).includes("RESOLUTION"));assert(last.evidence.some(x=>x.code==="ending_committed"))});

test("长局结束后 NPC knowledge boundary 仍区分管家禁知事实",()=>{const entry=api.knowledge().npcs.find(x=>x.npcId==="npc-butler");assert(entry.forbiddenFacts.some(x=>x.id==="fact-experiment"))});
test("完整诊断同时包含进展、时钟、NPC 知识与结局边界",()=>{const d=api.diagnostic();assert(d.progressSemantics);assert(d.authoredThreatClock);assert(d.npcKnowledgeBoundary);assert(d.endingResolutionGate)});
test("Schema 8 归一后完整长局 canonical 结果仍保持",()=>{const s=api.sanitize();assert.equal(s.campaign.ending.id,"ending-solved");assert(s.clues.some(x=>x.id==="clue-notes"));assert(s.npcs.find(x=>x.id==="npc-butler").continuity.knownClueIds.includes("clue-ledger"));assert(s.campaign.directorState.clocks.find(x=>x.id==="clock-storm").resolved)});
test("全流程没有进入技术 error dead-end",()=>{assert.notEqual(current().runtime.phase,"error")});

console.log(`V1513_FULL_CASE_E2E_TESTS:${passed}:PASS`);
