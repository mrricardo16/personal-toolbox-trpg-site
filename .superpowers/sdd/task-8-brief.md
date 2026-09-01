# Commit 2 Task 8 Brief — Resolve, Pass, and End

## Objective

Implement the trusted internal `ResolvePendingExchange`, `PassCombatTurn`, and `EndCombat` transitions in `GameCoordinator` and their focused `GameStateTests`. This is still part of the second feature commit; do not commit or push. Preserve the reviewed Task 6/7 working-tree changes and the Commit 1 baseline at `633e0477efe4903900844250316935548d3c1b9a`.

## Files

- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`

## Context and boundaries

Read the approved design/spec and implementation plan before editing. Read the existing Task 7 implementation and reports. Work test-first: add the failing tests, run the focused filter and record RED, then implement the smallest canonical transitions and run GREEN. Do not implement later round-wrap dying scheduling, projection/realtime/reconnect, Vue changes, public routes, Combat Damage, HP, weapons, armor, DB, persistence, NPC HP, AI gameplay, timeout/disconnect forfeits, or player intent transport/UI.

## Required tests

Cover all of the Task 8 plan: wrong and duplicate `ExchangeId`; stale expected revision; response outside pending `AvailableResponses`; attacker attempting to select a player-owned defender response; trusted NPC policy and response membership; server-side `IDiceRoller` usage with no client rolls; response/action increments only after successful resolution; pending clear; same-ID completed history; turn advance; disposition survival after turn advancement and after `LastExchange` replacement; more than 120 damage-eligible resolved exchanges where the oldest history entry is trimmed but its `PendingDamageDispositions[ExchangeId]` remains addressable; duplicate Resolve does not add another registry entry or roll; Pass rejected while pending; Pass mutation without pending; End cancellation reason `combat_ended_before_resolution`; End does not roll or append history; inactive plus pending is impossible; and all stale/rejected paths are no-op.

Use deterministic fake dice and trusted policy fixtures. Assert no HP mutation and no Combat Damage behavior. Assert the registry is independent from bounded history and `LastExchange`; Phase 2F does not consume dispositions.

## Canonical rules

Under one room lock and the expected-revision/state-store conventions:

- Resolve requires active state, exact current pending `ExchangeId`, expected revision, participant identities, defender authority, and a response contained in the pending canonical `AvailableResponses`. Player-owned defender response must use `RequestingPlayerId == Defender.OwnerPlayerId`; the attacker cannot override it. NPC response comes only from trusted server policy and must belong to the pending set. Empty/mismatched response sets fail before dice.
- Resolve derives outnumbered from canonical response count, rolls only via existing `IDiceRoller`, reuses existing `ICheckResolutionEngine`/CoC success-level semantics, and invokes the pure `ICombatOpposedEngine`. Do not accept client rolls or compute HP/damage amounts.
- Successful Resolve creates one completed exchange with the existing stable `ExchangeId`, creates a `DamageDisposition` only for a winner using strong `CombatParticipantId` identities, increments response/action only after resolution, clears pending, appends bounded history (120), registers non-null disposition in `PendingDamageDispositions` keyed by the same `ExchangeId`, and advances turn. History trimming and `LastExchange` replacement must not remove registry entries. Duplicate/stale Resolve must fail closed before a second roll or mutation.
- Pass requires active combat, expected revision, no pending exchange, and current actor ownership; it increments action and advances turn. It cannot progress a pending turn.
- Trusted End is valid even with pending, atomically clears pending, marks combat inactive with reason `combat_ended_before_resolution`, increments one revision, and commits without dice, counters, history, or disposition. Never allow ended plus pending.
- Commit state before any snapshot/realtime publication. Keep internal interfaces truthful: current `IInternalCombatCoordinator` has Start/Begin; `IInternalCombatResolutionCoordinator` carries Resolve/Pass/End and `GameCoordinator` should implement the latter only when these methods are actually implemented.

## Validation and report

Run in TDD order:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests" --nologo -v:minimal
```

Run the focused filter again after implementation and record exact counts. Run `git diff --check` and strict UTF-8 checks for all edited files. Write/append `.superpowers/sdd/task-8-report.md` with RED/GREEN commands, transition/revision/registry evidence, changed files, self-review, and concerns. Do not commit or push. Return status, changed files, test summary, and concerns.
