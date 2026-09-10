# Multiplayer Phase 2I NPC Attacker Driver Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete an already-active Multiplayer Combat loop when the current actor is an unowned NPC opponent, using deterministic server-internal Attack-or-Pass selection while preserving human defender authority and all existing canonical transition rules.

**Architecture:** `PlayerCombatIntentCoordinator` retains the latest state returned by successful canonical player transitions and delegates it to `ICombatContinuationCoordinator`. The continuation validates canonical structure, uses a pure `NpcCombatTurnDriver`, and invokes narrow NPC Begin/Pass methods implemented by the same singleton `GameCoordinator`; each canonical wrapper independently locks, commits, increments one revision, and publishes once.

**Tech Stack:** .NET 8 / ASP.NET Core minimal API, C# records and internal interfaces, xUnit, SignalR integration tests, Vue 3 / TypeScript compatibility validation, PowerShell validation scripts.

## Global Constraints

- Start from approved design commit plus its boundary correction. Preserve every existing uncommitted change; never reset, stash, revert, amend, rebase, or force-push.
- TDD is mandatory. Add the named test first, run the exact RED command, record the actual failure, then make only the minimum implementation and run the exact GREEN command.
- Never break working behavior to manufacture RED. A feature RED must prove behavior genuinely absent at that task boundary; if a later task adds only regression coverage for intentionally earlier behavior, label it integration/regression GREEN-on-add rather than a feature RED.
- Every edited text file must decode with strict UTF-8. Stop before editing any authorized existing file that fails strict UTF-8.
- Keep public Combat routes exactly `melee-attack`, `respond`, and `pass`. Add no public NPC, Start, End, Damage, or ResolveDamage route.
- Do not add client NPC triggers, controls, timers, polling, calculations, or a second Combat state machine. `GameSnapshot` remains authoritative.
- Do not add direct store access to the player or continuation application layer; Host impersonation; `Guid.Empty`/nullable-player/trusted sentinels; public authority flags; AI; random targeting; attacker policies; Firearms; Impaling; weapon switching; movement/range; timeout/forfeit; automatic pending-damage recovery; Scenario; persistence; DB/Redis; queue/worker; revision collapse; publication suppression; whole-continuation retry; or rollback.
- `PlayerCombatIntentCoordinator` must finish with exactly three dependencies: `IGameCoordinator`, `IInternalCombatResolutionCoordinator`, and `ICombatContinuationCoordinator`.
- Continuation canonical input is transition-returned `MultiplayerGameState` only; no application-layer store/query recovery is permitted.
- `PlayerCombatIntentCoordinator`, `ICombatContinuationCoordinator`, and `NpcCombatTurnDriver` publish zero times. Only a canonical `GameCoordinator` transition wrapper publishes, exactly once after each successful changed transition.
- One HTTP-owned `RoomMutationDeliveryGate` lease surrounds the entire player intent plus synchronous continuation. No application coordinator reacquires it. Each canonical NPC transition uses only the existing private per-room `GameCoordinator` lock.
- Treat malformed canonical state and pending damage as `NpcCombatContinuationInvariantException`, with no new mutation. A Pass is legal only for a structurally valid empty legal-action set.
- Earlier commits remain canonical after later failure. Never roll back, refresh-and-retry, reroll, or retry the whole continuation.

## Planned File Structure

- `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs`: internal NPC commands, continuation outcome, and dedicated invariant exception.
- `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/IGameCoordinator.cs`: narrow `IInternalNpcCombatTurnExecutor` and `ICombatContinuationCoordinator` contracts.
- `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs`: shared Begin/Pass mutation cores and same-instance NPC wrappers.
- `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/NpcCombatContinuation.cs`: structural validator, pure deterministic selector, and bounded iterative coordinator.
- `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/PlayerCombatIntentCoordinator.cs`: latest-state retention and delegation only.
- `multiplayer/server/src/Trpg.Multiplayer.Api/GameApi.cs`: exact dedicated invariant catch only; no route additions.
- `multiplayer/server/src/Trpg.Multiplayer.Api/Program.cs`: singleton alias registrations that resolve executor to the same `GameCoordinator` instance.
- `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`: player compatibility plus NPC executor authority/commit tests.
- `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/NpcCombatTurnDriverTests.cs`: pure validation/selection tests.
- `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/CombatContinuationCoordinatorTests.cs`: continuation, bound, failure, and concurrency tests.
- `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/PlayerCombatIntentCoordinatorTests.cs`: exact dependency and latest-state integration tests.
- `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameApiTests.cs`: dedicated safe error and route-count tests.
- `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/SignalRGameDeliveryTests.cs`: ordered per-commit publication/privacy tests.
- `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/DisconnectReconnectTests.cs`: disconnected-target and reconnect neutrality tests.
- `.superpowers/sdd/phase-2i-task-1-report.md` through `.superpowers/sdd/phase-2i-task-10-report.md`: exact per-task RED/GREEN evidence.
- `.superpowers/sdd/phase-2i-implementation-report.md`, `docs/CURRENT_STATE.md`, and `docs/HANDOFF.md`: aggregate factual evidence only in Task 10.

---

### Task 1: Split Player Begin Authority from the Shared Canonical Mutation Core

