"use strict";

const fs = require("fs");
const path = require("path");
const vm = require("vm");
const { webcrypto } = require("crypto");
const { TextEncoder, TextDecoder } = require("util");

const root = path.resolve(__dirname, "../../../..");
const fixedTimestamp = "2026-09-02T12:00:00.000Z";
const referenceSources = [
  "src/check-engine.js",
  "src/hp-damage-state.js",
  "src/health-stabilization.js",
  "src/combat-opposed.js",
  "src/combat-damage.js"
];
const sourceFiles = [
  "scenarios/library.js", "state.js", "check-engine.js", "scenario-engine.js", "case-integrity.js",
  "memory.js", "ai-protocol.js", "player-action-guard.js", "interaction-availability.js", "saves.js",
  "api-response-resilience.js", "progress-semantics.js", "authored-threat-clock.js", "npc-knowledge-boundary.js",
  "ending-resolution-gate.js", "coc-resolution-engine.js", "coc-consequence-contract.js",
  "failure-forward-cost-engine.js", "san-loss-resolution.js", "san-loss-window.js", "hp-damage-state.js",
  "health-stabilization.js", "combat-opposed.js", "combat-damage.js"
];

function storage() {
  const map = new Map();
  return {
    getItem: key => map.get(String(key)) ?? null,
    setItem: (key, value) => map.set(String(key), String(value)),
    removeItem: key => map.delete(String(key)),
    clear: () => map.clear(),
    key: index => Array.from(map.keys())[index] ?? null,
    get length() { return map.size; }
  };
}

const RealDate = Date;
class FixedDate extends RealDate {
  constructor(...args) { super(...(args.length ? args : [fixedTimestamp])); }
  static now() { return RealDate.parse(fixedTimestamp); }
}

const sandbox = {
  fs, path, root, Object, Array, JSON, Map, Set, Math, Number, String, Boolean, Date: FixedDate, console,
  crypto: webcrypto, TextEncoder, TextDecoder, URL, AbortController, Blob, structuredClone,
  fetch: async () => { throw new Error("fetch not expected"); },
  setTimeout: () => 0, clearTimeout() {}, setInterval: () => 0, clearInterval() {},
  window: { localStorage: storage(), sessionStorage: storage(), addEventListener() {} },
  document: {
    addEventListener() {}, querySelector() { return null; }, querySelectorAll() { return []; },
    createElement() {
      return {
        className: "", textContent: "", innerHTML: "", value: "", checked: false, disabled: false,
        style: {}, dataset: {}, classList: { add() {}, remove() {}, toggle() {} }, appendChild() {},
        remove() {}, click() {}, insertAdjacentHTML() {}, setAttribute() {}, removeAttribute() {},
        addEventListener() {}, querySelector() { return null; }, querySelectorAll() { return []; },
        scrollHeight: 0, scrollTop: 0
      };
    },
    body: { appendChild() {} }
  },
  navigator: { clipboard: { writeText: async () => {} } },
  confirm: () => false, alert() {}, renderAll() {}, renderTopbar() {}, renderSidebar() {}, renderChat() {},
  renderChatLog() {}, renderChatComposer() {}, renderSaves() {}, toast() {}
};
sandbox.globalThis = sandbox;
vm.createContext(sandbox);
vm.runInContext(
  sourceFiles.map(file => fs.readFileSync(path.join(root, "src", file), "utf8")).join("\n\n"),
  sandbox,
  { filename: "combat-damage-reference.js" });
vm.runInContext(
  "randomInt = () => 1; addLog = () => {}; scheduleAutosave = () => {}; renderAll = () => {};",
  sandbox,
  { filename: "combat-damage-fixture-shims.js" });

function run(expression) {
  return vm.runInContext(expression, sandbox, { filename: "combat-damage-fixture-operation.js" });
}

function json(value) {
  return JSON.stringify(value);
}

function referenceCase(id, input, expected) {
  return { id, scope: "reference_conformance", input, expected };
}

function deferredCase(id, input, expected) {
  return { id, scope: "deferred", input, expected };
}

function potentialIssueCase(id, input, expected) {
  return { id, scope: "potential_reference_issue", input, expected };
}

function bonusCase(id, str, siz) {
  const input = { str, siz };
  return referenceCase(id, input, run(`combatDamageBonusProfile(${json(str)}, ${json(siz)})`));
}

