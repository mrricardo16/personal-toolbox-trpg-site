"use strict";

const fs = require("fs");
const path = require("path");
const vm = require("vm");
const { webcrypto } = require("crypto");
const { TextEncoder, TextDecoder } = require("util");

const root = path.resolve(__dirname, "../../../..");
const fixedTimestamp = "2026-08-25T12:00:00.000Z";
const sourceFiles = [
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
  "health-stabilization.js"
];

function storage() {
  const map = new Map();
  return {
    getItem: key => map.has(String(key)) ? map.get(String(key)) : null,
    setItem: (key, value) => map.set(String(key), String(value)),
    removeItem: key => map.delete(String(key)),
    clear: () => map.clear(),
    key: index => Array.from(map.keys())[index] ?? null,
    get length() { return map.size; }
  };
}

const realDate = Date;
class FixedDate extends realDate {
  constructor(...args) {
    super(...(args.length === 0 ? [fixedTimestamp] : args));
  }

  static now() {
    return realDate.parse(fixedTimestamp);
  }
}

const sandbox = {
  fs,
  path,
  root,
  Object,
  Array,
  JSON,
  Map,
  Set,
  Math,
  Number,
  String,
  Boolean,
  Date: FixedDate,
  console,
  crypto: webcrypto,
  TextEncoder,
  TextDecoder,
  URL,
  AbortController,
  Blob,
  structuredClone,
  fetch: async () => { throw new Error("fetch not expected"); },
  setTimeout: () => 0,
  clearTimeout() {},
  setInterval: () => 0,
  clearInterval() {},
  window: { localStorage: storage(), sessionStorage: storage(), addEventListener() {} },
  document: {
    addEventListener() {},
    querySelector() { return null; },
    querySelectorAll() { return []; },
    createElement() {
      return {
        className: "",
        textContent: "",
        innerHTML: "",
        value: "",
        checked: false,
        disabled: false,
        style: {},
        dataset: {},
        classList: { add() {}, remove() {}, toggle() {} },
        appendChild() {},
        remove() {},
        click() {},
        insertAdjacentHTML() {},
        setAttribute() {},
        removeAttribute() {},
        addEventListener() {},
        querySelector() { return null; },
        querySelectorAll() { return []; },
        scrollHeight: 0,
        scrollTop: 0
      };
    },
    body: { appendChild() {} }
  },
  navigator: { clipboard: { writeText: async () => {} } },
  confirm: () => false,
  alert() {},
  renderAll() {},
  renderTopbar() {},
  renderSidebar() {},
  renderChat() {},
  renderChatLog() {},
  renderChatComposer() {},
  renderSaves() {},
  toast() {}
};
sandbox.globalThis = sandbox;

const source = sourceFiles
  .map(file => fs.readFileSync(path.join(root, "src", file), "utf8"))
  .join("\n\n");
vm.createContext(sandbox);
vm.runInContext(source, sandbox, { filename: "health-stabilization-reference.js" });
vm.runInContext(`
  randomInt = () => 1;
  addLog = () => {};
  scheduleAutosave = () => {};
  renderAll = () => {};
`, sandbox, { filename: "health-stabilization-fixture-shims.js" });

function runInReference(expression) {
  return vm.runInContext(expression, sandbox, { filename: "health-stabilization-fixture-operation.js" });
}

function resetCharacter({ hp = 12, maxHp = 12, con = 60, firstAid = 50, healthState = null } = {}) {
  runInReference(`state = makeInitialState(); state.character = {
    system: "coc7",
    name: "Fixture Investigator",
    hp: ${JSON.stringify(hp)},
    maxHp: ${JSON.stringify(maxHp)},
    attributes: { con: ${JSON.stringify(con)} },
    skills: [{ id: "first_aid", name: "急救", value: ${JSON.stringify(firstAid)} }],
    healthState: ${JSON.stringify(healthState || emptyHealthState())}
  }; normalizeHpDamageState(state.character);`);
}

