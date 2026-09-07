# Phase 2G Task 7 Report

## Outcome

Task 7 now provides the internal-only `ResolveCombatDamage` transition required by the approved Phase 2G plan. The transition performs exact registry/status lookup before Pending revision validation, returns a Consumed result as an immutable `Changed=false` replay, validates the complete canonical damage roll plan before RNG, consumes an already-ineligible target without RNG, and classifies a post-RNG store replacement failure as a dedicated internal consistency exception that explicitly prohibits re-rolling the ExchangeId.

Task 5 and Task 6 changes already present in the shared working tree were preserved. No public route, projection, realtime delivery, client, HP application, opponent vitality mutation, turn repair, termination, Task 8 behavior, commit, push, reset, revert, or stash was added.

## Confirmed Baseline and Decision Rationale

- The approved design requires `ExchangeId` to remain the sole consumption identity and retained `Pending -> Consumed` status/result to remain the exactly-once authority.
- The pre-Task-7 implementation had the Task 5 profile snapshots and Task 6 retained disposition registry/gate, but it had no `ResolveCombatDamageAsync` command, result, or internal interface method.
- The current `GameCoordinator` already serializes mutations with a per-room `SemaphoreSlim`, while `IGameStateStore.TryReplace` performs expected-object replacement. Therefore all ordinary validation can finish under the room lock before RNG, and a failed replacement after RNG is evidence that the storage/locking invariant was broken. Returning an ordinary `StateConflict` would incorrectly authorize a retry and possible re-roll.

## TDD Evidence

Exact RED command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.ResolveCombatDamage" --nologo -v:minimal
```

Observed RED:

- Failed: 9
- Passed: 0
- Skipped: 0
- Total: 9
- The test assembly built successfully. Failures were caused by the absent `ResolveCombatDamageAsync` method/internal-interface member, including the expected reflection lookup failure.

Exact GREEN command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.ResolveCombatDamage" --nologo -v:minimal
```

Observed final GREEN:

- Failed: 0
- Passed: 9
- Skipped: 0
- Total: 9
- Duration: 110 ms

Focused class regression:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests" --nologo -v:minimal
```

Observed:

- Failed: 0
- Passed: 68
- Skipped: 0
- Total: 68
- Duration: 272 ms

## Implemented Behavior

1. Added internal `ResolveCombatDamageCommand`, `ResolveCombatDamageResult`, and the `IInternalCombatResolutionCoordinator.ResolveCombatDamageAsync` member. `GameCoordinator` implements the member explicitly as well as through its internal concrete method. No public API surface was added.
2. Implemented the approved lookup order:
   - room and active game/session;
   - registry existence;
   - exact ordinal ExchangeId entry and lifecycle consistency;
   - Consumed immutable replay before revision validation;
   - Pending expected revision;
   - deterministic blocker;
   - unique owner/target and canonical profile/mode/vitality/roll-plan validation;
   - no-RNG `TargetAlreadyIneligible` consumption or the RNG path.
3. Added the minimal generic roll planning and pure `ICombatDamageEngine` calculation needed to create and retain a Task 7 result. Task 8 HP/vitality/order changes remain deliberately absent: eligible-target results retain the pre-damage HP values with `HpDamageApplied=false`.
4. A Pending-to-Consumed replacement increments the revision once and retains the immutable result. A stale replay returns the exact stored state/result instances with `Changed=false` and performs no replacement, RNG, HP call, vitality update, history append, turn repair, or notification.
5. A failed `TryReplace` after the first generic damage roll throws `CombatDamageCommitInvariantException`. Its message states that the ExchangeId must not be re-rolled. There is no loop, automatic retry, `StateConflict` translation, HP call, or notifier call.

## Test Coverage

The 9 Task 7 tests cover:

- internal interface ownership;
- consumed replay with the original stale revision and no additional side effects;
- Pending stale revision before throwing dice;
- missing ExchangeId before throwing dice;
- deterministic wrong blocker before throwing dice;
- malformed Consumed status/result as an internal invariant;
- malformed canonical profile as an internal invariant;
- `TargetAlreadyIneligible` one-time consumption, revision increment, gate release, unchanged vitality/turn/history, and stale replay;
- controlled post-RNG replacement failure with exactly one generic roll, exactly one consumption replacement attempt, retained Pending state, zero HP calls, zero notifier calls, and an explicit no-reroll exception.

## Risk Assessment

- **Exactly-once risk:** Controlled by checking Consumed before revision and making the post-RNG failure non-retryable. The coordinator never auto-retries.
- **State compatibility:** Existing Task 5/6 records and gate semantics are retained. Consumed results are validated only for identity/mode/outcome consistency on replay; damage is not reconstructed from bounded exchange or HP history.
- **Task 8 boundary:** HP, opponent vitality, participant activity repair, turn repair, and termination are not applied for an otherwise eligible target. This is intentional Task 7 scaffolding and is not a claim that combat damage is user-ready.
- **Persistence/deployment:** No database, Redis, migration, configuration, secret, route, or deployment change exists. The blast radius is the internal in-memory gameplay coordinator and focused tests only.
- **Operational recovery:** If `CombatDamageCommitInvariantException` occurs, the stored disposition remains Pending, but replay is explicitly prohibited because the consumed roll was not durably reserved. Operators must investigate the lock/store invariant. A future distributed or optimistic store requires durable roll reservation or an equivalent exactly-once mechanism before retries can be safe.

## Rollback

Rollback is source-only and requires removing only the Task 7 additions from the four owned C# files and deleting this report. Do not reset or revert the shared files wholesale because they contain Task 5/6 work that must remain. No data recovery or schema rollback is required.

## Remaining Unknowns and Deferred Work

- Task 8 must atomically apply positive investigator damage through `IHpDamageEngine`, opponent vitality changes, eligibility synchronization, turn repair, and termination without changing the no-reroll boundary.
- No public route, projection, realtime/reconnect behavior, client action, or concurrency duplicate-consumption test is part of this task.
- Real multi-process/distributed-store safety is not claimed; the current guarantee relies on the verified in-process room lock and expected-object in-memory replacement architecture.

## Modification Record

1. Modification time: 2026-09-07 14:40:33
   Location: `GameContracts.cs`, `IGameCoordinator.cs`, and `GameCoordinator.ResolveCombatDamageAsync`
   Change: Added the internal command/result/interface seam, exact validation order, immutable replay, generic roll calculation, no-op ineligible consumption, and post-RNG invariant exception.
   Reason: Implement the approved Task 7 exactly-once boundary without entering Task 8 or public/realtime scope.
   Business impact: Changes internal combat-damage lifecycle behavior from unavailable to an internal retained result transition. It does not yet apply eligible-target HP/vitality damage.
   Performance impact: One dictionary lookup/copy and one in-memory CAS per new consumption; replay is read-only and performs no CAS or RNG.
   Risk: Replaying after a post-RNG invariant exception is unsafe and explicitly prohibited.
   Regression recommendation: Keep the exact Task 7 filter and broader `GameStateTests` filter green, then retain the invariant tests while Task 8 integrates atomic health and turn changes.

2. Modification time: 2026-09-07 14:40:33
   Location: `GameStateTests.ResolveCombatDamage*`
   Change: Added RED/GREEN coverage and controlled dice/store/HP/notifier instrumentation for validation order and exactly-once failure paths.
   Reason: Prove the required behavior and side-effect exclusions before implementation.
   Business impact: Test-only; no runtime behavior change.
   Performance impact: Test-only.
   Risk: None outside the test process.
   Regression recommendation: Run the exact focused command after every Task 8 or storage/locking change.
