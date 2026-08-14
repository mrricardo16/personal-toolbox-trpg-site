"use strict";

const fs = require("fs");
const path = require("path");

const root = path.resolve(__dirname, "..");
const sourceRoot = path.join(root, "src");
const outputPath = path.join(root, "outputs", "trpg-dm-assistant.html");
const moduleFiles = [
  "scenarios/library.js",
  "state.js",
  "check-engine.js",
  "scenario-engine.js",
  "case-integrity.js",
  "memory.js",
  "ai-protocol.js",
  "player-action-guard.js",
  "interaction-availability.js",
  "saves.js",
  "ui.js",
  "api-response-resilience.js",
  "progress-semantics.js",
  "authored-threat-clock.js",
  "npc-knowledge-boundary.js",
  "ending-resolution-gate.js",
  "coc-resolution-engine.js",
  "coc-consequence-contract.js",
  "failure-forward-cost-engine.js",
  "san-loss-resolution.js",
  "san-loss-window.js",
  "hp-damage-state.js",
  "health-stabilization.js",
  "healing-recovery.js",
  "combat-opposed.js",
  "combat-damage.js",
  "firearms-impaling.js"
];

function readUtf8(filePath) {
  const bytes = fs.readFileSync(filePath);
  return new TextDecoder("utf-8", { fatal: true }).decode(bytes).replace(/\r\n/g, "\n");
}

function readSource(relativePath) {
  const filePath = path.join(sourceRoot, relativePath);
  if (!fs.existsSync(filePath)) throw new Error(`缺少构建输入：src/${relativePath}`);
  return readUtf8(filePath);
}

const shell = readSource("shell.template");
const styles = readSource("styles.css").trimEnd();
const scripts = moduleFiles.map(relativePath => {
  const source = readSource(relativePath).trimEnd();
  return `/* src/${relativePath} */\n${source}`;
}).join("\n\n");

if (!shell.includes("<!-- STYLES -->") || !shell.includes("<!-- SCRIPTS -->")) {
  throw new Error("src/shell.template 必须包含 STYLES 与 SCRIPTS 占位符");
}

const output = shell
  .replace("<!-- STYLES -->", () => styles)
  .replace("<!-- SCRIPTS -->", () => scripts)
  .replace(/\r\n/g, "\n");

fs.mkdirSync(path.dirname(outputPath), { recursive: true });
if (!fs.existsSync(outputPath) || fs.readFileSync(outputPath, "utf8") !== output) {
  fs.writeFileSync(outputPath, output, "utf8");
}
console.log(`Built ${path.relative(root, outputPath)} (${Buffer.byteLength(output, "utf8")} bytes)`);