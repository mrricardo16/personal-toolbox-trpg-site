# Phase 2H Task 8 Report

## Status and scope

Task 8 adds the 13 required regression tests for committed realtime ordering, viewer privacy, partial-commit recovery, non-retryable post-RNG failure, lost-success retry, and revision-neutral disconnect/reconnect.

Changed test files:

- `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/SignalRGameDeliveryTests.cs`
- `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/DisconnectReconnectTests.cs`
- `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/PlayerCombatIntentCoordinatorTests.cs`

No production file changed. In particular, `PlayerCombatIntentCoordinator.cs` already retained committed Begin/Resolve state and mapped transition failures using the returned committed revision, so no approved-seam correction was defensible.

No rollback, aggregation, worker, `IntentId`, application notifier call, disconnect mutation, whole-intent replay, or distributed retry was added.

## UTF-8 precheck

Before editing, strict throwing UTF-8 decoding passed for all four existing authorized files:

- `SignalRGameDeliveryTests.cs`
- `DisconnectReconnectTests.cs`
- `PlayerCombatIntentCoordinatorTests.cs`
- `PlayerCombatIntentCoordinator.cs`

The report was created as UTF-8 text.

## RED evidence

Exact required command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~SignalRGameDeliveryTests.PlayerCombatIntent|FullyQualifiedName~DisconnectReconnectTests.PendingHumanResponse|FullyQualifiedName~DisconnectReconnectTests.Disconnect_|FullyQualifiedName~DisconnectReconnectTests.Reconnect_|FullyQualifiedName~PlayerCombatIntentCoordinatorTests.Partial|FullyQualifiedName~PlayerCombatIntentCoordinatorTests.PostRng|FullyQualifiedName~PlayerCombatIntentCoordinatorTests.LostSuccess" --nologo -v:minimal
```

Initial result: build failed before test execution with four compiler errors in the newly added test fixture:

- two `CS8858` errors because `MultiplayerGameState` is an immutable class, not a record;
- two `CS0200` errors because `MultiplayerGameState.Combat` is read-only.

The fixture was corrected to construct a replacement `MultiplayerGameState` explicitly. No production code was changed.

After this test-only correction, the same exact selection passed `11` tests, with `0` failed and `0` skipped. This is important evidence rather than a manufactured RED: Tasks 1-7 had already connected the public orchestration and implemented the approved partial-state behavior. The brief's RED filter also does not select the three required methods whose names begin `PlayerMeleeAttack_`; those are included by the exact GREEN selection below.

## GREEN evidence

Exact required command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~SignalRGameDeliveryTests|FullyQualifiedName~DisconnectReconnectTests|FullyQualifiedName~PlayerCombatIntentCoordinatorTests.Partial|FullyQualifiedName~PlayerCombatIntentCoordinatorTests.PostRng|FullyQualifiedName~PlayerCombatIntentCoordinatorTests.LostSuccess" --nologo -v:minimal
```

First GREEN attempt ran `27` tests and exposed three test-fixture/assertion defects:

1. The human-defender fixture inherited an NPC definition that advertised only Dodge, while the assertion expected Dodge and Fight Back.
2. Two client callback assertions compared an intermediate payload revision with canonical state after a later transition had already committed. This measured client scheduling, not commit-before-publication ordering.

Corrections were test-only:

- the opponent fixture now snapshots both approved responses;
- an `IGameRealtimeNotifier` test decorator reads and records canonical revision immediately before forwarding each real SignalR publication. Hub clients still collect the delivered viewer-specific snapshots. This proves the server commit/publication boundary without assuming that a client callback runs before the next canonical transition.

Second GREEN result: `27` passed, `0` failed, `0` skipped.

## Verified behavior

- NPC damage publishes exactly Begin, Resolve, and Damage revisions `3, 4, 5`, each recorded canonical before publication, with no aggregation or duplicate application publication.
- NPC no-damage publishes exactly Begin and Resolve revisions `3, 4`.
- A human defender publishes Begin only. The actor has no exact pending response, the defender receives the exact exchange and approved response choices, and a nonparticipant receives `Combat = null`.
- Nonparticipants receive `Combat = null` at every published revision.
- Realtime JSON omits NPC policy, allowance, attacker/defender checks and raw rolls, damage registry, dying schedule, history, source, and provenance.
- A Resolve pre-RNG failure retains the committed Begin revision and pending exchange, invokes Begin once, and never retries the whole intent.
- A Damage pre-RNG failure retains the committed Resolve revision and exact pending disposition, invokes Begin and Resolve once each, and never retries them.
- A post-RNG damage commit invariant exception propagates as non-result failure, is not mapped to stale/retryable conflict, and performs no reroll.
- A lost-success retry using the old revision is rejected as stale without duplicate Begin, Resolve, Damage, exchange generation, dice, or publication.
- Disconnect changes room presence only; canonical Game revision, round, turn, pending exchange, dispositions, dying schedule, action counts, and response counts remain unchanged.
- Reconnect returns the same canonical Game revision and restores only the defender owner's exact pending-response affordance.

## Production impact and remaining risk

Production behavior, database, Redis, MQ, PLC, third-party interfaces, and client code are unchanged. Test execution uses in-memory stores and TestServer SignalR long polling.

The required focused gate does not constitute a full server suite. Task 10 remains responsible for full restore/build/test/format and aggregate product validation. No Sol High escalation is required because no security implementation, production data, migration, concurrency correction, destructive change, or architecture expansion was needed.

## Review-fix verification

The four requested review corrections were rechecked in the shared worktree. The first fresh run of the exact RED command executed `11` tests and found one deterministic test-fixture defect: `PlayerCombatIntent_ApplicationCoordinatorAddsNoDuplicatePublish` expected attack revisions `3, 4, 5`, but the singleton notifier tracker also contained legitimate setup publications `1, 2`. The test now captures the tracker count after setup and evaluates only publications emitted by the attack under test. This keeps the assertion deterministic and does not use a timing delay.

After that test-only correction:

- Exact RED command: `11` passed, `0` failed, `0` skipped.
- Exact GREEN command: `27` passed, `0` failed, `0` skipped.
- Reviewer-focused selection covering owner-only Stats JSON shape, real three-viewer AttachSession affordances, both real TestServer partial-failure reconnect cases, and deterministic notifier counts: `6` passed, `0` failed, `0` skipped.

Reviewer-focused command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~SignalRGameDeliveryTests.PlayerMeleeAttack_HumanDefender_PublishesBeginOnlyWithViewerSpecificAffordances|FullyQualifiedName~SignalRGameDeliveryTests.PlayerCombatIntent_RealtimeJsonNeverContainsPolicyAllowanceRollsStatsRegistryScheduleHistorySourceOrProvenance|FullyQualifiedName~SignalRGameDeliveryTests.PlayerCombatIntent_ApplicationCoordinatorAddsNoDuplicatePublish|FullyQualifiedName~DisconnectReconnectTests.PendingHumanResponse_DisconnectReconnectPreservesExactStateAndRestoresOwnerAffordance|FullyQualifiedName~PlayerCombatIntentCoordinatorTests.PartialBeginThenResolvePreRngFailure_ReconnectSeesCommittedPendingWithoutWholeIntentRetry|FullyQualifiedName~PlayerCombatIntentCoordinatorTests.PartialResolveThenDamagePreRngFailure_ReconnectSeesResolvedPendingDispositionWithoutWholeIntentRetry" --nologo -v:minimal
```

No production file was edited during the review-fix. No files were staged, committed, pushed, reset, reverted, or stashed.