function diceCase(id, expression) {
  const input = { expression };
  return referenceCase(id, input, run(`parseDiceExpression(${json(expression)})`));
}

function weaponCase(id, weapon) {
  const input = { weapon };
  try {
    return referenceCase(id, input, { ok: true, value: run(`combatDamageNormalizeWeapon(${json(weapon)})`) });
  } catch (error) {
    return referenceCase(id, input, { ok: false, error: String(error?.message || error) });
  }
}

function amountCase(id, { str, siz, weapon, mode, weaponRolls = [], bonusRolls = [], armor = 0 }) {
  const input = { str, siz, weapon, mode, weaponRolls, bonusRolls, armor };
  const expected = run(`(() => {
    const owner = {
      str: ${json(str)},
      siz: ${json(siz)},
      weapon: combatDamageNormalizeWeapon(${json(weapon)}),
      damageBonus: combatDamageBonusProfile(${json(str)}, ${json(siz)})
    };
    const weaponRolls = ${json(weaponRolls)};
    const bonusRolls = ${json(bonusRolls)};
    return combatDamageResolveAmount({
      owner,
      mode: ${json(mode)},
      weaponRoller: (faces, index) => weaponRolls[index],
      bonusRoller: (faces, index) => bonusRolls[index]
    });
  })()`);
  return referenceCase(id, input, expected);
}

function emptyHealthState() {
  return {
    version: "1.0", authority: "browser_coc_health", majorWound: null, unconscious: null,
    dying: null, dead: null, lastDamageEvent: null, history: [], stabilized: null, treatmentHistory: []
  };
}

function dispositionCase(id, options, scope = "deferred") {
  const input = {
    ownerSide: options.ownerSide,
    str: options.str,
    siz: options.siz,
    weapon: options.weapon,
    mode: options.mode,
    weaponRolls: options.weaponRolls || [],
    bonusRolls: options.bonusRolls || [],
    armor: options.armor,
    targetHp: options.targetHp,
    targetMaxHp: options.targetMaxHp,
    targetAlreadyDefeated: options.targetAlreadyDefeated === true,
    conRoll: options.conRoll ?? 1
  };
  const expected = run(`(() => {
    state = makeInitialState();
    state.character = {
      system: "coc7",
      name: "Fixture Investigator",
      hp: ${json(options.ownerSide === "opponent" ? options.targetHp : 12)},
      maxHp: ${json(options.ownerSide === "opponent" ? options.targetMaxHp : 12)},
      attributes: { dex: 90, con: 60, str: ${json(options.ownerSide === "player" ? options.str : 50)}, siz: ${json(options.ownerSide === "player" ? options.siz : 50)} },
      skills: [{ id: "fighting_brawl", name: "斗殴", value: 60 }, { id: "dodge", name: "闪避", value: 60 }],
      combatLoadout: { version: "1.0", authority: "browser_coc_combat_damage", weapon: ${json(options.ownerSide === "player" ? options.weapon : { id: "player-unarmed", label: "Player", damage: "1d3", addsDamageBonus: true, mode: "melee_non_impaling" })}, armor: ${json(options.ownerSide === "opponent" ? options.armor : 0)} },
      healthState: ${json(emptyHealthState())}
    };
    normalizeHpDamageState(state.character);
    combatStart({ opponents: [{
      id: "opponent-1", label: "Fixture Opponent", dex: 60, fighting: 60, dodge: 60,
      str: ${json(options.ownerSide === "opponent" ? options.str : 50)},
      siz: ${json(options.ownerSide === "opponent" ? options.siz : 50)},
      hp: ${json(options.ownerSide === "player" ? options.targetHp : 10)},
      maxHp: ${json(options.ownerSide === "player" ? options.targetMaxHp : 10)},
      armor: ${json(options.ownerSide === "player" ? options.armor : 0)},
      weapon: ${json(options.ownerSide === "opponent" ? options.weapon : { id: "opponent-unarmed", label: "Opponent", damage: "1d3", addsDamageBonus: true, mode: "melee_non_impaling" })}
    }] });
    const ownerId = ${json(options.ownerSide === "player" ? "player" : "opponent-1")};
    const targetId = ${json(options.ownerSide === "player" ? "opponent-1" : "player")};
    if (${json(options.targetAlreadyDefeated === true)}) {
      const target = combatParticipant(targetId);
      target.hp = 0;
      target.active = false;
      target.defeated = true;
    }
    const exchange = {
      id: ${json(`fixture-${id}`)},
      damageDisposition: { pending: true, ownerId, targetId, mode: ${json(options.mode)}, authority: "deferred_weapon_damage_engine", hpCommitted: false }
    };
    const weaponRolls = ${json(options.weaponRolls || [])};
    const bonusRolls = ${json(options.bonusRolls || [])};
    const revisionBefore = state.revision;
    const applied = combatDamageApplyDisposition(exchange, {
      weaponRoller: (faces, index) => weaponRolls[index],
      bonusRoller: (faces, index) => bonusRolls[index]
    });
    const revisionAfter = state.revision;
    const result = applied.result || null;
    return {
      applied: applied.applied,
      reason: applied.reason || null,
      revisionBefore,
      revisionAfter,
      disposition: {
        pending: applied.exchange.damageDisposition.pending,
        hpCommitted: applied.exchange.damageDisposition.hpCommitted
      },
      result: result ? {
        weapon: result.weapon,
        weaponResult: result.weaponResult,
        damageBonusResult: result.damageBonusResult,
        grossDamage: result.grossDamage,
        armor: result.armor,
        netDamage: result.netDamage,
        hpBefore: result.hpBefore,
        hpAfter: result.hpAfter,
        targetDefeated: result.targetDefeated,
        hpDamageState: result.hpDamageState
      } : null
    };
  })()`);
  if (scope === "reference_conformance") return referenceCase(id, input, expected);
  if (scope === "potential_reference_issue") return potentialIssueCase(id, input, expected);
  return deferredCase(id, input, expected);
}

