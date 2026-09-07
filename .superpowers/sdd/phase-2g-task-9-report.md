# Phase 2G Task 9 Report

## Outcome

Task 9 now exposes only a derived `CombatDamageSnapshot` through `CombatSnapshot.LastDamage`, publishes the existing viewer-specific `GameSnapshot` only after a successful changed damage-consumption commit, restores that safe state on reconnect without mutation, and proves that the public Game API remains limited to initialize/get/check.

No combat/damage route, request DTO, damage-specific event stream, client behavior, persistence, migration, configuration, deployment change, commit, or push was added. Existing Task 5-8 working-tree changes were preserved without reset, revert, or stash.

## Pre-edit Escalation and Scope Extension

The initial mandatory implementation-path review found that `GameCoordinator.ResolveCombatDamageAsync` called `ResolveCombatDamageCore` under the room lock but did not invoke the existing realtime notifier. The originally delegated file list excluded `GameCoordinator.cs`, so the required commit-before-broadcast behavior could not be made green honestly. Work stopped before edits and before RED, exactly as the task's escalation condition required.

The main executor then explicitly authorized one additional production hunk: only the `ResolveCombatDamageAsync` wrapper may mirror neighboring combat wrappers by publishing after `IsSuccess && Changed`. No other coordinator logic was authorized or changed for Task 9.

## Confirmed Root Cause and Decision Rationale

- Canonical damage consumption and repair were already committed atomically by Task 8, but its internal wrapper returned the result without realtime delivery.
- `SignalRGameRealtimeNotifier.PublishGameSnapshotAsync` already reads the committed state and builds a separate `GameProjection` for every connected player in that room. Reusing it preserves viewer privacy and cross-room isolation.
- `GameResult.Changed` is false for a consumed stale-revision replay. Publishing only for successful changed results therefore makes replay silent without adding another idempotency mechanism.
- Exceptions and failed results return or throw before the notifier branch. A post-RNG replacement invariant failure therefore cannot publish an uncommitted snapshot.
- The canonical retained `CombatDamageResult` remains internal. The public DTO copies only ExchangeId, owner/target participant IDs, semantic outcome, net damage, and defeated state.

## TDD Evidence

Exact Task 9 command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.Projection|FullyQualifiedName~SignalRGameDeliveryTests|FullyQualifiedName~GameApiTests" --nologo -v:minimal
```

Initial RED after the scope extension and test-first edits:

- The API project built.
- The test assembly did not run, so there was no executed-test count.
- Compilation failed with six intended missing-contract errors: one CS0246 for absent `CombatDamageSnapshot` and five CS1061 errors for absent `CombatSnapshot.LastDamage` across projection and SignalR tests.

First GREEN attempt after the minimal production implementation:

- Failed: 3
- Passed: 21
- Skipped: 0
- Total: 24
- One privacy assertion incorrectly treated the viewer's authorized lowercase `checkValues["str"]` as a forbidden opponent `Str` DTO property.
- Two realtime tests used pre-Task-5 character fixtures without canonical STR/SIZ values, so combat start correctly returned `InvalidParticipant`.

Those test-only fixture/assertion defects were corrected without relaxing the privacy matrix or production validation.

Final exact GREEN:

- Failed: 0
- Passed: 24
- Skipped: 0
- Total: 24
- Duration: 5 seconds
- Build output contained no warnings.

Broader `GameStateTests` regression:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests" --nologo -v:minimal
```

- Failed: 0
- Passed: 81
- Skipped: 0
- Total: 81
- Duration: 546 ms
- Build output contained no warnings.

Final artifact validation after creating this report:

- All seven Task 9 source, test, and report files decoded successfully with strict UTF-8 validation.
- Scoped `git diff --check` reported no whitespace errors. Git emitted only the repository line-ending policy warnings that LF will be converted to CRLF when Git next touches the six tracked C# files.
- The status scope audit found zero paths outside the initial Task 5-8 dirty baseline plus the seven authorized Task 9 paths.

## Changes Made

1. Added `CombatDamageSnapshot` with exactly six safe fields and nullable `CombatSnapshot.LastDamage`.
2. Added a derived projection that selects the latest retained Consumed result by `ResolvedAt`, with an ordinal ExchangeId tie-breaker, and maps only safe semantic values. Pending entries and malformed missing results are not projected as consumed damage.
3. Updated only `GameCoordinator.ResolveCombatDamageAsync` to call the existing notifier after a successful `Changed=true` result while still under the established room mutation wrapper.
4. Added projection/privacy tests covering two combat viewers, a room nonparticipant, latest-result selection, existing own-only Health, an exact six-property damage DTO, an exact safe opponent DTO, and absence of raw rolls, weapon/profile, opponent vitality/stats, disposition/result state, HP event identity, history, source, and provenance.
5. Added SignalR coverage for committed-state observation before delivery, one viewer-specific snapshot per connected room viewer, nonparticipant `Combat=null`, stale replay silence, cross-room silence, disconnected-owner recovery, latest own Health, defeated state, repaired current actor/order, retained result identity, and unchanged revision/state during attach.
6. Kept the existing controlled post-RNG store-failure test in the exact Task 9 filter and verified zero notifications.
7. Enumerated runtime endpoint route patterns and asserted that Game API has exactly initialize/get/check and zero combat/damage/resolve-damage/weapon/armor/start/attack/respond/dodge/fight-back/pass/end routes. The forbidden paths also return 404.

