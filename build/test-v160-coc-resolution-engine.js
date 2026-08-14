"use strict";
const fs=require("fs"),path=require("path"),vm=require("vm"),assert=require("assert"),{webcrypto}=require("crypto"),{TextEncoder,TextDecoder}=require("util");
const root=path.resolve(__dirname,"..");
const files=["scenarios/library.js","state.js","check-engine.js","scenario-engine.js","case-integrity.js","memory.js","ai-protocol.js","player-action-guard.js","interaction-availability.js","saves.js","api-response-resilience.js","progress-semantics.js","authored-threat-clock.js","npc-knowledge-boundary.js","ending-resolution-gate.js","coc-resolution-engine.js"];
function storage(){const map=new Map();return{getItem:k=>map.has(String(k))?map.get(String(k)):null,setItem:(k,v)=>map.set(String(k),String(v)),removeItem:k=>map.delete(String(k)),clear:()=>map.clear(),key:i=>Array.from(map.keys())[i]??null,get length(){return map.size}}}
function node(){return{className:"",textContent:"",innerHTML:"",value:"",checked:false,disabled:false,style:{},dataset:{},classList:{add(){},remove(){},toggle(){}},appendChild(){},remove(){},click(){},insertAdjacentHTML(){},setAttribute(){},removeAttribute(){},addEventListener(){},querySelector(){return null},querySelectorAll(){return[]},scrollHeight:0,scrollTop:0}}
const localStorage=storage(),sessionStorage=storage();
const sandbox={Object,Array,JSON,Map,Set,console,crypto:webcrypto,TextEncoder,TextDecoder,URL,AbortController,Blob,structuredClone,fetch:async()=>{throw new Error("fetch not expected")},setTimeout:()=>0,clearTimeout(){},setInterval:()=>0,clearInterval(){},window:{localStorage,sessionStorage,addEventListener(){}},document:{addEventListener(){},querySelector(){return null},querySelectorAll(){return[]},createElement:node,body:{appendChild(){}}},navigator:{clipboard:{writeText:async()=>{}}},confirm:()=>false,alert(){},renderAll(){},renderTopbar(){},renderSidebar(){},renderChat(){},renderChatLog(){},renderChatComposer(){},renderSaves(){},toast(){}};
sandbox.globalThis=sandbox;
const source=files.map(file=>fs.readFileSync(path.join(root,"src",file),"utf8")).join("\n\n")+`\n;scheduleAutosave=()=>{};maybeAutoSummarize=()=>{};renderAll=()=>{};renderTopbar=()=>{};renderSidebar=()=>{};renderChat=()=>{};renderChatLog=()=>{};renderChatComposer=()=>{};renderSaves=()=>{};toast=()=>{};
function __character(){return{system:"coc7",name:"Resolution Investigator",hp:12,maxHp:12,san:64,maxSan:99,luck:55,attributes:{str:50,con:60,siz:60,dex:65,app:50,int:70,pow:65,edu:70},skills:[{id:"spot_hidden",name:"侦查",value:60},{id:"library_use",name:"图书馆使用",value:70},{id:"persuade",name:"说服",value:40}]}}
function __scenario(){return{id:"scenario-v160",title:"v1.6 Resolution",mode:"structured",system:"coc7",metadata:{era:"1920s"},briefing:{subtitle:"test",playerRole:"调查员",knownFacts:[],caseObjectives:[],opening:"测试开始。"},director:{},initialLeads:[],initialQuestions:[],endings:[{id:"withdraw",title:"撤离",alwaysAvailable:true}],chapters:[{id:"c",title:"c",scenes:[{id:"s",title:"s",nodes:[{id:"n",title:"测试室",background:"",goals:[],clues:[],npcs:[],optionalChecks:[],mandatoryChecks:[],exits:[],keeperNotes:""}]}]}]}}
function __ready(){state=makeInitialState();state.character=__character();activateScenario(__scenario());return deepClone(state)}
function __normalize(raw){return deepClone(normalizeCheck(raw))}
function __record(check,roll,skip=false){return deepClone(makeCheckRecord(check,roll,skip))}
function __guidance(record){return deepClone(checkOutcomeGuidance(record))}
function __ensure(record){const copy=deepClone(record);ensureCocOutcomeContract(copy);return copy}
function __validateOutcome(record){return validateCocOutcomeContract(record)}
function __resolveTampered(check){return resolveCheck(check)}
function __context(){return deepClone(cocResolutionContext())}
function __payload(){return deepClone(buildRequestPayload("public_check_continuation","req-v160",state.revision,{playerAction:"我观察房间。"}))}
function __systemPrompt(){return buildSystemPrompt()}
function __userPrompt(){return buildUserPrompt(buildRequestPayload("public_check_continuation","req-v160-user",state.revision,{playerAction:"我观察房间。"}))}
function __sanitize(){sanitizeRuntimeAfterLoad();return deepClone(state)}
function __state(){return deepClone(state)}
function __setRecords(records){state.checkRecords=deepClone(records);return deepClone(state.checkRecords)}
globalThis.__test={APP_VERSION,SCHEMA_VERSION,AI_PROTOCOL_VERSION,COC_RESOLUTION_ENGINE_VERSION,COC_RESOLUTION_AUTHORITY,ready:__ready,normalize:__normalize,record:__record,guidance:__guidance,ensure:__ensure,validateOutcome:__validateOutcome,resolveTampered:__resolveTampered,context:__context,payload:__payload,systemPrompt:__systemPrompt,userPrompt:__userPrompt,sanitize:__sanitize,state:__state,setRecords:__setRecords};`;
vm.createContext(sandbox);vm.runInContext(source,sandbox,{filename:"v160-coc-resolution-engine-runtime.js"});
const api=sandbox.__test;let passed=0;
function test(name,fn){fn();passed++;console.log(`PASS ${name}`)}
function baseCheck(extra={}){return api.normalize({required:true,mandatory:false,visibility:"public",trigger:"ai",origin:"ai",system:"coc7",type:"skill",skillId:"spot_hidden",label:"侦查",reason:"寻找痕迹",purpose:"寻找痕迹",difficulty:"regular",bonusDice:0,penaltyDice:0,...extra})}
function roll(total,target=60,difficulty="regular",rank,result){const computed=rank||(()=>{if(total===1)return"critical";if(target<50&&total>=96||target>=50&&total===100)return"fumble";if(total<=Math.floor(target/5))return"extreme";if(total<=Math.floor(target/2))return"hard";if(total<=target)return"regular";return"failure"})();const order={fumble:0,failure:0,regular:1,hard:2,extreme:3,critical:4},need={regular:1,hard:2,extreme:3}[difficulty]||1;return{expression:"1d100",rawRolls:[total],modifier:0,total,target,difficulty,difficultyTarget:difficulty==="hard"?Math.floor(target/2):difficulty==="extreme"?Math.floor(target/5):target,rank:computed,result:result??order[computed]>=need}}

