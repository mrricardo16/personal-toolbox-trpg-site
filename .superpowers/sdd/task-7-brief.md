# Task 7: Implement trusted Start Combat and BeginOpposedExchange

## Files

- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`

## Context

Commit 1 is pushed at `633e0477efe4903900844250316935548d3c1b9a`. Task 6 state/contracts are reviewed in the working tree. This is Commit 2 Task 7; do not commit or push. Do not implement Resolve, Pass, End, round-wrap health linkage, projection, realtime, reconnect, or Vue yet.

## TDD first

Add failing GameStateTests for valid internal start, descending DEX and stable input-order ties, explicit CharacterId participant subset, duplicate IDs, dead investigator rejection, missing canonical keys, duplicate start, current-actor enforcement, active enemy/defender validation, stable server ExchangeId, pending creation, Begin revision increment, no dice, no action/response increment, no turn advance, second pending rejection, and non-empty `AvailableResponses`. Use a fake `IDiceRoller` that throws if called by Begin. Run the GameState filter and record the expected RED failure before implementation:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests" --nologo -v:minimal
```

## Canonical behavior

Implement the internal `IInternalCombatCoordinator` methods for Start and Begin under the existing per-room serialization and expected-revision/state-store conventions. Add `IInternalCombatCoordinator` to `GameCoordinator` only as part of this implementation. Keep public `IGameCoordinator` and `GameApi.MapGameEndpoints` route surface unchanged.

Start must validate trusted authorization, room/game existence, expected revision, no active CombatSession, explicit `CharacterIds`, owner relation, no duplicates, active/non-dead investigators, required canonical `CharacterState.CheckValues` keys `dex`, `fighting_brawl`, and `dodge` in reference range, at least one valid opponent, opponent stats/response definitions, and the adopted participant limit. Do not auto-include all room characters. Snapshot investigator stats from `CheckValues`; do not create a second stat source. Create stable participant IDs and order by descending DEX with stable input-order ties, set round 1/turn 0, initialize action/response counts, initialize the independent empty `PendingDamageDispositions` registry, and observe already-dying investigators without rolling. Replace state with revision +1, commit, project through the existing path, and publish only after commit.

Begin must validate active session, current actor, active enemy defender, no existing pending exchange, expected revision, and participant identities. Generate one server `ExchangeId`, copy defender owner/authority and the canonical non-empty `AvailableResponses` into a `PendingCombatExchange`, capture `ResponseCountBefore` and `CreatedRevision`, write only pending state, increment revision exactly once, commit, and publish after commit. Do not call dice, Check, Health, HP, action/response counters, turn/round progression, or resolved-history append. A rejected Begin has no revision/state mutation. Ensure an ended session cannot receive a pending exchange.

## Boundaries

- No Resolve/Pending resolution, Pass, End cancellation, dying checks, Combat Damage, HP, weapons, armor, routes, HTTP DTOs, projection changes, SignalR event changes, reconnect behavior, client/Vue changes, or Single Player changes.
- `CharacterId` is gameplay identity; `PlayerId` is controller authority only. The attacker cannot choose a player-owned defender response in this task.
- `AvailableResponses` must be read-only/distinct/non-empty at pending creation. Preserve the independent registry shape from Commit 1.
- Preserve UTF-8, existing state replacement behavior, and all earlier changes.

## Validation and report

Run the focused GameState filter after implementation and record the green count. Run `git diff --check` and strict UTF-8 checks for edited files. Write the full report to `.superpowers/sdd/task-7-report.md` with RED/GREEN commands, authorization/revision evidence, changed files, self-review, and concerns. Return only status, changed files, test summary, and concerns. Do not commit or push.

## Review correction before re-review

The first implementation review requires one focused correction pass before Task 7 can be approved. Read `.superpowers/sdd/task-7-review.md` and preserve the existing implementation while addressing every finding below.

Add tests first for each previously missing case: stable equal-DEX input-order preservation, dead investigator rejection, missing canonical `CheckValues` key rejection, duplicate-start rejection, invalid or non-active enemy defender rejection, owner-relation rejection, and a distinct/read-only `AvailableResponses` assertion. Also add a seam test proving the currently authorized internal Start/Begin surface is consumed by `GameCoordinator` rather than merely declared.

Split the internal interface so the currently implemented surface is truthful and does not require placeholders: `IInternalCombatCoordinator` must contain only Start and Begin; add an internal derived/future interface (for example `IInternalCombatResolutionCoordinator : IInternalCombatCoordinator`) containing Resolve, Pass, and End for later tasks. Make `GameCoordinator` implement `IGameCoordinator, IInternalCombatCoordinator`. Do not add placeholder Resolve/Pass/End methods and do not implement any later-task behavior.

Run the focused GameState filter in test-first order for the new assertions, record the expected RED before the interface/behavior correction, then run it green. Append the correction evidence and self-review to the existing Task 7 report. Do not commit or push.
