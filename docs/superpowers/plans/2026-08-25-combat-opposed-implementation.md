# Multiplayer Phase 2F Combat Opposed Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate the verified Single Player Combat Opposed semantics into a pure C# engine, then integrate an internal-only server-authoritative `CombatSession` with pending exchanges, per-character dying timing, safe projection, realtime/reconnect recovery, and read-only Vue combat status.

**Architecture:** Keep `src/combat-opposed.js` and all Single Player artifacts unchanged. Commit 1 creates the pure opposed engine, shared success-level reuse, minimum domain records, and a real VM-exported JavaScript conformance fixture. Commit 2 mounts `CombatSession` in `MultiplayerGameState`, routes internal canonical transitions through the existing per-room lock and revision store, projects viewer-safe combat status, and links round wrap to the existing Health Stabilization engine.

**Tech Stack:** .NET 8, C# records and xUnit, ASP.NET Core Minimal APIs/SignalR, Vue 3 + TypeScript, Vitest, Node VM fixture exporters, PowerShell/Git on Windows.

## Global Constraints

- Keep every edited text file UTF-8 and preserve existing Chinese comments, copy, and logs.
- Use `main` and only `https://github.com/mrricardo16/personal-toolbox-trpg-site.git` for writes.
- Design baseline is final commit `615a06e77d8f06258d61213faa732a465e8f79a3`; re-check it before implementation.
- Produce exactly two future feature commits: `feat: migrate combat opposed rules` and `feat: integrate multiplayer combat opposed`.
- Commit 1 must not modify Vue, public HTTP routes, `GameApi.cs`, `GameProjection.cs`, or `MultiplayerGameState.cs` unless a minimum domain type placement requires a naming-only adjustment documented in the commit.
- Do not modify `src/`, `build/`, `outputs/`, or the formal Single Player HTML artifact.
- Do not implement Combat Damage, weapons, damage dice, DB, Armor, HP mutation, Major Wound, defeat repair, NPC HP, Firearms, Impaling, Healing, SAN, Scenario, Location, Communication, PlayerKnowledge runtime, persistence, Redis, matchmaking, accounts, billing, or AI gameplay.
- Do not add public `/combat/start`, `/combat/attack`, `/combat/respond`, `/combat/dodge`, `/combat/fight-back`, `/combat/pass`, `/combat/end`, or equivalent routes.
- Do not add public Player Combat Intent or player-owned defender response APIs.
- Do not add Vue Attack, Dodge, Fight Back, Pass, Start, End, timeout, or pending-response action buttons.
- `CharacterId` is investigator gameplay identity; `PlayerId` is controller identity only.
- Combat stats are read from canonical `CharacterState.CheckValues` at Combat start and snapshot into the participant; no third editable stat source is allowed.
- `BeginOpposedExchange` creates `ExchangeId`, writes `PendingCombatExchange`, increments revision once, and does not roll, count response/action, advance turn, apply HP, or append resolved history.
- `ResolvePendingExchange` must match the current pending `ExchangeId`, expected revision, defender authority, and pending `AvailableResponses`; stale/duplicate requests fail closed without a second roll or state mutation.
- `AvailableResponses` must be non-empty and canonical; NPC policy must choose only from that pending set.
- A trusted explicit End Combat cancels pending atomically with reason `combat_ended_before_resolution`; it clears pending, does not create a resolved exchange, and never leaves `ended + pending`.
- Only a validated canonical transition advances turn or round. Disconnect, reconnect, refresh, chat, SignalR delivery, and AI narrative never advance combat.
- Production dice come from the existing `IDiceRoller`; forced rolls are test/internal seams only.
- Combat Opposed consumes existing `CocCheckResolutionEngine` / shared success-level semantics and must not implement a second `cocRank`.
- `DamageDisposition` is exchange-scoped, remains pending, and does not mutate HP in Phase 2F.
- Completed exchange history is bounded to 120; pending exchange is separate and never counts as resolved history.
- Dying timing is keyed by `CharacterId`; health check ordinal, target, history, stabilization, and death records remain in canonical `CharacterHealthState`.
- Multiple eligible dying investigators are processed in deterministic combat order in one room-locked round-wrap transaction, one replacement, one revision, and one snapshot broadcast.
- `CombatSession` and domain subrecords are never directly serialized; all network data is produced by viewer-specific projection.
- Each future feature commit must be independently tested, reviewed, committed, pushed, and verified with local `HEAD == origin/main`.
- After the second future feature commit is pushed and verified, stop. This plan does not authorize implementation in the current documentation-only task.

---

## File Map

### Future Commit 1 — deterministic migration

- Create: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CombatOpposedResolution.cs` — pure `CocCombatOpposedEngine`, response/outcome contracts, and success-level comparison through the shared Check rule.
- Create: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CombatSessionState.cs` — minimum canonical participant, session, pending exchange, completed exchange, dying schedule, and disposition records used by Commit 2.
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CheckResolution.cs` — expose one shared success-level ranking helper without changing existing Check behavior.
- Create: `multiplayer/server/tests/Fixtures/export-combat-opposed-conformance.js` — execute actual Single Player sources in the established Node VM harness and write deterministic JSON.
- Create: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/combat-opposed.json` — committed expected reference cases.
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Trpg.Multiplayer.Api.Tests.csproj` — copy the combat fixture to test output.
- Create: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/CombatOpposedResolutionTests.cs` — pure engine and fixture conformance tests.

