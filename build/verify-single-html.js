"use strict";

const fs = require("fs");
const path = require("path");
const vm = require("vm");

const root = path.resolve(__dirname, "..");
const output = path.join(root, "outputs", "trpg-dm-assistant.html");
const requiredSources = [
  "src/state.js",
  "src/check-engine.js",
  "src/ai-protocol.js",
  "src/scenario-engine.js",
  "src/case-integrity.js",
  "src/memory.js",
  "src/player-action-guard.js",
  "src/interaction-availability.js",
  "src/saves.js",
  "src/ui.js",
  "src/api-response-resilience.js",
  "src/progress-semantics.js",
  "src/authored-threat-clock.js",
  "src/npc-knowledge-boundary.js",
  "src/ending-resolution-gate.js",
  "src/coc-resolution-engine.js",
  "src/coc-consequence-contract.js",
  "src/failure-forward-cost-engine.js",
  "src/san-loss-resolution.js",
  "src/san-loss-window.js",
  "src/hp-damage-state.js",
  "src/health-stabilization.js",
  "src/healing-recovery.js",
  "src/combat-opposed.js",
  "src/combat-damage.js",
  "src/firearms-impaling.js",
  "src/scenarios/library.js",
  "src/styles.css",
  "src/shell.template"
];

function fail(message) {
  throw new Error(message);
}

for (const relativePath of requiredSources) {
  if (!fs.existsSync(path.join(root, relativePath))) fail(`缺少模块化源码：${relativePath}`);
}
if (fs.existsSync(path.join(root, "index.html"))) fail("旧 index.html 会形成第二个 TRPG 产品入口");
if (!fs.existsSync(output)) fail("缺少构建产物：outputs/trpg-dm-assistant.html");

for (const relativePath of [...requiredSources, "outputs/trpg-dm-assistant.html"]) {
  const filePath = path.join(root, relativePath);
  const bytes = fs.readFileSync(filePath);
  let text;
  try {
    text = new TextDecoder("utf-8", { fatal: true }).decode(bytes);
  } catch {
    fail(`不是严格 UTF-8：${relativePath}`);
  }
  if (text.includes("\uFFFD")) fail(`包含替换字符：${relativePath}`);
}

const html = fs.readFileSync(output, "utf8");
const librarySource = fs.readFileSync(path.join(root, "src/scenarios/library.js"), "utf8");
const appVersion = librarySource.match(/const APP_VERSION = "([^"]+)";/)?.[1];
const schemaVersion = librarySource.match(/const SCHEMA_VERSION = (\d+);/)?.[1];
if (!appVersion || !html.includes(`const APP_VERSION = "${appVersion}";`)) fail("输出版本号与源码不一致");
if (!schemaVersion || !html.includes(`const SCHEMA_VERSION = ${schemaVersion};`)) fail("输出 Schema 与源码不一致");
if (!html.includes('const NPC_KNOWLEDGE_BOUNDARY_VERSION="1.0";')) fail("输出缺少 NPC Knowledge Boundary 模块");
if (!html.includes('const ENDING_RESOLUTION_GATE_VERSION="1.0";')) fail("输出缺少 Ending / Resolution Gate 模块");
if (!html.includes('const COC_RESOLUTION_ENGINE_VERSION="1.0";')) fail("输出缺少 CoC Resolution Engine 模块");
if (!html.includes('const COC_CONSEQUENCE_CONTRACT_VERSION="1.0";')) fail("输出缺少 Mechanical Consequence Contract 模块");
if (!html.includes('const FAILURE_FORWARD_COST_ENGINE_VERSION="1.0";')) fail("输出缺少 Failure-Forward Cost Engine 模块");
if (!html.includes('const SAN_LOSS_RESOLUTION_VERSION="1.0";')) fail("输出缺少 SAN Loss Resolution 模块");
if (!html.includes('const SAN_LOSS_WINDOW_VERSION="1.0";')) fail("输出缺少 SAN Loss Window 模块");
if (!html.includes('const HP_DAMAGE_STATE_VERSION="1.0";')) fail("输出缺少 HP Damage State 模块");
if (!html.includes('const HEALTH_STABILIZATION_VERSION="1.0";')) fail("输出缺少 Health Stabilization 模块");
if (!html.includes('const HEALING_RECOVERY_VERSION="1.0";')) fail("输出缺少 Healing Recovery 模块");
if (!html.includes('const COMBAT_OPPOSED_VERSION="1.0";')) fail("输出缺少 Combat Opposed 模块");
if (!html.includes('const COMBAT_DAMAGE_VERSION="1.0";')) fail("输出缺少 Combat Damage 模块");
if (!html.includes('const FIREARMS_IMPALING_VERSION="1.0";')) fail("输出缺少 Firearms / Impaling 模块");
if (/\b(?:eval|Function)\s*\(/.test(html)) fail("输出包含 eval/new Function 风险调用");
if (html.includes("window.__TRPG_TEST_API__")) fail("生产输出暴露测试接口");
if (/<script\b[^>]+\bsrc\s*=|<link\b[^>]+\bhref\s*=\s*["']https?:\/\//i.test(html)) fail("输出包含外部运行时资源");
if (/(?:https?:)?\/\/(?:cdn|unpkg|jsdelivr)\./i.test(html)) fail("输出包含 CDN 依赖");

const inlineScripts = [...html.matchAll(/<script>([\s\S]*?)<\/script>/gi)];
if (inlineScripts.length !== 1) fail(`输出必须恰好包含一个内联脚本，实际为 ${inlineScripts.length}`);
try {
  new vm.Script(inlineScripts[0][1], { filename: "trpg-dm-assistant.html:inline-script" });
} catch (error) {
  fail(`输出内联脚本语法错误：${error.message}`);
}

const symbols = new Map();
for (const relativePath of requiredSources.filter(file => file.endsWith(".js"))) {
  const source = fs.readFileSync(path.join(root, relativePath), "utf8");
  for (const match of source.matchAll(/^(?:async\s+)?function\s+([A-Za-z_$][\w$]*)\s*\(/gm)) {
    const name = match[1];
    if (symbols.has(name)) fail(`源模块存在重复正式函数：${name} (${symbols.get(name)} / ${relativePath})`);
    symbols.set(name, relativePath);
  }
  if (/\b(?:TODO|FIXME)\b/.test(source)) fail(`源模块存在旧残留标记：${relativePath}`);
}

console.log("VERIFY_SINGLE_HTML:PASS");