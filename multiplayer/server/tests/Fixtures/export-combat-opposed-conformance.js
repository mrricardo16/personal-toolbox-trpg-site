"use strict";

const fs = require("fs");
const path = require("path");
const vm = require("vm");
const { webcrypto } = require("crypto");
const { TextEncoder, TextDecoder } = require("util");

const root = path.resolve(__dirname, "../../../..");
const fixedTimestamp = "2026-08-25T12:00:00.000Z";
const referenceSources = [
  "src/check-engine.js",
  "src/coc-resolution-engine.js",
  "src/hp-damage-state.js",
  "src/health-stabilization.js",
  "src/combat-opposed.js"
];
const sourceFiles = [
  "scenarios/library.js", "state.js", "check-engine.js", "scenario-engine.js", "case-integrity.js",
  "memory.js", "ai-protocol.js", "player-action-guard.js", "interaction-availability.js", "saves.js",
  "api-response-resilience.js", "progress-semantics.js", "authored-threat-clock.js", "npc-knowledge-boundary.js",
  "ending-resolution-gate.js", "coc-resolution-engine.js", "coc-consequence-contract.js",
  "failure-forward-cost-engine.js", "san-loss-resolution.js", "san-loss-window.js", "hp-damage-state.js",
  "health-stabilization.js", "combat-opposed.js"
];

function storage() {
  const map = new Map();
  return { getItem: key => map.get(String(key)) ?? null, setItem: (key, value) => map.set(String(key), String(value)), removeItem: key => map.delete(String(key)), clear: () => map.clear(), key: index => Array.from(map.keys())[index] ?? null, get length() { return map.size; } };
}

const RealDate = Date;
class FixedDate extends RealDate {
  constructor(...args) { super(...(args.length ? args : [fixedTimestamp])); }
  static now() { return RealDate.parse(fixedTimestamp); }
}

const sandbox = {
  fs, path, root, Object, Array, JSON, Map, Set, Math, Number, String, Boolean, Date: FixedDate, console,
  crypto: webcrypto, TextEncoder, TextDecoder, URL, AbortController, Blob, structuredClone,
  fetch: async () => { throw new Error("fetch not expected"); }, setTimeout: () => 0, clearTimeout() {}, setInterval: () => 0, clearInterval() {},
  window: { localStorage: storage(), sessionStorage: storage(), addEventListener() {} },
  document: { addEventListener() {}, querySelector() { return null; }, querySelectorAll() { return []; }, createElement() { return { className: "", textContent: "", innerHTML: "", value: "", checked: false, disabled: false, style: {}, dataset: {}, classList: { add() {}, remove() {}, toggle() {} }, appendChild() {}, remove() {}, click() {}, insertAdjacentHTML() {}, setAttribute() {}, removeAttribute() {}, addEventListener() {}, querySelector() { return null; }, querySelectorAll() { return []; }, scrollHeight: 0, scrollTop: 0 }; }, body: { appendChild() {} } },
  navigator: { clipboard: { writeText: async () => {} } }, confirm: () => false, alert() {}, renderAll() {}, renderTopbar() {}, renderSidebar() {}, renderChat() {}, renderChatLog() {}, renderChatComposer() {}, renderSaves() {}, toast() {}
};
sandbox.globalThis = sandbox;
vm.createContext(sandbox);
vm.runInContext(sourceFiles.map(file => fs.readFileSync(path.join(root, "src", file), "utf8")).join("\n\n"), sandbox, { filename: "combat-opposed-reference.js" });
vm.runInContext("randomInt = () => 1; addLog = () => {}; scheduleAutosave = () => {}; renderAll = () => {};", sandbox, { filename: "combat-opposed-fixture-shims.js" });

function run(expression) { return vm.runInContext(expression, sandbox, { filename: "combat-opposed-fixture-operation.js" }); }
function json(value) { return JSON.stringify(value); }

function emptyHealthState() {
  return { version: "1.0", authority: "browser_coc_health", majorWound: null, unconscious: null, dying: null, dead: null, lastDamageEvent: null, history: [], stabilized: null, treatmentHistory: [] };
}

