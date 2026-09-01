# Task 9 final review: per-character dying scheduling and atomic round wrap

## Verdict

**PASS**

No actionable issues remain after the P1 correction.

## Review scope

Read and reviewed:

- `docs/superpowers/specs/2026-08-25-combat-opposed-design.md`
- `docs/superpowers/plans/2026-08-25-combat-opposed-implementation.md`, Task 9
- `docs/superpowers/specs/2026-08-25-health-stabilization-design.md`
- `docs/superpowers/plans/2026-08-25-health-stabilization-implementation.md`
- `.superpowers/sdd/task-9-brief.md`, including the review-correction requirement
- `.superpowers/sdd/task-9-report.md`
- `.superpowers/sdd/task-9-review.md`
- the current cumulative working-tree diff

No production or test files were modified during this re-review. This file is the only review artifact added.

## P1 correction verification

`GameCoordinator.ApplyDamageCore` now handles a trusted fresh-dying transition while combat is active and the participant is active. In the same canonical replacement that applies the HP result, it copies the schedule and unconditionally assigns:

```text
DyingSchedule[characterId] = new DyingScheduleState(combat.Round, null)
```

Because the assignment replaces an existing key, a stale entry left after a prior successful check cannot authorize a check for the new episode in the same round. `ApplyDamageAsync` executes this path under the existing room lock, and duplicate damage exits before schedule mutation when the HP engine reports no change.

The regression `InternalCombat_FreshDyingAfterSameRoundStabilizationResetsExistingScheduleWithoutWrapCheck` covers the required sequence: initial dying observation, prior successful round check, same-round First Aid stabilization, trusted same-round fresh damage, then round wrap. It asserts the fresh schedule is `{ ObservedRound = 3, LastCheckCompletedRound = null }`, no fresh episode check record is added, and the dice count remains `2` (the prior dying check plus First Aid, with no same-round wrap check).

## Task 9 invariant review

- `CombatSession.DyingSchedule` is keyed by `CharacterId` and contains only `ObservedRound` and `LastCheckCompletedRound`; health episode history, ordinal, CON, stabilization, and death remain in `CharacterHealthState`.
- Start and fresh-damage observation do not roll. Round-wrap processing iterates stable `CombatSession.Order` and uses active, unstabilized, non-dead health with `finishedRound > observedRound` and `lastCheckCompletedRound != finishedRound` eligibility.
- Eligible checks use the existing pure Health Stabilization engine, server `IDiceRoller.RollPercentile(0, 0).SelectedRoll`, and source `combat-round-{finishedRound}-{characterId:N}`.
- All eligible characters are resolved in memory before the wrapped session is formed. Counts reset, the round advances, and the next active turn is selected. `ReplaceCombatState` performs one replacement with one revision increment for the completed round-wrap transition; the outer room-locked wrapper publishes one snapshot only after the replacement succeeds. No single-character coordinator method is looped for the checks.
- A successful check retains dying and records the completed round. A failed check changes only that character to dead, removes its schedule, marks only its participant inactive, and leaves the combat session active. Stabilized or otherwise ineligible characters are removed from the schedule.
- Phase 2E health semantics are preserved: `HealthStabilizationResolution.cs` is unchanged, the damage correction only updates combat scheduling metadata, and the health/stabilization regression filter remains green.
- The cumulative diff is limited to the existing server gameplay contracts/coordinator/state and `GameStateTests.cs`. It adds no client, docs, Single Player source, formal artifact, public stabilization/combat route, Combat Damage, weapons, armor, persistence, AI, or other forbidden Phase 2F scope.

## Fresh verification evidence

```text
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~FreshDyingAfterSameRoundStabilizationResetsExistingScheduleWithoutWrapCheck" --nologo -v:normal
1 passed, 0 failed, 0 skipped; 0 warnings, 0 errors

dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests" --nologo -v:minimal
32 passed, 0 failed, 0 skipped

dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests|FullyQualifiedName~HealthStabilizationResolutionTests|FullyQualifiedName~HpDamageResolutionTests" --nologo -v:minimal
43 passed, 0 failed, 0 skipped

git diff --check
passed

Strict UTF-8 decoding
passed for all current modified C# files and `.superpowers/sdd` Markdown artifacts
```

No commit or push was performed.
