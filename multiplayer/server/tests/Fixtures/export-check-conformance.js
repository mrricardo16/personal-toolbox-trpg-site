"use strict";

const fs = require("fs");
const path = require("path");
const vm = require("vm");

const root = path.resolve(__dirname, "../../../..");
const source = fs.readFileSync(path.join(root, "src", "check-engine.js"), "utf8");

const inputs = [
  { name: "critical", input: { target: 60, difficulty: "regular", bonusDice: 0, penaltyDice: 0, randomSequence: [1, 0] } },
  { name: "very-low-roll", input: { target: 60, difficulty: "regular", bonusDice: 0, penaltyDice: 0, randomSequence: [7, 0] } },
  { name: "exact-target", input: { target: 60, difficulty: "regular", bonusDice: 0, penaltyDice: 0, randomSequence: [0, 6] } },
  { name: "just-above-target", input: { target: 60, difficulty: "regular", bonusDice: 0, penaltyDice: 0, randomSequence: [1, 6] } },
  { name: "hard-boundary", input: { target: 70, difficulty: "hard", bonusDice: 0, penaltyDice: 0, randomSequence: [5, 3] } },
  { name: "hard-just-outside", input: { target: 70, difficulty: "hard", bonusDice: 0, penaltyDice: 0, randomSequence: [6, 3] } },
  { name: "extreme-boundary", input: { target: 70, difficulty: "extreme", bonusDice: 0, penaltyDice: 0, randomSequence: [4, 1] } },
  { name: "extreme-just-outside", input: { target: 70, difficulty: "extreme", bonusDice: 0, penaltyDice: 0, randomSequence: [5, 1] } },
  { name: "low-target-fumble", input: { target: 40, difficulty: "regular", bonusDice: 0, penaltyDice: 0, randomSequence: [6, 9] } },
  { name: "target-49-fumble-boundary", input: { target: 49, difficulty: "regular", bonusDice: 0, penaltyDice: 0, randomSequence: [6, 9] } },
  { name: "target-50-fumble-boundary", input: { target: 50, difficulty: "regular", bonusDice: 0, penaltyDice: 0, randomSequence: [6, 9] } },
  { name: "high-target-fumble", input: { target: 70, difficulty: "regular", bonusDice: 0, penaltyDice: 0, randomSequence: [0, 0] } },
  { name: "target-one", input: { target: 1, difficulty: "regular", bonusDice: 0, penaltyDice: 0, randomSequence: [2, 0] } },
  { name: "target-one-critical", input: { target: 1, difficulty: "regular", bonusDice: 0, penaltyDice: 0, randomSequence: [1, 0] } },
  { name: "target-one-hundred", input: { target: 100, difficulty: "regular", bonusDice: 0, penaltyDice: 0, randomSequence: [5, 9] } },
  { name: "bonus-dice-selects-lowest", input: { target: 60, difficulty: "regular", bonusDice: 1, penaltyDice: 0, randomSequence: [5, 7, 4] } },
  { name: "penalty-dice-selects-highest", input: { target: 60, difficulty: "regular", bonusDice: 0, penaltyDice: 1, randomSequence: [5, 4, 7] } },
  { name: "net-bonus-dice", input: { target: 60, difficulty: "regular", bonusDice: 2, penaltyDice: 1, randomSequence: [5, 7, 4] } },
  { name: "net-penalty-dice", input: { target: 60, difficulty: "regular", bonusDice: 1, penaltyDice: 2, randomSequence: [5, 4, 7] } }
];

function runReference(input) {
  const sequence = [...input.randomSequence];
  const sandbox = {
    Boolean,
    Error,
    Math,
    Number,
    String,
    clamp: (value, min, max) => Math.min(max, Math.max(min, value)),
    randomInt(min, max) {
      const next = sequence.shift();
      if (!Number.isInteger(next) || next < min || next > max) {
        throw new Error(`Fixture random sequence value ${next} is outside ${min}..${max}`);
      }
      return next;
    }
  };
  sandbox.globalThis = sandbox;
  vm.runInNewContext(`${source}\n;globalThis.__resolveCheck=resolveCheck;`, sandbox, { filename: "check-engine-reference.js" });
  const result = sandbox.__resolveCheck({ system: "coc7", ...input });
  if (sequence.length !== 0) {
    throw new Error(`Fixture ${JSON.stringify(input)} did not consume all random values`);
  }

  return {
    rawRolls: result.rawRolls,
    roll: result.total,
    target: result.target,
    difficulty: result.difficulty,
    difficultyTarget: result.difficultyTarget,
    successLevel: result.rank,
    passed: result.result,
    critical: result.rank === "critical",
    fumble: result.rank === "fumble"
  };
}

const fixture = {
  version: 1,
  referenceSource: "src/check-engine.js",
  cases: inputs.map(({ name, input }) => ({ name, input, expected: runReference(input) }))
};

const outputPath = path.join(root, "multiplayer", "server", "tests", "Trpg.Multiplayer.Api.Tests", "Fixtures", "check-resolution.json");
fs.mkdirSync(path.dirname(outputPath), { recursive: true });
fs.writeFileSync(outputPath, `${JSON.stringify(fixture, null, 2)}\n`, "utf8");
console.log(`CHECK_CONFORMANCE_FIXTURE:PASS cases=${fixture.cases.length} path=${path.relative(root, outputPath)}`);
