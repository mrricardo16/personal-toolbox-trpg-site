# Multiplayer Phase 2E Health Stabilization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port the verified Single Player dying-CON and First Aid semantics into a pure C# engine, integrate them as server-internal canonical transitions, and expose only owner-safe read-only stabilization status.

**Architecture:** Keep the Single Player JavaScript unchanged and derive a committed fixture by executing `src/hp-damage-state.js` and `src/health-stabilization.js` in the existing Node VM style. Model condition-local data as structured C# records, with an active `DyingEpisode` owning its checks and ordinal. `GameCoordinator` will be the only application layer allowed to invoke stabilization transitions; it will use the existing room lock and `IDiceRoller`, commit before projection/broadcast, and expose no new HTTP route.

**Tech Stack:** .NET 8, C# records and xUnit, ASP.NET Core Minimal APIs/SignalR, Vue 3 + TypeScript, Vitest, Node VM fixture exporters, PowerShell/Git on Windows.

## Global Constraints

- Keep all edited text files UTF-8 and preserve existing Chinese text.
- Use `main` and only `https://github.com/mrricardo16/personal-toolbox-trpg-site.git` for writes.
- Starting remote baseline is `b21bf802144fbfecdfc3197c92fc60fce82c3246`; re-check it before implementation.
- Produce only two feature commits: `feat: migrate health stabilization rules` and `feat: integrate multiplayer health stabilization`.
- A documentation-only design commit may remain as the separate pre-feature commit; do not fold it into either feature commit.
- Commit 1 must not modify Vue or production HTTP routes.
- Do not modify `src/`, `build/`, `outputs/`, or the Single Player formal artifact.
- Do not implement Healing Recovery, Natural Healing, Medicine, SAN, Combat, Firearms/Impaling, Scenario, Location, Communication, Player Knowledge runtime, AI gameplay, DB, Redis, persistence, matchmaking, accounts, or billing.
- Never expose public `/game/stabilize`, `/game/dying-round`, `/game/first-aid`, or an equivalent arbitrary health mutation route.
- Dying ordinal is derived from active episode history; CON target is derived from `CharacterHealthState.Con`; neither is caller-supplied.
- Pure engines accept explicit forced rolls but never call `DateTime.Now`, `DateTime.UtcNow`, `Random.Shared`, `RandomNumberGenerator`, HTTP, SignalR, stores, rooms, Vue, AI, or credentials.
- Production rolls use the existing `IDiceRoller`; forced rolls are available only through internal/test seams.
- Preserve current owner-only detailed health projection while documenting future `AlwaysVisible`, `Contextual`, `Last Known Status`, and `PlayerKnowledgeState` policy without runtime implementation.
- Validate each feature commit independently; after Commit 2 is pushed, stop.

## File Map

Create or modify only these responsibilities:

- Create `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/HealthStabilizationResolution.cs` for structured stabilization records, inputs, result types, interface, and pure C# implementation.
- Modify `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/HpDamageResolution.cs` only to use the structured health state and clear stale stabilization on a fresh dying/dead damage transition.
- Modify `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/MultiplayerGameState.cs` to store structured health state through immutable `CharacterState.WithHealth` replacement.
- Modify `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs` to inject the stabilization engine, use `IDiceRoller` for HP CON fallback, and add internal transitions.
- Modify `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs`, `IGameCoordinator.cs`, `GameProjection.cs`, and `GameApi.cs` only for internal command/result/projection contracts; do not map new stabilization routes.
- Create `multiplayer/server/tests/Fixtures/export-health-stabilization-conformance.js` and `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/health-stabilization.json` using the existing VM/fixture conventions.
- Create or modify `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/HealthStabilizationResolutionTests.cs`, `GameStateTests.cs`, `GameApiTests.cs`, and `Realtime/SignalRGameDeliveryTests.cs` for conformance, state, privacy, concurrency, and reconnect coverage.
- Modify `multiplayer/client/src/contracts/rooms.ts`, `multiplayer/client/src/components/LobbyView.vue`, and `multiplayer/client/src/components/HomeView.test.ts` for projected status only.
- Modify `docs/CURRENT_STATE.md`, `docs/HANDOFF.md`, and minimally `docs/ARCHITECTURE.md` to record Phase 2E and future team visibility policy.

## Domain Contract

Use these exact semantic shapes unless the existing code requires a naming-only adjustment:

```csharp
public sealed record DyingCheckRecord(
    int Ordinal,
    int Roll,
    int Target,
    bool Success,
    string? SourceId,
    DateTimeOffset? OccurredAt);

public sealed record DyingEpisodeState(
    string? SourceEventKey,
    IReadOnlyList<DyingCheckRecord> Checks,
    int NextRoundOrdinal,
    bool RoundChecksManaged);

public sealed record StabilizedConditionState(
    string? SourceId,
    string Reason,
    int? FirstAidTarget,
    int? FirstAidRoll,
    DateTimeOffset? OccurredAt);

public sealed record DeadConditionState(
    string? SourceEventKey,
    string Reason,
    DateTimeOffset? OccurredAt);

public sealed record TreatmentRecord(
    string? SourceId,
    int Target,
    int Roll,
    bool Success,
    bool WithinHour,
    int HpBefore,
    int HpAfter,
    int HealedHp,
    bool WasDying,
    bool WasUnconscious,
    bool StabilizedDying,
    bool RousedUnconscious,
    DateTimeOffset? OccurredAt);

public sealed record DyingRoundInput(
    int Roll,
    string? SourceId,
    DateTimeOffset? OccurredAt);

public sealed record FirstAidInput(
    int Target,
    bool WithinHour,
    int Roll,
    string? SourceId,
    DateTimeOffset? OccurredAt);
```

The engine derives `ordinal = state.DyingEpisode.Checks.Count + 1` and `target = state.Con`. It never accepts either value as input. The engine returns a result containing the replacement state, the resolving `DyingCheckRecord` or `TreatmentRecord`, and `Changed`; it does not persist resolved dying checks after the reference clears the active episode. `TreatmentHistory` remains bounded at 60. `DyingEpisode.Checks` remains bounded at 40 while the episode is active.

## Commit 1 — Health Stabilization Rule Migration

### Task 1: Establish the red conformance and engine tests

**Files:**
- Create: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/HealthStabilizationResolutionTests.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Trpg.Multiplayer.Api.Tests.csproj` only if the fixture copy convention requires it.

**Interfaces:**
- Consumes: the domain contract above and fixture path `Fixtures/health-stabilization.json`.
- Produces: failing tests that require `IHealthStabilizationEngine.ResolveDyingRound` and `ResolveFirstAid`.

- [ ] **Step 1: Write the failing fixture test.**

The test must load `Fixtures/health-stabilization.json`, deserialize `version`, `referenceSources`, `cases`, and for every operation compare final semantic state and returned record/error. It must assert `referenceSources` contains both `src/hp-damage-state.js` and `src/health-stabilization.js`. It must compare `CurrentHp`, `MaxHp`, `Con`, Major Wound, Unconscious, Dying episode presence and fields, Stabilized, Dead reason, bounded treatment history, and resolving records; exclude only timestamp values when the fixture marks them non-semantic.

- [ ] **Step 2: Run the focused test and verify the expected red state.**

Run:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter FullyQualifiedName~HealthStabilizationResolutionTests --nologo -v:minimal
```

Expected: compile failure because the new engine types and fixture are not yet present. Do not proceed if the failure is caused by a test typo or unrelated baseline failure.

### Task 2: Implement the pure structured health stabilization engine

**Files:**
- Create: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/HealthStabilizationResolution.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/HpDamageResolution.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/MultiplayerGameState.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/HpDamageResolutionTests.cs`

**Interfaces:**
- Consumes: explicit `DyingRoundInput`/`FirstAidInput`, `CharacterHealthState`, existing HP event semantics.
- Produces: `IHealthStabilizationEngine`, `CocHealthStabilizationEngine`, structured `DyingEpisode`, `StabilizedCondition`, `DeadCondition`, and `TreatmentRecord` state.

- [ ] **Step 1: Add focused unit tests for reference boundaries before implementation.**

Add tests that directly call the wished-for pure engine API and assert:

```csharp
var result = engine.ResolveDyingRound(
    dyingState,
    new DyingRoundInput(60, "dying-1", fixedTime));