const meleeWeapon = (damage, addsDamageBonus = true) => ({
  id: "fixture-weapon", label: "Fixture Weapon", damage, addsDamageBonus, mode: "melee_non_impaling"
});

const cases = [
  bonusCase("db-64", 32, 32), bonusCase("db-65", 32, 33), bonusCase("db-84", 42, 42),
  bonusCase("db-85", 42, 43), bonusCase("db-124", 62, 62), bonusCase("db-125", 62, 63),
  bonusCase("db-164", 82, 82), bonusCase("db-165", 82, 83), bonusCase("db-204", 102, 102),
  bonusCase("db-205", 102, 103), bonusCase("db-284", 142, 142), bonusCase("db-285", 142, 143),
  diceCase("dice-d3", "d3"), diceCase("dice-1d3", "1d3"), diceCase("dice-1d6-plus-2", "1d6+2"),
  diceCase("dice-2d4-minus-1", "2d4-1"), diceCase("dice-trim-uppercase", "  D6+2  "),
  diceCase("dice-empty", ""), diceCase("dice-invalid-format", "2d"), diceCase("dice-count-zero", "0d6"),
  diceCase("dice-count-101", "101d6"), diceCase("dice-faces-one", "1d1"),
  diceCase("dice-faces-10001", "1d10001"), diceCase("dice-modifier-min", "1d6-100000"),
  diceCase("dice-modifier-below-min", "1d6-100001"), diceCase("dice-modifier-max", "1d6+100000"),
  diceCase("dice-modifier-above-max", "1d6+100001"), diceCase("dice-length-33", "111111111111111111111111111111111"),
  weaponCase("weapon-melee-non-impaling", meleeWeapon(" 2D4-1 ", false)),
  weaponCase("weapon-unsupported-mode", { ...meleeWeapon("1d8"), mode: "impaling" }),
  amountCase("regular-dice-db", { str: 65, siz: 60, weapon: meleeWeapon("1d6"), mode: "regular", weaponRolls: [4], bonusRolls: [3] }),
  amountCase("regular-flat-negative-db", { str: 32, siz: 33, weapon: meleeWeapon("1d4"), mode: "regular", weaponRolls: [3] }),
  amountCase("regular-gross-floor-zero", { str: 0, siz: 0, weapon: meleeWeapon("1d2-2"), mode: "regular", weaponRolls: [1] }),
  amountCase("regular-adds-db-false", { str: 182, siz: 183, weapon: meleeWeapon("1d6+2", false), mode: "regular", weaponRolls: [4] }),
  amountCase("extreme-weapon-modifier-max", { str: 42, siz: 43, weapon: meleeWeapon("100d10000+100000"), mode: "initiator_extreme_eligible" }),
  amountCase("extreme-positive-db-max", { str: 182, siz: 183, weapon: meleeWeapon("2d4-1"), mode: "initiator_extreme_eligible" }),
  amountCase("extreme-zero-db", { str: 42, siz: 43, weapon: meleeWeapon("2d4-1"), mode: "initiator_extreme_eligible" }),
  amountCase("extreme-negative-db", { str: 0, siz: 0, weapon: meleeWeapon("2d4-1"), mode: "initiator_extreme_eligible" }),
  amountCase("fight-back-regular-cap", { str: 65, siz: 60, weapon: meleeWeapon("1d6+1"), mode: "fight_back_regular_cap", weaponRolls: [2], bonusRolls: [1] }),
  dispositionCase("armor-partial", { ownerSide: "player", str: 65, siz: 60, weapon: meleeWeapon("1d6"), mode: "regular", weaponRolls: [4], bonusRolls: [3], armor: 2, targetHp: 10, targetMaxHp: 10 }, "reference_conformance"),
  dispositionCase("armor-equal-gross", { ownerSide: "player", str: 65, siz: 60, weapon: meleeWeapon("1d6"), mode: "regular", weaponRolls: [4], bonusRolls: [3], armor: 7, targetHp: 10, targetMaxHp: 10 }, "reference_conformance"),
  dispositionCase("armor-above-gross", { ownerSide: "player", str: 65, siz: 60, weapon: meleeWeapon("1d6"), mode: "regular", weaponRolls: [4], bonusRolls: [3], armor: 9, targetHp: 10, targetMaxHp: 10 }, "reference_conformance"),
  dispositionCase("zero-net-no-hp-event", { ownerSide: "opponent", str: 65, siz: 60, weapon: meleeWeapon("1d6"), mode: "regular", weaponRolls: [1], bonusRolls: [1], armor: 9, targetHp: 12, targetMaxHp: 12 }),
  dispositionCase("investigator-positive-hp", { ownerSide: "opponent", str: 65, siz: 60, weapon: meleeWeapon("1d6"), mode: "regular", weaponRolls: [4], bonusRolls: [3], armor: 2, targetHp: 12, targetMaxHp: 12 }),
  dispositionCase("major-wound-after-armor", { ownerSide: "opponent", str: 65, siz: 60, weapon: meleeWeapon("1d6+1"), mode: "regular", weaponRolls: [3], bonusRolls: [4], armor: 2, targetHp: 12, targetMaxHp: 12, conRoll: 1 }),
  dispositionCase("instant-death-after-armor", { ownerSide: "opponent", str: 205, siz: 80, weapon: meleeWeapon("2d6+2"), mode: "regular", weaponRolls: [6, 6], bonusRolls: [6, 6, 6], armor: 2, targetHp: 12, targetMaxHp: 12 }),
  dispositionCase("opponent-defeat", { ownerSide: "player", str: 65, siz: 60, weapon: meleeWeapon("1d6"), mode: "regular", weaponRolls: [6], bonusRolls: [4], armor: 0, targetHp: 5, targetMaxHp: 10 }),
  dispositionCase("target-already-defeated-reference-issue", { ownerSide: "player", str: 65, siz: 60, weapon: meleeWeapon("1d6"), mode: "regular", weaponRolls: [6], bonusRolls: [4], armor: 0, targetHp: 5, targetMaxHp: 10, targetAlreadyDefeated: true }, "potential_reference_issue")
];

const fixture = { version: 1, referenceSources, cases };
const outputPath = path.join(root, "multiplayer", "server", "tests", "Trpg.Multiplayer.Api.Tests", "Fixtures", "combat-damage.json");
fs.mkdirSync(path.dirname(outputPath), { recursive: true });
fs.writeFileSync(outputPath, `${JSON.stringify(fixture, null, 2)}\n`, "utf8");
console.log(`COMBAT_DAMAGE_CONFORMANCE_FIXTURE:PASS cases=${cases.length} path=${path.relative(root, outputPath)}`);