function emptyHealthState() {
  return {
    version: "1.0",
    authority: "browser_coc_health",
    majorWound: null,
    unconscious: null,
    dying: null,
    dead: null,
    lastDamageEvent: null,
    history: [],
    stabilized: null,
    treatmentHistory: []
  };
}

function applyDamage({ damage, roll = 1, eventKey }) {
  return runInReference(`(() => {
    const hpBefore = state.character.hp;
    const hpAfter = Math.max(0, Math.min(state.character.maxHp, hpBefore - ${JSON.stringify(damage)}));
    state.character.hp = hpAfter;
    return hpDamageApplyEvent({
      eventKey: ${JSON.stringify(eventKey)},
      source: "fixture",
      sourceId: ${JSON.stringify(eventKey)},
      index: 0,
      damage: ${JSON.stringify(damage)},
      hpBefore,
      hpAfter,
      maxHp: state.character.maxHp
    }, { roller: () => ${JSON.stringify(roll)} });
  })()`);
}

function setupDying() {
  resetCharacter({ hp: 6 });
  applyDamage({ damage: 6, roll: 1, eventKey: "dying-source" });
}

function setupDead() {
  setupDying();
  runInReference(`healthStabilizationResolveDyingRound({ roller: () => 99, sourceId: "setup-death" })`);
}

function setupStabilized() {
  setupDying();
  runInReference(`healthStabilizationResolveFirstAid({ target: 70, roller: () => 20, withinHour: true, sourceId: "setup-aid" })`);
}

function setupUnconscious() {
  resetCharacter();
  applyDamage({ damage: 6, roll: 99, eventKey: "unconscious-source" });
}

function setupUnconsciousAtMaxHp() {
  setupUnconscious();
  runInReference("state.character.hp = state.character.maxHp; normalizeHpDamageState(state.character)");
}

function mapDamageEvent(event) {
  if (!event) return null;
  return {
    eventKey: event.eventKey,
    damage: event.damage,
    majorWound: event.majorWound,
    instantDeath: event.instantDeath,
    conCheck: event.conCheck ? {
      roll: event.conCheck.roll,
      target: event.conCheck.target,
      success: event.conCheck.success
    } : null
  };
}

function mapDyingCheck(check) {
  return check ? {
    ordinal: check.ordinal,
    roll: check.roll,
    target: check.target,
    success: check.success,
    sourceId: check.sourceId ?? null,
    occurredAt: check.at ?? null
  } : null;
}

function mapTreatment(record) {
  return record ? {
    sourceId: record.sourceId ?? null,
    target: record.target,
    roll: record.roll,
    success: record.success,
    withinHour: record.withinHour,
    hpBefore: record.hpBefore,
    hpAfter: record.hpAfter,
    healedHp: record.healedHp,
    wasDying: record.wasDying,
    wasUnconscious: record.wasUnconscious,
    stabilizedDying: record.stabilizedDying,
    rousedUnconscious: record.rousedUnconscious,
    occurredAt: record.at ?? null
  } : null;
}

function mapCondition(condition) {
  return condition?.active === true ? {
    sourceId: condition.sourceId ?? null,
    reason: condition.reason,
    firstAidTarget: condition.firstAidTarget ?? null,
    firstAidRoll: condition.firstAidRoll ?? null,
    occurredAt: condition.at ?? null
  } : null;
}

function mapDead(condition) {
  return condition?.active === true ? {
    sourceEventKey: condition.sourceEventKey ?? null,
    reason: condition.reason,
    occurredAt: condition.at ?? null
  } : null;
}

function mapDyingEpisode(condition) {
  return condition?.active === true ? {
    sourceEventKey: condition.sourceEventKey ?? null,
    checks: (condition.checks || []).map(mapDyingCheck),
    nextRoundOrdinal: condition.nextRoundOrdinal,
    roundChecksManaged: condition.roundChecksManaged === true
  } : null;
}

