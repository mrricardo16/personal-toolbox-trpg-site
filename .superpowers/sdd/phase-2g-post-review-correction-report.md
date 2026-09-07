# Phase 2G Post-Implementation Correction Report

## Scope

- Correction is limited to `GameProjection`: `LastExchange.DispositionPending` is derived from the canonical `CombatSession.DamageDispositions` registry entry for `LastExchange.ExchangeId`.
- `LastExchange` and bounded `History` remain historical records and are not rewritten during damage consumption.
- `GameCoordinator`, combat math, HP integration, dice, causal gate, public API, client action UI, Single Player, and formal HTML were not changed.

## TDD evidence

- RED command:
  `dotnet test multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Trpg.Multiplayer.Api.Tests.csproj --no-restore --nologo -v:minimal --filter "FullyQualifiedName~Projection_ResolveCombatDamage_ConsumedReplayWithOriginalStaleRevisionReturnsStoredResultWithoutSecondNotification|FullyQualifiedName~ResolveCombatDamage_TargetAlreadyIneligibleConsumesOnceWithoutDiceHpVitalityOrTurnRepair|FullyQualifiedName~InternalCombatDamage_CommitsBeforeOneViewerSnapshot_ReplayAndCrossRoomStaySilent_AndReconnectRecovers"`
- RED result: 3 failed, 0 passed. Both gameplay assertions observed `DispositionPending == true` after a consumed registry entry. The realtime assertion could not inspect `LastExchange` because its seed had no historical completed exchange; the seed now represents the ordinary historical pending exchange and the same projection defect was then exercised.
- GREEN command: the exact same focused command.
- GREEN result: 3 passed, 0 failed.

## Regression coverage

- Pre-consumption projection: `DispositionPending == true`; `LastDamage == null`.
- Successful consumption: canonical entry is `Consumed`; projection reports `DispositionPending == false`; safe `LastDamage.ExchangeId` matches the completed exchange.
- Stale consumed replay remains `DispositionPending == false` with the same safe last-damage summary.
- `TargetAlreadyIneligible` consumed result projects `DispositionPending == false` and `target_already_ineligible`.
- Realtime `GameSnapshot` after consumption exposes `DispositionPending == false` and the matching safe `LastDamage.ExchangeId`.
- Tests explicitly retain the historical `LastExchange` reference and bounded `History` through consumption.

## Required validation

- `dotnet build multiplayer/server/Trpg.Multiplayer.slnx --no-restore --nologo -v:minimal`: passed; 0 warnings, 0 errors.
- `dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --nologo -v:minimal`: passed; 329 passed, 0 failed, 0 skipped.
- `dotnet format multiplayer/server/Trpg.Multiplayer.slnx --verify-no-changes --no-restore`: passed.
- Client test fixture review: the existing consumed snapshot already has `dispositionPending: false`; no client file change or client build/test was required.

## Scope verification

- Single Player changed: no.
- Formal HTML changed: no.
- Combat damage source fixture changed: no.
- GameCoordinator changed: no.
- Commit, push, and remote changes: none (intentionally excluded from this correction task).
