# Task 7 review: Commit 2

Baseline: Commit 1 `633e0477efe4903900844250316935548d3c1b9a`. Review was read-only; no commit or push was performed.

## Spec compliance

**Verdict: CHANGES REQUIRED**

### Findings

1. **The required internal-interface seam is not implemented.** `IInternalCombatCoordinator` declares Start, Begin, Resolve, Pass, and End at `IGameCoordinator.cs:22-32`, but `GameCoordinator` declares only `IGameCoordinator` at `GameCoordinator.cs:7`. The new Start/Begin methods exist as internal concrete methods (`GameCoordinator.cs:170-196`), yet no `GameCoordinator` instance can be consumed through the approved internal combat interface. This is an explicit Task 7 requirement, not merely a future-task concern. Reconcile the interface boundary and implement the authorized Task 7 seam before approval; keep Resolve/Pass/End behavior out of scope.

### Verified compliance

- Start validates host authorization, expected revision, active-session duplication, explicit non-empty CharacterIds, duplicate IDs, owner membership, non-dead characters, canonical `CheckValues` keys/range, opponent definitions, and the 16-participant bound (`GameCoordinator.cs:198-298`). It snapshots stats from `CheckValues`, creates stable `character:{guid}` / `opponent:{input-index}` IDs, orders by descending DEX with input-index tie stability, initializes round/turn/counters/history/pending-damage state, observes selected dying characters, and commits revision +1.
- Begin validates active combat, expected revision, no pending exchange, current actor, active distinct opposing participants, attacker authority, defender authority metadata, and non-empty response availability (`GameCoordinator.cs:331-390`). `CreatePendingExchange` normalizes to a distinct read-only list and preserves the exact generated ID, response-count snapshot, and creating revision.
- Start and Begin mutate only through the existing per-room lock and `TryReplace`; publication occurs only after successful replacement (`GameCoordinator.cs:170-196`, `392-406`). No dice, Check, health/HP, action/response count, turn/round, or resolved-history mutation is present in these paths.
- `GameApi.cs` and `GameProjection.cs` are unchanged from Commit 1, and no public combat route or transport DTO was added.

### Approval

**Not approved pending the interface-seam finding above.**

## Task quality

**Verdict: CHANGES REQUIRED**

### Findings

1. **TDD coverage is materially narrower than the approved Task 7 brief.** The added combat tests at `GameStateTests.cs:83-151` verify a valid start, one descending-Dex ordering, explicit subset, duplicate IDs, revision transitions, wrong current actor, pending creation, non-empty responses, no dice, unchanged counters/turn, and second-pending rejection. They do not individually exercise the required stable DEX tie order, dead investigator rejection, missing canonical keys, duplicate-start rejection, invalid/non-active enemy defender, owner-relation rejection, or distinct/read-only `AvailableResponses`. The report's historical RED evidence is credible for the two new coordinator tests, but it is not evidence for the full required RED matrix. Add the missing focused cases and rerun the prescribed RED/GREEN cycle in a disposable/reproducible manner before approval.

### State-propagation assessment

**Safe for this task.** The trailing optional `CombatSession?` state constructor/property at `MultiplayerGameState.cs:50-78` preserves backward-compatible initialization (`Combat == null`). Existing Check, HP damage, and stabilization replacements pass through `state.Combat` (`GameCoordinator.cs:567-574`, `623-630`, `731-738`), while Start/Begin carry forward `LastCheck` and characters. No unrelated route/projection/client scope was introduced.

### Verification

- `dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests" --nologo -v:minimal`: **19 passed, 0 failed, 0 skipped**.
- Combined `GameStateTests|GameApiTests` filter: **27 passed, 0 failed, 0 skipped**.
- `git diff --check`: passed.
- Strict UTF-8 decoding: passed for all five modified C# files and the Task 7 SDD files checked.
- Current diff against Commit 1 contains the expected five dirty C# files from the reviewed Task 6 baseline plus Task 7 changes; no source commit or push was performed.