### Future Commit 2 — canonical Multiplayer integration

- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/MultiplayerGameState.cs` — attach optional `CombatSession?` to canonical game state without duplicate combat stats.
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs` — internal combat commands/results, safe projection records, and combat error codes.
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/IGameCoordinator.cs` — internal-only coordinator methods; no route mapping.
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs` — start, begin, resolve, pass, end, revision, room-lock, projection, realtime, pending cancellation, and round-wrap health linkage.
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameProjection.cs` — viewer-safe `CombatSnapshot` generation.
- Do not modify: `multiplayer/server/src/Trpg.Multiplayer.Api/GameApi.cs` to add routes; retain route-surface absence.
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs` — coordinator, revision, ownership, pending, end, stale, and dying integration coverage.
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameApiTests.cs` — prove no public Combat route exists.
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/SignalRGameDeliveryTests.cs` — commit-before-broadcast and viewer-safe CombatSnapshot coverage.
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/DisconnectReconnectTests.cs` — pending preservation and latest snapshot recovery.
- Modify: `multiplayer/client/src/contracts/rooms.ts` — read-only `CombatSnapshot` contract only.
- Modify: `multiplayer/client/src/components/LobbyView.vue` — read-only combat status, turn order, safe last exchange summary, and safe waiting status; no action controls.
- Modify: `multiplayer/client/src/components/HomeView.test.ts` — projected CombatSnapshot rendering/privacy/no-action regression.
- Modify: `multiplayer/client/src/state/gameSnapshot.test.ts` only if snapshot acceptance needs a combat-specific monotonicity assertion.
- Modify: `docs/CURRENT_STATE.md`, `docs/HANDOFF.md`, and minimally `docs/ARCHITECTURE.md` — record actual Phase 2F completion facts after implementation validation; preserve historical sections.

No file outside these responsibilities may be changed by the future feature commits.

## Domain Contracts

Commit 1 defines the pure rule and minimum domain shapes. Exact C# names may receive naming-only adjustments during implementation, but later tasks must use the same concepts and fields.

```csharp
public enum CombatResponse
{
    Dodge,
    FightBack,
}

public sealed record CombatParticipantId(string Value);

public sealed record CombatParticipantState(
    CombatParticipantId ParticipantId,
    Guid? CharacterId,
    Guid? OwnerPlayerId,
    string Label,
    string Kind,
    string Side,
    int Dex,
    int Fighting,
    int Dodge,
    IReadOnlyList<CombatResponse> AvailableResponses,
    int ResponseAllowance,
    bool Active);

public sealed record DamageDisposition(
    string ExchangeId,
    string OwnerParticipantId,
    string TargetParticipantId,
    string Mode,
    bool Pending,
    bool HpCommitted);

public sealed record PendingCombatExchange(
    string ExchangeId,
    int Round,
    int TurnIndex,
    CombatParticipantId AttackerParticipantId,
    CombatParticipantId DefenderParticipantId,
    Guid? DefenderOwnerPlayerId,
    IReadOnlyList<CombatResponse> AvailableResponses,
    int ResponseCountBefore,
    long CreatedRevision,
    DateTimeOffset CreatedAt);

public sealed record CombatExchange(
    string ExchangeId,
    int Round,
    int TurnIndex,
    CombatParticipantId AttackerParticipantId,
    CombatParticipantId DefenderParticipantId,
    CombatResponse Response,
    CheckResolutionResult AttackerCheck,
    CheckResolutionResult DefenderCheck,
    int OutnumberedBonusDice,
    int ResponseCountBefore,
    int ResponseAllowance,
    string Outcome,
    CombatParticipantId? WinnerParticipantId,
    DamageDisposition? DamageDisposition,
    DateTimeOffset CreatedAt);

public sealed record DyingScheduleState(
    int ObservedRound,
    int? LastCheckCompletedRound);

public sealed record CombatSession(
    Guid CombatId,
    bool Active,
    int Round,
    int TurnIndex,
    IReadOnlyList<CombatParticipantId> Order,
    IReadOnlyList<CombatParticipantState> Participants,
    IReadOnlyDictionary<string, int> ResponseCounts,
    IReadOnlyDictionary<string, int> ActionCounts,
    PendingCombatExchange? PendingExchange,
    CombatExchange? LastExchange,
    IReadOnlyList<CombatExchange> History,
    IReadOnlyDictionary<Guid, DyingScheduleState> DyingSchedule,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string? EndReason);

public sealed record CombatOpposedResolutionInput(
    CheckResolutionResult AttackerCheck,
    CheckResolutionResult DefenderCheck,
    CombatResponse Response);

public sealed record CombatOpposedResolutionResult(
    string Outcome,
    string? WinnerSide,
    string? DamageMode);