api.ready();
test("v1.6 Resolution Engine 模块已加载且 Schema/协议保持稳定",()=>{const v=api.APP_VERSION.split(".").map(Number);assert(v[0]>1||v[0]===1&&(v[1]>6||v[1]===6&&v[2]>=0));assert.equal(api.COC_RESOLUTION_ENGINE_VERSION,"1.0");assert.equal(api.COC_RESOLUTION_AUTHORITY,"browser_coc_resolution");assert.equal(api.SCHEMA_VERSION,8);assert.equal(api.AI_PROTOCOL_VERSION,"1.3")});

test("AI 提供的 skill target 不具有权威，浏览器使用角色卡技能值",()=>{const c=baseCheck({target:99});assert.equal(c.target,60);assert.equal(c.resolutionContract.target,60)});
test("属性检定目标来自角色卡属性",()=>{const c=baseCheck({type:"attribute",skillId:"dex",label:"敏捷"});assert.equal(c.target,65);assert.equal(c.resolutionContract.target,65)});
test("幸运检定目标来自浏览器角色卡 LUCK",()=>{const c=baseCheck({type:"luck",skillId:"luck",label:"幸运"});assert.equal(c.target,55);assert.equal(c.resolutionContract.target,55)});
test("SAN 检定目标来自当前 SAN 且强制公开",()=>{const c=baseCheck({type:"san",skillId:"",label:"理智",visibility:"secret",mandatory:false,loss:"1/1d6",exposureKey:"v160-san"});assert.equal(c.target,64);assert.equal(c.mandatory,true);assert.equal(c.visibility,"public");assert.equal(c.resolutionContract.target,64)});
test("困难等级和实际通过线在 Check Contract 中锁定",()=>{const c=baseCheck({difficulty:"hard"});assert.equal(c.resolutionContract.difficulty,"hard");assert.equal(c.resolutionContract.difficultyTarget,30)});
test("奖惩骰数量进入 Check Contract",()=>{const c=baseCheck({bonusDice:2,penaltyDice:1});assert.equal(c.resolutionContract.bonusDice,2);assert.equal(c.resolutionContract.penaltyDice,1)});
test("Check Contract 标记浏览器为机械裁决权威",()=>{const c=baseCheck();assert.equal(c.resolutionContract.authority,"browser_coc_resolution");assert.equal(c.resolutionContract.version,"1.0");assert(c.resolutionContract.contractId)});
test("待检定目标被篡改时在掷骰前 fail-closed",()=>{const c=baseCheck();c.target=90;assert.throws(()=>api.resolveTampered(c),e=>e.code==="COC_CHECK_CONTRACT_MISMATCH")});
test("待检定难度被篡改时在掷骰前 fail-closed",()=>{const c=baseCheck();c.difficulty="extreme";assert.throws(()=>api.resolveTampered(c),e=>e.code==="COC_CHECK_CONTRACT_MISMATCH")});