Assert.Equal(1, result.Record!.Ordinal);
Assert.Equal(60, result.Record.Target);
Assert.True(result.Record.Success);
Assert.NotNull(result.State.DyingEpisode);
Assert.Null(result.State.Stabilized);
```

Also add tests for roll 61 failure/dead, second success ordinal 2, First Aid target/range/within-hour rejection, +1 HP capped at max, unconscious wake, dying stabilization, Major Wound retention, failed First Aid treatment record without healing, dead rejection, and no-dying rejection.

- [ ] **Step 2: Run the focused unit tests and verify they fail for missing implementation.**

Run the same focused `dotnet test` filter. Expected: compile failures for missing domain types/methods, not assertion failures caused by an incorrect fixture.

- [ ] **Step 3: Implement the smallest pure engine.**

Implement `CocHealthStabilizationEngine` with these rules:

```csharp
var episode = state.DyingEpisode
    ?? throw new HealthStabilizationRuleException(HealthStabilizationError.NoActiveDying);

var ordinal = episode.Checks.Count + 1;
var target = state.Con;
var success = input.Roll <= target;
```

Validate roll 1..100, target 1..100 where the reference requires a valid CON/First Aid target, `WithinHour == true`, active condition prerequisites, and dead rejection. On dying failure, create the `DeadConditionState` with `Reason = "dying_con_failure"`, return the resolving record, and clear `DyingEpisode`, `Unconscious`, and `Stabilized`. On dying success, append only to the active episode, set `NextRoundOrdinal = ordinal + 1`, and retain dying without stabilization. On First Aid success, set `CurrentHp = min(MaxHp, CurrentHp + 1)`, clear Unconscious, create Stabilized only when `WasDying`, clear the dying episode, and preserve Major Wound. On First Aid failure, preserve HP/conditions and append the treatment record. Never use current wall-clock time or random APIs.

- [ ] **Step 4: Make HP damage clear stale stabilization in the domain transition.**

When `CocHpDamageEngine` applies a new, non-deduplicated event that produces a fresh active dying or dead transition, its returned structured health state must have `Stabilized = null`. Preserve stabilized state for unrelated damage that does not create fresh dying/dead. Keep existing HP thresholds, event dedupe, event history limit 80, and HP fixture semantics unchanged.

- [ ] **Step 5: Run the focused unit tests and existing HP tests.**

Run:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~HealthStabilizationResolutionTests|FullyQualifiedName~HpDamageResolutionTests" --nologo -v:minimal
```

Expected: new pure-engine tests pass and all existing HP conformance tests pass.

### Task 3: Build the real JS stabilization exporter and committed fixture

**Files:**
- Create: `multiplayer/server/tests/Fixtures/export-health-stabilization-conformance.js`
- Create: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/health-stabilization.json`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/HealthStabilizationResolutionTests.cs` to match the emitted schema.

**Interfaces:**
- Consumes: actual `src/hp-damage-state.js` and `src/health-stabilization.js` through Node VM plus the minimum shared Single Player runtime dependencies used by `build/test-v166-health-stabilization.js`.
- Produces: deterministic JSON with `version`, `referenceSources`, `cases`, initial state, ordered operations, expected final state, and semantic result/error data.

- [ ] **Step 1: Copy the existing VM harness pattern and expose only actual reference calls.**

The exporter must create a sandbox with the same state/document/storage shims used by the v1.6.6 regression, load the actual source files as UTF-8, and expose helpers that call `healthStabilizationResolveDyingRound`, `healthStabilizationResolveFirstAid`, and the actual HP event path. It must not contain a second implementation of ordinal derivation, CON target selection, stabilization transitions, or HP invalidation.

- [ ] **Step 2: Add the required cases.**

Use fixed character/time/roll inputs and include at least:

```text
dying-baseline
dying-roll-equals-con
dying-roll-just-above-con
dying-two-successive-rounds
dying-failure-death
dying-already-dead-rejected
dying-not-active-rejected
first-aid-within-hour-rejected
first-aid-target-range-rejected
first-aid-injured-success
first-aid-injured-failure
first-aid-max-hp-cap
first-aid-unconscious-wake
first-aid-dying-stabilizes
first-aid-dying-failure
first-aid-stabilized-cannot-dying-round
first-aid-dead-rejected
damage-dying-con-success-sequence
damage-dying-first-aid-sequence
damage-stabilized-fresh-damage-invalidates
damage-dying-con-failure-sequence
```

For rejected reference operations, export a stable semantic error kind and do not compare localized message text. For active dying success, export the resolving check record separately from the final state so the fixture proves episode-local history is cleared only when the reference clears dying.

