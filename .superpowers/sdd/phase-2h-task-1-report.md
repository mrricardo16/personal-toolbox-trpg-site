# Phase 2H Task 1 Evidence

## Scope

- Implemented the typed, canonical NPC response-policy snapshot only.
- No routes, public input or projection DTOs, client, realtime behavior, persistence, or later-phase work changed.

## UTF-8 precondition

Before editing, strict UTF-8 decoding succeeded for every existing authorized Task 1 text file:

- `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs`
- `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CombatSessionState.cs`
- `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs`
- `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`
- `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/SignalRGameDeliveryTests.cs`
- `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/DisconnectReconnectTests.cs`

## RED

Command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.InternalCombat_StartSnapshotsTypedPrivateNpcPolicy|FullyQualifiedName~GameStateTests.InternalCombat_StartRejectsNpcPolicyOutsideSnapshottedResponses" --nologo -v:minimal
```

Result: expected compilation failure, exit code `1`. Test discovery/execution did not begin, so there was no failed-test count. The compiler reported two `CS1061` errors in `GameStateTests.cs` at lines 160 and 161: `CombatParticipantState` did not define `NpcResponsePolicy`.

## Implementation

- Replaced free-form `OpponentDefinition.ResponsePolicy` with typed `CombatResponse NpcResponsePolicy`.
- Added nullable `CombatResponse? NpcResponsePolicy` to canonical `CombatParticipantState` and its factory.
- Stored `null` for investigators and the typed opponent policy for NPC opponents.
- Rejected an NPC policy that is undefined or absent from that opponent's snapshotted `AvailableResponses` with `InvalidCombat` before participant creation.
- Updated internal reflection-based test factories to pass `CombatResponse` values.
- Added canonical snapshot, invalid-membership/no-revision, and projection-privacy tests.

## GREEN

Command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.InternalCombat_Start|FullyQualifiedName~GameStateTests.Projection|FullyQualifiedName~SignalRGameDeliveryTests|FullyQualifiedName~DisconnectReconnectTests" --nologo -v:minimal
```

Result: exit code `0`; `46` passed, `0` failed, `0` skipped.

## Final validation

- Strict UTF-8 decoding passed for all seven edited files, including both evidence reports.
- `git diff --check` completed successfully (exit code `0`). Git emitted only existing line-ending normalization warnings; it reported no whitespace errors.