**Files:**
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`
- Create: `.superpowers/sdd/phase-2i-task-1-report.md`

**Contracts:**
- Preserve: `BeginOpposedExchangeAsync(BeginOpposedExchangeCommand)` and its player/Host behavior.
- Create private context and helpers equivalent to `LoadBeginOpposedExchangeContext`, `ValidatePlayerBeginAuthority`, and `CommitBeginOpposedExchange`.
- The commit helper receives already validated canonical state/session/attacker/defender and alone creates `ExchangeId`, pending state, replacement, and result.

- [ ] **Step 1: Add failing boundary and compatibility tests.**

Add `InternalCombat_PlayerBeginUsesSeparatedAuthorityAndSharedMutationCore` and extend existing Begin coverage to assert unchanged member/owner/Host authorization, target validity, stale revision before `ExchangeId`, pending blocking, one revision, one pending exchange, and one publication. The boundary test reads `GameCoordinator.cs` and requires the named private authority/core split while forbidding a trusted flag or sentinel.

- [ ] **Step 2: Run RED.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.InternalCombat_PlayerBeginUsesSeparatedAuthorityAndSharedMutationCore|FullyQualifiedName~GameStateTests.InternalCombat_StartAndBeginEnforceAuthorizationRevisionAndPendingInvariants|FullyQualifiedName~GameStateTests.InternalCombat_BeginRejectsInvalidOrInactiveEnemyAndSnapshotsDistinctReadOnlyResponses" --nologo -v:minimal
```

Expected RED: the new boundary test fails because the shared Begin context/commit helpers do not exist; existing player behavior tests remain green.

- [ ] **Step 3: Extract the minimum shared core.**

Keep `TryGetMember` and player/Host ownership decisions in the player entry. Move common game/session, pending-damage, revision, pending-exchange, current actor, participant uniqueness, opposing-side, active, and response validation into the shared context loader. Move only exchange creation and replace into `CommitBeginOpposedExchange`. Do not change validation order visible to existing public/internal callers and do not duplicate the mutation body.

- [ ] **Step 4: Run GREEN and record evidence.**

Run the Step 2 command. Expected GREEN: all selected tests pass; the old player/Host contract, revision, pending, and publication behavior is unchanged. Run `git diff --check` and strict UTF-8 on the three task files.

**Invariants:** no NPC seam, no authorization weakening, no new route, no new publication owner.

**Commit:** `refactor: split combat begin authority from mutation`

**Escalate if:** preserving existing error precedence requires changing a public/internal error code, or the extraction would create a second state read/replace path.

---

### Task 2: Split Player Pass Authority from the Shared Canonical Mutation Core

**Files:**
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`
- Create: `.superpowers/sdd/phase-2i-task-2-report.md`

**Contracts:**
- Preserve: `PassCombatTurnAsync(PassCombatTurnCommand)` and historical owner/Host control.
- Create private helpers equivalent to `LoadPassCombatTurnContext`, `ValidatePlayerPassAuthority`, and `CommitPassCombatTurn`.
- `CommitPassCombatTurn` alone updates action count, invokes existing `AdvanceTurn`, and uses `ReplaceCombatState`.

- [ ] **Step 1: Add failing boundary and compatibility tests.**

Add `InternalCombat_PlayerPassUsesSeparatedAuthorityAndSharedMutationCore`; preserve owner/Host authorization, pending exchange/damage, stale revision, stable order, round wrap, dying schedule, one revision, and one publication assertions.

- [ ] **Step 2: Run RED.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.InternalCombat_PlayerPassUsesSeparatedAuthorityAndSharedMutationCore|FullyQualifiedName~GameStateTests.InternalCombat_PassAndEndMaintainPendingAndInactiveInvariantsWithoutDice|FullyQualifiedName~GameStateTests.CombatDamageGate" --nologo -v:minimal
```

Expected RED: the new boundary test fails because the shared Pass authority/mutation split is absent; existing Pass tests remain green.

- [ ] **Step 3: Extract the minimum shared core.**

Retain player membership and `CanControlActor` behavior in the player entry. Move common active Combat, damage/pending gates, exact revision, current unique active actor, action count, `AdvanceTurn`, and replace mechanics into the shared path. Do not alter round or dying logic.

- [ ] **Step 4: Run GREEN and record evidence.**

Run the Step 2 command. Expected GREEN: all selected tests pass with unchanged player/Host Pass semantics. Run task UTF-8 and `git diff --check`.

**Invariants:** Pass is one canonical transition; no NPC identity, no dice, no copied `AdvanceTurn` logic.

**Commit:** `refactor: split combat pass authority from mutation`

**Escalate if:** shared extraction changes round-wrap/dying scheduling or requires weakening `CanControlActor`.

---

### Task 3: Add the Same-Instance Narrow NPC Begin and Pass Executor

**Files:**
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/IGameCoordinator.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Program.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`
- Create: `.superpowers/sdd/phase-2i-task-3-report.md`

**Contracts:**

```csharp
internal sealed record BeginNpcOpposedExchangeCommand(
    Guid RoomId, long ExpectedGameRevision, string NpcParticipantId, string TargetParticipantId);
internal sealed record PassNpcCombatTurnCommand(
    Guid RoomId, long ExpectedGameRevision, string NpcParticipantId);
internal sealed class NpcCombatContinuationInvariantException(string message) : Exception(message);

internal interface IInternalNpcCombatTurnExecutor
{
    Task<GameResult<BeginOpposedExchangeResult>> BeginNpcOpposedExchangeAsync(BeginNpcOpposedExchangeCommand command);
    Task<GameResult<PassCombatTurnResult>> PassNpcCombatTurnAsync(PassNpcCombatTurnCommand command);
}
```

`GameCoordinator` implements this interface explicitly. DI registers `IInternalNpcCombatTurnExecutor` by returning `GetRequiredService<GameCoordinator>()`, guaranteeing the executor is the same canonical `GameCoordinator` instance.

- [ ] **Step 1: Add failing executor authority, mutation, and DI tests.**

Cover NPC Begin/Pass success for the exact current active unowned opponent; wrong actor; owned investigator; Host/player identity absence in command reflection; no trusted flag; stale revision before `ExchangeId`; pending exchange; pending damage; caller-supplied wrong/same-side/inactive target rejection; structurally malformed canonical actor/target state throwing the dedicated invariant before mutation; exactly one commit/revision/publication; one Pass transition; and `Assert.Same(GameCoordinator, IInternalNpcCombatTurnExecutor)`.

- [ ] **Step 2: Run RED.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.InternalNpcCombat" --nologo -v:minimal
```