function captureState() {
  const snapshot = runInReference("healthStabilizationSnapshot(state.character)");
  const damage = runInReference("hpDamageStateSnapshot(state.character)");
  return {
    currentHp: snapshot.hp,
    maxHp: snapshot.maxHp,
    con: snapshot.con,
    majorWound: snapshot.majorWound?.active === true,
    unconscious: snapshot.unconscious?.active === true,
    dyingEpisode: mapDyingEpisode(snapshot.dying),
    stabilized: mapCondition(snapshot.stabilized),
    dead: mapDead(snapshot.dead),
    treatmentHistory: (snapshot.treatmentHistory || []).map(mapTreatment),
    history: (damage.history || []).map(mapDamageEvent),
    lastEvent: mapDamageEvent(damage.lastDamageEvent)
  };
}

function classifyError(error) {
  const message = String(error?.message || error);
  if (message.includes("已经死亡")) return "AlreadyDead";
  if (message.includes("没有待结算")) return "NoActiveDying";
  if (message.includes("一小时")) return "FirstAidOutsideHour";
  if (message.includes("1 到 100")) return "InvalidTarget";
  if (message.includes("没有需要急救")) return "NoTreatableInjury";
  throw new Error(`Unmapped health stabilization error: ${message}`);
}

function execute(operation) {
  try {
    if (operation.kind === "damage") {
      const result = applyDamage({ damage: operation.damage, roll: operation.roll ?? 1, eventKey: operation.eventKey });
      return {
        changed: result.tracked === true && result.deduped !== true,
        error: null,
        dyingCheck: null,
        treatment: null,
        damageEvent: mapDamageEvent(result.event)
      };
    }

    if (operation.kind === "dyingRound") {
      const result = runInReference(`healthStabilizationResolveDyingRound({ roller: () => ${JSON.stringify(operation.roll)}, sourceId: ${JSON.stringify(operation.sourceId)} })`);
      return { changed: true, error: null, dyingCheck: mapDyingCheck(result.record), treatment: null, damageEvent: null };
    }

    if (operation.kind === "firstAid") {
      const result = runInReference(`healthStabilizationResolveFirstAid({ target: ${JSON.stringify(operation.target)}, roller: () => ${JSON.stringify(operation.roll)}, withinHour: ${JSON.stringify(operation.withinHour)}, sourceId: ${JSON.stringify(operation.sourceId)} })`);
      return { changed: true, error: null, dyingCheck: null, treatment: mapTreatment(result.record), damageEvent: null };
    }

    throw new Error(`Unknown operation kind: ${operation.kind}`);
  } catch (error) {
    return { changed: false, error: classifyError(error), dyingCheck: null, treatment: null, damageEvent: null };
  }
}

function fixtureCase(name, setup, operations) {
  setup();
  const initial = captureState();
  const emittedOperations = operations.map(operation => {
    const outcome = execute(operation);
    return {
      ...operation,
      occurredAt: operation.kind === "damage" ? null : fixedTimestamp,
      expected: { ...outcome, state: captureState() }
    };
  });
  return {
    name,
    timestampsNonSemantic: true,
    initial,
    operations: emittedOperations,
    expected: captureState()
  };
}

