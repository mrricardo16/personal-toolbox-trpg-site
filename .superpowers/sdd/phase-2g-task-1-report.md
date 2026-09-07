# Phase 2G Task 1 RED Evidence

## Scope and baseline

- Baseline branch: `codex/01a0565ef59c7d70868a46e38cd3026a`
- Baseline HEAD: `d0df031acdd092a311f9d7d6ec173af3fb748d1d`
- Both Task 1 target files were absent before this task.
- The approved design and Task 1 plan decoded as strict UTF-8.
- No production source, fixture JSON, exporter, Single Player file, or other test file was modified by Task 1.

## Files created

- `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/CombatDamageResolutionTests.cs`
- `.superpowers/sdd/phase-2g-task-1-report.md`

## RED contracts

- Parser: `DiceExpressionParser.Parse` normalizes `"  D6+2 "` to `DiceExpression("d6+2", 1, 6, 2)`.
- Pure DB rules: `CocCombatDamageRules.DeriveDamageBonus` covers the 125 threshold and the specified zero/negative effective-sum clamp.
- Pure engine: `CocCombatDamageEngine.Resolve` locks the specified weapon + DB + Armor result (`NetDamage == 5`).
- Fixture loader: requires version 1, exactly 48 cases, unique ordinal IDs, all four required actual-JS reference sources, and only `reference_conformance`, `potential_reference_issue`, or `deferred` labels.

## Focused RED execution

Command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter FullyQualifiedName~CombatDamageResolutionTests --nologo -v:minimal
```

Result:

- Exit code: `1`
- API production project: built successfully before test-project compilation.
- Failure count: `10` compiler errors.
- Failure cause: only the planned Task 1 public production types are absent: `DiceExpressionParser`, `DiceExpression`, `CocCombatDamageRules`, `CocCombatDamageEngine`, `CombatDamageInput`, `CombatDamageMode`, and `GenericDiceRoll`.
- No restore, project-load, baseline compilation, or unrelated test failure appeared.
- The missing future `combat-damage.json` fixture was not reached because compile-time RED occurs first.

## UTF-8 and focused review

- Strict UTF-8 decoding passed for the created C# test after writing.
- Focused review confirmed the test uses the exact planned names/signatures and does not introduce production implementation.
- Final strict UTF-8 and trailing-whitespace checks passed for both created files; focused status shows only the two Task 1 files as untracked outputs.
