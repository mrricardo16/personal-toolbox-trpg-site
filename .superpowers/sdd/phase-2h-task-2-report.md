# Phase 2H Task 2 Report

## Scope

Implemented only the viewer-specific `CombatViewerActionsSnapshot` projection and its focused tests. No routes, clients, coordinators, realtime behavior, or later-phase work were changed.

## TDD evidence

### RED

Command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.Projection_CombatViewerActions|FullyQualifiedName~GameApiTests.CombatProjection" --nologo -v:minimal
```

Result: failed at compilation as expected, with 10 errors. The errors were only the absent `CombatViewerActionsSnapshot` type and absent `CombatSnapshot.ViewerActions` member referenced by the new tests.

### GREEN

Command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.Projection|FullyQualifiedName~GameApiTests.CombatProjection" --nologo -v:minimal
```

Result: passed with 14 passed, 0 failed, 0 skipped. The output reported no warnings or errors.

## Acceptance evidence

- The owned unblocked current investigator gets only `ActorCharacterId`, melee/pass flags, and active opposite-side target IDs.
- A non-current owner, attacker awaiting a response, observer, pending-damage owner, and nonparticipant receive no inappropriate affordance; a nonparticipant still has `Combat = null`.
- Only the exact active investigator defender owner receives a pending response, sourced from canonical `PendingExchange.AvailableResponses` and mapped to `dodge` / `fight_back`.
- `ViewerActions` and `PendingResponse` serialize to the exact required camel-case property sets. The tests reject internal policy, allowance, rolls, raw target data, stats, registry, schedule, history, source, and provenance fields.
- The projection is read-only and does not alter game revisions.

## Changed files

- `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs`
- `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameProjection.cs`
- `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`
- `.superpowers/sdd/phase-2h-task-2-report.md`

## Remaining verification boundary

Only the required focused projection test selection was run. The full suite was intentionally not run for this task.

## Inactive combat review-finding regression

### RED

Command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.Projection_CombatViewerActions_InactiveCombatHasNoActionableAffordance" --nologo -v:minimal
```

Result: failed with 1 failed, 0 passed, 0 skipped. The inactive session projected a non-null `CombatViewerActionsSnapshot` with `CanMeleeAttack = True` and `CanPass = True`.

### GREEN

Command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.Projection|FullyQualifiedName~GameApiTests.CombatProjection" --nologo -v:minimal
```

Result: passed with 15 passed, 0 failed, 0 skipped. The new inactive-combat regression and all prior Task 2 projection tests were included.

### Fix

`GameProjection.BuildViewerActions` now returns `null` before deriving any affordance when `CombatSession.Active` is `false`. The existing read-only combat summary remains unchanged.
