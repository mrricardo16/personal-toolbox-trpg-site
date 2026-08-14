"use strict";
const fs=require("fs"),path=require("path"),vm=require("vm"),assert=require("assert"),{webcrypto}=require("crypto"),{TextEncoder,TextDecoder}=require("util");
const root=path.resolve(__dirname,"..");
const files=["scenarios/library.js","state.js","check-engine.js","scenario-engine.js","memory.js","ai-protocol.js","saves.js"];
function storage(){const map=new Map();return{getItem:k=>map.has(String(k))?map.get(String(k)):null,setItem:(k,v)=>map.set(String(k),String(v)),removeItem:k=>map.delete(String(k)),clear:()=>map.clear(),key:i=>Array.from(map.keys())[i]??null,get length(){return map.size}}}
const localStorage=storage(),sessionStorage=storage();
const sandbox={Object,Array,JSON,Map,Set,console,crypto:webcrypto,TextEncoder,TextDecoder,URL,AbortController,Blob,structuredClone,fetch:async()=>{throw new Error("fetch not expected")},setTimeout:()=>0,clearTimeout(){},setInterval:()=>0,clearInterval(){},window:{localStorage,sessionStorage,addEventListener(){}},document:{querySelector(){return null},querySelectorAll(){return[]},createElement(){return{className:"",textContent:"",style:{},classList:{add(){},remove(){},toggle(){}},appendChild(){},remove(){},click(){}}},body:{appendChild(){}}},confirm:()=>false,renderAll(){},renderTopbar(){},renderSidebar(){},renderChat(){},renderChatLog(){},renderChatComposer(){},renderSaves(){},toast(){}};
sandbox.globalThis=sandbox;
const source=files.map(file=>fs.readFileSync(path.join(root,"src",file),"utf8")).join("\n\n")+`\n;scheduleAutosave=()=>{};renderAll=()=>{};renderTopbar=()=>{};renderSidebar=()=>{};renderChat=()=>{};renderChatLog=()=>{};renderChatComposer=()=>{};renderSaves=()=>{};toast=()=>{};globalThis.__test={scenarioById:id=>deepClone(SCENARIO_LIBRARY.find(x=>x.id===id)),activateScenario,validateAiResponse,prepareStateChanges,snapshot:()=>deepClone(state),reset:()=>{state=makeInitialState();state.character={system:"coc7",name:"E2E",hp:12,maxHp:12,san:65,maxSan:99,luck:50,attributes:{str:55,con:60,siz:60,dex:65,app:50,int:70,pow:65,edu:70},skills:[{id:"spot_hidden",name:"侦查",value:75}]};activateScenario(deepClone(SCENARIO_LIBRARY.find(x=>x.id==="scenario-old-house")));return deepClone(state)}};`;
vm.createContext(sandbox);vm.runInContext(source,sandbox,{filename:"v154-operation-id-runtime.js"});
const api=sandbox.__test;let passed=0;function test(name,fn){fn();passed++;console.log(`PASS ${name}`)}
function raw(baseRevision,stateChanges=[],campaignChanges=[]){return{protocolVersion:"1.3",requestId:"r-id",baseRevision,decision:"no_check",narrative:"保持当前地点。",check:null,stateChanges,campaignChanges,locationEffect:{type:"stay",targetNodeId:null},nodeProposal:null,endingProposal:null,actionSuggestions:[]}}
function parse(changes=[],campaign=[]){const s=api.reset();return api.validateAiResponse(raw(s.revision,changes,campaign),{requestId:"r-id",baseRevision:s.revision,stage:"action_adjudication"})}

test("updateNpc.id 精确归一为 npcId",()=>{const parsed=parse([{operation:"updateNpc",id:"old-butler",claim:"承认听见墙内动静"}]);assert.equal(parsed.stateChanges[0].npcId,"old-butler");const prepared=api.prepareStateChanges(parsed.stateChanges,parsed.campaignChanges);assert.ok(prepared.draft.npcs.find(n=>n.id==="old-butler").continuity.claims.includes("承认听见墙内动静"))});
test("显式 npcId 优先于通用 id",()=>{const parsed=parse([{operation:"updateNpc",id:"wrong",npcId:"old-butler",attitude:"合作"}]);assert.equal(parsed.stateChanges[0].npcId,"old-butler")});
test("不存在 NPC 即使用 id 别名仍严格拒绝",()=>{const parsed=parse([{operation:"updateNpc",id:"missing-npc",attitude:"合作"}]);assert.throws(()=>api.prepareStateChanges(parsed.stateChanges,parsed.campaignChanges),/NPC 不存在/)});
test("removeItem.id 只归一为 itemId",()=>{const parsed=parse([{operation:"removeItem",id:"item-a"}]);assert.equal(parsed.stateChanges[0].itemId,"item-a");assert.equal(parsed.stateChanges[0].npcId,undefined)});
test("removeStatus.id 只归一为 statusId",()=>{const parsed=parse([{operation:"removeStatus",id:"status-a"}]);assert.equal(parsed.stateChanges[0].statusId,"status-a")});
test("revealClue.id 只归一为 clueId",()=>{const parsed=parse([{operation:"revealClue",id:"old-key",sourceRouteId:"old-key-automatic"}]);assert.equal(parsed.stateChanges[0].clueId,"old-key")});
test("resolveLead.id 归一为 leadId",()=>{const parsed=parse([],[{operation:"resolveLead",id:"lead-a"}]);assert.equal(parsed.campaignChanges[0].leadId,"lead-a")});
test("resolveQuestion.id 归一为 questionId",()=>{const parsed=parse([],[{operation:"resolveQuestion",id:"question-a"}]);assert.equal(parsed.campaignChanges[0].questionId,"question-a")});
test("advanceClock.id 归一为 clockId 且 by 仍归一 amount",()=>{const parsed=parse([],[{operation:"advanceClock",id:"clock-a",by:1}]);assert.equal(parsed.campaignChanges[0].clockId,"clock-a");assert.equal(parsed.campaignChanges[0].amount,1)});
test("未知 operation 的 id 不会被猜测成任何类型化字段",()=>{const s=api.reset(),input=raw(s.revision,[{operation:"unknownOperation",id:"x"}],[]);assert.throws(()=>api.validateAiResponse(input,{requestId:"r-id",baseRevision:s.revision,stage:"action_adjudication"}),/非法操作/)});
const protocolSource=fs.readFileSync(path.join(root,"src/ai-protocol.js"),"utf8");
test("请求提示明确要求类型化实体 ID",()=>assert.ok(protocolSource.includes("实体操作请使用类型化 ID 字段")));
console.log(`V154_OPERATION_ID_ALIAS_TESTS:${passed}:PASS`);
