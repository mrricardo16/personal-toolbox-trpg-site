# Phase 2H Task 5 Report

## Scope

Implemented only exact human-defender response orchestration in `PlayerCombatIntentCoordinator`. The change reads the current viewer projection and revision first, performs the specified safe prevalidation order, submits the projected exact response to canonical Resolve as final authority, optionally consumes only that exchange's pending damage using the Resolve-returned revision, and returns a fresh final viewer projection.

No store read, route, client, public damage endpoint, direct publication, retry, reroll, rollback, Pass implementation, Single Player, or release artifact change was made. Task 1-4 behavior remains in place.

## UTF-8 precheck

Before mutation, strict throwing UTF-8 decoding succeeded for the Task 5 source and test files:

- `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/PlayerCombatIntentCoordinator.cs` (`10910` bytes)
- `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/PlayerCombatIntentCoordinatorTests.cs` (`25828` bytes)

Strict decoding also succeeded for `.superpowers/sdd/task-5-brief.md` and `AGENTS.md`. This report did not exist before Task 5.

## Root cause and execution path

`RespondAsync` still returned `InvalidIntent` unconditionally even though the safe projected `Combat.Pending` marker, defender-only `ViewerActions.PendingResponse`, canonical Resolve command, Resolve-returned state, and Task 4 exact pending-damage helper were already available.

Implemented path:

```text
GetProjection(viewer)
-> current projected revision check
-> active Combat check
-> generic Pending check
-> defender-only PendingResponse check
-> exact projected exchange check
-> projected available-response check
-> canonical Resolve with authenticated defender player ID
-> Resolve returned State exact DamageDispositions[projected ExchangeId]
-> optional canonical Damage using post-Resolve revision
-> fresh GetProjection(viewer)
```

An observer or non-owning player with only generic pending combat receives `DefenderNotOwned`; the result contains no snapshot and therefore does not disclose the exact exchange ID or available responses. Canonical Resolve remains responsible for revalidating exact exchange, ownership, membership, response, revision, and damage gates.

## RED

Command:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~PlayerCombatIntentCoordinatorTests.Respond" --nologo -v:minimal
```

Result: failed as expected with `0` passed, `11` failed, and `0` skipped. Both success-path tests and all rejection-path assertions reached the existing unconditional `InvalidIntent` stub. The projects compiled successfully, so this was the exact missing Task 5 behavior rather than a compilation or fixture error.

## GREEN

Required Task 5 plus canonical Resolve selection:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~PlayerCombatIntentCoordinatorTests.Respond|FullyQualifiedName~GameStateTests.InternalCombat_Resolve" --nologo -v:minimal
```

Result: passed with `15` passed, `0` failed, and `0` skipped. The selected coverage verifies exact authenticated defender command fields, no Damage call for a no-hit result, rejection before any Resolve, Damage, dice, publication, or revision mutation, exact post-Resolve damage lookup, Resolve-returned revision use, and fresh final projection.

Additional complete coordinator-test selection:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~PlayerCombatIntentCoordinatorTests" --nologo -v:minimal
```

Result: passed with `26` passed, `0` failed, and `0` skipped. One prior attempt at this optional command omitted the required space after `--filter`; MSBuild rejected that malformed command as an unknown switch before running tests. The corrected command above is the recorded regression result.

## Modification record

1. Modification time: `2026-09-08 13:20:43 +08:00`
   Location: `PlayerCombatIntentCoordinator.RespondAsync`, its existing exact pending-damage helper call, and focused coordinator tests.
   Change: Added projection-ordered response prevalidation, exact defender Resolve orchestration, optional exact Damage from Resolve-returned state/revision, fresh final projection, and rejection/success regression coverage.
   Reason: Replace the Task 3 response stub without leaking defender-only response details or duplicating Task 4 damage-disposition logic.
   Business impact: Changes `RespondAsync` from unconditional `InvalidIntent` to the approved Task 5 response behavior; MeleeAttack and Pass behavior are unchanged.
   Performance impact: Adds two viewer projections on successful response and no new storage reads or external I/O beyond the existing coordinator calls.
   Risk: Canonical state can change after projection, so canonical Resolve remains the final authority and may still reject the command without coordinator retry.
   Regression recommendation: Keep the exact Respond/InternalCombat_Resolve gate and the complete coordinator-test gate in later tasks.

## Remaining risks

Verification is intentionally focused. Per Task 5 constraints, no full suite, staging, commit, or push was performed. The canonical Resolve behavior itself is covered only by the selected existing `InternalCombat_Resolve` tests; route/client exposure remains outside this task.

The new report is present on disk but `.superpowers/sdd/.gitignore` contains `*`, so ordinary `git status` and `git diff` do not list it. The source and test working-tree diff passed `git diff --check`; the report separately passed strict UTF-8 decoding and a trailing-whitespace scan. A later authorized commit must account for the existing ignore rule.
