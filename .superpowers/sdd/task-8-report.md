# Task 8 report: trusted resolve, pass, and end transitions

## Scope

- Modified only `GameCoordinator.cs`, `GameStateTests.cs`, and this report.
- Preserved the dirty Task 6/7 source changes; no commit, push, reset, stash, checkout, public route, projection, client, HP, damage, persistence, AI, or timeout work was performed.
- All three scoped text files decode as strict UTF-8.

## TDD evidence and test infrastructure limitation

The required command was attempted first:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests" --nologo -v:minimal
```

In this isolated worktree it exited successfully without discovering or running a test project (the output was empty; `--list-tests` only validated solution configuration). This is not GREEN evidence.

Focused Task 8 tests were then added before production code. During the subsequently authorized one-time restore/test attempt, the test-project command produced the expected RED: 4 new Task 8 tests failed because `ResolvePendingExchangeAsync`, `PassCombatTurnAsync`, and `EndCombatAsync` did not yet exist; the existing 24 tests passed. The failures were reflection `NullReferenceException`s at the absent internal methods, not assertion or fixture defects.

After the parent delegation instructed that no further restore or test attempts be made in this worktree, no post-implementation test run was performed. The parent task must run the required focused RED/GREEN commands in its checkout with existing test outputs. No test-pass claim is made here.

The following non-test compile check was run after implementation:

```powershell
dotnet build multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Trpg.Multiplayer.Api.Tests.csproj --no-restore --nologo -v:minimal
```

Result: succeeded, 0 warnings, 0 errors.

## Implemented transitions and invariants

- `GameCoordinator` now implements `IInternalCombatResolutionCoordinator` only after supplying all three explicit internal adapters and implementations.
- Resolve runs under the existing room lock and validates active state, expected revision, exact current pending `ExchangeId`, canonical response membership, active participant identities, and defender authority before rolling.
- A player-owned defender can resolve only as `RequestingPlayerId == Defender.OwnerPlayerId`; an NPC defender accepts only the trusted internal null requester. Both paths require the pending canonical `AvailableResponses` membership.
- Resolve derives the outnumbered bonus from the pending response count and defender allowance, rolls only through `IDiceRoller`, uses the shared `ICheckResolutionEngine` regular-check semantics, and invokes `CocCombatOpposedEngine`. It accepts no client roll, HP, damage amount, weapon, or armor data.
- Only a successful resolve increments the attacker action and defender response counts, clears pending, retains the stable ExchangeId in completed history and any disposition, advances turn, and records a non-null deferred disposition in the independent `PendingDamageDispositions` registry. History is bounded at 120 while the registry and `LastExchange` remain independent.
- Duplicate/stale/rejected resolve paths return before dice, registry updates, counters, or state replacement.
- Pass rejects pending combat; a valid controlled current actor increments only its action and advances turn. Round wrap resets per-round counters without adding Task 9 dying behavior.
- Trusted host End atomically clears pending, marks inactive, sets `combat_ended_before_resolution`, and commits one revision without dice, counters, history, or disposition. An inactive session cannot retain a pending exchange.

## Test coverage added

- Wrong, stale, and duplicate exchange identity; unsupported/pending-external response; pre-dice no-op checks.
- Player defender authority and trusted NPC-only response authority.
- Server deterministic dice, post-success counter increments, pending clear, stable completed history/disposition, turn advance, and duplicate no-second-roll/registry-entry behavior.
- Pass rejection while pending and successful no-pending pass.
- End cancellation reason, no-roll/no-history cancellation, and inactive-without-pending invariant.
- 121 deterministic resolutions demonstrate 120-entry history trimming while the first deferred disposition and a replaced `LastExchange` remain independently addressable.

## Self-review and concerns

- The state replacement pattern remains `TryReplace` under the room lock and notifier publication remains post-commit.
- The NPC trusted-policy boundary is represented by a null requester: no room member can select an NPC defender response; canonical pending response membership still constrains the trusted internal caller. No player intent transport or public API is introduced.
- No Task 9 health/dying scheduling is included; on wrap this task advances round and resets action/response counters only.
- The focused GREEN result remains an infrastructure handoff item, not a completed validation result in this worktree.

## Correction and fresh GREEN evidence

The parent requested a fresh verification of player-defender authority and the remaining fail-closed rules after the temporary checkout cleanup. The valid owner path is covered by `InternalCombat_ResolveRequiresDefenderOwnerForPlayerAndTrustedNullAuthorityForOpponent`: the player-owned defender's owner succeeds, while another room member is rejected before dice. The NPC branch likewise rejects a non-null player requester and accepts only the trusted internal null requester, with canonical pending response membership still checked first.

The first fresh focused run found one test-fixture defect, not a production authority defect: the 121-exchange history fixture supplied `1, 1` dice pairs. Equal critical Dodge results are correctly no-damage, so no disposition could exist for its first exchange. The fixture now supplies `1, 100` per exchange, making each exchange damage-eligible and correctly proving the independent registry invariant.

Fresh focused GREEN commands:

```powershell
dotnet test multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Trpg.Multiplayer.Api.Tests.csproj --no-restore --filter "FullyQualifiedName~GameStateTests" --nologo -v:minimal
```

Result: passed — 28 passed, 0 failed, 0 skipped (368 ms).

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests" --nologo -v:minimal
```

Result: passed — 28 passed, 0 failed, 0 skipped (288 ms).

The previous isolated-worktree limitation no longer applies after the available test outputs were restored. These results are the current Task 8 GREEN evidence.
