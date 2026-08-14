"use strict";
const fs=require("fs"),path=require("path"),vm=require("vm"),assert=require("assert"),{webcrypto}=require("crypto"),{TextEncoder,TextDecoder}=require("util");
const NativeAbortController=AbortController,root=path.resolve(__dirname,"..");
const files=["scenarios/library.js","state.js","check-engine.js","scenario-engine.js","case-integrity.js","memory.js","ai-protocol.js","player-action-guard.js","interaction-availability.js","saves.js","api-response-resilience.js"];
function storage(){const map=new Map();return{getItem:k=>map.has(String(k))?map.get(String(k)):null,setItem:(k,v)=>map.set(String(k),String(v)),removeItem:k=>map.delete(String(k)),clear:()=>map.clear(),key:i=>Array.from(map.keys())[i]??null,get length(){return map.size}}}
const localStorage=storage(),sessionStorage=storage();let mode="timeout",lastController=null;
class TestAbortController extends NativeAbortController{constructor(){super();lastController=this}}
const sandbox={Object,Array,JSON,Map,Set,console,crypto:webcrypto,TextEncoder,TextDecoder,URL,AbortController:TestAbortController,Blob,structuredClone,
  fetch:async()=>{if(mode==="user"&&lastController&&!lastController.signal.aborted)lastController.abort("user");throw new TypeError("invalid_argument")},
  setTimeout:fn=>{if(mode==="timeout")fn();return 1},clearTimeout(){},setInterval:()=>0,clearInterval(){},
  window:{localStorage,sessionStorage,addEventListener(){}},document:{querySelector(){return null},querySelectorAll(){return[]},createElement(){return{className:"",textContent:"",style:{},classList:{add(){},remove(){},toggle(){}},appendChild(){},remove(){},click(){},insertAdjacentHTML(){},scrollHeight:0,scrollTop:0}},body:{appendChild(){}}},confirm:()=>false,renderAll(){},renderTopbar(){},renderSidebar(){},renderChat(){},renderChatLog(){},renderChatComposer(){},renderSaves(){},toast(){}};
sandbox.globalThis=sandbox;
const source=files.map(file=>fs.readFileSync(path.join(root,"src",file),"utf8")).join("\n\n")+`\n;scheduleAutosave=()=>{};renderAll=()=>{};renderTopbar=()=>{};renderSidebar=()=>{};renderChat=()=>{};renderChatLog=()=>{};renderChatComposer=()=>{};renderSaves=()=>{};toast=()=>{};
function __ready(){state=makeInitialState();state.character={system:"coc7",name:"Transport",hp:10,maxHp:10,san:60,maxSan:99,luck:50,attributes:{str:50,con:50,siz:50,dex:50,app:50,int:50,pow:60,edu:50},skills:[]};state.config.apiUrl="https://api.deepseek.com/v1/chat/completions";state.config.model="deepseek-v4-flash";saveApiKey("test-key",false,{apiUrl:state.config.apiUrl});state.runtime.activeRequestId="transport-test";return deepClone(state)}
async function __call(){return await callChatCompletion([{role:"user",content:"x"}],{timeoutMs:5000,jsonMode:true})}
function __retryable(code){return apiResponseRetryable(protocolError(code,code))}
globalThis.__test={ready:__ready,call:__call,retryable:__retryable};`;
vm.createContext(sandbox);vm.runInContext(source,sandbox,{filename:"v160-transport-abort-runtime.js"});
const api=sandbox.__test;let passed=0;async function test(name,fn){await fn();passed++;console.log(`PASS ${name}`)}
(async()=>{
await test("本地 timeout reason 即使表现为 Node TypeError 仍精确分类为 provider timeout",async()=>{mode="timeout";api.ready();await assert.rejects(()=>api.call(),e=>e?.code==="AI_PROVIDER_TIMEOUT"&&e?.details?.retryable===true)});
await test("provider timeout 仍属于 API Resilience 可重试错误",async()=>{assert.equal(api.retryable("AI_PROVIDER_TIMEOUT"),true)});
await test("用户主动 abort reason 保持取消语义而不是 provider timeout",async()=>{mode="user";api.ready();await assert.rejects(()=>api.call(),e=>e?.code==="AI_REQUEST_CANCELLED")});
await test("用户主动取消不会进入 provider 自动重试集合",async()=>{assert.equal(api.retryable("AI_REQUEST_CANCELLED"),false)});
console.log(`V160_TRANSPORT_ABORT_CLASSIFICATION_TESTS:${passed}:PASS`);
})().catch(error=>{console.error(error?.stack||error);process.exitCode=1});