public interface ICombatOpposedEngine
{
    CombatOpposedResolutionResult Resolve(CombatOpposedResolutionInput input);
}
```

`CombatOpposedResolutionResult` does not accept or calculate HP, weapons, Armor, damage dice, or participant counts. The application layer derives the outnumbered bonus from canonical counts, calls `IDiceRoller`, calls `ICheckResolutionEngine.Resolve`, and passes the two resolved Check results into `ICombatOpposedEngine`.

## Commit 1 — Combat Opposed Deterministic Migration

### Task 1: Expose shared CoC success-level ranking without changing Check behavior

**Files:**
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CheckResolution.cs`
- Test: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/CheckResolutionTests.cs`

**Interfaces:**
- Consumes: existing `CheckResolutionResult.SuccessLevel` values and `CocCheckResolutionEngine.Resolve`.
- Produces: one shared `CocCheckResolutionRules.SuccessLevelRank(string successLevel)` helper used by both existing Check assertions and `CocCombatOpposedEngine`.

- [ ] **Step 1: Add failing assertions for the shared rank helper.**

Add:

```csharp
[Theory]
[InlineData("fumble", 0)]
[InlineData("failure", 0)]
[InlineData("regular", 1)]
[InlineData("hard", 2)]
[InlineData("extreme", 3)]
[InlineData("critical", 4)]
public void SuccessLevelRank_matches_single_player_order(string level, int expected)
{
    Assert.Equal(expected, CocCheckResolutionRules.SuccessLevelRank(level));
}
```

- [ ] **Step 2: Run the focused test and verify the expected missing-helper failure.**

Run:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~CheckResolutionTests" --nologo -v:minimal
```

Expected: compile failure because `CocCheckResolutionRules.SuccessLevelRank` does not yet exist.

- [ ] **Step 3: Implement the shared helper and route the existing engine through it.**

Use:

```csharp
public static class CocCheckResolutionRules
{
    private static readonly IReadOnlyDictionary<string, int> SuccessOrder =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["fumble"] = 0,
            ["failure"] = 0,
            ["regular"] = 1,
            ["hard"] = 2,
            ["extreme"] = 3,
            ["critical"] = 4,
        };

    public static int SuccessLevelRank(string successLevel) =>
        SuccessOrder.TryGetValue(successLevel, out var rank) ? rank : 0;
}
```

Keep `CocCheckResolutionEngine.Resolve` output, thresholds, fumble rules, and dice behavior unchanged. Do not add a second `cocRank` implementation.

- [ ] **Step 4: Run the focused test and the existing Check suite.**

Run:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~CheckResolutionTests" --nologo -v:minimal
```

Expected: all existing Check tests and new rank assertions pass.

### Task 2: Write pure Combat Opposed tests before implementation

**Files:**
- Create: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/CombatOpposedResolutionTests.cs`
- Test fixture input: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/combat-opposed.json`

**Interfaces:**
- Consumes: `CheckResolutionResult`, approved `CombatResponse` values, and fixture schema.
- Produces: red tests for pure Dodge/Fight Back semantics, disposition modes, and fixture conformance. These tests must not instantiate `GameCoordinator`, room stores, SignalR, Vue, or production RNG.

- [ ] **Step 1: Add explicit pure rule tests.**

Add named tests:

```text
DodgeEqualRegular_defenderDodges
DodgeAttackerHigher_attackerHits
DodgeBothFail_noDamageDisposition
FightBackEqualRegular_attackerWins
FightBackDefenderStrictlyHigher_defenderFightsBack
FightBackAttackerFailsDefenderSucceeds_defenderFightsBack
FightBackBothFail_noDamageDisposition
ExtremeAttacker_usesInitiatorExtremeEligible
ExtremeFightBack_defenderUsesRegularCap
InvalidResponse_rejected
```

Use explicit resolved Check results and assert exact outcome/damage mode. Assert no HP field or HP mutation exists.

- [ ] **Step 2: Run the focused test and verify the expected missing-engine failure.**

Run:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~CombatOpposedResolutionTests" --nologo -v:minimal
```

Expected: compile failure because the pure Combat types and engine do not yet exist.

- [ ] **Step 3: Add fixture-loading tests with semantic error comparison.**

Load `Fixtures/combat-opposed.json`, assert the reference sources include the actual Combat, Check, and resolution files, execute every portable case through the C# engine, and compare outcome, winner side, damage mode, roll rank, and semantic error kind. Do not compare localized browser messages or incidental timestamps.

- [ ] **Step 4: Keep Multiplayer-only cases out of JS expected-value assertions.**

Create a separate test section for Commit 2 generalization cases: CharacterId, owner relation, pending exchange, `AvailableResponses`, revision, projection, and per-character dying. Do not label these as JS conformance.

### Task 3: Implement the pure opposed engine and minimum domain records

**Files:**
- Create: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CombatOpposedResolution.cs`
- Create: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CombatSessionState.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CheckResolution.cs` only as specified in Task 1

**Interfaces:**
- Consumes: `CheckResolutionResult` and `CocCheckResolutionRules.SuccessLevelRank`.
- Produces: `ICombatOpposedEngine.Resolve`, immutable domain records for Commit 2, and no application/storage/network dependency.

- [ ] **Step 1: Implement the pure comparison through the shared rank helper.**