Expected RED: compile/reflection failures report missing NPC commands/interface/methods; no existing test is weakened.

- [ ] **Step 3: Implement minimum executor wrappers.**

Each method calls `WithRoomLockAsync`, performs NPC authority validation against exact command revision/current actor and unique canonical participants, calls the Task 1/2 shared mutation core, publishes only when `IsSuccess && Changed`, and returns the committed state. NPC identity requires active `opponent/opponent`, null `CharacterId`, null `OwnerPlayerId`; Begin target requires unique active opposing `investigator`, non-null character/owner, canonical order membership, and valid response capability. Reject an invalid command as the existing narrow `GameResult` failure, but throw `NpcCombatContinuationInvariantException` when the loaded canonical state itself is structurally contradictory; both paths mutate and publish zero times.

- [ ] **Step 4: Run GREEN and record evidence.**

Run the Step 2 command plus:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.InternalCombat_StartAndBegin|FullyQualifiedName~GameStateTests.InternalCombat_Begin|FullyQualifiedName~GameStateTests.InternalCombat_Pass|FullyQualifiedName~GameStateTests.InternalNpcCombat" --nologo -v:minimal
```

Expected GREEN: executor and compatibility selections pass. Strict UTF-8 and `git diff --check` pass.

**Invariants:** no PlayerId/Host/trusted field; same singleton, lock, store, replace, revision, and publication wrapper; no second engine/repository.

**Commit:** `feat: add internal npc combat executor`

**Escalate if:** DI resolves a second `GameCoordinator`, a command needs caller-controlled trust, or NPC authority cannot be proven before mutation.

---

### Task 4: Add Structural Validation and the Pure Deterministic Selector

**Files:**
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs`
- Create: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/NpcCombatContinuation.cs`
- Create: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/NpcCombatTurnDriverTests.cs`
- Create: `.superpowers/sdd/phase-2i-task-4-report.md`

**Contracts:**

```csharp
internal enum NpcCombatActionKind { BeginOpposedExchange, Pass }
internal sealed record NpcCombatTurnDecision(
    NpcCombatActionKind Kind, string NpcParticipantId, string? TargetParticipantId);
internal sealed record ValidatedNpcCombatTurn(
    MultiplayerGameState State,
    CombatSession Session,
    CombatParticipantState CurrentNpc,
    IReadOnlyList<CombatParticipantState> LegalTargets);
internal static class CombatContinuationStateValidator
{
    internal static ValidatedNpcCombatTurn ValidateNpcTurn(MultiplayerGameState state);
}
internal sealed class NpcCombatTurnDriver
{
    internal NpcCombatTurnDecision Select(ValidatedNpcCombatTurn turn);
}
```

- [ ] **Step 1: Write failing pure tests.**

Test stable first valid investigator by `Combat.Order`; inactive structurally valid participants skipped; dictionary/list enumeration cannot alter target; disconnected owner remains eligible without a connection dependency; a manually supplied already-validated empty legal-action set selects Pass; duplicate/missing order participant, invalid `TurnIndex`, malformed current actor, character/owner on NPC, side/kind contradiction, malformed active investigator, active Combat with no active investigator after side defeat, and inconsistent target relation throw the dedicated invariant before a validated decision is produced; input state remains reference/value unchanged; repeated selection is identical and uses no RNG.

- [ ] **Step 2: Run RED.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter FullyQualifiedName~NpcCombatTurnDriverTests --nologo -v:minimal
```

Expected RED: compile failures identify the missing validator/decision/driver/exception.

- [ ] **Step 3: Implement validation-before-selection.**

Validate canonical shape first, derive legal targets by iterating `Session.Order`, then select index zero or Pass. Do not enumerate a dictionary as authority. Do not mutate, clone into a new canonical state, read a store, inspect connections, call dice/rules/AI, or convert malformed state to Pass.

- [ ] **Step 4: Run GREEN and record evidence.**

Run the Step 2 command. Expected GREEN: all pure tests pass deterministically. Run strict UTF-8, `git diff --check`, and a source scan proving absence of store/dice/AI/random/notifier references.

**Invariants:** `State → ValidateContinuationState → legal set → decision`; malformed state means exception and zero mutation.

**Commit:** `feat: add deterministic npc combat selector`

**Escalate if:** the current canonical model cannot distinguish corruption from a supported empty legal-action set without a new gameplay rule.

---

### Task 5: Add the Bounded Iterative Combat Continuation Coordinator

**Files:**
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/IGameCoordinator.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/NpcCombatContinuation.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Program.cs`
- Create: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/CombatContinuationCoordinatorTests.cs`
- Create: `.superpowers/sdd/phase-2i-task-5-report.md`

**Contracts:**

```csharp
internal enum CombatContinuationStatus { NoChange, WaitingForPlayerResponse, CombatEnded, Advanced }
internal sealed record CombatContinuationOutcome(
    MultiplayerGameState State, CombatContinuationStatus Status, int AutomaticActions);
internal interface ICombatContinuationCoordinator
{
    Task<CombatContinuationOutcome> ContinueAsync(MultiplayerGameState entryState);
}
internal sealed class CombatContinuationCoordinator(
    IInternalNpcCombatTurnExecutor executor,
    NpcCombatTurnDriver driver) : ICombatContinuationCoordinator;
```

Register `NpcCombatTurnDriver` as a singleton and `ICombatContinuationCoordinator` as the singleton `CombatContinuationCoordinator`; neither registration may resolve a store, notifier, gate, or second `GameCoordinator`.

- [ ] **Step 1: Write failing coordinator tests.**

Cover inactive = `CombatEnded`/zero calls; pending exchange = `WaitingForPlayerResponse`/zero calls; human current = `NoChange`; pending damage = exception/zero calls; NPC+target = one Begin using the exact entry revision/actor/target, then uses the exact returned state and stops at its pending exchange; maximum successful automatic actions is defensively bounded by entry `Order.Count` and the `Count+1` action is never invoked; iteration not recursion; unexpected executor failure throws; no store/query/dice/engine/notifier/Hub/connection/AI/gate dependency. Do not manufacture a runtime NPC Pass chain: under current valid two-side melee rules, the validated current NPC always has a legal investigator target.

- [ ] **Step 2: Run RED.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter FullyQualifiedName~CombatContinuationCoordinatorTests --nologo -v:minimal
```

