# Phase 2H Task 6 evidence

## Scope

- Implemented `PlayerCombatIntentCoordinator.PassAsync` only.
- Added the seven required Pass orchestration tests.
- The application layer performs viewer-projection validation and delegates exactly one successful transition to `PassCombatTurnAsync`.
- It does not calculate the next actor, round wrap, Dying schedule, action counts, or publishing.

## TDD evidence

RED command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~PlayerCombatIntentCoordinatorTests.Pass" --nologo -v:minimal
```

RED result: 7 failed, 0 passed. Each failure showed the pre-existing `InvalidIntent` result from the unimplemented Pass coordinator path.

GREEN command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~PlayerCombatIntentCoordinatorTests.Pass|FullyQualifiedName~GameStateTests.InternalCombat_Pass|FullyQualifiedName~GameStateTests.CombatDamageGate" --nologo -v:minimal
```

GREEN result: 11 passed, 0 failed, 0 skipped.

## Verified invariants

- Successful Pass emits one `PassCombatTurnCommand` containing only room ID, requesting player ID, and expected game revision, then returns a fresh projection at revision `expected + 1`.
- Rejections for stale revision, unowned actor, non-current actor, NPC actor, and blocked progression make no Pass call or publication and leave the recorded canonical state revision unchanged.
- The selected canonical Pass and damage-gate regressions remain green; round wrap and Dying scheduling remain owned by the canonical transition.

## Deliberately out of scope

- No HTTP route, client, realtime, state-store, dice, publish, or multiplayer Task 7 changes.
- No commit, push, amend, rebase, stash, reset, or full-suite run.

## Review follow-up evidence

Additional RED command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~PlayerCombatIntentCoordinatorTests.Pass" --nologo -v:minimal
```

Additional RED result: 1 failed and 6 passed. The newly added pending-damage-disposition regression failed because the pre-helper projection still allowed Pass, proving the regression was not a synthetic `CanPass: false` case.

Follow-up GREEN command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~PlayerCombatIntentCoordinatorTests.Pass|FullyQualifiedName~GameStateTests.InternalCombat_Pass|FullyQualifiedName~GameStateTests.CombatDamageGate|FullyQualifiedName~GameStateTests.InternalCombat_RoundWrapDelaysDyingCheckUntilFollowingRoundAndRetainsSuccessfulSchedule" --nologo -v:minimal
```

Follow-up GREEN result: 12 passed, 0 failed, 0 skipped.

- The pending-blocker test independently constructs a pending `DamageDispositionState` and a pending exchange; each causes its viewer projection to suppress `CanPass` and results in no transition.
- The canonical round-wrap regression executes real `PassCombatTurnAsync` calls and verifies round 2 retains `DyingScheduleState(1, null)`, round 3 records exactly one round-2 dying check with `DyingScheduleState(1, 2)`, and the next wrap records exactly one additional check with `DyingScheduleState(1, 3)`.
- Every coordinator rejection now asserts zero Pass/Begin/Resolve/Damage commands, zero exchange IDs/dice/publications, and unchanged canonical revision, turn index, round, and history.