Implement Dodge and Fight Back exactly:

```text
Dodge:
  attacker passed AND attacker rank > defender rank -> attacker_hits
  otherwise either passed -> defender_dodges
  otherwise -> both_fail_no_damage

Fight Back:
  defender passed AND defender rank > attacker rank -> defender_fights_back
  otherwise attacker passed -> attacker_hits
  otherwise -> both_fail_no_damage
```

Map attacker extreme/critical to `initiator_extreme_eligible` and defender wins to `fight_back_regular_cap`. The engine may classify already-resolved success-level strings but must not calculate a roll rank or target.

- [ ] **Step 2: Implement immutable domain records without transport serialization.**

Normalize `AvailableResponses` to a read-only distinct list. Reject empty lists at pending creation. Keep `CombatSession.History` bounded to 120 in canonical transition helpers, not in a DTO serializer. Keep `PendingCombatExchange` outside history; keep `LastExchange` as a convenience reference but never the only disposition source.

- [ ] **Step 3: Run pure tests and existing Check/HP/Stabilization tests.**

Run:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~CombatOpposedResolutionTests|FullyQualifiedName~CheckResolutionTests|FullyQualifiedName~HpDamageResolutionTests|FullyQualifiedName~HealthStabilizationResolutionTests" --nologo -v:minimal
```

Expected: pure Combat cases and all existing Check/HP/Stabilization tests pass.

### Task 4: Export the real JavaScript Combat reference fixture

**Files:**
- Create: `multiplayer/server/tests/Fixtures/export-combat-opposed-conformance.js`
- Create: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/combat-opposed.json`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Trpg.Multiplayer.Api.Tests.csproj`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/CombatOpposedResolutionTests.cs`

**Interfaces:**
- Consumes: actual UTF-8 sources `src/check-engine.js`, `src/coc-resolution-engine.js`, `src/hp-damage-state.js`, `src/health-stabilization.js`, and `src/combat-opposed.js` through the established Node VM pattern.
- Produces: deterministic JSON with `version`, `referenceSources`, `cases`, stable inputs, semantic expected outcomes, and explicit `reference_conformance` versus `multiplayer_generalization` labels.

- [ ] **Step 1: Copy the established VM harness shape without copying rule implementation.**

Use the fixed Date, storage/document shims, `randomInt` override, render/log no-ops, UTF-8 source loading, and `vm.createContext` pattern from `export-health-stabilization-conformance.js`. Load the actual dependency order needed by `combat-opposed.js`. Do not reimplement Combat rank, order, resolution, or dying timing in the exporter.

- [ ] **Step 2: Add the portable case groups.**

The exporter must produce:

```text
participant-normalization
invalid-opponent-stat
stable-dex-order
equal-dex-input-order
dodge-equal-regular
dodge-attacker-higher
dodge-both-fail
fight-back-equal-regular
fight-back-defender-higher
fight-back-attacker-failure-defender-success
fight-back-both-fail
initiator-extreme-eligibility
fight-back-regular-cap
response-allowance-outnumbered
pass-turn-and-round-wrap
no-winning-disposition-is-null
winning-disposition-is-pending
active-combat-dying-observation
first-following-eligible-dying-round
```

Every expected disposition includes mode, owner, target, pending, and hpCommitted; no expected case contains an HP result.

- [ ] **Step 3: Register the fixture as a copied test asset.**

Add to the test project:

```xml
<None Include="Fixtures\combat-opposed.json" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 4: Run exporter twice and verify byte determinism.**

Run:

```powershell
node multiplayer/server/tests/Fixtures/export-combat-opposed-conformance.js
$first = (Get-FileHash multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/combat-opposed.json -Algorithm SHA256).Hash
node multiplayer/server/tests/Fixtures/export-combat-opposed-conformance.js
$second = (Get-FileHash multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/combat-opposed.json -Algorithm SHA256).Hash
Write-Output "COMBAT_FIXTURE_SHA1=$first"
Write-Output "COMBAT_FIXTURE_SHA2=$second"
if ($first -ne $second) { exit 1 }
```

Expected: identical hashes and complete case count.

- [ ] **Step 5: Run C# fixture conformance.**

Run:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~CombatOpposedResolutionTests" --nologo -v:minimal
```

Expected: every `reference_conformance` case passes; Multiplayer-only cases remain separately marked.

### Task 5: Validate and commit Future Feature Commit 1

**Files:** Only the Commit 1 files listed above.

- [ ] **Step 1: Review the staged allowlist.**

Run:

```powershell
git status --short
git diff --stat
git diff --name-only
```

Expected: only pure engine, minimum domain records, shared Check helper, exporter, fixture, test project asset declaration, and pure Combat tests appear. No coordinator, projection, GameState, route, client, `src/`, `build/`, or `outputs/` file is staged.

- [ ] **Step 2: Run full Commit 1 validation.**

Run:

