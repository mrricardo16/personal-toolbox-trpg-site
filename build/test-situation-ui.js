"use strict";
const fs=require("fs"),path=require("path"),vm=require("vm"),assert=require("assert");
const ui=fs.readFileSync(path.resolve(__dirname,"../src/ui.js"),"utf8"),library=fs.readFileSync(path.resolve(__dirname,"../src/scenarios/library.js"),"utf8");
const start=ui.indexOf("function tensionStage("),end=ui.indexOf("function situationMeter(",start);assert.ok(start>=0&&end>start,"无法提取剧情态势阶段函数");
const sandbox={clamp:(n,min,max)=>Math.min(max,Math.max(min,n)),Number,Math};sandbox.globalThis=sandbox;
vm.runInNewContext(ui.slice(start,end)+"\n;globalThis.api={tensionStage,investigationStage};",sandbox,{filename:"situation-stage-functions.js"});
const api=sandbox.api;let passed=0;function test(name,fn){fn();passed++;console.log(`PASS ${name}`)}
test("张力 1/6 显示局势平静",()=>assert.equal(api.tensionStage(1,6).label,"局势平静"));
test("张力 3/6 显示威胁正在行动",()=>assert.equal(api.tensionStage(3,6).label,"威胁正在行动"));
test("张力 3/6 说明不会直接修改骰点",()=>assert.match(api.tensionStage(3,6).description,/不会直接修改骰点/));
test("张力 6/6 显示危机爆发",()=>assert.equal(api.tensionStage(6,6).label,"危机爆发"));
test("调查进度 5 显示调查起步",()=>assert.equal(api.investigationStage(5).label,"调查起步"));
test("调查进度 50 显示案情展开",()=>assert.equal(api.investigationStage(50).label,"案情展开"));
test("调查进度 100 显示调查充分",()=>assert.equal(api.investigationStage(100).label,"调查充分"));
test("侧栏使用张力阶段说明",()=>assert.ok(ui.includes('situationMeter("张力"')));
test("侧栏使用调查进度阶段说明",()=>assert.ok(ui.includes('situationMeter("调查进度"')));
test("正式版本字段有效",()=>assert.match(library,/const APP_VERSION = "\d+\.\d+\.\d+";/));
console.log(`SITUATION_UI_TESTS:${passed}:PASS`);
