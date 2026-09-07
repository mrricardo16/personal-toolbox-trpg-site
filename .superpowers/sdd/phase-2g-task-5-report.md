# Phase 2G Task 5 Report

## Scope

Implemented only the approved canonical Combat profiles and fail-closed Combat start snapshots.

Owned files changed:

- `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/MultiplayerGameState.cs`
- `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CombatSessionState.cs`
- `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs`
- `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs`
- `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`
- `.superpowers/sdd/phase-2g-task-5-report.md`

No resolve-damage command, causal gate, HP/vitality mutation, projection, realtime, client, public loadout editor, full inventory, NPC health domain, route, commit, push, reset, revert, or stash work was performed.

## UTF-8 gate

Before editing, strict throwing UTF-8 decoding reported `UTF8_OK` for all five existing task-owned source/test files. The report did not yet exist. After editing, the same strict gate reported `UTF8_OK` for all six owned files. No suspected ANSI/GBK file was found and the existing Chinese text remains valid.

## TDD RED evidence

The exact Task 5 command was first run after adding the start/profile/snapshot tests and before production implementation:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.InternalCombat_Start|FullyQualifiedName~GameStateTests.CombatDamageProfile" --nologo -v:minimal
```

Initial RED:

- Exit code: `1`
- Production API project built.
- Test compilation failed with 2 `CS0246` errors because the approved `CharacterCombatLoadout` type did not exist.
- No unrelated baseline failure was reported.

A separate fail-closed null-loadout regression was then added before its guard. The same exact command produced a behavioral RED:

- Exit code: `1`
- Passed: `25`
- Failed: `1`
- Skipped: `0`
- Failure: `CombatDamageProfile_StartRejectsMissingCharacterLoadoutWithoutMutation` threw `NullReferenceException` from `GameCoordinator.StartCombatCore` instead of returning `InvalidParticipant`.

## Implementation

- Added `CharacterCombatLoadout`, `CombatDamageProfile`, and `OpponentVitalityState` as narrow immutable records.
- Preserved the existing five-argument `CharacterState` constructor and assigned the server-canonical default unarmed loadout: `unarmed`, `徒手/拳脚`, `1d3`, Damage Bonus enabled, `melee_non_impaling`, Armor `0`.
- Added an explicit trusted-loadout constructor and made `WithHealth` preserve the canonical loadout.
- Extended internal `OpponentDefinition` with required STR, SIZ, current/max HP, fixed Armor, and weapon fields; no production fallback exists.
- Extended each `CombatParticipantState` with the immutable start-time damage profile and optional opponent vitality.
- Combat start now requires investigator `str` and `siz` from canonical `CheckValues` in `1..100`, and validates the narrow loadout before participant construction.
- Opponent start now requires STR/SIZ in `1..999`, positive current HP, max HP not below current HP, Armor in `0..99`, and a non-null supported normalized weapon.
- Damage Bonus is always derived with `CocCombatDamageRules.DeriveDamageBonus`; it is never accepted from a caller.
- Weapon records are normalized into new snapshot records. Mid-combat replacement of source Character check values/loadout does not alter DEX/Fighting/Dodge/STR/SIZ/DB/weapon/Armor stored in the participant.
- Every validation failure returns before state replacement; tests assert no dice, revision, or Combat mutation.

## GREEN and compatibility evidence

Exact Task 5 focused GREEN:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.InternalCombat_Start|FullyQualifiedName~GameStateTests.CombatDamageProfile" --nologo -v:minimal
```

- Exit code: `0`
- Passed: `26`
- Failed: `0`
- Skipped: `0`

Existing Phase 2F Combat Opposed compatibility gate:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.InternalCombat" --nologo -v:minimal
```

- Exit code: `0`
- Passed: `33`
- Failed: `0`
- Skipped: `0`

All `GameStateTests`:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests" --nologo -v:minimal
```

- Exit code: `0`
- Passed: `55`
- Failed: `0`
- Skipped: `0`

Formatting verification:

```powershell
dotnet format multiplayer/server/Trpg.Multiplayer.slnx --verify-no-changes --no-restore --verbosity minimal
```

- Exit code: `0`
- No formatting changes required.

`git diff --check` passed. The only Git messages were the repository's existing prospective LF-to-CRLF working-copy warnings; strict UTF-8 decoding remained successful.

## Self-review

- Investigator profile authority comes only from canonical Character state; opponent authority comes only from the trusted internal definition.
- Start validation is complete before the single existing `TryReplace`; rejected profiles cannot partially commit.
- Participant profiles contain newly derived/normalized immutable records and do not perform later live Character lookups.
- Existing public Game commands/routes and safe projection contracts were not changed.
- No Task 6+ disposition, causal-gate, damage resolution, HP, opponent-defeat, delivery, or client behavior was pulled forward.

## Modification record

1. Modification time: `2026-09-07 12:04:50 +08:00`
   Location: `CharacterState`, `CombatParticipantState`, internal `OpponentDefinition`, and `GameCoordinator.StartCombatCore`.
   Change: Added narrow canonical loadouts, immutable participant damage/vitality snapshots, strict investigator/opponent validation, normalized weapon copying, and derived Damage Bonus.
   Reason: Implement the approved Phase 2G Task 5 authority boundary and prevent missing or drifting profiles from entering Combat.
   Business impact: Combat start now intentionally rejects incomplete profiles that Phase 2F accepted; valid existing Combat Opposed behavior remains compatible.
   Performance impact: Adds constant-time bounded validation and dice-expression parsing per participant at Combat start only; no runtime I/O or additional RNG.
   External-system impact: No database, Redis, MQ, PLC, third-party API, public HTTP contract, or client impact.
   Risk: Future trusted non-default loadout authoring remains intentionally undefined; this task only supplies the canonical default and internal test seam.
   Regression recommendation: Keep the exact Task 5 gate and the broader `GameStateTests.InternalCombat` gate in subsequent disposition/damage tasks.

## Escalation

Sol High escalation is not required. No production data, authentication/authorization change, migration, destructive operation, concurrency redesign, or broad architecture change was involved.