```powershell
dotnet restore multiplayer/server/Trpg.Multiplayer.slnx --nologo
dotnet build multiplayer/server/Trpg.Multiplayer.slnx --no-restore --nologo -v:minimal
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --nologo -v:minimal
dotnet format multiplayer/server/Trpg.Multiplayer.slnx --verify-no-changes --no-restore --verbosity minimal
Get-ChildItem build -Filter 'test-*.js' | ForEach-Object { node $_.FullName }
Get-ChildItem src,build -Recurse -Filter '*.js' | ForEach-Object { node --check $_.FullName }
$formalBefore = (Get-FileHash outputs/trpg-dm-assistant.html -Algorithm SHA256).Hash
node build/build-single-html.js
node build/verify-single-html.js
$firstBuild = (Get-FileHash outputs/trpg-dm-assistant.html -Algorithm SHA256).Hash
node build/build-single-html.js
$secondBuild = (Get-FileHash outputs/trpg-dm-assistant.html -Algorithm SHA256).Hash
if ($firstBuild -ne $secondBuild -or $formalBefore -ne $secondBuild) { exit 1 }
if ((Get-ChildItem outputs -Filter '*.html').Count -ne 1) { exit 1 }
git diff --exit-code -- outputs/trpg-dm-assistant.html
git diff --check
```

Expected: all Server tests, 37 Single Player scripts, 69 syntax checks, verifier, double-build, one-HTML, and unchanged formal SHA pass.

- [ ] **Step 3: Commit and push Commit 1.**

Run:

```powershell
git add multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CombatOpposedResolution.cs multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CombatSessionState.cs multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CheckResolution.cs multiplayer/server/tests/Fixtures/export-combat-opposed-conformance.js multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/combat-opposed.json multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Trpg.Multiplayer.Api.Tests.csproj multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/CombatOpposedResolutionTests.cs
git diff --cached --check
git commit -m "feat: migrate combat opposed rules"
git push origin main
```

- [ ] **Step 4: Verify remote before Commit 2.**

Run:

```powershell
git status -sb
git rev-parse HEAD
git rev-parse origin/main
git rev-list --left-right --count origin/main...HEAD
```

Expected: clean workspace, equal local/remote SHA, divergence `0 0`. Record Commit 1 SHA.

## Commit 2 — Canonical Multiplayer Combat Integration

### Task 6: Add CombatSession to canonical state and safe contracts

**Files:**
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/MultiplayerGameState.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/IGameCoordinator.cs`

- [ ] **Step 1: Add failing state assertions.**

Extend `GameStateTests.cs` to assert initialized games have `Combat == null`, state replacement can carry one `CombatSession`, and `CharacterState` has no duplicate Dex/Fighting/Dodge properties.

- [ ] **Step 2: Add internal command/result records.**

Use:

```csharp
public sealed record StartCombatCommand(
    Guid RoomId,
    Guid AuthorizedPlayerId,
    long ExpectedGameRevision,
    IReadOnlyList<Guid> CharacterIds,
    IReadOnlyList<OpponentDefinition> Opponents);

public sealed record BeginOpposedExchangeCommand(
    Guid RoomId,
    Guid RequestingPlayerId,
    long ExpectedGameRevision,
    string AttackerParticipantId,
    string DefenderParticipantId);

public sealed record ResolvePendingExchangeCommand(
    Guid RoomId,
    Guid? RequestingPlayerId,
    long ExpectedGameRevision,
    string ExchangeId,
    CombatResponse Response);

public sealed record PassCombatTurnCommand(
    Guid RoomId,
    Guid RequestingPlayerId,
    long ExpectedGameRevision);

public sealed record EndCombatCommand(
    Guid RoomId,
    Guid AuthorizedPlayerId,
    long ExpectedGameRevision,
    string Reason);
```

`OpponentDefinition` contains only label, DEX, Fighting, Dodge, `AvailableResponses`, and response allowance/policy. Add internal result records returning a viewer-safe `GameSnapshot` plus internal semantic exchange/cancellation data; do not use them as HTTP DTOs.

- [ ] **Step 3: Add error codes and optional state field.**

Add explicit invalid-combat, invalid-participant, invalid-response, pending-conflict, ended-combat, and invalid-exchange error codes. Preserve `StateConflict` for expected revision failures. Add `CombatSession? Combat` to `MultiplayerGameState` at the end of its constructor and propagate it through every replacement.

- [ ] **Step 4: Add internal coordinator signatures without routes.**

Extend `IGameCoordinator` with internal-only methods for Start, Begin, Resolve, Pass, and End. Keep `GameApi.MapGameEndpoints` unchanged.

- [ ] **Step 5: Run focused state/API tests.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests|FullyQualifiedName~GameApiTests" --nologo -v:minimal
```

Expected: state construction passes and route-surface tests still prove no public Combat endpoint.

### Task 7: Implement trusted Start Combat and BeginOpposedExchange

**Files:**
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`

- [ ] **Step 1: Add failing coordinator tests.**

Cover valid internal start, descending DEX and stable ties, explicit participant subset, duplicate IDs, dead investigator rejection, missing canonical keys, duplicate start, current-actor enforcement, enemy/active defender validation, stable ExchangeId, pending creation, Begin revision++, no dice, no action/response increment, no turn advance, second pending rejection, and non-empty AvailableResponses.

Use a fake `IDiceRoller` that throws if called by Begin.

- [ ] **Step 2: Run focused tests and verify missing-flow failures.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests" --nologo -v:minimal
```