function fixtureCharacter({ dex = 90, hp = 12, maxHp = 12 } = {}) {
  return { system: "coc7", name: "Fixture Investigator", hp, maxHp, attributes: { dex, con: 60 }, skills: [{ id: "fighting_brawl", name: "斗殴", value: 60 }, { id: "dodge", name: "闪避", value: 60 }], healthState: emptyHealthState() };
}

function resetCharacter(character = fixtureCharacter()) {
  run(`state = makeInitialState(); state.character = ${json(character)}; normalizeHpDamageState(state.character);`);
}

function startCombat(opponents = [{ id: "opponent-1", label: "Fixture Opponent", dex: 60, fighting: 60, dodge: 60 }], character = fixtureCharacter()) {
  resetCharacter(character);
  return run(`combatStart({ opponents: ${json(opponents)} })`);
}

function applyFixtureDamage(operation) {
  return run(`(() => { const hpBefore = state.character.hp; const hpAfter = Math.max(0, Math.min(state.character.maxHp, hpBefore - ${json(operation.damage)})); state.character.hp = hpAfter; return hpDamageApplyEvent({ eventKey: ${json(operation.eventKey)}, source: "fixture", sourceId: ${json(operation.eventKey)}, index: 0, damage: ${json(operation.damage)}, hpBefore, hpAfter, maxHp: state.character.maxHp }, { roller: () => ${json(operation.roll)} }); })()`);
}

function mapParticipant(participant) {
  return { id: participant.id, label: participant.label, kind: participant.kind, side: participant.side, dex: participant.dex, fighting: participant.fighting, dodge: participant.dodge, responsePreference: participant.responsePreference, responseAllowance: participant.responseAllowance, active: participant.active };
}

function mapCombatSnapshot(snapshot) {
  return { active: snapshot.active, round: snapshot.round, turnIndex: snapshot.turnIndex, currentActorId: snapshot.currentActorId, order: snapshot.order, participants: snapshot.participants.map(mapParticipant), responseCounts: snapshot.responseCounts, actionCounts: snapshot.actionCounts };
}

function executeScenario(input) {
  resetCharacter(input.setup.character);
  const outputs = [];
  if (input.setup.damage) outputs.push({ kind: "damage", result: applyFixtureDamage(input.setup.damage) });
  for (const operation of input.operations) {
    if (operation.kind === "combatStart") outputs.push({ kind: operation.kind, result: run(`combatStart({ opponents: ${json(input.setup.opponents)} })`) });
    if (operation.kind === "passTurn") outputs.push({ kind: operation.kind, result: run(`combatPassTurn({ dyingRoller: ${operation.dyingRoll == null ? "null" : `() => ${json(operation.dyingRoll)}`} })`) });
    if (operation.kind === "melee") outputs.push({ kind: operation.kind, result: run(`combatResolveMelee({ defenderId: ${json(operation.defenderId)}, response: ${json(operation.response)}, attackerRoller: () => ${json(operation.attackerRoll)}, defenderRoller: () => ${json(operation.defenderRoll)} })`) });
    if (operation.kind === "snapshot") outputs.push({ kind: operation.kind, result: run("combatSnapshot()") });
  }
  return outputs;
}

function scenarioCase(name, input, expected) {
  return generalizationCase(name, input, expected);
}

function checkFixture(roll) {
  const target = 60;
  const result = run(`combatRoll(${target}, { roller: () => ${json(roll)} })`);
  return { roll: result.total, target: result.target, difficulty: result.difficulty, difficultyTarget: result.difficultyTarget, successLevel: result.rank, passed: result.result, critical: result.rank === "critical", fumble: result.rank === "fumble" };
}

function mapDisposition(disposition) {
  return disposition ? { ownerId: disposition.ownerId, targetId: disposition.targetId, mode: disposition.mode, pending: disposition.pending === true, hpCommitted: disposition.hpCommitted === true } : null;
}

