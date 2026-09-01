# Task 6: Add CombatSession to canonical state and safe contracts

## Files

- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/MultiplayerGameState.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/IGameCoordinator.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`

## Context

Commit 1 is pushed and synchronized at `633e0477efe4903900844250316935548d3c1b9a`. Task 6 begins the single unpushed Commit 2. Preserve that commit and all approved invariants. Do not commit or push this task.

## TDD first

Extend `GameStateTests.cs` with failing assertions that initialized games have `Combat == null`, state replacement can carry one `CombatSession`, and `CharacterState` has no duplicate Dex/Fighting/Dodge properties. Run the focused filter and record the expected RED failure before implementation:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests|FullyQualifiedName~GameApiTests" --nologo -v:minimal
```

## State/contracts

Add `CombatSession? Combat` at the end of `MultiplayerGameState`'s constructor and propagate it through every existing state replacement path. Do not duplicate combat stats beside canonical `CharacterState.CheckValues` (`dex`, `fighting_brawl`, `dodge`). Add internal command/result records with these exact command fields:

```csharp
StartCombatCommand(Guid RoomId, Guid AuthorizedPlayerId, long ExpectedGameRevision, IReadOnlyList<Guid> CharacterIds, IReadOnlyList<OpponentDefinition> Opponents)
BeginOpposedExchangeCommand(Guid RoomId, Guid RequestingPlayerId, long ExpectedGameRevision, string AttackerParticipantId, string DefenderParticipantId)
ResolvePendingExchangeCommand(Guid RoomId, Guid? RequestingPlayerId, long ExpectedGameRevision, string ExchangeId, CombatResponse Response)
PassCombatTurnCommand(Guid RoomId, Guid RequestingPlayerId, long ExpectedGameRevision)
EndCombatCommand(Guid RoomId, Guid AuthorizedPlayerId, long ExpectedGameRevision, string Reason)
```

`OpponentDefinition` contains only label, DEX, Fighting, Dodge, `AvailableResponses`, and response allowance/policy. Add internal result records for future transitions without using them as HTTP DTOs. Add explicit invalid-combat, invalid-participant, invalid-response, pending-conflict, ended-combat, and invalid-exchange error codes; preserve `StateConflict` for expected-revision failures.

## Coordinator interface boundary

The current `GameCoordinator` directly implements `IGameCoordinator`. Adding unimplemented members to that interface would break the clean build before Tasks 7–8 can implement them. To preserve the task boundary, add an internal-only `IInternalCombatCoordinator` declaration in `IGameCoordinator.cs` containing the future Start/Begin/Resolve/Pass/End method signatures, but do not yet make `GameCoordinator` implement it. Tasks 7–8 will implement the methods and add the interface to the class. This is an internal application seam, not a public HTTP API; keep `GameApi.MapGameEndpoints` untouched.

## Boundaries

- No `GameCoordinator` behavior, public route, `GameProjection` DTO, Vue/client, SignalR, reconnect, HP, Combat Damage, exporter, or fixture change in this task.
- No public Attack/Dodge/Fight Back/Pass/Start/End Combat endpoint or player intent transport.
- Preserve UTF-8 and existing Chinese text. Do not modify Single Player sources/artifacts.

## Validation

After implementation rerun the focused GameState/API test filter. Route-surface tests must continue to pass and initialized state must preserve existing behavior. Record red/green output and changed files.

## Report

Write the full report to `.superpowers/sdd/task-6-report.md`, including RED/GREEN commands, state/contract shape, interface-boundary rationale, self-review, and concerns. Return only status, changed files, test summary, and concerns. Do not commit or push.