Expected RED: missing continuation contract/coordinator/status types.

- [ ] **Step 3: Implement the minimum iterative loop.**

Capture `maxActions = entryState.Combat?.Order.Count ?? 0`; examine only the current returned state. At entry and after every returned state, check pending damage first and fail closed. Then stop at inactive Combat, pending human exchange, or human actor. Validate every active continuation state before selection. Before invoking another executor action, require `automaticActions < maxActions`; invoke Begin or Pass with the current state's exact revision/actor/target; increment count only after a successful changed action; replace local state with `result.Value.State`; never refresh or retry. After a successful current-rule Begin, require the returned active Combat state to contain the exact canonical pending exchange for the selected NPC attacker and investigator defender; otherwise throw the dedicated invariant rather than continuing. Keep the Pass branch for a future validated legal empty-action rule, but do not claim it is currently reachable.

- [ ] **Step 4: Run GREEN and record evidence.**

Run the Step 2 command. Expected GREEN: all stop, defensive bound, dependency, and Begin-returned-state tests pass. The bound coverage is structural/control-flow only because exhaustion is unreachable under the current valid runtime policy. Strict UTF-8 and `git diff --check` pass.

**Invariants:** exactly two coordinator dependencies; zero publication; no outer gate; no private lock; no recursion; no pending-damage recovery.

**Commit:** `feat: add bounded npc combat continuation`

**Escalate if:** a valid active two-side state can require more than `entryState.Combat.Order.Count` automatic NPC commits before reaching a human/end boundary.

---

### Task 6: Prepare Latest-State Retention and Add the Third Player Coordinator Dependency

**Files:**
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/PlayerCombatIntentCoordinator.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Program.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/PlayerCombatIntentCoordinatorTests.cs`
- Create: `.superpowers/sdd/phase-2i-task-6-report.md`

**Contracts:**

```csharp
internal sealed record PlayerCombatTransitionOutcome(
    MultiplayerGameState LastCommittedState,
    PlayerCombatIntentResult? Failure);

internal sealed class PlayerCombatIntentCoordinator(
    IGameCoordinator games,
    IInternalCombatResolutionCoordinator combat,
    ICombatContinuationCoordinator continuation);
```

`ResolveExactPendingDamageAsync` returns the Resolve state when no Damage follows, the Damage state when Damage commits, and the last committed state plus mapped failure when Damage fails. Pass state is retained in the same narrow application form for later delegation.

Task 6 creates the dependency and state-retention seam only. It must not call `ICombatContinuationCoordinator.ContinueAsync` from any production Pass, Melee, or Respond success path; existing Phase 2H public behavior remains unchanged.

- [ ] **Step 1: Add failing latest-state and dependency tests.**

Require exactly the three constructor dependencies in exact order; forbid every listed direct dependency category. Assert the private/application helpers retain the exact `PassCombatTurnResult.State`, no-Damage `ResolvePendingExchangeResult.State`, and Damage `ResolveCombatDamageResult.State` without reconstruction or a state query. Assert all public Pass/Melee/Respond success paths invoke the continuation fake zero times, while existing Phase 2H behavior remains green.

- [ ] **Step 2: Run RED.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~PlayerCombatIntentCoordinatorTests.PlayerCombatIntentCoordinator_HasOnlyApprovedStateAndTransitionDependencies|FullyQualifiedName~PlayerCombatIntentCoordinatorTests.LatestState|FullyQualifiedName~PlayerCombatIntentCoordinatorTests.HumanDefenderBegin" --nologo -v:minimal
```

Expected RED: constructor count is two, the latest-state outcome/helper is absent, and the successful Damage helper discards the final committed state. This RED must not require actual NPC continuation.

- [ ] **Step 3: Implement delegation-only state retention.**

Add only `ICombatContinuationCoordinator`. Refactor the damage helper to return `PlayerCombatTransitionOutcome`, and retain successful Pass/Resolve/Damage state in the narrow private/application helper needed by Tasks 7 and 8. Do not call `ContinueAsync` yet. Do not put NPC selection, target, loop, authority, dice, damage, or turn logic in this class.

- [ ] **Step 4: Run GREEN and record evidence.**

Run the Step 2 command and all `PlayerCombatIntentCoordinatorTests`. Expected GREEN: the dependency/state-retention seam passes, the continuation fake records zero public-success calls, and Phase 2H behavior is unchanged. Run strict UTF-8 and `git diff --check`.

**Invariants:** constructor dependency count exactly three; continuation call count is zero in Task 6 public flows; application publications zero; no store recovery; no extra public intent.

**Commit:** `refactor: retain combat state for continuation`

**Escalate if:** retaining latest state requires changing a public response DTO or adding an application state query.

---

### Task 7: Integrate Player Pass into NPC Continuation

**Files:**
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/PlayerCombatIntentCoordinator.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/PlayerCombatIntentCoordinatorTests.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/SignalRGameDeliveryTests.cs`
- Create: `.superpowers/sdd/phase-2i-task-7-report.md`

**Contracts:** This is the first production continuation call site. Player Pass commits `N→N+1`, then continuation consumes exactly that returned state once. If NPC attacks, Begin commits/publishes `N+1→N+2`; final HTTP projection is revision `N+2` and application layers publish zero times.

- [ ] **Step 1: Add failing Pass integration tests.**

Add `Pass_LatestCommittedStateContinuesNpcTurn`, `Pass_NpcBeginPreMutationFailurePreservesCommittedPlayerPassWithoutRetry`, and realtime `PlayerPassThenNpcBegin_PublishesNPlusOneThenNPlusTwoExactlyOnce`. Assert ordered viewer-specific payloads, nonparticipant `Combat=null`, no raw policy/roll/stats/registry/schedule/history/source/provenance, and no duplicate final application publication. Do not require an unreachable runtime `NpcPassThenNpcBegin` sequence; the Task 3 lower-layer NPC Pass executor coverage remains authoritative.

- [ ] **Step 2: Run RED.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~PlayerCombatIntentCoordinatorTests.Pass_|FullyQualifiedName~SignalRGameDeliveryTests.PlayerPassThenNpc" --nologo -v:minimal
```

