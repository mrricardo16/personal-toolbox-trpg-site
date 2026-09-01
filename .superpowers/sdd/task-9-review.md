# Task 9 review: per-character dying scheduling and atomic round wrap

## Verdict

**CHANGES REQUIRED**

One actionable timing defect remains in fresh-dying re-observation. The focused tests pass, but the current fresh-dying test only covers a case where the stale schedule entry has already been removed at an earlier wrap.

## Review scope

Read the approved Phase 2F design, the implementation plan Task 9 section, `task-9-brief.md`, `task-9-report.md`, and the cumulative current working-tree diff for Task 6–8 plus Task 9. Production and test files were not modified. This review artifact is the only new file.

## Verified requirements

- `CombatSession.DyingSchedule` is keyed by `CharacterId` and its value contains only `ObservedRound` and `LastCheckCompletedRound`.
- Already-dying selected investigators are observed at Combat start without a roll; wrap eligibility requires a later completed round and excludes a second check in the same completed round.
- Successful stabilization checks retain dying, update the per-character last-check round, and later wraps can check again.
- Stabilized, dead, and inactive participants are removed from the schedule; a failed check marks only that participant inactive and leaves the combat session active.
- Wrap processing follows `CombatSession.Order`, computes health and participant changes in memory, resets counts, advances round/turn, performs one `TryReplace`/revision increment, and publishes once after the locked operation commits.
- Round-wrap checks use `RollPercentile(0, 0).SelectedRoll` and source `combat-round-{finishedRound}-{characterId:N}`.
- The cumulative diff does not add Combat Damage, exchange HP mutation, weapons, armor, AI gameplay, disconnect/reconnect progression, public combat routes, or Single Player changes.

## Finding

### [P1] Re-observe a fresh dying episode while an old schedule entry still exists

Locations: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs:945-972` and `:681-712`.

`ApplyDamageCore` replaces the character health but preserves `state.Combat` unchanged, so it never records a new `DyingSchedule[characterId]` observation when damage changes a stabilized character into a fresh dying episode. `AdvanceTurn` only creates an observation when the schedule key is absent; if an older entry remains, it uses that entry's old `ObservedRound` and `LastCheckCompletedRound`.

Concrete violating sequence:

1. A character is observed in round 1 and successfully checked at the end of round 2, leaving `{ ObservedRound = 1, LastCheckCompletedRound = 2 }`.
2. During round 3, first aid stabilizes that episode, then trusted damage creates a fresh dying episode before the round wraps. The old schedule entry is still present because schedule cleanup occurs only in `AdvanceTurn`.
3. At the end of round 3, `finishedRound = 3` is greater than the old observed round and differs from the old last-check round, so the code performs a dying check immediately in the fresh episode's observation round.

The approved design requires the damage transition to record `{ ObservedRound = 3, LastCheckCompletedRound = null }`; no check should occur until the end of round 4. The existing test `InternalCombat_RoundWrapRemovesStabilizedScheduleAndObservesFreshDyingEpisode` does not catch this because it first wraps once to remove the old schedule, then applies fresh damage.

Required correction: update the active-combat schedule atomically when a trusted damage transition enters a fresh dying episode, or otherwise prove equivalent current-round re-observation without allowing an existing stale entry to authorize a same-round check. Add a regression test for fresh dying after a prior successful check and stabilization in the same round, asserting zero round-wrap dice and `ObservedRound` equal to the current round.

## Test and verification evidence

Focused tests:

```text
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests" --nologo -v:minimal
31 passed, 0 failed, 0 skipped
```

Combined health/combat tests:

```text
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests|FullyQualifiedName~HealthStabilizationResolutionTests|FullyQualifiedName~HpDamageResolutionTests" --nologo -v:minimal
42 passed, 0 failed, 0 skipped
```

Additional checks:

- `git diff --check`: passed.
- Strict UTF-8 decoding: passed for all current modified C# files and `.superpowers/sdd` Markdown artifacts.
- No commit or push was performed.