## Privacy and Failure-path Evidence

- Combat participants receive `LastDamage` containing only `ExchangeId`, `OwnerParticipantId`, `TargetParticipantId`, `Outcome`, `NetDamage`, and `TargetDefeated`.
- Exact investigator Health remains owner-only. The other combat participant cannot read it.
- Exact opponent HP/MaxHp/Armor/STR/SIZ/DB, full weapon/profile, raw weapon/DB rolls, gross damage, HP before/after, internal status/result/registry, event key, source/provenance, and histories are absent from the serialized projection.
- A room member who is not a combat participant receives the normal game snapshot with `Combat=null`.
- A changed consumption publishes only after the retained result and revision can be observed in the store. A stale consumed replay and post-RNG replacement failure publish zero additional snapshots.
- AttachSession returns the latest committed safe snapshot without changing the stored aggregate object, revision, or retained result identity.

## Risk Assessment and Affected Scope

- **Privacy:** The canonical damage records are never returned directly. Risk is limited to future additions to the safe DTO/projection; exact JSON property-set tests protect the current boundary.
- **Exactly-once:** Notification is downstream of commit and gated by `Changed`. It does not alter RNG, HP, disposition status, or replacement semantics.
- **Delivery:** As with existing gameplay delivery, a transport failure occurs after commit and does not roll back damage. No durable outbox is introduced or claimed.
- **Compatibility:** `LastDamage` is nullable with a default, preserving construction/deserialization compatibility for snapshots without consumed damage. Existing routes and event names are unchanged.
- **Performance:** Projection scans and sorts the retained combat-scoped disposition registry in O(n log n); no external I/O is added. The registry can grow with consumed exchanges during a long-running combat, so later lifecycle work should retain this cost as an explicit review point.
- **Operational blast radius:** In-memory multiplayer gameplay projection and existing SignalR snapshot delivery only. No database, Redis, MQ, PLC, credentials, authorization policy, migration, or deployment setting is affected.

## Rollback and Recovery

Rollback is source-only: remove only the Task 9 DTO/projection tests and hunks, the `ResolveCombatDamageAsync` notifier wrapper hunk, and this report. Do not restore `GameContracts.cs`, `GameCoordinator.cs`, or `GameStateTests.cs` wholesale because those shared files contain preserved Task 5-8 work.

No data recovery is required. If a realtime send fails, the committed aggregate remains authoritative and AttachSession can recover its latest safe projection. The existing post-RNG invariant rule still prohibits re-rolling an ExchangeId after a replacement failure.

## Remaining Unknowns and Deferred Work

- Real multi-process delivery durability and distributed-store exactly-once guarantees are not claimed.
- No full unfiltered solution test was run; verification used the approved exact Task 9 filter plus the broader full `GameStateTests` class.
- Task 10 client rendering/documentation/publishing remains deferred.
- Firearms, Impaling, Scenario, AI gameplay, public combat actions, and a damage event stream remain absent.

## Modification Record

1. Modification time: 2026-09-07 15:16:09
   Location: `GameContracts.cs` and `GameProjection.BuildCombat/BuildLastDamage`
   Change: Added the six-field safe damage DTO and latest-consumed derived projection.
   Reason: Allow participants to observe the canonical outcome without exposing internal damage authority or private stats.
   Business impact: Adds read-only safe Combat Damage state to existing snapshots.
   Performance impact: One bounded in-memory scan/sort during Combat projection.
   Risk: Incorrect projection could leak private combat data; exact property and viewer matrix tests are green.
   Regression recommendation: Keep the exact Task 9 privacy filter green whenever combat domain records change.

2. Modification time: 2026-09-07 15:16:09
   Location: `GameCoordinator.ResolveCombatDamageAsync`
   Change: Published the existing viewer-specific snapshot only after successful changed consumption.
   Reason: Task 8 committed damage without notifying connected viewers.
   Business impact: Connected room viewers now observe committed damage; replay/failure behavior remains silent.
   Performance impact: Reuses one existing per-room viewer delivery pass for each new consumption.
   Risk: Transport failure remains post-commit; reconnect is the recovery path.
   Regression recommendation: Retain commit-before-delivery, one-delivery, replay silence, failure silence, and cross-room tests.

3. Modification time: 2026-09-07 15:16:09
   Location: `GameStateTests`, `GameApiTests`, and `SignalRGameDeliveryTests`
   Change: Added Task 9 RED/GREEN privacy, delivery, reconnect, failure, and route-absence coverage and updated pre-Task-5 realtime fixtures.
   Reason: Prove all approved Task 9 boundaries against real projection, endpoint metadata, coordinator failure controls, and SignalR delivery.
   Business impact: Test-only.
   Performance impact: Test-only.
   Risk: None outside test execution.
   Regression recommendation: Run both recorded commands after future combat projection or notifier changes.