function mapMeleeCase(name, response, attackerRoll, defenderRoll) {
  startCombat();
  const result = run(`combatResolveMelee({ defenderId: "opponent-1", response: ${json(response)}, attackerRoller: () => ${json(attackerRoll)}, defenderRoller: () => ${json(defenderRoll)} })`);
  const exchange = result.exchange;
  return {
    name,
    scope: "reference_conformance",
    input: { response, attackerId: exchange.attackerId, defenderId: exchange.defenderId, attacker: checkFixture(attackerRoll), defender: checkFixture(defenderRoll) },
    expected: { outcome: exchange.outcome, winnerSide: exchange.winnerId === exchange.attackerId ? "attacker" : exchange.winnerId === exchange.defenderId ? "defender" : null, damageMode: exchange.damageDisposition?.mode ?? null, attackerRollRank: run(`combatRankLevel(${json(exchange.attackerRoll.rank)})`), defenderRollRank: run(`combatRankLevel(${json(exchange.defenderRoll.rank)})`), error: null, damageDisposition: mapDisposition(exchange.damageDisposition) }
  };
}

function semanticError(error) {
  const message = String(error?.message || error);
  if (message.includes("必须在 1 到 100 之间")) return "InvalidOpponentStat";
  throw new Error(`Unmapped combat fixture error: ${message}`);
}

function generalizationCase(name, input, expected) {
  return { name, scope: "multiplayer_generalization", input, expected };
}

const standardOpponents = [{ id: "opponent-1", label: "Fixture Opponent", dex: 60, fighting: 60, dodge: 60 }];
const outnumberedOpponents = [{ id: "opponent-1", label: "Primary", dex: 60, fighting: 60, dodge: 60 }, { id: "opponent-2", label: "Secondary", dex: 10, fighting: 60, dodge: 60 }];
const dyingCharacter = fixtureCharacter({ hp: 6, maxHp: 12 });
const dyingDamage = { kind: "damage", eventKey: "combat-dying-source", damage: 6, roll: 1 };

function participantNormalizationCase() {
  const input = { setup: { character: fixtureCharacter(), opponents: [{ id: "opponent-raw", name: "Raw Opponent", dex: 55, fighting: 45, dodge: 35, responsePreference: "unexpected", responseAllowance: 0 }] }, operations: [{ kind: "combatStart" }, { kind: "snapshot" }] };
  const outputs = executeScenario(input);
  const snapshot = outputs.find(output => output.kind === "snapshot").result;
  return scenarioCase("participant-normalization", input, { normalized: mapCombatSnapshot(snapshot), damageDisposition: null });
}

function invalidOpponentStatCase() {
  const input = { setup: { character: fixtureCharacter(), opponents: [{ id: "bad", label: "Bad opponent", dex: 101, fighting: 60, dodge: 60 }] }, operations: [{ kind: "combatStart" }] };
  resetCharacter(input.setup.character);
  try {
    run(`combatStart({ opponents: ${json(input.setup.opponents)} })`);
    throw new Error("Expected invalid opponent stat rejection");
  } catch (error) {
    return generalizationCase("invalid-opponent-stat", input, { error: semanticError(error), damageDisposition: null });
  }
}

function orderCase(name, participants) {
  const order = run(`combatOrder(${json(participants)})`);
  return generalizationCase(name, { participants }, { order, damageDisposition: null });
}

function responseAllowanceCase() {
  const input = { setup: { character: fixtureCharacter(), opponents: outnumberedOpponents }, operations: [{ kind: "combatStart" }, { kind: "passTurn" }, { kind: "melee", defenderId: "player", response: "dodge", attackerRoll: 50, defenderRoll: 50 }, { kind: "melee", defenderId: "player", response: "dodge", attackerRoll: 50, defenderRoll: 50 }] };
  const outputs = executeScenario(input);
  const exchanges = outputs.filter(output => output.kind === "melee").map(output => output.result.exchange);
  return scenarioCase("response-allowance-outnumbered", input, { firstOutnumberedBonus: exchanges[0].outnumberedBonus, secondOutnumberedBonus: exchanges[1].outnumberedBonus, responseCountBeforeSecond: exchanges[1].responseCountBefore, damageDisposition: null });
}