const cases = [
  fixtureCase("dying-baseline", setupDying, []),
  fixtureCase("dying-roll-equals-con", setupDying, [{ kind: "dyingRound", roll: 60, sourceId: "dying-equals" }]),
  fixtureCase("dying-roll-just-above-con", setupDying, [{ kind: "dyingRound", roll: 61, sourceId: "dying-above" }]),
  fixtureCase("dying-two-successive-rounds", setupDying, [
    { kind: "dyingRound", roll: 20, sourceId: "dying-round-1" },
    { kind: "dyingRound", roll: 30, sourceId: "dying-round-2" }
  ]),
  fixtureCase("dying-failure-death", setupDying, [{ kind: "dyingRound", roll: 99, sourceId: "dying-failure" }]),
  fixtureCase("dying-already-dead-rejected", setupDead, [{ kind: "dyingRound", roll: 1, sourceId: "dead-round" }]),
  fixtureCase("dying-not-active-rejected", () => resetCharacter({ hp: 8 }), [{ kind: "dyingRound", roll: 1, sourceId: "not-dying" }]),
  fixtureCase("first-aid-within-hour-rejected", () => resetCharacter({ hp: 8 }), [{ kind: "firstAid", target: 50, roll: 1, withinHour: false, sourceId: "aid-window" }]),
  fixtureCase("first-aid-target-range-rejected", () => resetCharacter({ hp: 8 }), [{ kind: "firstAid", target: 101, roll: 1, withinHour: true, sourceId: "aid-range" }]),
  fixtureCase("first-aid-injured-success", () => resetCharacter({ hp: 8 }), [{ kind: "firstAid", target: 50, roll: 50, withinHour: true, sourceId: "aid-success" }]),
  fixtureCase("first-aid-injured-failure", () => resetCharacter({ hp: 8 }), [{ kind: "firstAid", target: 50, roll: 51, withinHour: true, sourceId: "aid-failure" }]),
  fixtureCase("first-aid-max-hp-cap", setupUnconsciousAtMaxHp, [{ kind: "firstAid", target: 80, roll: 1, withinHour: true, sourceId: "aid-cap" }]),
  fixtureCase("first-aid-unconscious-wake", setupUnconscious, [{ kind: "firstAid", target: 70, roll: 20, withinHour: true, sourceId: "aid-wake" }]),
  fixtureCase("first-aid-dying-stabilizes", setupDying, [{ kind: "firstAid", target: 70, roll: 20, withinHour: true, sourceId: "aid-stabilize" }]),
  fixtureCase("first-aid-dying-failure", setupDying, [{ kind: "firstAid", target: 30, roll: 80, withinHour: true, sourceId: "aid-dying-failure" }]),
  fixtureCase("first-aid-stabilized-cannot-dying-round", setupStabilized, [{ kind: "dyingRound", roll: 1, sourceId: "stabilized-round" }]),
  fixtureCase("first-aid-dead-rejected", setupDead, [{ kind: "firstAid", target: 80, roll: 1, withinHour: true, sourceId: "dead-aid" }]),
  fixtureCase("damage-dying-con-success-sequence", () => resetCharacter(), [
    { kind: "damage", eventKey: "sequence-major", damage: 6, roll: 60 },
    { kind: "damage", eventKey: "sequence-zero", damage: 6, roll: 1 },
    { kind: "dyingRound", roll: 60, sourceId: "sequence-round" }
  ]),
  fixtureCase("damage-dying-first-aid-sequence", () => resetCharacter(), [
    { kind: "damage", eventKey: "sequence-aid-major", damage: 6, roll: 1 },
    { kind: "damage", eventKey: "sequence-aid-zero", damage: 6, roll: 1 },
    { kind: "firstAid", target: 70, roll: 20, withinHour: true, sourceId: "sequence-aid" }
  ]),
  fixtureCase("damage-stabilized-fresh-damage-invalidates", setupStabilized, [
    { kind: "damage", eventKey: "fresh-damage", damage: 1, roll: 1 }
  ]),
  fixtureCase("damage-dying-con-failure-sequence", () => resetCharacter(), [
    { kind: "damage", eventKey: "sequence-fail-major", damage: 6, roll: 1 },
    { kind: "damage", eventKey: "sequence-fail-zero", damage: 6, roll: 1 },
    { kind: "dyingRound", roll: 61, sourceId: "sequence-fail-round" }
  ])
];

const fixture = {
  version: 1,
  referenceSources: ["src/hp-damage-state.js", "src/health-stabilization.js"],
  cases
};
const outputPath = path.join(root, "multiplayer", "server", "tests", "Trpg.Multiplayer.Api.Tests", "Fixtures", "health-stabilization.json");
fs.mkdirSync(path.dirname(outputPath), { recursive: true });
fs.writeFileSync(outputPath, `${JSON.stringify(fixture, null, 2)}\n`, "utf8");
console.log(`HEALTH_STABILIZATION_CONFORMANCE_FIXTURE:PASS cases=${cases.length} path=${path.relative(root, outputPath)}`);
