# Phase 2G Task 6 Report

## Scope

Implemented only the approved disposition-state migration and causal damage gate.

Task-owned files changed by Task 6:

- `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CombatSessionState.cs`
- `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs`
- `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`
- `.superpowers/sdd/phase-2g-task-6-report.md`

The Task 5 changes already present in the shared source/test files were preserved. `GameContracts.cs` remains owned by this task boundary but required no additional Task 6 edit beyond its existing Task 5 changes. No Task 7 `ResolveCombatDamage`, generic damage RNG, HP/vitality mutation, profile work, projection, realtime, client, route, commit, push, reset, revert, or stash work was performed.

## UTF-8 gate

Before project file inspection or editing, strict throwing UTF-8 decoding reported `UTF8_OK` for all four existing owned source/test files. The Task 6 report did not yet exist. Final strict validation also reported `UTF8_OK` for all five owned files. No suspected ANSI/GBK file was found and existing Chinese text remains valid.

## TDD RED evidence

After adding the migration, durability, causal-gate, legacy-order, and no-hit tests but before production implementation, the exact required command was run:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.InternalCombat|FullyQualifiedName~GameStateTests.CombatDamageGate" --nologo -v:minimal
```

Initial RED:

- Exit code: `1`
- API production project built.
- Test compilation failed with 3 `CS0246` errors because the approved `DamageDispositionState` type did not exist.
- Error locations at that run: `GameStateTests.cs` lines 1655, 1747, and 1761.
- No unrelated baseline failure was reported.

The first implementation run then produced a behavioral RED:

- Exit code: `1`
- Passed: `36`
- Failed: `1`
- Skipped: `0`
- The old duplicate-resolve assertion expected `InvalidExchange`, while the newly active causal gate correctly returned `PendingConflict` before additional dice. The assertion was migrated to the approved gate behavior.

## Design rationale and implementation

- Replaced the old `DamageDisposition` primary-constructor booleans with canonical `DamageDispositionData`, `DamageDispositionStatus` (`Pending`/`Consumed`), and `DamageDispositionState` with nullable `Result`.
- Added the complete immutable `CombatDamageResult` data contract required by `DamageDispositionState.Result`. This is type-only groundwork forced by the Task 6 contract; no Task 7/8 resolution or HP behavior was implemented.
- Renamed the durable registry to `DamageDispositions`, keyed by `ExchangeId`; consumed entries are retained rather than deleted.
- A damage-eligible opposed resolution creates `Status=Pending`, `Result=null`, and `CreatedGameRevision=state.Revision+1`, which is the same revision committed by that resolve transition.
- A no-hit resolution creates no disposition.
- Added the pure `FindBlockingDamageDisposition` coordinator helper. It filters only `Pending`, then selects the lowest `CreatedGameRevision`, with ordinal `ExchangeId` as the deterministic tie-break. Earlier consumed truth does not block.
- Begin, Pass, manual End, and pending-exchange resolution check the blocker before expected-revision validation, dice, action-count changes, turn advancement, or state replacement.
- Trusted End still cancels an unresolved `PendingCombatExchange` when no damage disposition exists. Once a resolved hit creates a Pending disposition, End returns `PendingConflict` and cannot erase it.
- Normal flow cannot create a second Pending disposition because the first Pending state blocks the next Begin and any legacy unresolved progression.
- Exchange-history projection compatibility uses a read-only derived `Pending` accessor computed from `Status`; the former stored `Pending` and `HpCommitted` constructor booleans are gone, and `Status` is the sole lifecycle field.

## GREEN and compatibility evidence

Exact Task 6 focused GREEN:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.InternalCombat|FullyQualifiedName~GameStateTests.CombatDamageGate" --nologo -v:minimal
```

- Exit code: `0`
- Passed: `37`
- Failed: `0`
- Skipped: `0`
- No build warnings were emitted.

Broader `GameStateTests` compatibility gate:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests" --nologo -v:minimal
```

- Exit code: `0`
- Passed: `59`
- Failed: `0`
- Skipped: `0`
- No build warnings were emitted.

Formatting verification:

```powershell
dotnet format multiplayer/server/Trpg.Multiplayer.slnx --verify-no-changes --no-restore --verbosity minimal
```

- Exit code: `0`
- No formatting changes were required.

## Self-review

- Gate checks precede revision checks on every scoped normal-progression entry point and precede all percentile rolls.
- The disposition created by a hit is retained both after `LastExchange` replacement and after the 120-entry exchange-history trim; the durability test retains 121 registry entries while history remains capped at 120.
- The durability fixture marks prior entries Consumed only through test-owned state setup because Task 6 intentionally has no consumer command; production consumption is deferred.
- No consumed entry is deleted and no resolved hit is erased by End.
- No public API or projection contract was expanded.
- Existing Task 5 profile/start behavior remains covered by the broader `GameStateTests` run.

## Modification record

1. Modification time: `2026-09-07 12:17:35 +08:00`
   Location: `CombatSessionState` disposition records and `GameCoordinator` opposed-resolution/progression transitions.
   Change: Migrated to one explicit disposition status model, created durable revision-stamped Pending entries, and added the deterministic causal blocker to Begin, Pass, End, and pending-exchange resolution.
   Reason: Prevent any normal action, turn mutation, dice, or manual termination from overtaking an already-adjudicated hit.
   Business impact: Combat now pauses after a damage-eligible exchange until a later approved damage-consumption transition consumes that exact disposition. Unresolved exchanges remain cancellable by trusted End.
   Performance impact: Adds an in-memory ordering scan over the combat-lifetime disposition registry at scoped progression gates; no I/O, database, Redis, MQ, PLC, or third-party calls were added.
   Risk: The registry is intentionally lifetime-retained and therefore grows with consumed exchanges; persistence/compaction is explicitly deferred by the approved design. The derived history projection accessor is compatibility-only and not canonical state.
   Regression recommendation: Keep the exact Task 6 filter, the broader `GameStateTests` filter, and future Task 7 stale replay/wrong-blocker tests active.

## Escalation

Sol High escalation is not required. The implementation did not require deleting consumed truth, erasing a resolved hit, changing authorization/security, migrating data, performing destructive operations, or redesigning concurrency.