const critical=api.record(baseCheck(),roll(1));
test("1 点产生浏览器 critical Outcome Contract",()=>{assert.equal(critical.outcomeContract.rank,"critical");assert.equal(critical.outcomeContract.passed,true);assert.equal(critical.outcomeContract.quality,"critical")});
test("critical 允许最多两项额外洞察和有限优势",()=>{assert.equal(critical.outcomeContract.narrativeBudget.extraInsightLimit,2);assert.equal(critical.outcomeContract.narrativeBudget.limitedAdvantageAllowed,true)});
const extreme=api.record(baseCheck(),roll(12));
test("五分之一边界产生 extreme",()=>{assert.equal(extreme.outcomeContract.rank,"extreme");assert.equal(extreme.outcomeContract.passed,true);assert.equal(extreme.outcomeContract.narrativeBudget.extraInsightLimit,2)});
const hard=api.record(baseCheck(),roll(30));
test("二分之一边界产生 hard",()=>{assert.equal(hard.outcomeContract.rank,"hard");assert.equal(hard.outcomeContract.narrativeBudget.extraInsightLimit,1)});
const regular=api.record(baseCheck(),roll(60));
test("技能值等值仍为普通成功",()=>{assert.equal(regular.outcomeContract.rank,"regular");assert.equal(regular.outcomeContract.passed,true);assert.equal(regular.outcomeContract.narrativeBudget.extraInsightLimit,0)});
const failure=api.record(baseCheck(),roll(61));
test("超过技能值产生 failure 且不能叙述为通过",()=>{assert.equal(failure.outcomeContract.rank,"failure");assert.equal(failure.outcomeContract.passed,false);assert.equal(failure.outcomeContract.narrativeBudget.mayDescribeCheckAsPassed,false);assert.equal(failure.outcomeContract.narrativeBudget.mayDescribeCheckAsFailed,true)});
const fumbleLow=api.record(baseCheck({skillId:"persuade",label:"说服"}),roll(96,40));
test("目标低于 50 时 96 为大失败",()=>{assert.equal(fumbleLow.outcomeContract.rank,"fumble");assert.equal(fumbleLow.outcomeContract.passed,false)});
const high96=api.record(baseCheck(),roll(96,60));
test("目标至少 50 时 96 只是普通失败而不是大失败",()=>{assert.equal(high96.outcomeContract.rank,"failure")});
const high100=api.record(baseCheck(),roll(100,60));
test("目标至少 50 时 100 为大失败",()=>{assert.equal(high100.outcomeContract.rank,"fumble")});
const hardMiss=api.record(baseCheck({difficulty:"hard"}),roll(45,60,"hard"));
test("达到 regular rank 但要求 hard 时 Outcome Contract 仍判失败",()=>{assert.equal(hardMiss.outcomeContract.rank,"regular");assert.equal(hardMiss.outcomeContract.passed,false);assert.equal(hardMiss.outcomeContract.quality,"failure")});
const extremeMiss=api.record(baseCheck({difficulty:"extreme"}),roll(20,60,"extreme"));
test("达到 hard rank 但要求 extreme 时仍判失败",()=>{assert.equal(extremeMiss.outcomeContract.rank,"hard");assert.equal(extremeMiss.outcomeContract.passed,false)});
const skipped=api.record(baseCheck({mandatory:false}),null,true);
test("可选检定跳过产生 skipped Outcome Contract 而不是成功",()=>{assert.equal(skipped.outcomeContract.rank,"skipped");assert.equal(skipped.outcomeContract.mechanicalResult,"skipped");assert.equal(skipped.outcomeContract.passed,false)});