Expected RED: Pass commits, the continuation fake invocation count is zero, no NPC Begin/Pass follows, and no `N+2` NPC publication exists.

- [ ] **Step 3: Complete only the Pass call site.**

Forward the exact `passed.Value.State` once to continuation before the final fresh viewer projection. Preserve partial Pass state if continuation fails; do not catch and retry executor failure or publish from the application layer.

- [ ] **Step 4: Run GREEN and record evidence.**

Run Step 2. Expected GREEN: ordered two-revision publication and partial-commit tests pass. Run strict UTF-8 and `git diff --check`.

**Invariants:** one outer gate; one publish per canonical transition; Pass remains canonical after later failure.

**Commit:** `feat: continue npc combat after player pass`

**Escalate if:** publication order cannot be preserved without buffering/suppression or reacquiring `RoomMutationDeliveryGate`.

---

### Task 8: Integrate Melee and Respond Continuation Across Resolve and Damage

**Files:**
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/PlayerCombatIntentCoordinator.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/PlayerCombatIntentCoordinatorTests.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/SignalRGameDeliveryTests.cs`
- Create: `.superpowers/sdd/phase-2i-task-8-report.md`

**Contracts:** This task adds the remaining production continuation call sites. NPC-defender Melee and human Respond continue exactly once from the latest Resolve/Damage state. Human-defender Melee forwards the exact Begin state once; continuation observes `PendingExchange`, performs zero NPC executor mutation, and returns before the final fresh projection. NPC Begin against a human always stops before Resolve/Damage.

The final per-intent call matrix is:

```text
Pass → ContinueAsync once
Melee vs NPC defender → ContinueAsync once after latest Resolve/Damage
Melee vs human defender → ContinueAsync once with Begin State → pending stop
Respond → ContinueAsync once after latest Resolve/Damage
```

Neither the Task 6 helper nor the Damage helper invokes continuation internally. No player intent calls continuation before and after final projection.

- [ ] **Step 1: Add failing Melee/Respond integration tests.**

Cover NPC-defender Melee no-damage and Damage returned revisions; human Respond no-damage and Damage; Fight Back defeat/order repair uses Damage state with no duplicate wrap; human defender Begin stops; normal NPC-attacker Begin invokes no Resolve/Damage; partial Resolve then Damage failure leaves disposition; NPC Begin commit then later application/final-projection failure leaves exact `PendingExchange` and never replays Begin. Assert exactly one continuation call for each successful Melee/Respond path, the exact latest returned State argument, zero executor mutation for human-defender Begin, and no second continuation before or after final projection.

- [ ] **Step 2: Run RED.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~PlayerCombatIntentCoordinatorTests.MeleeAttack_|FullyQualifiedName~PlayerCombatIntentCoordinatorTests.Respond_|FullyQualifiedName~SignalRGameDeliveryTests.PlayerCombatContinuation" --nologo -v:minimal
```

Expected RED: after Task 7 only Pass is integrated; successful Melee/Respond flows still do not invoke continuation from the retained latest state, and the new continuation/publication assertions fail.

- [ ] **Step 3: Complete Melee/Respond call sites.**

Use the Task 6 helper result, return immediately on its error, otherwise call continuation exactly once with `LastCommittedState`, then request fresh projection. Leave canonical Resolve, Damage, Fight Back, defeat, turn repair, round, and dying behavior untouched.

- [ ] **Step 4: Run GREEN and record evidence.**

Run Step 2 and all coordinator/realtime Combat intent tests. Expected GREEN: latest-state source and stop-boundary tests pass; no duplicate publication/wrap/dice. Run strict UTF-8 and `git diff --check`.

**Invariants:** driver never resolves human response or damage; partial commits survive; no whole-flow retry/reroll.

**Commit:** `feat: continue npc combat after player resolution`

**Escalate if:** existing transition results do not identify the exact latest committed state or Fight Back requires new canonical rules.

---

### Task 9: Lock Error, Concurrency, Delivery, Disconnect, and Public-Surface Boundaries

**Files:**
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/GameApi.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameApiTests.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/CombatContinuationCoordinatorTests.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/SignalRGameDeliveryTests.cs`
- Modify: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/DisconnectReconnectTests.cs`
- Create: `.superpowers/sdd/phase-2i-task-9-report.md`

**Contracts:** `RunCombatIntentAsync` catches exactly `NpcCombatContinuationInvariantException` in addition to existing dedicated invariants and maps it to status 500 body `{ "code": "combat_consistency_failure" }`, with no revision/detail/retry hint. No `catch (Exception)`.

- [ ] **Step 1: Add failing safety tests.**

Cover two continuations with the same entry revision: one canonical Begin action, no second `ExchangeId`, loser does not refresh/retry; Player Pass commits then NPC Begin pre-mutation failure preserves the Player Pass; NPC Begin commits then later HTTP/application/final-projection failure preserves the pending exchange; Human Respond Resolve commits then Damage fails before commit preserves the resolved exchange and pending disposition; post-RNG Damage invariant remains non-retryable without reroll; a lost HTTP success retried with the old player revision is stale and replays neither player nor NPC work; defensive safety bound fails before action `Order.Count+1`; pending damage means zero action; disconnected active owner remains targetable, Begin creates pending, no auto-response; reconnect restores exact owner-only affordance without revision change; other viewers/nonparticipants remain safe; route enumeration stays exactly three; dedicated invariant wire response is minimal and generic catches absent. Do not fabricate an unreachable runtime NPC Pass partial-failure chain.

