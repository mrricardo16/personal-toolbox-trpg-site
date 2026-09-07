# Phase 2G Task 2 Report

## Design rationale

- `DiceExpressionParser` mirrors the approved Single Player grammar and bounds without changing `src/check-engine.js`.
- Canonical text is only trimmed and lowercased; omitted count text is preserved (`d3` remains `d3`) while the structured count defaults to 1.
- .NET regex matching uses ECMAScript semantics so `\d` remains ASCII-only like the JavaScript reference.
- `SecureDiceRoller` remains the only production RNG authority. Generic dice use `RandomNumberGenerator.GetInt32(1, Faces + 1)` and percentile behavior is unchanged.
- Generic requests are validated before allocation or rolling. Returned raw rolls are read-only and the total is accumulated in a checked context.

## Files changed

- Created `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/DiceExpression.cs`.
- Modified `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CheckResolution.cs`.
- Modified `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/CheckResolutionTests.cs`.
- Mechanically modified the two existing `IDiceRoller` fake bases/implementations in `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`; unsupported generic rolls throw exactly `InvalidOperationException("No deterministic generic roll remains.")`.
- Did not modify the Task 1 `CombatDamageResolutionTests.cs` file.

## TDD evidence

Exact required RED command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~CombatDamageResolutionTests|FullyQualifiedName~CheckResolutionTests" --nologo -v:minimal
```

Initial RED result after adding Task 2 tests and before production implementation:

- Exit code: `1`.
- Production API project built.
- Test compilation reported 12 errors, all caused by the absent Task 2 `DiceExpressionError` contract.
- No unrelated restore, project-load, or baseline production compilation failure appeared.

Post-implementation result from the same exact command:

- Exit code: `1`, as expected while Task 3 is absent.
- Production API project built.
- Test compilation reported exactly 6 errors, all limited to the missing Task 3 symbols `CocCombatDamageRules`, `CocCombatDamageEngine`, `CombatDamageInput`, and `CombatDamageMode` in `CombatDamageResolutionTests.cs`.
- No Task 2 parser, generic-dice, interface, or fake compiler error remains.
- Because Task 3 compile-time RED blocks the test assembly, xUnit executed 0 tests and therefore cannot yet provide a focused xUnit pass count for Task 2.

Independent Task 2 contract probe against the freshly built API assembly:

- 29 checks passed and 0 failed.
- Checks covered 7 accepted parser cases, 12 rejected parser/error cases, one 100d10000 generic roll contract, four invalid generic requests, and five unchanged percentile configurations.
- The probe included the JavaScript-parity case `1d٦`, which now fails as `InvalidFormat` instead of being interpreted as a numeric faces value.

Production build:

```powershell
dotnet build multiplayer/server/src/Trpg.Multiplayer.Api/Trpg.Multiplayer.Api.csproj --no-restore --nologo -v:minimal
```

- Exit code: `0`.
- 0 warnings, 0 errors.

## UTF-8 and scope checks

- Before editing, strict UTF-8 decoding passed for every authorized existing file.
- Final strict UTF-8 decoding and trailing-whitespace checks passed for all five Task 2 files; `git diff --check` also passed.
- No Combat Damage engine/rules, fixture/exporter, integration, Single Player source, commit, stash, reset, or push was performed.

## Modification record

- Modification time: `2026-09-07 11:25:24`.
- Location: dice-expression contract/parser, existing secure dice authority, parser/generic-dice tests, and existing server-test dice fakes.
- Reason: implement the approved Task 2 pure parser boundary and reuse the existing cryptographic RNG for validated generic dice.
- Business behavior: adds the approved generic dice capability and preserves existing percentile behavior; no Combat Damage resolution behavior is implemented.
- Performance: bounded to at most 100 rolls and 1,000,000 total; validation occurs before allocation.
- Remaining risk: the exact focused xUnit command cannot execute tests until Task 3 supplies its planned production contracts.
- Sol High escalation: not required.