Expected: failures are limited to absent Start/Begin behavior.

- [ ] **Step 3: Implement Start under `WithRoomLockAsync`.**

Validate room/trusted authorization, expected revision, no active session, explicit CharacterIds, owner relation, canonical CheckValues keys `dex`/`fighting_brawl`/`dodge`, opponent definition, and non-empty allowed responses. Create stable participant IDs, order by descending DEX with stable input ties, set round 1/turn 0, observe already-dying investigators without rolling, replace with Revision+1, commit, project, and publish after commit.

- [ ] **Step 4: Implement Begin as a canonical mutation.**

Validate active/current actor/active enemy/no pending/expected revision. Generate one server ExchangeId, copy defender authority and canonical non-empty AvailableResponses, capture ResponseCountBefore and CreatedRevision, write only pending session state, increment revision once, commit, project, and publish. Do not call dice, Check, Health, HP, action/response counters, turn, or resolved history.

- [ ] **Step 5: Run focused green tests.**

Repeat the GameStateTests filter and require exactly one revision increment per successful Start/Begin and zero for rejection.

### Task 8: Implement ResolvePendingExchange, Pass, and EndCombat

**Files:**
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`

- [ ] **Step 1: Add failing Resolve/Pending/End tests.**

Cover wrong/duplicate ExchangeId, response outside pending AvailableResponses, attacker selecting player defender response, trusted NPC policy, server dice, response/action increments only after resolution, pending clear, completed history, turn advance, disposition survival after turn advance, Pass rejection while pending, Pass mutation without pending, End cancellation reason, no roll/history on cancellation, no ended+pending state, and stale revision no-op.

- [ ] **Step 2: Run focused tests and verify missing behavior.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests" --nologo -v:minimal
```

- [ ] **Step 3: Enforce authority and response membership.**

For a player defender require `RequestingPlayerId == Defender.OwnerPlayerId`; attacker cannot override. For an opponent invoke only trusted server policy. In both cases require `pending.AvailableResponses.Contains(selectedResponse)`; empty sets fail before dice.

- [ ] **Step 4: Implement Resolve with shared Check resolution.**

Under one room lock: validate expected revision, active state, exact pending ID, identities, authority, and response membership; derive `responseCountBefore >= responseAllowance ? 1 : 0`; call attacker/defender `IDiceRoller`; call existing `ICheckResolutionEngine.Resolve`; call `ICombatOpposedEngine.Resolve`; create same-ID completed exchange; create disposition only for a winner; increment response/action; clear pending; append bounded history and retain disposition independently of LastExchange; advance turn; run wrap if needed; replace once, commit, project, publish.

Do not accept client rolls, damage amounts, weapons, Armor, HP, DB, or NPC HP.

- [ ] **Step 5: Implement Pass and trusted End.**

Pass requires no pending and current ownership, then increments action, advances turn, and wraps when appropriate. End is valid even with pending, clears it, sets inactive and `combat_ended_before_resolution`, commits one revision, and never rolls, increments response/action, appends resolved history, or creates disposition. It is impossible to commit inactive plus pending.

- [ ] **Step 6: Run focused green tests.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests" --nologo -v:minimal
```

### Task 9: Implement per-character dying scheduling in round wrap

**Files:**
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/HealthStabilizationResolution.cs` only if a pure helper is needed; preserve Phase 2E behavior.
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`

- [ ] **Step 1: Add failing timing tests.**

Cover already-dying start, no same-round check, first following eligible completed round, success retention, once-per-round checks, later checks, stabilization removal, fresh-dying new observation, independent A/B observed rounds, stable combat-order processing, one revision for multiple checks, one participant death/inactive, and no automatic full-combat end.

- [ ] **Step 2: Run focused tests and verify missing linkage.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests" --nologo -v:minimal
```

- [ ] **Step 3: Implement schedule observation and eligibility.**

Store only `DyingSchedule[characterId] = { observedRound, lastCheckCompletedRound }`. At wrap use stable `CombatSession.Order`, active/unstabilized/non-dead health, `finishedRound > observedRound`, and `lastCheckCompletedRound != finishedRound`.

- [ ] **Step 4: Implement one round-wrap transaction.**

For each eligible investigator in stable order, call existing Health Stabilization with `diceRoller.RollPercentile(0, 0).SelectedRoll` and source `combat-round-{finishedRound}-{characterId:N}`. Apply all health and participant updates in memory, reset counts, advance round/turn, replace state once, revision++ once, commit once, and publish once. Do not loop through single-character coordinator methods.

- [ ] **Step 5: Run focused green health/combat tests.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests|FullyQualifiedName~HealthStabilizationResolutionTests|FullyQualifiedName~HpDamageResolutionTests" --nologo -v:minimal
```

### Task 10: Add safe CombatSnapshot and prove route absence

**Files:**
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameProjection.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameApiTests.cs`

- [ ] **Step 1: Add failing projection/privacy tests.**

