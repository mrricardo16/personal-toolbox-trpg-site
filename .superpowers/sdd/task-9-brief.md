# Commit 2 Task 9 Brief — Per-character dying scheduling and atomic round wrap

## Objective

Implement the approved per-character dying timing and round-wrap linkage for Multiplayer Combat. Continue the same second feature commit; do not commit or push. Preserve all reviewed Task 6–8 changes and do not alter Phase 2E behavior.

## Files

- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/HealthStabilizationResolution.cs` only if a pure helper is strictly needed; preserve Phase 2E behavior
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`

Read the approved design/spec, implementation plan Task 9, and Task 6–8 reports/reviews before editing. Use TDD: add timing tests, run the focused filter and record RED, then implement the smallest canonical linkage and run GREEN. Keep all edited files UTF-8.

## Required behavior and tests

Cover already-dying observation at Start, no same-round check, first following eligible completed round, success retention, once-per-round checks, later checks, stabilization removal, fresh-dying new observation, independent A/B observed rounds, deterministic combat-order processing, one revision for multiple checks, one participant death without full combat end, and no automatic full-combat end. Include no-op/rejection cases for inactive/dead/stabilized/non-eligible participants and verify no HP or health semantics regressions outside existing stabilization.

## Canonical rules

- Store only per-character `DyingSchedule[CharacterId] = { ObservedRound, LastCheckCompletedRound }`; do not reintroduce a single-player timing field.
- At round wrap, use stable `CombatSession.Order`, active/unstabilized/non-dead health, `finishedRound > observedRound`, and `lastCheckCompletedRound != finishedRound` to select eligible investigators.
- For each eligible investigator in deterministic combat order, invoke the existing pure Health Stabilization behavior with `diceRoller.RollPercentile(0, 0).SelectedRoll` and source `combat-round-{finishedRound}-{characterId:N}`. Do not loop through single-character coordinator methods.
- Apply all health and participant updates in memory, reset counts, advance round/turn, replace state once, increment revision once, commit once, and publish one snapshot only after commit. Multiple eligible characters in one round wrap are one room-lock/one replacement/one revision/one commit/one broadcast transaction.
- A single investigator’s death does not automatically end Multiplayer Combat. Disconnect/reconnect/refresh/chat/SignalR remain non-progressing. Combat Opposed still does not implement Combat Damage, weapons, armor, HP mutation from exchanges, or defeat logic; use only the existing stabilization linkage as already approved.

## Validation and report

Run RED and GREEN with:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests" --nologo -v:minimal
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests|FullyQualifiedName~HealthStabilizationResolutionTests|FullyQualifiedName~HpDamageResolutionTests" --nologo -v:minimal
```

Record exact counts and any infrastructure limitation. Run `git diff --check` and strict UTF-8 checks for every edited file. Append `.superpowers/sdd/task-9-report.md` with RED/GREEN evidence, transaction/revision/broadcast evidence, changed files, self-review, and concerns. Do not commit or push. Return status, files, tests, and concerns.

## Review correction required before re-review

The first review found a P1 fresh-dying timing defect: when a character has an old schedule entry after a prior successful check, is stabilized, and then receives trusted damage in the same round, `ApplyDamageCore` preserves the old schedule and the next wrap can check the fresh episode immediately. Add a regression test first for this exact sequence and record RED. Then update the active-combat damage transition atomically so a fresh dying episode records `DyingSchedule[characterId] = new DyingScheduleState(currentRound, null)` even when an old entry exists. Do not let the old schedule authorize a same-round check. Preserve all other Phase 2E and Task 9 semantics, run the focused and combined GREEN filters, diff/UTF-8 checks, and append the correction evidence to the report. Do not commit or push.
