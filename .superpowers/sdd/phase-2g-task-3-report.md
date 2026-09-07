# Phase 2G Task 3 Report

## Scope

Implemented only the approved pure deterministic Combat Damage rule layer.

Files owned by this task:

- `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CombatDamageResolution.cs`
- `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/CombatDamageResolutionTests.cs`
- `.superpowers/sdd/phase-2g-task-3-report.md`

No `GameCoordinator`, game state, HP integration, fixture/exporter, Single Player source, route, client, commit, stash, reset, or push work was performed. Existing Task 1-2 changes were preserved. No corrective edit to `DiceExpression.cs` or `CheckResolution.cs` was necessary.

## UTF-8 gate

Before editing, strict throwing UTF-8 decoding reported:

- `CombatDamageResolutionTests.cs`: `UTF8_OK`
- `CombatDamageResolution.cs`: absent
- `phase-2g-task-3-report.md`: absent

After all edits, strict throwing UTF-8 decoding reported `UTF8_OK` for all three task-owned files. A trailing-whitespace search returned no matches.

## TDD RED evidence

Tests were expanded first to cover the finalized Damage Bonus table and clamp, checked STR+SIZ addition, supported and unsupported weapon modes, regular dice and flat negative Damage Bonus, disabled Damage Bonus, gross clamping, extreme maximization and null-roll contract, Fight Back regular cap, Armor, undefined modes, and required roll count/faces/raw-count/range/raw-sum validation.

Command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter FullyQualifiedName~CombatDamageResolutionTests --nologo -v:minimal
```

Actual RED:

- Exit code: `1`
- Production API project built.
- Test compilation failed with 18 errors, all caused by the intentionally missing Task 3 symbols (`DamageBonusKind`, `DamageBonusProfile`, `CombatDamageMode`, and `CombatDamageCalculation`, including their assertion usages).
- No unrelated baseline failure was reported.

## Implementation

- Added the exact approved public enums, records, interface, rules surface, and `CocCombatDamageEngine`.
- `DeriveDamageBonus` uses checked `str + siz`, then `Math.Max(2, sum)`, the finalized threshold table, and the post-204 80-point band formula. It intentionally performs no canonical investigator/opponent profile validation.
- `NormalizeWeapon` parses the existing exact dice grammar and accepts only `melee_non_impaling`.
- Regular and Fight Back resolution validate required roll count, faces, raw count, every raw value, and raw sum before use.
- Disabled Damage Bonus produces a zero component without requiring Damage Bonus dice. Flat Damage Bonus also requires no dice.
- Extreme resolution requires both roll inputs to be null, maximizes weapon dice with its actual modifier, applies positive dice maxima, preserves flat zero/negative maxima, and performs no RNG.
- Gross and net damage are clamped at zero; Armor is applied after gross damage.
- The engine has no RNG dependency and no state, coordinator, HTTP, SignalR, HP, or AI dependency.

## Validation evidence

Task 3 tests excluding the future fixture:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~CombatDamageResolutionTests&FullyQualifiedName!~CombatDamageFixture" --nologo -v:minimal
```

Actual result:

- Exit code: `0`
- Passed: `50`
- Failed: `0`
- Skipped: `0`

Specified Task 3 combined gate:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~CombatDamageResolutionTests|FullyQualifiedName~CheckResolutionTests" --nologo -v:minimal
```

Actual result:

- Exit code: `1`
- Passed: `83`
- Failed: `1`
- Skipped: `0`
- The sole failure is `CombatDamageFixture_RequiresExactly48UniqueLabeledCasesAndReferenceMetadata` because Task 4 has not created `Fixtures/combat-damage.json`.
- No Task 3 rule/engine assertion and no Task 2 parser/generic-dice assertion failed.

## Self-review

- Public names and constructor parameter order match the approved Task 3 signatures.
- The only accepted weapon mode is exact ordinal `melee_non_impaling`, checked by both normalization and the engine before roll use.
- Required roll shape is validated before totals contribute to damage.
- Extreme mode rejects supplied rolls and records the real weapon expression modifier.
- Damage Bonus exclusion does not require or consume a Damage Bonus roll.
- No canonical profile validation was pulled forward from Task 5.
- No production file outside the new Task 3 source was changed by this task.

## Modification record

1. Modification time: `2026-09-07 11:36:10 +08:00`
   Location: `CocCombatDamageRules` and `CocCombatDamageEngine`
   Change: Added deterministic Combat Damage profiles, normalization, table derivation, validation, and calculation.
   Reason: Implement the approved Phase 2G Task 3 pure rule boundary after genuine RED coverage.
   Business impact: Adds the new pure non-impaling melee calculation behavior only; it does not integrate or mutate Multiplayer combat state.
   Performance impact: Constant-time arithmetic plus bounded iteration over at most the supplied validated dice lists; no I/O or RNG.
   Risk: Actual-JS 48-case fixture conformance remains unverified until Task 4 generates the fixture.
   Regression recommendation: Re-run the combined gate after Task 4 and require all 84 current tests plus fixture-expanded assertions to pass.

## Escalation

Sol High escalation is not required. The work is pure, deterministic, isolated, and carries no production data, security, migration, concurrency, or destructive-change risk.