Cover non-serialization of CombatSession, owner-safe active/round/current actor/order/last summary, own-private stats only, hidden other-player/opponent raw stats, safe pending role/status, absence of raw rolls/targets/policy/allowance/history/source/provenance, no Combat for room nonparticipant, and projection without revision change.

- [ ] **Step 2: Add safe DTOs and viewer-specific projection.**

Extend `GameSnapshot` with optional `CombatSnapshot? Combat`. Safe records include participant ID/CharacterId/label/side/active/current/viewer-owned, safe last exchange outcome/winner/disposition-pending, and safe pending role/status. No raw opponent stats, roll, target, policy, allowance, full history, source, or DyingSchedule may be serialized. Nonparticipants receive `Combat = null`.

- [ ] **Step 3: Keep public route surface unchanged.**

Keep `GameApi.MapGameEndpoints` unchanged. Verify:

```powershell
rg -n "combat/start|combat/attack|combat/respond|combat/dodge|combat/fight-back|combat/pass|combat/end" multiplayer/server/src/Trpg.Multiplayer.Api/GameApi.cs
```

Expected: no matching route.

- [ ] **Step 4: Run focused projection/API tests.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests|FullyQualifiedName~GameApiTests" --nologo -v:minimal
```

### Task 11: Verify realtime/reconnect and read-only Vue status

**Files:**
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/SignalRGameDeliveryTests.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/DisconnectReconnectTests.cs`
- Modify: `multiplayer/client/src/contracts/rooms.ts`
- Modify: `multiplayer/client/src/components/LobbyView.vue`
- Modify: `multiplayer/client/src/components/HomeView.test.ts`
- Modify: `multiplayer/client/src/state/gameSnapshot.test.ts` only if snapshot acceptance needs a Combat assertion

- [ ] **Step 1: Add failing realtime/reconnect tests.**

Assert commit-before-GameSnapshot broadcast, cross-room isolation, pending survival across disconnect, AttachSession recovery of latest pending/safe summary, no disconnect auto-response, and no reconnect revision increment. Do not add a Combat-specific SignalR event.

- [ ] **Step 2: Add failing Vue tests.**

Mount active projected Combat and assert read-only text for `COMBAT ACTIVE`, round, current actor, turn order, safe last exchange, and waiting status. Assert no button/API text matches Attack, Dodge, Fight Back, Pass, Start, End, timeout, or Resolve, and no combat API is called.

- [ ] **Step 3: Add TypeScript read-only contracts.**

Add optional `combat: CombatSnapshot | null` to `GameSnapshot` and safe projection interfaces only. Do not add Combat request interfaces or API methods.

- [ ] **Step 4: Render read-only status.**

In `LobbyView.vue` consume server-projected active/round/current actor/order/summary/pending status. Do not add action handlers, dice, combat `ref` state, response buttons, HP inference, or auto-progression.

