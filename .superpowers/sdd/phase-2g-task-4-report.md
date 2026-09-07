# Phase 2G Task 4 Report

## Scope

- Implemented only the approved Feature Commit 1 fixture/exporter/validation preparation.
- Created `multiplayer/server/tests/Fixtures/export-combat-damage-conformance.js`.
- Created `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/combat-damage.json`.
- Modified `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/CombatDamageResolutionTests.cs`.
- Did not modify `src/`, `build/`, `outputs/`, production C#, project files, or existing Task 1–3 changes.
- Did not commit, push, reset, revert, or stash.

## UTF-8 preflight

Strict UTF-8 decoding succeeded before editing for the authorized existing C# test, the Task 4 plan/specification, Task 1–3 reports, all inspected exporter patterns, and the five actual Single Player reference files. No suspected ANSI/GBK file was found.

## TDD evidence

Before the exporter or fixture existed, the exact focused command was run:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter FullyQualifiedName~CombatDamageResolutionTests --nologo -v:minimal
```

Genuine RED result:

- Exit code: `1`
- Passed: `50`
- Failed: `1`
- Skipped: `0`
- Sole failure: `CombatDamageFixture_RequiresExactly48UniqueLabeledCasesAndReferenceMetadata`
- Failure reason: `Fixtures/combat-damage.json` did not exist.

Final GREEN result for the same exact command:

- Exit code: `0`
- Passed: `52`
- Failed: `0`
- Skipped: `0`

## Exporter and fixture

The VM harness loads the actual Single Player implementation and exposes controlled calls to:

- `parseDiceExpression`
- `combatDamageBonusProfile`
- `combatDamageNormalizeWeapon`
- `combatDamageResolveAmount`
- `combatDamageApplyDisposition`

Forced roller callbacks provide deterministic raw values. Expected parser, profile, weapon, amount, armor, HP, and disposition values are returned by the actual reference functions; the exporter does not contain a parallel expected-damage implementation.

Final deterministic generation pair:

- Case count: `48`
- Unique stable IDs: `48`
- `reference_conformance`: `42`
- `deferred`: `5`
- `potential_reference_issue`: `1`
- SHA1 (first SHA-256 capture): `0036133BF2BF1F37CBEF7DC7707832C258DE869870B46C17ADCA748FE905CEE7`
- SHA2 (second SHA-256 capture): `0036133BF2BF1F37CBEF7DC7707832C258DE869870B46C17ADCA748FE905CEE7`
- Byte-deterministic: yes, SHA1 equals SHA2.

`referenceSources` is exactly:

1. `src/check-engine.js`
2. `src/hp-damage-state.js`
3. `src/health-stabilization.js`
4. `src/combat-opposed.js`
5. `src/combat-damage.js`

The `target-already-defeated-reference-issue` case preserves actual reference behavior:

- `applied=false`
- reason `target_already_defeated`
- unchanged revision
- disposition remains `pending=true`
- disposition remains `hpCommitted=false`

The C# conformance test does not force this potential reference issue or any deferred case into Multiplayer semantics. It executes only the 42 `reference_conformance` cases against the applicable C# parser, Damage Bonus, weapon normalization, and pure damage engine boundaries.

## Full server validation

Final validation was run after the finalized fixture pair:

```powershell
dotnet restore multiplayer/server/Trpg.Multiplayer.slnx --nologo
dotnet build multiplayer/server/Trpg.Multiplayer.slnx --no-restore --nologo -v:minimal
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --nologo -v:minimal
dotnet format multiplayer/server/Trpg.Multiplayer.slnx --verify-no-changes --no-restore --verbosity minimal
```

Results:

- Restore: exit `0`; all projects up to date.
- Build: exit `0`; `0` warnings, `0` errors.
- Full tests: exit `0`; `275` passed, `0` failed, `0` skipped.
- Format verification: exit `0`; no changes required.

## Modification record

1. Modification time: `2026-09-07`
   Location: Combat Damage actual-JS fixture exporter and fixture.
   Change: Added the deterministic five-source VM exporter and the exact 48-case generated fixture.
   Reason: Prepare and validate the approved Phase 2G actual-reference conformance boundary.
   Business impact: Test-only preparation; no production Multiplayer or Single Player behavior changed.
   Performance impact: Test/export execution only; no runtime impact.
   Risk: Five browser-owned HP/disposition cases remain deferred, and the already-defeated reference behavior remains explicitly labeled as a potential reference issue.
   Verification: Deterministic generation, focused 52-test gate, full 275-test gate, build, format, UTF-8, and diff checks.

2. Modification time: `2026-09-07`
   Location: `CombatDamageResolutionTests` fixture validation and conformance helpers.
   Change: Required the exact source list and stable ID order, validated fixture metadata, preserved the known reference issue, and consumed all applicable reference-conformance cases through the C# public rule APIs.
   Reason: Ensure the generated data is both structurally exact and behaviorally connected to the actual Multiplayer pure-rule implementation.
   Business impact: Test-only; no database, Redis, MQ, PLC, third-party API, or production behavior impact.
   Performance impact: Adds bounded test-time deserialization and 42 deterministic case checks.
   Risk: State-integration semantics are intentionally not asserted until their approved later task.
   Verification: Focused and full server gates pass.

## Escalation

Sol High escalation is not required. The task is deterministic test preparation with no production data, security, migration, concurrency, destructive change, or architecture risk.
