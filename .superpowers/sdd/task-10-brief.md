# Commit 2 Task 10 Brief — Safe CombatSnapshot and route absence

## Objective

Add viewer-specific, safe Combat projection to `GameSnapshot` and prove the public combat route surface remains absent. Continue the second feature commit; do not commit or push. Preserve all Task 6–9 behavior and do not serialize `CombatSession` directly.

## Files

- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameProjection.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameApiTests.cs`

Read the approved design/spec/plan and Task 6–9 reports/reviews. Use TDD: add failing projection/privacy tests first, run the focused filter and record RED, then implement minimal safe DTOs/projection and run GREEN. Keep all edited text UTF-8.

## Required tests and DTO rules

Cover non-serialization of `CombatSession` and `PendingDamageDispositions`; owner-safe active/round/current actor/order/last summary; own-private stats only; hidden other-player and opponent raw stats; safe pending role/status; no raw rolls, targets, policy, allowance, full history, source/provenance, or `DyingSchedule`; nonparticipants receive `Combat = null`; projection does not change revision; and route absence.

Extend `GameSnapshot` with optional `CombatSnapshot? Combat` in a compatible position. Safe records may expose only participant ID/CharacterId/label/side/active/current/viewer-owned, safe last exchange outcome/winner/disposition-pending, and safe pending role/status. Do not expose internal registry, raw opponent stats, roll values, target numbers, policy, response allowance, full history, source/provenance, or dying schedule. Nonparticipants must receive null. Use strong domain identity internally but safe DTO strings/IDs only as appropriate for the existing public snapshot contract.

Keep `GameApi.MapGameEndpoints` unchanged and verify no route matches:

```powershell
rg -n "combat/start|combat/attack|combat/respond|combat/dodge|combat/fight-back|combat/pass|combat/end" multiplayer/server/src/Trpg.Multiplayer.Api/GameApi.cs
```

No public Start/End/Attack/Dodge/Fight Back/Pass/Resolve API and no client changes in this task.

## Validation and report

Run RED/GREEN:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests|FullyQualifiedName~GameApiTests" --nologo -v:minimal
```

Run `git diff --check` and strict UTF-8 checks for all edited files. Append `.superpowers/sdd/task-10-report.md` with RED/GREEN counts, privacy/route evidence, changed files, self-review, and concerns. Do not commit or push. Return status, files, tests, and concerns.