- [ ] **Step 5: Run focused realtime/client tests and build.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~SignalRGameDeliveryTests|FullyQualifiedName~DisconnectReconnectTests" --nologo -v:minimal
Push-Location multiplayer/client
npm ci
npm test -- --run src/components/HomeView.test.ts src/state/gameSnapshot.test.ts src/realtime/roomConnection.test.ts
npm run build
Pop-Location
```

### Task 12: Update current documentation after implementation

**Files:**
- Modify: `docs/CURRENT_STATE.md`
- Modify: `docs/HANDOFF.md`
- Modify: `docs/ARCHITECTURE.md`

- [ ] **Step 1: Assert current documentation headings before editing.**

```powershell
rg -n "Current Phase|Phase 2E|Phase 2F|CombatSession|PendingCombatExchange|AvailableResponses|combat_ended_before_resolution|PlayerKnowledgeState|AlwaysVisible|Contextual|Last Known Status" docs/CURRENT_STATE.md docs/HANDOFF.md docs/ARCHITECTURE.md
```

- [ ] **Step 2: Update only current sections with actual implementation facts.**

Record Phase 2F completion, actual class/method names, fixture count/hash, validation counts, CharacterId identity, two-stage exchange, AvailableResponses, pending End cancellation, shared Check semantics, per-character dying, deferred disposition, safe projection, commit-before-broadcast, AttachSession recovery, read-only Vue status, no public route, and no public Player Intent. Do not claim Combat Damage, Firearms, AI gameplay, timeout UX, or PlayerKnowledge runtime.

- [ ] **Step 3: Validate UTF-8, scope, and documentation.**

Use a strict UTF-8 decoder for every edited Markdown file, then run:

```powershell
git diff --check
rg -n "POST /combat|combat/attack|combat/respond|Dodge button|Fight Back button|Combat Damage|Firearms|AI gameplay|DB|Redis|PlayerKnowledge runtime" docs/CURRENT_STATE.md docs/HANDOFF.md docs/ARCHITECTURE.md
```

### Task 13: Full Commit 2 validation, push, and stop

**Files:** Only the Commit 2 file map.

- [ ] **Step 1: Run full server validation.**

```powershell
dotnet restore multiplayer/server/Trpg.Multiplayer.slnx --nologo
dotnet build multiplayer/server/Trpg.Multiplayer.slnx --no-restore --nologo -v:minimal
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --nologo -v:minimal
dotnet format multiplayer/server/Trpg.Multiplayer.slnx --verify-no-changes --no-restore --verbosity minimal
```

- [ ] **Step 2: Run full client validation.**

```powershell
Push-Location multiplayer/client
npm ci
npm test -- --run
npm run build
Pop-Location
```

- [ ] **Step 3: Re-run conformance and formal artifact gates.**

```powershell
node multiplayer/server/tests/Fixtures/export-combat-opposed-conformance.js
$combatSha1 = (Get-FileHash multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/combat-opposed.json -Algorithm SHA256).Hash
node multiplayer/server/tests/Fixtures/export-combat-opposed-conformance.js
$combatSha2 = (Get-FileHash multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/combat-opposed.json -Algorithm SHA256).Hash
if ($combatSha1 -ne $combatSha2) { exit 1 }
Get-ChildItem build -Filter 'test-*.js' | ForEach-Object { node $_.FullName }
Get-ChildItem src,build -Recurse -Filter '*.js' | ForEach-Object { node --check $_.FullName }
$formalBefore = (Get-FileHash outputs/trpg-dm-assistant.html -Algorithm SHA256).Hash
node build/build-single-html.js
node build/verify-single-html.js
$formalAfterFirst = (Get-FileHash outputs/trpg-dm-assistant.html -Algorithm SHA256).Hash
node build/build-single-html.js
$formalAfterSecond = (Get-FileHash outputs/trpg-dm-assistant.html -Algorithm SHA256).Hash
if ($formalBefore -ne $formalAfterFirst -or $formalAfterFirst -ne $formalAfterSecond) { exit 1 }
if ((Get-ChildItem outputs -Filter '*.html').Count -ne 1) { exit 1 }
git diff --exit-code -- outputs/trpg-dm-assistant.html
git diff --check
```

Expected: existing Single Player baseline remains 37/37 scripts and 69/69 syntax checks, Combat fixture double SHA matches, verifier passes, formal SHA is unchanged, and one HTML exists.

- [ ] **Step 4: Review final allowlist and prohibited scope.**

```powershell
git status --short
git diff --stat
git diff --name-only
rg -n "MapGet|MapPost|combat/start|combat/attack|combat/respond|combat/pass|combat/end" multiplayer/server/src/Trpg.Multiplayer.Api/GameApi.cs
```

Expected: only Commit 2 files changed and no public Combat route exists. No `src/`, `build/`, `outputs/`, Firearms, Combat Damage, AI gameplay, Healing, SAN, Scenario, DB, Redis, Location, Communication, or PlayerKnowledge runtime file changed.

- [ ] **Step 5: Commit and push Commit 2.**

```powershell
git add multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/MultiplayerGameState.cs multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/IGameCoordinator.cs multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameProjection.cs multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameApiTests.cs multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/SignalRGameDeliveryTests.cs multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/DisconnectReconnectTests.cs multiplayer/client/src/contracts/rooms.ts multiplayer/client/src/components/LobbyView.vue multiplayer/client/src/components/HomeView.test.ts multiplayer/client/src/state/gameSnapshot.test.ts docs/CURRENT_STATE.md docs/HANDOFF.md docs/ARCHITECTURE.md
git diff --cached --check
git commit -m "feat: integrate multiplayer combat opposed"
git push origin main
```

- [ ] **Step 6: Verify final synchronization and stop.**

```powershell
git status -sb
git rev-parse HEAD
git rev-parse origin/main
git rev-list --left-right --count origin/main...HEAD
```

Expected: clean workspace, local SHA equals `origin/main`, divergence `0 0`. Record both feature SHAs and validation counts, then STOP. Do not start another feature or implement Combat Damage.

## Validation and Handoff Checklist

Before declaring the future implementation complete, record:

- Final design SHA: `615a06e77d8f06258d61213faa732a465e8f79a3`.
- Future Commit 1 SHA/message and remote equality.
- Future Commit 2 SHA/message and remote equality.
- Combat fixture case count and two identical SHA values.
- Server build/test/format results and client test/build results.
- Existing Single Player regression count, syntax count, verifier, double-build hashes, one-HTML count, and unchanged formal artifact SHA.
- EndCombat pending policy: trusted cancellation with `combat_ended_before_resolution`.
- Contradictory inactive + pending state possible: `NO`.
- AvailableResponses canonical/non-empty: `YES`.
- Resolve response membership validation: `YES`.
- NPC policy constrained to pending AvailableResponses: `YES`.
- Public Combat API: `NO`.
- Player defender response API: `NO`.
- Pending timeout/disconnect auto response: `NO`.
- Combat Opposed HP mutation: `NO`.
- DamageDisposition exact ExchangeId: `YES`.
- Per-character dying timing: `YES`.
- One investigator death ends all Combat: `NO`.
- Production code, tests, client, Single Player, and formal artifact changed only within approved future Commit 1/2 boundaries.

## Stop Condition

After the two future feature commits are independently validated, pushed, and synchronized, stop immediately. The next task must be a separate implementation authorization or review request. This plan itself does not authorize implementation in the current turn.