- [ ] **Step 2: Run RED.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~CombatContinuationCoordinatorTests.Concurrent|FullyQualifiedName~CombatContinuationCoordinatorTests.Partial|FullyQualifiedName~GameApiTests.NpcCombatContinuation|FullyQualifiedName~GameApiTests.CombatIntentRoutes|FullyQualifiedName~SignalRGameDeliveryTests.NpcCombat|FullyQualifiedName~DisconnectReconnectTests.NpcCombat" --nologo -v:minimal
```

Expected RED: dedicated exception mapping and new end-to-end concurrency/disconnect cases are absent.

- [ ] **Step 3: Add only the dedicated API catch and test seams.**

Add the exact catch through `CombatInvariantError`. Make no endpoint mapping changes. Use test fakes/barriers to trigger competing calls and failures; do not add production retry/dedupe/connection logic.

- [ ] **Step 4: Run GREEN and record evidence.**

Run Step 2 plus:

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~SignalRGameDeliveryTests|FullyQualifiedName~DisconnectReconnectTests|FullyQualifiedName~CombatContinuationCoordinatorTests|FullyQualifiedName~GameApiTests.CombatIntentRoutes" --nologo -v:minimal
```

Expected GREEN: all safety/delivery/reconnect tests pass; route count is three; pending survives reconnect and reconnect is revision-neutral. Run strict UTF-8 and `git diff --check`.

**Invariants:** no disconnect eligibility check, auto-response, retry, rollback, extra route, client trigger, raw/private realtime data, or generic catch.

**Commit:** `test: lock npc combat continuation boundaries`

**Escalate if:** concurrent same-revision execution can commit twice under canonical compare/replace, or privacy requires changing `GameProjection`/wire contracts.

---

### Task 10: Aggregate Validation and Factual Documentation

**Files:**
- Modify: `docs/CURRENT_STATE.md`
- Modify: `docs/HANDOFF.md`
- Create: `.superpowers/sdd/phase-2i-implementation-report.md`
- Create: `.superpowers/sdd/phase-2i-task-10-report.md`
- Inspect only: all Phase 2I production/test files listed above plus `multiplayer/client/src/`

**Contracts:** documentation records actual evidence only. No client or Single Player functionality changes. The locked Phase 2I baseline is `639216b3f80384f6889ecac4b2343bf6f7b32f87`. Every aggregate scope, UTF-8, and evidence inventory audit compares that committed baseline through `HEAD` plus current working-tree Phase 2I work; it must not infer phase scope from the final uncommitted diff alone. Formal HTML SHA remains `0A635D94CDD7284B35433092C834D92BCAD44961C14E30DBD565AB48B7E14D4D`.

- [ ] **Step 1: Run documentation RED before factual updates.**

```powershell
$phaseBaseline = '639216b3f80384f6889ecac4b2343bf6f7b32f87'
git cat-file -e "$phaseBaseline^{commit}"
if ($LASTEXITCODE -ne 0) { throw "Missing Phase 2I baseline commit: $phaseBaseline" }
git merge-base --is-ancestor $phaseBaseline HEAD
if ($LASTEXITCODE -ne 0) { throw "Phase 2I baseline is not an ancestor of HEAD: $phaseBaseline" }
$requiredReports = @('.superpowers/sdd/phase-2i-implementation-report.md','.superpowers/sdd/phase-2i-task-10-report.md')
foreach ($path in $requiredReports) { if (-not (Test-Path -LiteralPath $path)) { throw "Missing Phase 2I factual report: $path" } }
if ((Get-Content -Raw docs/CURRENT_STATE.md) -notmatch 'Phase 2I.*NPC Attacker Driver') { throw 'CURRENT_STATE lacks Phase 2I factual state' }
if ((Get-Content -Raw docs/HANDOFF.md) -notmatch 'Phase 2I.*NPC Attacker Driver') { throw 'HANDOFF lacks Phase 2I handoff' }
```

Expected RED: the aggregate reports do not yet exist and/or current factual docs do not record the implemented Phase 2I result.