- [ ] **Step 3: Run the exporter twice and verify byte determinism.**

Run:

```powershell
node multiplayer/server/tests/Fixtures/export-health-stabilization-conformance.js
$first = (Get-FileHash multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/health-stabilization.json -Algorithm SHA256).Hash
node multiplayer/server/tests/Fixtures/export-health-stabilization-conformance.js
$second = (Get-FileHash multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/health-stabilization.json -Algorithm SHA256).Hash
Write-Output "SHA1=$first"
Write-Output "SHA2=$second"
if ($first -ne $second) { exit 1 }
```

Expected: exporter reports all cases, both hashes are identical, and the fixture has no incidental current-time values.

- [ ] **Step 4: Run C# fixture conformance.**

Run the focused health stabilization test. Expected: every emitted case passes, including all sequence final states and resolving-record semantics.

### Task 4: Correct HP production RNG and finish Commit 1

**Files:**
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs` only if the internal forced-roll property needs a precise `ConRollOverride` name.
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs` and `GameApiTests.cs` for injected dice assertions.

**Interfaces:**
- Consumes: existing `IDiceRoller.RollPercentile(0, 0)`.
- Produces: unchanged internal HP behavior with secure production fallback and unchanged no-public-damage-route boundary.

- [ ] **Step 1: Add a failing test proving the injected dice roller is used when no forced CON roll exists.**

Construct `GameCoordinator` with a deterministic fake `IDiceRoller` returning a known percentile result, apply a Major Wound damage command without a forced roll, and assert the stored HP damage event contains that roll. Add a second assertion that an explicit internal forced roll still wins in test code.

- [ ] **Step 2: Run the focused test and verify the old `Random.Shared` path fails the assertion.**

Run the game-state test filter. Expected: the result does not contain the fake roller value until the production fallback is changed.

- [ ] **Step 3: Replace only the fallback.**

Change the `ApplyDamageCore` fallback from:

```csharp
command.ConRoll ?? Random.Shared.Next(1, 101)
```

to:

```csharp
command.ConRoll ?? diceRoller.RollPercentile(0, 0).SelectedRoll
```

Keep forced rolls internal to coordinator/test construction and do not add a route or client request type.

- [ ] **Step 4: Run Commit 1 validation.**

Run restore, build, all server tests, format verification, both client checks/build, all Check/HP/Stabilization conformance, exporter determinism, Single Player regressions/syntax/build/verifier/double-build, formal SHA comparison, `git diff --check`, and confirm only Commit 1 files plus the already committed design/plan docs are changed. Do not modify Vue in this commit.

- [ ] **Step 5: Commit and push Commit 1.**

Use:

```powershell
git add multiplayer/server/src multiplayer/server/tests
git diff --cached --check
git commit -m "feat: migrate health stabilization rules"
git push origin main
git rev-parse HEAD
git rev-parse origin/main
git rev-list --left-right --count origin/main...HEAD
```

Expected: local and remote SHA equal and divergence `0 0`. Record the actual Commit 1 SHA before starting Commit 2.

## Commit 2 — Canonical Stabilization Integration

### Task 5: Add internal GameCoordinator stabilization transitions

**Files:**
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/IGameCoordinator.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/GameApi.cs` only to verify that no new route is mapped; do not add a route.
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs` and `GameApiTests.cs`.

**Interfaces:**
- Consumes: `ResolveDyingRoundCommand(Guid RoomId, Guid CharacterId, string? SourceId)`, `ResolveFirstAidCommand(Guid RoomId, Guid CharacterId, int Target, bool WithinHour, string? SourceId)`, `IHealthStabilizationEngine`, `IDiceRoller`.
- Produces: `ResolveDyingRoundAsync` and `ResolveFirstAidAsync` on `IGameCoordinator`, callable only by server code/tests, returning a projection plus semantic record.

- [ ] **Step 1: Write failing coordinator tests.**

Cover dying success/failure, repeated success ordinal, First Aid injured success/failure, dying First Aid success, dead/invalid prerequisites, treatment-history revision semantics, and stale stabilization invalidation after fresh damage. Assert every successful canonical transition increments revision exactly once, invalid operations do not, and all state replacement occurs inside the existing room lock.

- [ ] **Step 2: Run the focused tests and verify they fail because the internal methods are absent.**

Run the GameStateTests filter and confirm compile failure is the expected missing-method failure.

