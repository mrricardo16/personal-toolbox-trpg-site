# Phase 2H Task 4 Report

## Scope

Implemented only player melee-attack application orchestration in `PlayerCombatIntentCoordinator`. The change uses viewer projection before mutation, canonical Begin as final authority, server-only NPC response policy from the Begin-returned state, exact post-Resolve damage disposition/revision, and a fresh final viewer projection. Human defenders stop after Begin. Respond and Pass remain unimplemented Task 5/6 stubs.

No route, client, realtime, public damage, publication, room-lock, persistence, Single Player, or release artifact change was made.

## UTF-8 precheck

Before mutation, strict throwing UTF-8 decoding succeeded for both Task 4 files:

- `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/PlayerCombatIntentCoordinator.cs` (`2631` bytes)
- `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/PlayerCombatIntentCoordinatorTests.cs` (`4110` bytes)

The report did not exist before Task 4.

## Root cause and execution path

Task 3 had defined and registered the narrow coordinator, but `MeleeAttackAsync` still returned `InvalidIntent` unconditionally. Inspection confirmed that no broader dependency is needed: `BeginOpposedExchangeResult.State` contains the exact pending exchange, defender, ownership, snapshotted response list, and private typed `NpcResponsePolicy`; `ResolvePendingExchangeResult.State` contains the exact damage-disposition registry and latest committed revision.

Implemented path:

```text
GetProjection(viewer)
-> revision / ActorNotOwned / NotCurrentActor / projected-target prevalidation
-> canonical Begin
-> Begin returned State only
-> human defender: stop
-> NPC defender: private typed policy membership check
-> canonical Resolve with RequestingPlayerId = null
-> Resolve returned State exact DamageDispositions[ExchangeId]
-> optional canonical Damage using post-Resolve revision
-> fresh GetProjection(viewer)
```

There is no direct store read, outcome/`LastExchange` inference, retry, reroll, rollback, or application publication.

## RED

Command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~PlayerCombatIntentCoordinatorTests.MeleeAttack" --nologo -v:minimal
```

Result: failed as expected, with `0` passed, `12` failed, and `0` skipped. The success-path tests received no snapshot/transitions, while rejection tests received the existing unconditional `InvalidIntent` instead of `StaleGameRevision`, `ActorNotOwned`, `NotCurrentActor`, `TargetNotEligible`, or `CombatConsistencyFailure`. This was the exact missing Task 4 behavior, not a compilation or fixture error.

## GREEN

Required Task 4 plus canonical mutation command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~PlayerCombatIntentCoordinatorTests.MeleeAttack|FullyQualifiedName~GameStateTests.InternalCombat_Begin|FullyQualifiedName~GameStateTests.ResolveCombatDamage" --nologo -v:minimal
```

Result: passed, with `29` passed, `0` failed, and `0` skipped.

Additional complete coordinator-test command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~PlayerCombatIntentCoordinatorTests" --nologo -v:minimal
```

Result: passed, with `15` passed, `0` failed, and `0` skipped. This includes the three preserved Task 3 dependency/DI/interface tests and all twelve Task 4 melee tests.

## Covered behavior

- exact `12 -> 13 -> 14 -> 15` preprojection/Begin/Resolve/Damage/final revision chain;
- exact attacker and target IDs and `RequestingPlayerId = null` for NPC Resolve;
- human defender Begin-only boundary;
- distinct `ActorNotOwned`, `NotCurrentActor`, and `TargetNotEligible` pre-mutation failures;
- stale/rejected requests with zero Begin, Resolve, Damage, generated exchange ID, dice, or publication calls;
- host cannot act as an unowned NPC;
- NPC policy outside the exact pending response snapshot fails closed before Resolve dice;
- Begin returned state supplies exchange, policy, and Begin revision without intermediate projection/store read;
- Resolve returned state supplies the exact disposition and latest revision for Damage, even with an unrelated pending disposition present;
- no-hit state does not call Damage;
- final success is the exact fresh viewer projection object.

## Boundary and risk

The coordinator still has exactly two dependencies: `IGameCoordinator` and `IInternalCombatResolutionCoordinator`. Respond/Pass, routes, client, realtime/reconnect, and public delivery remain for later approved tasks. No Sol High escalation was required: the task did not involve production writes, schema/data migration, credentials/privacy changes, or new concurrency/locking behavior.