- [ ] **Step 2: Run the focused aggregate server gate.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.InternalNpcCombat|FullyQualifiedName~NpcCombatTurnDriverTests|FullyQualifiedName~CombatContinuationCoordinatorTests|FullyQualifiedName~PlayerCombatIntentCoordinatorTests|FullyQualifiedName~GameApiTests.CombatIntentRoutes|FullyQualifiedName~SignalRGameDeliveryTests|FullyQualifiedName~DisconnectReconnectTests" --nologo -v:minimal
```

Expected: all Phase 2I focused tests pass. Any failure is an aggregate blocker; record its exact count/reason and correct only an implementation defect within Phase 2I scope before proceeding.

- [ ] **Step 3: Run clean client validation.**

```powershell
Push-Location multiplayer/client
npm ci
npm test -- --run
npm run build
Pop-Location
```

Expected: all client tests and build pass. Record exact test/file counts and modules transformed. No client file should differ.

- [ ] **Step 4: Run full server validation.**

```powershell
dotnet restore multiplayer/server/Trpg.Multiplayer.slnx --nologo
dotnet build multiplayer/server/Trpg.Multiplayer.slnx --no-restore --nologo -v:minimal
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --nologo -v:minimal
dotnet format multiplayer/server/Trpg.Multiplayer.slnx --verify-no-changes --no-restore --verbosity minimal
```

Expected: restore/build/full test/format pass. Record exact server passed/failed/skipped counts, warnings, errors, and format result.

- [ ] **Step 5: Verify conformance counts and Combat Damage determinism.**

```powershell
$expectedCases = @{
  'check-resolution.json' = 19
  'hp-damage.json' = 10
  'health-stabilization.json' = 21
  'combat-opposed.json' = 19
  'combat-damage.json' = 48
}
foreach ($entry in $expectedCases.GetEnumerator()) {
  $fixturePath = Join-Path 'multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures' $entry.Key
  $json = Get-Content -Raw -LiteralPath $fixturePath -Encoding UTF8 | ConvertFrom-Json
  if ($json.cases.Count -ne $entry.Value) { throw "$($entry.Key): expected $($entry.Value), got $($json.cases.Count)" }
}
node multiplayer/server/tests/Fixtures/export-combat-damage-conformance.js
$damageSha1 = (Get-FileHash multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/combat-damage.json -Algorithm SHA256).Hash
node multiplayer/server/tests/Fixtures/export-combat-damage-conformance.js
$damageSha2 = (Get-FileHash multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/combat-damage.json -Algorithm SHA256).Hash
$approvedCombatDamageSha = '0036133BF2BF1F37CBEF7DC7707832C258DE869870B46C17ADCA748FE905CEE7'
if ($damageSha1 -ne $damageSha2 -or $damageSha2 -ne $approvedCombatDamageSha) { throw "Combat Damage fixture SHA mismatch: $damageSha1 / $damageSha2" }
```

Expected: Check 19, HP 10, Stabilization 21, Combat Opposed 19, Combat Damage 48, and both exporter SHA values equal `0036133BF2BF1F37CBEF7DC7707832C258DE869870B46C17ADCA748FE905CEE7`.

- [ ] **Step 6: Run the exact authoritative Single Player regressions and legacy alias.**

```powershell
$regressions = @(
  'build/test-security-hardening.js','build/test-save-ui.js','build/test-coc-outcomes.js','build/test-situation-ui.js','build/test-ai-json-repair.js',
  'build/test-v150-experience.js','build/test-v151-investigation-stability.js','build/test-v152-long-session.js','build/test-v153-clue-routes.js',
  'build/test-v154-protocol-routing.js','build/test-v154-npc-materialization.js','build/test-v154-location-alias.js','build/test-v154-empty-response-retry.js','build/test-v154-operation-id-alias.js',
  'build/test-v155-location-transition-alias.js','build/test-v156-player-assertion-guard.js','build/test-v157-case-integrity.js','build/test-v157-canonical-assertion-state.js',
  'build/test-v158-api-response-resilience.js','build/test-v159-progress-semantics.js','build/test-v1510-authored-threat-clock.js','build/test-v1511-npc-knowledge-boundary.js',
  'build/test-v1512-ending-resolution-gate.js','build/test-v1513-full-case-e2e.js','build/test-v160-coc-resolution-engine.js','build/test-v160-coc-resolution-diagnostics.js',
  'build/test-v160-transport-abort-classification.js','build/test-v161-mechanical-consequence-contract.js','build/test-v162-failure-forward-cost-engine.js',
  'build/test-v163-san-loss-resolution.js','build/test-v164-indefinite-insanity-tracking.js','build/test-v165-hp-damage-state.js','build/test-v166-health-stabilization.js',
  'build/test-v167-healing-recovery.js','build/test-v168-combat-opposed.js','build/test-v169-combat-damage.js','build/test-v1610-firearms-impaling.js'
)
if ($regressions.Count -ne 37) { throw "Expected 37 regressions, got $($regressions.Count)" }
$workflowRegressions = @(
  Get-Content -LiteralPath '.github/workflows/trpg-ci.yml' -Encoding UTF8 | ForEach-Object {
    if ($_ -match '^\s*run:\s+node\s+(build/test-[^\s]+\.js)\s*$') { $Matches[1] }
  }
)
$credentialed = @('build/test-real-api-v1513.js','build/test-real-api-v152.js')
$workflowOfflineRegressions = @($workflowRegressions | Where-Object { $_ -notin $credentialed })
if ($workflowOfflineRegressions.Count -ne 37) { throw "Workflow authoritative offline count must be 37, got $($workflowOfflineRegressions.Count)" }
if (@(Compare-Object -ReferenceObject $regressions -DifferenceObject $workflowOfflineRegressions -SyncWindow 0).Count -ne 0) { throw 'Workflow offline regression sequence differs from the Phase 2I allowlist' }
if (@($workflowOfflineRegressions | Where-Object { $_ -in $credentialed }).Count -ne 0) { throw 'Credentialed real-API tests entered the offline workflow gate' }
Write-Host 'workflow authoritative count = 37'
Write-Host 'plan allowlist count = 37'
Write-Host 'sequence identical = YES'
foreach ($test in $regressions) { node $test; if ($LASTEXITCODE -ne 0) { throw "Failed: $test" } }
node build/test-protocol-stability.js
if ($LASTEXITCODE -ne 0) { throw 'legacy protocol-stability alias failed' }
```

Expected: workflow authoritative count 37, plan allowlist count 37, sequence identical YES, 37/37 authoritative offline regressions, and the legacy alias PASS. `build/test-real-api-v1513.js` and `build/test-real-api-v152.js` remain outside this gate and are not required.

- [ ] **Step 7: Run JavaScript syntax and formal artifact validation.**

```powershell
$phaseBaseline = '639216b3f80384f6889ecac4b2343bf6f7b32f87'
$scripts = @(Get-ChildItem src,build -Recurse -File -Filter '*.js' | Sort-Object FullName)
if ($scripts.Count -ne 69) { throw "Expected 69 JS files, got $($scripts.Count)" }
foreach ($script in $scripts) { node --check $script.FullName; if ($LASTEXITCODE -ne 0) { throw "Syntax failed: $($script.FullName)" } }
$approvedFormalSha = '0A635D94CDD7284B35433092C834D92BCAD44961C14E30DBD565AB48B7E14D4D'
$before = (Get-FileHash outputs/trpg-dm-assistant.html -Algorithm SHA256).Hash
if ($before -ne $approvedFormalSha) { throw "Unexpected formal baseline $before" }
node build/build-single-html.js
node build/verify-single-html.js
$first = (Get-FileHash outputs/trpg-dm-assistant.html -Algorithm SHA256).Hash
node build/build-single-html.js
$second = (Get-FileHash outputs/trpg-dm-assistant.html -Algorithm SHA256).Hash
if ($first -ne $second -or $second -ne $approvedFormalSha) { throw "Formal artifact changed: $first / $second" }
if (@(Get-ChildItem outputs -File -Filter '*.html').Count -ne 1) { throw 'Formal output inventory changed' }
git diff --exit-code "$phaseBaseline..HEAD" -- outputs
if ($LASTEXITCODE -ne 0) { throw 'Committed Phase 2I formal output diff detected' }
git diff --exit-code -- outputs/trpg-dm-assistant.html
if ($LASTEXITCODE -ne 0) { throw 'Working-tree formal output diff detected' }
```

Expected: 69/69 syntax, verifier PASS, one HTML, byte-identical double build, approved SHA unchanged.

- [ ] **Step 8: Run strict scope, dependency, route, UTF-8, and diff audit.**

```powershell
$phaseBaseline = '639216b3f80384f6889ecac4b2343bf6f7b32f87'
git diff --check
$committedWhitespace = git diff --check "$phaseBaseline..HEAD"
if ($LASTEXITCODE -ne 0) { throw "Committed Phase 2I whitespace error: $committedWhitespace" }
$committedPhaseFiles = @(git diff --name-only --diff-filter=ACMRT "$phaseBaseline..HEAD")
$workingPhaseFiles = @(
  git diff --name-only --diff-filter=ACMRT
  git diff --cached --name-only --diff-filter=ACMRT
  '.superpowers/sdd/phase-2i-implementation-report.md', '.superpowers/sdd/phase-2i-task-10-report.md' |
    Where-Object { Test-Path -LiteralPath $_ }
)
$phaseFiles = @($committedPhaseFiles + $workingPhaseFiles | Sort-Object -Unique)
$phaseFiles
$strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
$changedText = @($phaseFiles | Where-Object { $_ -match '\.(cs|ts|vue|md|json|js|yml|yaml|slnx|csproj)$' })
foreach ($path in $changedText) {
  $text = $strictUtf8.GetString([IO.File]::ReadAllBytes((Resolve-Path $path)))
  if ($text.Contains([char]0xFFFD)) { throw "Replacement character found: $path" }
}
foreach ($protectedPath in @('multiplayer/client', 'src', 'build', 'outputs', 'multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures')) {
  git diff --exit-code "$phaseBaseline..HEAD" -- $protectedPath
  if ($LASTEXITCODE -ne 0) { throw "Committed Phase 2I protected-scope diff: $protectedPath" }
  git diff --exit-code -- $protectedPath
  if ($LASTEXITCODE -ne 0) { throw "Working-tree protected-scope diff: $protectedPath" }
}
rg -n -S 'IGameStateStore|IDiceRoller|ICheckResolutionEngine|ICombatDamageEngine|IHpDamageEngine|IGameRealtimeNotifier|HubContext|IPlayerConnectionRegistry|RoomMutationDeliveryGate' multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/NpcCombatContinuation.cs
rg -n -S 'IGameStateStore|IDiceRoller|ICheckResolutionEngine|ICombatDamageEngine|IHpDamageEngine|IGameRealtimeNotifier|HubContext|IPlayerConnectionRegistry' multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/PlayerCombatIntentCoordinator.cs
rg -n -S 'Guid.Empty|trusted\s*=|RequestingPlayerId|HostPlayerId|AuthorizedPlayerId' multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/NpcCombatContinuation.cs
rg -n -S 'BeginNpcOpposedExchangeCommand|PassNpcCombatTurnCommand' multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs
rg -n -S 'combat/(npc|start|end|damage|resolve-damage)' multiplayer/server/src multiplayer/client/src
rg -n -S 'continueNpc|autoNpc|npcMode|npc-attack|npc-pass|Npc.*Button' multiplayer/client/src
```

Expected: current and committed-range diff/UTF-8 checks pass; `$phaseFiles` inventories every committed and current Phase 2I file; dependency/forbidden/public/client scans have no disallowed production matches; client, Single Player, fixtures, and formal artifact have no baseline-range or working-tree diff. Report the exact Phase 2I server production/test inventory rather than requiring it to be empty. Inspect `GameApi.MapGameEndpoints` and assert exactly three Combat routes.

- [ ] **Step 9: Write the minimum factual reports and docs.**

Record exact commands/counts, ordered revision/publication evidence, pending survival, reconnect neutrality, dependency counts, partial commits, privacy, three routes, zero client/AI/Firearms/persistence changes, and all known concerns. Update only factual Phase 2I state/handoff lines.

- [ ] **Step 10: Run documentation GREEN and commit.**

Run the Step 1 command again, followed by `git diff --check` and strict UTF-8 validation of every Phase 2I changed text file. Expected GREEN: both reports exist, both factual docs contain the exact Phase 2I handoff, and all diff/encoding checks pass.

**Invariants:** documentation does not claim unrun evidence; no implementation scope expansion or artifact change.

**Commit:** `docs: record npc combat driver validation`

**Escalate if:** any full regression changes canonical rule fixtures/formal SHA, route count differs from three, strict UTF-8 fails, a second coordinator/store appears, concurrency permits duplicate commit, or a privacy assertion fails.

---

## Implementation Stop Gate

After Task 10, stop. Do not start AI NPC behavior, Firearms/Impaling, client NPC controls, Start/End/Damage routes, pending-damage recovery, Scenario integration, persistence, or another Combat phase without a new approved design.

Plan complete and saved to `docs/superpowers/plans/2026-09-09-npc-attacker-driver-implementation.md`. Implement only after a separate implementation authorization, using subagent-driven development task-by-task with review gates or executing-plans with explicit checkpoints.