- [ ] **Step 3: Implement the two internal application flows.**

For dying round:

```text
WithRoomLock(roomId)
  -> member/game/character lookup
  -> validate active unstabilized dying and non-dead
  -> diceRoller.RollPercentile(0, 0).SelectedRoll
  -> healthStabilizationEngine.ResolveDyingRound(character.Health, input)
  -> replace only the character health in a new MultiplayerGameState
  -> revision + 1
  -> TryReplace
  -> build owner-safe snapshot
  -> publish snapshot only after commit
```

For First Aid, validate the internal `Target` and `WithinHour` inputs before calling the pure engine, use the same server roller, and apply the same commit/revision/broadcast sequence. Do not accept `roll` in either command. Return the resolving record in the internal result while exposing only the safe snapshot to clients.

- [ ] **Step 4: Verify public route absence.**

Add/retain a route-surface test or source assertion proving `GameApi.MapGameEndpoints` contains only the existing initialize/get/check routes for game gameplay; no stabilization endpoint is present. Run `rg -n "dying-round|first-aid|stabilize|ResolveDyingRound" multiplayer/server/src/Trpg.Multiplayer.Api/GameApi.cs` and verify only internal symbols are outside route mapping.

- [ ] **Step 5: Run focused server integration tests.**

Expected: canonical transitions, revisions, invalid state behavior, cross-room isolation, and concurrent damage-vs-stabilization operations pass with no lost update.

### Task 6: Extend projection, realtime, and reconnect safely

**Files:**
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameProjection.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs` only for projection DTOs if needed.
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/SignalRGameDeliveryTests.cs`

**Interfaces:**
- Consumes: canonical structured health state and existing `GameProjection.Build(state, viewerPlayerId)`/`IGameRealtimeNotifier`.
- Produces: owner-only projected `Stabilized` status and no raw dying checks, rolls, targets, treatment history, source IDs, event keys, or provenance.

- [ ] **Step 1: Write failing projection/realtime tests.**

Assert the owner receives HP plus `stabilized`, `dying`, `dead`, and `unconscious` status booleans; a non-owner receives `Health = null`; serialized snapshots contain none of `DyingCheckRecord`, `TreatmentRecord`, `Roll`, `Target`, `SourceId`, `EventKey`, or internal reason fields. Attach A/B, perform an internal mutation, assert both receive the latest allowed snapshot and the other room receives nothing. Disconnect the owner, mutate state internally, reattach the same session, and assert the latest stabilization projection is recovered.

- [ ] **Step 2: Run tests and verify red before projection changes.**

Run the focused GameStateTests and SignalRGameDeliveryTests filters. Expected: new status assertions fail because the DTO does not yet contain stabilized state.

- [ ] **Step 3: Add only the simplified projection field.**

Extend `CharacterHealthSnapshot` with `bool Stabilized`. Build it only for the owner from canonical `Health.Stabilized != null`; keep non-owner `Health = null`. Do not serialize canonical records or add a new SignalR event. Existing CheckValues privacy must remain unchanged.

- [ ] **Step 4: Run focused realtime/reconnect tests.**

Expected: commit-before-broadcast, revision monotonicity, owner-only details, cross-room isolation, and reconnect recovery all pass.

### Task 7: Add read-only Vue status display and client regression tests

**Files:**
- Modify: `multiplayer/client/src/contracts/rooms.ts`
- Modify: `multiplayer/client/src/components/LobbyView.vue`
- Modify: `multiplayer/client/src/components/HomeView.test.ts`

**Interfaces:**
- Consumes: `CharacterHealthSnapshot.stabilized` from server projection.
- Produces: text-only status rendering; no action request or local rule function.

- [ ] **Step 1: Add failing Vue tests.**

Add snapshots with `stabilized: true`, `dying: true`, `dead: true`, and `unconscious: true`; assert the exact server-projected labels render. Add a projection-mismatch test where HP is positive while `stabilized` is true and assert the label still renders, proving Vue does not infer from HP. Assert no button text matches `First Aid|Dying Round|Stabilize|Heal|Medicine|Damage|Kill`, and no API method is called.

- [ ] **Step 2: Run the focused client test and verify the new assertions fail.**

Run:

```powershell
npm test -- --run src/components/HomeView.test.ts
```

Expected: missing `stabilized` projection/label failure.

- [ ] **Step 3: Implement the read-only field.**

