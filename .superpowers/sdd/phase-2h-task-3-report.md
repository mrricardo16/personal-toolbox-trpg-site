# Phase 2H Task 3 Report

## Scope

Defined the internal player combat-intent contract and dependency-injection boundary only. No player attack, response, or pass orchestration, routes, client behavior, projection behavior, publication, persistence, AI integration, or realtime behavior was added.

## RED

Command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter FullyQualifiedName~PlayerCombatIntentCoordinatorTests --nologo -v:minimal
```

Result: failed as expected, with 0 passed and 3 failed. The failures reported that `PlayerCombatIntentCoordinator` and `IPlayerCombatIntentCoordinator` were not found.

## GREEN

Command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~PlayerCombatIntentCoordinatorTests.PlayerCombatIntentCoordinator_HasOnlyApprovedStateAndTransitionDependencies|FullyQualifiedName~PlayerCombatIntentCoordinatorTests.PlayerCombatIntentCoordinator_ExposesExactlyThreePlayerIntents|FullyQualifiedName~PlayerCombatIntentCoordinatorTests.PlayerCombatIntentCoordinator_ResolvesFromDependencyInjection" --nologo -v:minimal
```

Result: passed, with 3 passed, 0 failed, and 0 skipped. The test confirms the coordinator has only `IGameCoordinator` and `IInternalCombatResolutionCoordinator` constructor dependencies, the interface exposes exactly the three required intent methods, and DI resolves one coordinator while both existing coordinator interfaces resolve to the same `GameCoordinator` instance.

## Boundary verification

The coordinator source test rejects direct references to canonical state storage, dice, damage engines, realtime notifier/hub, persistence, and AI type names. The coordinator remains stateless apart from its two injected application dependencies and does not acquire room locks or publish state.

## Review follow-up: category boundary regression

The source-boundary test now rejects type-name categories rather than only a fixed list: any `*Store`, `*Dice*`, `*Engine`, `*Notifier`, Hub/SignalR, persistence (`*Repository`, `*Persistence`, `*DbContext`, `*Database`, or `*EntityFramework`), and AI (`Ai*` or `AI*`) references. The exact two-constructor-parameter assertion remains unchanged.

Because the clean coordinator source had no missing feature to make RED, a controlled regression-on-add was used. After the strengthened test was written, a temporary non-behavioral `IRoomStore` comment was added to the production source and the focused boundary test failed with `Coordinator source must not reference the forbidden store dependency category.` The probe was removed immediately before GREEN.

Focused GREEN command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~PlayerCombatIntentCoordinatorTests.PlayerCombatIntentCoordinator_HasOnlyApprovedStateAndTransitionDependencies|FullyQualifiedName~PlayerCombatIntentCoordinatorTests.PlayerCombatIntentCoordinator_ExposesExactlyThreePlayerIntents|FullyQualifiedName~PlayerCombatIntentCoordinatorTests.PlayerCombatIntentCoordinator_ResolvesFromDependencyInjection" --nologo -v:minimal
```

Result: passed, with 3 passed, 0 failed, and 0 skipped.
