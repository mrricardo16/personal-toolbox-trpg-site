"use strict";

const fs = require("fs");
const path = require("path");
const vm = require("vm");
const { webcrypto } = require("crypto");
const { TextEncoder, TextDecoder } = require("util");

const root = path.resolve(__dirname, "../../../..");
const files = ["scenarios/library.js", "state.js", "check-engine.js", "scenario-engine.js", "case-integrity.js", "memory.js", "ai-protocol.js", "player-action-guard.js", "interaction-availability.js", "saves.js", "api-response-resilience.js", "progress-semantics.js", "authored-threat-clock.js", "npc-knowledge-boundary.js", "ending-resolution-gate.js", "coc-resolution-engine.js", "coc-consequence-contract.js", "failure-forward-cost-engine.js", "san-loss-resolution.js", "san-loss-window.js", "hp-damage-state.js"];

function storage() {
  const map = new Map();
  return { getItem: key => map.get(String(key)) ?? null, setItem: (key, value) => map.set(String(key), String(value)), removeItem: key => map.delete(String(key)), clear: () => map.clear(), key: index => Array.from(map.keys())[index] ?? null, get length() { return map.size; } };
}

const sandbox = {
  fs, path, root, Object, Array, JSON, Map, Set, Math, Number, String, Boolean, console, crypto: webcrypto, TextEncoder, TextDecoder, URL, AbortController, Blob, structuredClone,
  fetch: async () => { throw new Error("fetch not expected"); }, setTimeout: () => 0, clearTimeout() {}, setInterval: () => 0, clearInterval() {},
  window: { localStorage: storage(), sessionStorage: storage(), addEventListener() {} },
  document: { addEventListener() {}, querySelector() { return null; }, querySelectorAll() { return []; }, createElement() { return { style: {}, dataset: {}, classList: { add() {}, remove() {}, toggle() {} }, appendChild() {}, remove() {}, addEventListener() {}, querySelector() { return null; }, querySelectorAll() { return []; } }; }, body: { appendChild() {} } },
  navigator: { clipboard: { writeText: async () => {} } }, confirm: () => false, alert() {}, renderAll() {}, renderTopbar() {}, renderSidebar() {}, renderChat() {}, renderChatLog() {}, renderChatComposer() {}, renderSaves() {}, toast() {}
};
sandbox.globalThis = sandbox;
const source = files.map(file => fs.readFileSync(path.join(root, "src", file), "utf8")).join("\n\n");
vm.createContext(sandbox);
vm.runInContext(source, sandbox, { filename: "hp-damage-reference.js" });
vm.runInContext(`
  globalThis.__runHpFixture = input => {
    const initial = input.initial;
    state = { character: { system: "coc7", hp: initial.currentHp, maxHp: initial.maxHp, attributes: { con: initial.con }, healthState: { history: [] } } };
    normalizeHpDamageState(state.character);
    for (const command of input.commands) {
      const character = state.character;
      if (character.healthState.history.some(item => item.eventKey === command.eventKey) || character.healthState.dead?.active === true) continue;
      const hpBefore = character.hp;
      const hpAfter = Math.max(0, Math.min(character.maxHp, hpBefore - command.damage));
      character.hp = hpAfter;
      hpDamageApplyEvent({ eventKey: command.eventKey, damage: command.damage, hpBefore, hpAfter, maxHp: character.maxHp }, { roller: () => command.conRoll ?? 1 });
    }
    return { hp: state.character.hp, health: hpDamageStateSnapshot(state.character) };
  };
`, sandbox);

const inputs = [
  { name: "minor-damage", initial: { currentHp: 12, maxHp: 12, con: 60 }, commands: [{ eventKey: "minor", damage: 5 }] },
  { name: "major-wound-con-success", initial: { currentHp: 12, maxHp: 12, con: 60 }, commands: [{ eventKey: "half", damage: 6, conRoll: 60 }] },
  { name: "major-wound-con-failure", initial: { currentHp: 12, maxHp: 12, con: 60 }, commands: [{ eventKey: "con-fail", damage: 6, conRoll: 61 }] },
  { name: "instant-death", initial: { currentHp: 12, maxHp: 12, con: 60 }, commands: [{ eventKey: "instant", damage: 12 }] },
  { name: "zero-hp-without-major-wound", initial: { currentHp: 3, maxHp: 12, con: 60 }, commands: [{ eventKey: "zero", damage: 3 }] },
  { name: "major-wound-then-dying", initial: { currentHp: 8, maxHp: 12, con: 60 }, commands: [{ eventKey: "major", damage: 6, conRoll: 1 }, { eventKey: "zero", damage: 2 }] },
  { name: "separate-small-damage-events", initial: { currentHp: 12, maxHp: 12, con: 60 }, commands: [{ eventKey: "one", damage: 3 }, { eventKey: "two", damage: 3 }] },
  { name: "overkill-uses-original-damage", initial: { currentHp: 2, maxHp: 10, con: 60 }, commands: [{ eventKey: "overkill", damage: 10 }] },
  { name: "deduplicated-event", initial: { currentHp: 12, maxHp: 12, con: 60 }, commands: [{ eventKey: "duplicate", damage: 6, conRoll: 60 }, { eventKey: "duplicate", damage: 6, conRoll: 99 }] },
  { name: "already-dead-does-not-apply-next-event", initial: { currentHp: 12, maxHp: 12, con: 60 }, commands: [{ eventKey: "death", damage: 12 }, { eventKey: "after-death", damage: 1 }] }
];

function runReference(testCase) {
  const initial = testCase.initial;
  const result = sandbox.__runHpFixture(testCase);
  const health = result.health;
  const event = item => item ? ({ eventKey: item.eventKey, damage: item.damage, majorWound: item.majorWound, instantDeath: item.instantDeath, conCheck: item.conCheck ? { roll: item.conCheck.roll, target: item.conCheck.target, success: item.conCheck.success } : null }) : null;
  return {
    currentHp: result.hp,
    maxHp: initial.maxHp,
    con: initial.con,
    majorWound: health.majorWound?.active === true,
    unconscious: health.unconscious?.active === true,
    dying: health.dying?.active === true,
    dead: health.dead?.active === true,
    history: health.history.map(event),
    lastEvent: event(health.lastDamageEvent)
  };
}

const fixture = {
  version: 1,
  referenceSource: "src/hp-damage-state.js",
  cases: inputs.map(testCase => ({ name: testCase.name, initial: { ...testCase.initial, majorWound: false, unconscious: false, dying: false, dead: false, history: [], lastEvent: null }, commands: testCase.commands, expected: runReference(testCase) }))
};
const outputPath = path.join(root, "multiplayer", "server", "tests", "Trpg.Multiplayer.Api.Tests", "Fixtures", "hp-damage.json");
fs.writeFileSync(outputPath, `${JSON.stringify(fixture, null, 2)}\n`, "utf8");
console.log(`HP_DAMAGE_CONFORMANCE_FIXTURE:PASS cases=${fixture.cases.length} path=${path.relative(root, outputPath)}`);
