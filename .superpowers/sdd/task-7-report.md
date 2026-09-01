# Task 7 report: trusted combat Start and Begin

## Scope and constraints

- Started from the reviewed dirty Task 6 working tree; no existing edits were reverted.
- No commit or push was performed.
- Only `GameCoordinator.cs` and `GameStateTests.cs` were modified for Task 7 source/test behavior. This report is the required task artifact.
- No route, HTTP DTO, projection, SignalR/realtime contract, reconnect, client, Single Player, Resolve, Pass, End, health, HP, damage, dice, or turn-advance behavior was added.

## TDD evidence

### RED

Required command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests" --nologo -v:minimal
```

The first red run failed compilation because the Task 6 combat contracts are internal and the tests initially attempted direct access. No production behavior was introduced at that point. The tests were then arranged entirely within `GameStateTests.cs` to reflect over the existing internal seam, avoiding an `InternalsVisibleTo` or project-file change.

The subsequent red run used the same command and compiled the tests, then failed both new combat tests with `System.Reflection.TargetException: Object does not match target type.` This was the expected failure before implementation: `GameCoordinator` did not yet supply the Start/Begin internal methods.

### GREEN

Command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests" --nologo -v:minimal
```

Result: passed -- 19 passed, 0 failed, 0 skipped.

## Implemented behavior

- Start takes an explicit investigator `CharacterId` subset only, requires the trusted host authorization, validates room/game/revision, rejects an active session and duplicate IDs, validates owner membership, rejects dead characters, and reads `dex`, `fighting_brawl`, and `dodge` exclusively from canonical `CheckValues` in the 1..100 range.
- Start validates non-empty opponent definitions, bounded combat participants (16), stable `character:{id}` / `opponent:{input-index}` IDs, descending DEX ordering with input-order ties, round 1 / turn 0, empty action/response registries, independent empty pending-damage registry, and observed already-dying selected investigators. No dice are rolled.
- Begin validates member authority, an active combat and matching revision, no pending exchange, the current active attacker, a distinct active opposing defender, defender response availability, and attacker controller authority. It generates a server exchange ID, snapshots defender authority and distinct read-only responses, records the pre-response count and created revision, updates only pending state, increments revision once, commits via `TryReplace`, then publishes only after commit. No dice, check, health, counters, turn/round, or history state changes occur.

## Authorization and revision evidence

- Tests prove duplicate participant rejection without revision mutation, wrong-current-actor rejection, a successful Start from revision 1 to 2, a successful Begin from revision 2 to 3, and second-pending rejection preserving revision 3.
- The Begin test uses a throwing `IDiceRoller`; assertions confirm zero calls during Start and Begin.

## Changed files

- `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs`
- `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`
- `.superpowers/sdd/task-7-report.md`

## Self-review and concerns

- The established room-lock / `TryReplace` state-store pattern is used; realtime publication is after successful replacement only.
- The test uses reflection solely because Task 6 correctly made the combat contract internal while the brief limits Task 7 source/test edits to two files; no public surface or assembly visibility was added.
- `IInternalCombatCoordinator` still declares future Resolve/Pass/End members. Making `GameCoordinator` implement that full interface now would require prohibited placeholder implementations. Therefore Start/Begin are internal `GameCoordinator` methods with matching contracts, while the existing interface declaration remains untouched. This is the only scope/interface concern for the next combat task; escalation is not required to validate Task 7 behavior, but the next task should reconcile the interface once its remaining methods are authorized.

## Review correction pass

### Correction RED

After adding the required focused regression cases, the prescribed command was run before correcting the interface boundary:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests" --nologo -v:minimal
```

Result: 23 passed, 1 failed, 0 skipped. The sole expected failure was `InternalCombatInterface_IsConsumedByGameCoordinatorAndContainsOnlyAuthorizedSurface`: `IInternalCombatCoordinator.IsAssignableFrom(typeof(GameCoordinator))` was false. This proves the pre-correction seam was absent while the existing behavior already met the added validation assertions.

### Correction GREEN

The same focused command passed after the correction: 24 passed, 0 failed, 0 skipped.

- Added regression coverage for equal-DEX input-order stability; dead investigators; missing canonical `CheckValues` keys; owner relation; duplicate start; invalid and inactive enemy defenders; and distinct, read-only pending responses.
- Split `IInternalCombatCoordinator` to the currently authorized `StartCombatAsync` and `BeginOpposedExchangeAsync` surface only.
- Added `IInternalCombatResolutionCoordinator : IInternalCombatCoordinator` for the future Resolve/Pass/End contracts without implementing or exposing any later-task behavior.
- `GameCoordinator` now implements `IGameCoordinator, IInternalCombatCoordinator` through explicit adapters to the existing internal Start/Begin methods. The reflection seam test verifies both assignability and the exact two-method authorized surface.

### Correction validation and self-review

- `git diff --check` passed.
- Strict UTF-8 decoding passed for `GameCoordinator.cs`, `IGameCoordinator.cs`, `GameStateTests.cs`, and this report.
- The correction preserves the existing room lock, expected-revision checks, `TryReplace` state replacement, and post-commit realtime publication. It does not change public `IGameCoordinator`, route mapping, DTOs, projection, realtime contracts, reconnect, client code, or Resolve/Pass/End behavior.
- Concern: the future resolution interface intentionally declares contracts whose behavior remains unauthorized. It is not implemented by `GameCoordinator`; a later authorized combat-resolution task must supply that implementation and its tests. No escalation is required for this Task 7 correction.
