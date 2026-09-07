# Phase 2G Task 8 Report

## Outcome

Task 8 now consumes an eligible retained combat-damage disposition and applies its HP or opponent-vitality effects, participant eligibility, stable-order repair, side defeat, immutable result, lifecycle status, and one game revision through the existing single `TryReplace` performed while the per-room lock is held.

The implementation remains internal-only. It adds no route, projection, realtime delivery, reconnect behavior, client behavior, Multiplayer Scenario semantics, public weapon editor, database, migration, configuration, or deployment change. Existing Task 5–7 working-tree changes were preserved without reset, revert, stash, commit, or push.

## Confirmed Root Cause and Decision Rationale

- The Task 7 transition deliberately calculated and retained damage without mutating eligible targets. It therefore left `HpAfter == HpBefore`, `HpDamageApplied=false`, opponent vitality unchanged, and no turn/termination repair.
- `ResolveCombatDamageAsync` already executes inside `WithRoomLockAsync`, and the aggregate store exposes one expected-object `TryReplace`. The existing `IHpDamageEngine.Apply` is a pure state transformation. Task 8 could therefore include HP output in the same replacement without a second commit or recursive coordinator call.
- The HP engine previously kept its Major Wound/CON decision private. `CocHpDamageEngine.RequiresConRoll` is now the single public decision used both by `CocHpDamageEngine.Apply` and combat application; Combat does not copy the threshold.
- A focused RED review exposed an additional exactly-once risk: deterministic extreme damage can itself use no RNG while canonical order repair performs a scheduled Dying percentile roll. A failed post-wrap replacement must still throw the Task 7 non-retryable invariant exception. The repair result therefore carries whether wrap RNG began into the existing commit boundary.

## TDD Evidence

