# Task 10 report: safe CombatSnapshot and route absence

## Scope and constraints

- Modified only the approved Task 10 contracts, projection, focused state/API tests, and this report.
- Preserved the existing Task 6-9 working-tree changes. No commit, push, reset, stash, route mapping, client, or realtime change was made.
- `CombatSession`, `PendingDamageDispositions`, and `DyingSchedule` remain internal domain state and are never directly serialized.

## TDD evidence

### RED

Before production code, the required command was run after adding the projection/privacy and public-route-absence tests:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests|FullyQualifiedName~GameApiTests" --nologo -v:minimal
```

Result: test compilation failed as expected, before test execution. The new tests produced seven feature-missing compiler errors: `CombatSnapshot` and `CombatParticipantStatsSnapshot` did not exist, and `GameSnapshot.Combat` did not exist. This is RED evidence for the new public projection contract; no tests ran because compilation stopped.

### GREEN

The same required command was rerun after the minimal DTO/projection implementation.

Result: **43 passed, 0 failed, 0 skipped** (one test assembly; 1 second; no warnings or errors).

## Implemented safe projection

- `GameSnapshot` now has an optional trailing `CombatSnapshot? Combat` field, preserving existing positional callers.
- A combat participant receives only active/round/current actor, ordered safe participant summaries, a safe last-exchange outcome/winner/disposition-pending summary, and pending role/status.
- Each safe participant has only ID, optional `CharacterId`, label, side, active/current/viewer-owned flags. The viewer receives their own snapshotted DEX/Fighting/Dodge values only; other player and opponent stats are null.
- A room member who owns no combat participant receives `Combat = null`. Projection is read-only and preserves the canonical state revision.
- Projection does not expose raw rolls, targets, response policy/allowance, full history, source/provenance, `PendingDamageDispositions`, or `DyingSchedule`.

## Route evidence

`GameApi.MapGameEndpoints` was left unchanged. The source probe returned no matches:

```powershell
rg -n "combat/start|combat/attack|combat/respond|combat/dodge|combat/fight-back|combat/pass|combat/end" multiplayer/server/src/Trpg.Multiplayer.Api/GameApi.cs
```

The new API test also verified all seven corresponding HTTP paths return `404 Not Found`.

## Final validation and self-review

- `git diff --check`: passed (only non-blocking repository line-ending warnings were emitted).
- Strict UTF-8 decoding passed for `GameContracts.cs`, `GameProjection.cs`, `GameStateTests.cs`, and `GameApiTests.cs`.
- Tests serialize the safe `CombatSnapshot` and assert that internal session/registry, response authority, raw check, history, schedule, and provenance names are absent.
- No commit or push was performed.

## Concerns

- The public read-only client/realtime consumption of the optional Combat snapshot is intentionally deferred to Task 11. No escalation is required for this Task 10 server-only boundary.