Add `stabilized: boolean` to the TypeScript contract and render:

```vue
<span v-if="character.health.stabilized"> · STABILIZED</span>
```

Use only the existing `character.health` projection. Do not add `ref` state, dice, API methods, action handlers, or HP-derived condition logic.

- [ ] **Step 4: Run client tests and build.**

Expected: all client tests pass and `npm run build` succeeds.

### Task 8: Update dynamic documentation and finish Commit 2

**Files:**
- Modify: `docs/CURRENT_STATE.md`
- Modify: `docs/HANDOFF.md`
- Modify: `docs/ARCHITECTURE.md`

**Interfaces:**
- Consumes: final actual class/method names, validation counts, commit SHAs, and projection behavior.
- Produces: top-level current phase consistency and documented future Team Status Visibility policy.

- [ ] **Step 1: Write documentation assertions/checklist before editing.**

Use `rg -n "Current Phase|Phase 2B|Phase 2D|Phase 2E|AlwaysVisible|Contextual|Last Known Status|PlayerKnowledgeState" docs/CURRENT_STATE.md docs/HANDOFF.md docs/ARCHITECTURE.md`. The plan is satisfied only when top-level stale Phase 2B text is removed/replaced, while historical Phase 2B references remain clearly historical.

- [ ] **Step 2: Make minimal UTF-8 documentation edits.**

Set current phase to `Phase 2E Health Stabilization completed` at the top of CURRENT_STATE/HANDOFF. Record completed JS→C# stabilization conformance, structured canonical episode/treatment state, internal transitions, owner-safe projection, realtime/reconnect, read-only Vue display, and the HP `IDiceRoller` fallback correction. Record that detailed health remains owner-only for Phase 2E, while future AlwaysVisible/Contextual/Last Known Status/PlayerKnowledgeState are documented only; Location/Communication/knowledge runtime are not implemented.

- [ ] **Step 3: Run the complete Commit 2 validation.**

Run, in order:

```powershell
dotnet restore multiplayer/server/Trpg.Multiplayer.slnx --nologo
dotnet build multiplayer/server/Trpg.Multiplayer.slnx --no-restore --nologo -v:minimal
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --nologo -v:minimal
dotnet format multiplayer/server/Trpg.Multiplayer.slnx --verify-no-changes --no-restore --verbosity minimal
```

Then run client `npm ci`, `npm test -- --run`, and `npm run build`; Check 19/19, all HP and stabilization conformance; exporter twice with identical SHA; Single Player 37/37, 69/69 syntax, build, verifier, deterministic double-build, one HTML output, unchanged formal SHA; `git diff --check`; and UTF-8/documentation searches. Record exact output counts.

- [ ] **Step 4: Review the final diff against scope.**

Confirm Commit 2 changes only server/client/tests/docs required for stabilization, never `src/`, `build/`, `outputs/`, Healing, SAN, Combat, Scenario, AI, Location, Communication, DB, Redis, or a public stabilization route. Confirm no canonical health state is directly serialized to non-owners and no Vue action control exists.

- [ ] **Step 5: Commit and push Commit 2.**

Use:

```powershell
git add multiplayer/server/src multiplayer/server/tests multiplayer/client/src docs/CURRENT_STATE.md docs/HANDOFF.md docs/ARCHITECTURE.md
git diff --cached --check
git commit -m "feat: integrate multiplayer health stabilization"
git push origin main
git status -sb
git rev-parse HEAD
git rev-parse origin/main
git rev-list --left-right --count origin/main...HEAD
```

Expected: clean workspace, local SHA equals `origin/main`, divergence `0 0`. After this verification, stop. The final report recommends exactly one of Healing Recovery or Combat Opposed and does not implement it.

## Final handoff facts to record

- Starting SHA and pre-feature design-doc SHA.
- Commit 1 and Commit 2 full SHAs/messages.
- Stabilization fixture case count and two identical SHA values.
- Server/client/test/build counts and warnings/errors.
- Formal HTML SHA and unchanged status.
- Public dying-round endpoint: `NO`.
- Public First Aid endpoint: `NO`.
- Player-controlled time/round/roll/target authority: `NO`.
- Fresh damage invalidates stale stabilization: `YES`.
- Future team visibility policy documented: `YES`; runtime implementation: `NO`.
- Healing/SAN/Combat/Scenario/AI/Location/Communication/DB/Redis: deferred.