Exact Task 8 command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~HpDamageResolutionTests|FullyQualifiedName~GameStateTests.ResolveCombatDamage|FullyQualifiedName~GameStateTests.CombatDamageOrder" --nologo -v:minimal
```

Initial RED:

- API project built.
- Test project failed to compile at `HpDamageResolutionTests.cs:22` with CS0117 because `CocHpDamageEngine.RequiresConRoll` did not exist.
- This was the intended missing shared Task 8 boundary, before any production implementation.

Focused post-wrap RNG RED using the same exact command:

- Failed: 1
- Passed: 27
- Skipped: 0
- Total: 28
- Failure: `ResolveCombatDamage_PostWrapDyingRngStoreFailureAlsoProhibitsReroll` received no exception because wrap-only RNG was not yet propagated to the Task 7 post-RNG replacement boundary.

Final GREEN using the same exact command:

- Failed: 0
- Passed: 29
- Skipped: 0
- Total: 29
- Duration: 312 ms

## Changes Made

1. Added public `CocHpDamageEngine.RequiresConRoll(CharacterHealthState, int)` and refactored `Apply` to use it. Only positive damage below instant-death magnitude and at or above the canonical Major Wound threshold requires a CON percentile roll.
2. Applied positive investigator net damage only through `IHpDamageEngine.Apply` with stable key `combat:{ExchangeId}`. Zero net damage skips both HP and CON. Fresh Dying resets its combat schedule at the current round; Dying remains active. Only `DeadCondition` inactivates an investigator and removes its schedule.
3. Applied opponent damage only to `OpponentVitalityState.CurrentHp`, floored at zero. No `CharacterHealthState`, Major Wound, Dying, Stabilization, or CON behavior was created for opponents. Immutable opponent profile and MaxHp remain unchanged.
4. Preserved stable `Order` and participant identity. Repair starts at the already-advanced current index, leaves an active current actor untouched, scans from the same inactive index, and invokes the existing canonical round-wrap path exactly once only when the scan reaches the end.
5. Added minimum terminal reasons only: `opposition_defeated` when no active opposition remains and `investigators_defeated` when no active investigator remains. One dead investigator does not end combat while another investigator is active.
6. Preserved Task 7 Consumed replay as read-only and the post-RNG `CombatDamageCommitInvariantException` as non-retryable. Generic damage RNG, CON RNG, and Dying RNG reached during repair all participate in that boundary.

## Validation Coverage

The 29 focused tests cover the shared CON boundary and HP conformance fixtures; ordinary investigator HP loss; stable event identity; replay without a second HP event; Major Wound CON success/failure; unconsciousness; fresh Dying and stale Stabilization invalidation; instant death; one-survivor continuation; investigator-side defeat; zero net HP/CON skip; opponent zero and positive damage; vitality floor; immutable opponent profile; opposition-side defeat; inactive first/middle/last order positions; no second wrap/reset after an already-wrapped opposed resolution; one canonical genuine wrap; and non-retryable store failure after wrap-only Dying RNG.

Scoped `git diff --check` completed without whitespace errors. Git emitted only the repository line-ending policy warning that LF will be converted to CRLF when Git next touches the four C# files.

## Risk Assessment and Affected Scope

- **State consistency:** HP/vitality, participant state, schedule/order/termination, disposition result/status, and revision are assembled into one immutable replacement. No partial aggregate state is stored when `TryReplace` fails.
- **Exactly-once:** Consumed replay still returns the retained state/result without RNG, HP, replacement, turn repair, or notification. Any replacement failure after relevant RNG remains explicitly non-retryable.
- **Compatibility:** Existing public contracts and routes are unchanged. Stable participant/order records are retained for history identity.
- **Performance:** One HP transformation or one participant vitality replacement plus bounded order/schedule scans occurs per new damage consumption. No external I/O or new persistence work is added.
- **Operational scope:** In-memory gameplay coordinator only. No database, Redis, MQ, PLC, secret, permission, migration, or deployment blast radius exists.

## Rollback and Recovery

Rollback is source-only: remove only the Task 8 hunks from the four owned C# files and delete this report. Do not reset or restore whole shared files because `GameCoordinator.cs` and `GameStateTests.cs` also contain preserved Task 5–7 changes.

If `CombatDamageCommitInvariantException` occurs after any damage/CON/Dying RNG, the stored disposition remains Pending but the same ExchangeId must not be retried or re-rolled. Operators must investigate the lock/store invariant. No automated recovery or distributed durable-roll reservation was added.

## Remaining Unknowns and Deferred Work

- Real multi-process or distributed-store exactly-once safety is not claimed; the verified guarantee depends on the current in-process room lock and expected-object in-memory replacement.
- Task 9 projection/realtime/reconnect/routes/client work remains explicitly deferred.
- No full solution-wide unfiltered regression was requested or claimed; validation used the approved exact Task 8 scope.

## Modification Record

1. Modification time: 2026-09-07 14:56:39
   Location: `CocHpDamageEngine.RequiresConRoll` and `CocHpDamageEngine.Apply`
   Change: Added and reused the canonical CON-roll decision.
   Reason: Prevent threshold duplication and unnecessary percentile RNG.
   Business impact: Changes only when a CON roll is requested, not the canonical HP outcome rules.
   Performance impact: Removes unnecessary combat percentile rolls for zero, ordinary, and instant-death damage.
   Risk: Incorrect boundary logic would alter Major Wound handling; focused threshold tests and existing HP fixtures are green.
   Regression recommendation: Retain threshold-edge and committed fixture tests.

2. Modification time: 2026-09-07 14:56:39
   Location: `GameCoordinator.ResolveCombatDamageCore`, damage consumption, repair, and termination helpers
   Change: Added atomic investigator HP/opponent vitality application, schedule/activity synchronization, stable-order repair, minimum termination, and wrap-RNG propagation.
   Reason: Complete the approved Task 8 internal aggregate transition while preserving Task 7 exactly-once behavior.
   Business impact: Eligible retained combat damage now changes the canonical target and combat lifecycle.
   Performance impact: In-memory bounded collection copies/scans only; no new external operations.
   Risk: Turn/termination errors or unsafe re-roll after CAS failure; focused order, termination, replay, and failure-path tests are green.
   Regression recommendation: Keep the exact Task 8 filter and existing Task 7 failure-path tests green for later projection/storage changes.

3. Modification time: 2026-09-07 14:56:39
   Location: `HpDamageResolutionTests` and `GameStateTests`
   Change: Added Task 8 RED/GREEN behavior and failure-path coverage plus deterministic fixture controls.
   Reason: Prove HP, vitality, order, termination, RNG, and exactly-once requirements before completion.
   Business impact: Test-only.
   Performance impact: Test-only.
   Risk: None outside test execution.
   Regression recommendation: Continue using the exact approved Task 8 command.