test("Outcome Contract 保存 Check Contract 身份",()=>{assert.equal(regular.outcomeContract.contractId,regular.resolutionContract.contractId);assert.equal(regular.outcomeContract.recordId,regular.id)});
test("篡改 Outcome rank 会被浏览器一致性校验拒绝",()=>{const r=JSON.parse(JSON.stringify(regular));r.outcomeContract.rank="critical";assert.throws(()=>api.validateOutcome(r),e=>e.code==="COC_OUTCOME_CONTRACT_MISMATCH")});
test("篡改 Outcome passed 会被拒绝",()=>{const r=JSON.parse(JSON.stringify(failure));r.outcomeContract.passed=true;assert.throws(()=>api.validateOutcome(r),e=>e.code==="COC_OUTCOME_CONTRACT_MISMATCH")});
test("篡改额外洞察预算会被拒绝",()=>{const r=JSON.parse(JSON.stringify(regular));r.outcomeContract.narrativeBudget.extraInsightLimit=2;assert.throws(()=>api.validateOutcome(r),e=>e.code==="COC_OUTCOME_CONTRACT_MISMATCH")});

test("续写 guidance 携带 immutable browser outcome",()=>{const g=api.guidance(hard);assert.equal(g.authority,"browser_coc_resolution");assert.equal(g.immutableOutcome.roll,30);assert.equal(g.immutableOutcome.rank,"hard");assert.equal(g.immutableOutcome.passed,true);assert.equal(g.narrativeBudget.extraInsightLimit,1)});
test("失败续写 guidance 明确禁止翻转成成功",()=>{const g=api.guidance(failure);assert.equal(g.immutableOutcome.passed,false);assert(g.prohibited.includes("do_not_flip_pass_fail"))});
test("旧 Schema 8 检定记录可懒重建 contracts",()=>{const legacy=JSON.parse(JSON.stringify(regular));delete legacy.resolutionContract;delete legacy.outcomeContract;const rebuilt=api.ensure(legacy);assert.equal(rebuilt.resolutionContract.reconstructed,true);assert.equal(rebuilt.outcomeContract.reconstructed,true);assert.equal(rebuilt.outcomeContract.passed,true)});
test("旧存档 sanitize 后补齐 CoC contracts",()=>{const legacy=JSON.parse(JSON.stringify(failure));delete legacy.resolutionContract;delete legacy.outcomeContract;api.setRecords([legacy]);const s=api.sanitize();assert.equal(s.checkRecords[0].resolutionContract.authority,"browser_coc_resolution");assert.equal(s.checkRecords[0].outcomeContract.passed,false)});

test("请求 payload 暴露浏览器 Resolution authority",()=>{const p=api.payload();assert.equal(p.cocResolutionEngine.authority,"browser_coc_resolution");assert.equal(p.cocResolutionEngine.targetAuthority,"browser_character_sheet");assert.equal(p.cocResolutionEngine.randomnessAuthority,"browser_crypto")});
test("系统提示明确 AI 只能解释 immutableOutcome",()=>{const p=api.systemPrompt();assert(p.includes("CoC Resolution Engine"));assert(p.includes("immutableOutcome"));assert(p.includes("不得把失败叙述成通过"))});
test("用户提示携带机械裁决权威摘要",()=>{const p=api.userPrompt();assert(p.includes("CoC 机械裁决权威"));assert(p.includes("browser_coc_resolution"))});
test("Resolution context 明确 AI 只有请求检定和叙事权",()=>{const c=api.context();assert.equal(c.aiAuthority,"request_check_and_narrate_only");assert.equal(c.outcomeAuthority,"browser_check_record")});
test("引擎没有修改 Save Schema 或 AI protocol",()=>{assert.equal(api.SCHEMA_VERSION,8);assert.equal(api.AI_PROTOCOL_VERSION,"1.3")});

console.log(`V160_COC_RESOLUTION_ENGINE_TESTS:${passed}:PASS`);