function passTurnCase() {
  const input = { setup: { character: fixtureCharacter(), opponents: standardOpponents }, operations: [{ kind: "combatStart" }, { kind: "passTurn" }, { kind: "passTurn" }] };
  const outputs = executeScenario(input);
  const turns = outputs.filter(output => output.kind === "passTurn").map(output => output.result);
  const second = turns[1];
  return scenarioCase("pass-turn-and-round-wrap", input, { firstActorId: turns[0].entry.actorId, wrapped: second.wrap?.wrapped === true, finishedRound: second.wrap?.finishedRound ?? null, round: second.state.round, currentActorId: second.state.currentActorId, damageDisposition: null });
}

function dyingScenarioInput(operationCount) {
  return { setup: { character: dyingCharacter, damage: dyingDamage, opponents: standardOpponents }, operations: [{ kind: "combatStart" }, ...Array.from({ length: operationCount }, () => ({ kind: "passTurn", dyingRoll: 1 }))] };
}

function dyingObservationCase() {
  const input = { setup: { character: dyingCharacter, damage: dyingDamage, opponents: standardOpponents }, operations: [{ kind: "combatStart" }, { kind: "snapshot" }] };
  const outputs = executeScenario(input);
  const state = outputs.find(output => output.kind === "snapshot").result;
  return scenarioCase("active-combat-dying-observation", input, { active: state.active, observedRound: state.playerDyingObservedRound, lastAutoDyingCheckRound: state.lastAutoDyingCheckRound, damageDisposition: null });
}

function followingDyingRoundCase() {
  const input = dyingScenarioInput(4);
  const outputs = executeScenario(input);
  const final = outputs.filter(output => output.kind === "passTurn").at(-1).result;
  return scenarioCase("first-following-eligible-dying-round", input, { finishedRound: final.wrap?.finishedRound ?? null, dyingCheckOrdinal: final.wrap?.dyingResult?.record?.ordinal ?? null, lastAutoDyingCheckRound: final.state.lastAutoDyingCheckRound, damageDisposition: null });
}

const cases = [
  participantNormalizationCase(),
  invalidOpponentStatCase(),
  orderCase("stable-dex-order", [{ id: "slow", dex: 30 }, { id: "fast", dex: 80 }, { id: "middle", dex: 50 }]),
  orderCase("equal-dex-input-order", [{ id: "first", dex: 60 }, { id: "second", dex: 60 }, { id: "third", dex: 60 }]),
  mapMeleeCase("dodge-equal-regular", "dodge", 50, 50),
  mapMeleeCase("dodge-attacker-higher", "dodge", 30, 50),
  mapMeleeCase("dodge-both-fail", "dodge", 90, 90),
  mapMeleeCase("fight-back-equal-regular", "fight_back", 50, 50),
  mapMeleeCase("fight-back-defender-higher", "fight_back", 50, 30),
  mapMeleeCase("fight-back-attacker-failure-defender-success", "fight_back", 90, 50),
  mapMeleeCase("fight-back-both-fail", "fight_back", 90, 90),
  mapMeleeCase("initiator-extreme-eligibility", "dodge", 10, 50),
  mapMeleeCase("fight-back-regular-cap", "fight_back", 50, 10),
  responseAllowanceCase(),
  passTurnCase(),
  mapMeleeCase("no-winning-disposition-is-null", "dodge", 90, 90),
  mapMeleeCase("winning-disposition-is-pending", "dodge", 30, 50),
  dyingObservationCase(),
  followingDyingRoundCase()
];

const fixture = { version: 1, referenceSources, cases };
const outputPath = path.join(root, "multiplayer", "server", "tests", "Trpg.Multiplayer.Api.Tests", "Fixtures", "combat-opposed.json");
fs.mkdirSync(path.dirname(outputPath), { recursive: true });
fs.writeFileSync(outputPath, `${JSON.stringify(fixture, null, 2)}\n`, "utf8");
console.log(`COMBAT_OPPOSED_CONFORMANCE_FIXTURE:PASS cases=${cases.length} path=${path.relative(root, outputPath)}`);
