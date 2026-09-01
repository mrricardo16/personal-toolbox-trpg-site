# Task 6 report: canonical combat-state integration

## Scope and constraints

- Starting commit: `633e0477efe4903900844250316935548d3c1b9a`.
- No commit or push was performed.
- No endpoint, projection, client, SignalR, reconnect, HP, damage, exporter, or fixture behavior was changed.
- All edited C# files were validated as strict UTF-8.

## TDD evidence

### RED

Command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests|FullyQualifiedName~GameApiTests" --nologo -v:minimal
```

Result: failed at test compilation, as expected. `GameStateTests` referenced the requested `combat` constructor parameter and `MultiplayerGameState.Combat`; both were missing. The compiler reported CS1739 for the missing named parameter and CS1061 for the missing property.

### GREEN

Command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests|FullyQualifiedName~GameApiTests" --nologo -v:minimal
```

Result: passed — 25 passed, 0 failed, 0 skipped.

## State and contract shape

- `MultiplayerGameState` now has a trailing optional `CombatSession? combat` constructor parameter and a `Combat` property. Initialized games therefore retain the existing null state.
- Existing coordinator state replacements preserve `state.Combat` alongside `LastCheck`, so later state transitions cannot discard an active or ended combat session.
- `CharacterState` remains canonical for player combat values through `CheckValues`; it does not gain duplicate `Dex`, `Fighting`, or `Dodge` properties.
- Internal-only combat commands, `OpponentDefinition`, per-transition result records, and combat-specific `GameErrorCode` values were added. `StateConflict` remains unchanged for expected-revision conflicts.

## Interface boundary

`IInternalCombatCoordinator` declares the future Start/Begin/Resolve/Pass/End transition signatures but `GameCoordinator` does not implement it. This keeps the current coordinator and HTTP route surface unchanged until Tasks 7–8 supply those implementations.

## Self-review

- Reviewed the scoped diff: no route or projection file changed, and no public HTTP DTO was introduced.
- Ran `git diff --check`: no whitespace errors.
- Verified strict UTF-8 decoding for every edited C# file.
- Did not commit or push.

## Concerns

- `GameCoordinator.cs` has three minimal constructor-call edits solely to propagate `state.Combat`; this is required by the brief's state-replacement invariant, but it is not a new combat behavior or interface implementation.
- The focused test filter validates state and existing route behavior. Future combat transition behavior remains intentionally unimplemented for Tasks 7–8.
