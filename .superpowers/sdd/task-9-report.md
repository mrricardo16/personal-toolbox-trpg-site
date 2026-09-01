# Task 9 report: per-character dying scheduling and atomic round wrap

## Scope and constraints

- Preserved all pre-existing dirty Task 6–8 files and changes; no reset, stash, checkout, commit, or push was performed.
- Modified only `GameCoordinator.cs` and `GameStateTests.cs` for production/test behavior. `HealthStabilizationResolution.cs` was inspected but did not require a helper or modification.
- This report is the required Task 9 artifact. All edited text files decode as strict UTF-8.

## TDD evidence

### RED

Command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests" --nologo -v:minimal
```

Result: **28 passed, 2 failed, 0 skipped**. The two new round-wrap timing tests failed before production implementation because the existing wrap only advanced the round and reset counters; it neither created the new per-character observations nor invoked Health Stabilization.

### GREEN

Focused command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests" --nologo -v:minimal
```

Result: **31 passed, 0 failed, 0 skipped**.

Required combined command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests|FullyQualifiedName~HealthStabilizationResolutionTests|FullyQualifiedName~HpDamageResolutionTests" --nologo -v:minimal
```

Result: **42 passed, 0 failed, 0 skipped**.

## Implemented behavior

- `CombatSession.DyingSchedule` remains the sole timing authority and is keyed by `CharacterId`; no scalar single-player timing field was added.
- At a completed round, schedules are reconciled in stable `CombatSession.Order`. Active, unstabilized, non-dead dying investigators are observed at the completed round; therefore they are never checked in their observation round.
- Eligible later checks use the existing pure Health Stabilization engine, a server percentile roll, and source ID `combat-round-{finishedRound}-{characterId:N}`. Successful checks retain dying and update `LastCheckCompletedRound`; dead or stabilized investigators are removed from the schedule.
- Each round wrap computes all eligible health results and participant inactivity changes in memory, resets counters, advances the session, calls `TryReplace` once, increments revision once, and returns through the existing post-commit-only notifier wrapper. It does not call the single-character coordinator health methods in a loop.
- A failed investigator check marks only that participant inactive. The `CombatSession` remains active and later turn selection skips inactive participants; no automatic full-combat end was added.

## Test coverage and transaction evidence

- Already-dying start observation, no same-round check, first following completed-round check, later once-per-round checks, successful dying retention, and deterministic source IDs.
- Two independently observed investigators resolve in combat order with deterministic rolls. Their completed wrap advances revision by only the three validated turn transitions; no per-character additional replacement/revision is created during the final wrap.
- Death leaves the other investigator active, marks only the deceased participant inactive, and preserves `CombatSession.Active`.
- Stabilization removes a schedule entry; a later fresh dying episode is observed anew and is not checked in its observation round.

## Self-review and concerns

- `git diff --check` passed.
- Strict UTF-8 decoding passed for `GameCoordinator.cs`, `GameStateTests.cs`, and this report.
- No route, projection, SignalR contract, reconnect, client, Combat Damage, weapon, Armor, direct HP behavior, persistence, AI, or Single Player artifact was modified.
- The working tree retains unrelated reviewed Task 6–8 modifications by design; the aggregate diff therefore includes their files in addition to the Task 9-owned files.

## Review correction: fresh dying re-observation with an existing schedule entry

### Correction RED

Before production changes, the focused command was run after adding a regression for this exact sequence: a round-2 dying check succeeds, first aid stabilizes during round 3, then trusted same-round damage starts a fresh dying episode while the old schedule entry remains.

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests" --nologo -v:minimal
```

Result: **31 passed, 1 failed, 0 skipped**. The new regression failed as expected: it found stale `DyingScheduleState { ObservedRound = 1, LastCheckCompletedRound = 3 }` after the round-3 wrap instead of the required new observation `{ ObservedRound = 3, LastCheckCompletedRound = null }`. This proved that the stale entry incorrectly authorized a same-round check.

### Correction implementation

`ApplyDamageCore` now detects only an active combat participant's transition from non-dying health to a fresh dying episode. In the same canonical replacement that applies the HP/health result, it replaces that character's schedule entry with `DyingScheduleState(combat.Round, null)`. No interface, model, contract, helper, or non-combat damage behavior changed.

### Correction GREEN

Focused command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests" --nologo -v:minimal
```

Result: **32 passed, 0 failed, 0 skipped**.

Combined command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests|FullyQualifiedName~HealthStabilizationResolutionTests|FullyQualifiedName~HpDamageResolutionTests" --nologo -v:minimal
```

Result: **43 passed, 0 failed, 0 skipped**.

The regression proves no extra round-wrap dying roll occurs in the fresh episode's observation round; the next eligible check is deferred to the following completed round. No escalation is required.
