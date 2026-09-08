# Multiplayer Phase 2H Player Combat Intent Protocol Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add exactly three authenticated player Combat intents—melee attack, respond, and pass—while preserving server-only canonical rules, viewer privacy, committed revision boundaries, and authoritative snapshot recovery.

**Architecture:** `GameApi` authenticates the bearer session and holds one `RoomMutationDeliveryGate` lease around a request-level call to `IPlayerCombatIntentCoordinator`. The application coordinator prevalidates against `IGameCoordinator.GetProjectionAsync`, invokes the existing internal Combat transitions, continues only from each returned canonical `State`, and finishes with a fresh viewer-specific `GameSnapshot`; it never injects `IGameStateStore`, rolls dice, mutates state, or publishes. The Vue client renders and submits only server-projected `CombatViewerActionsSnapshot` affordances and accepts HTTP and SignalR responses through the existing monotonic `GameSnapshot` path.

**Tech Stack:** .NET 10 / ASP.NET Core Minimal API / xUnit / SignalR; Vue 3 / TypeScript 5.9 / Vitest; PowerShell validation on Windows.

## Global Constraints

- Baseline for implementation is finalized design commit `e8c0c9e8a8dd30c91a24ccfe7c25bc8c77eddc14` or a reviewed descendant containing that design unchanged.
- Follow strict RED → minimal GREEN TDD in every task; record exact commands, counts, and failure reasons in `.superpowers/sdd/phase-2h-task-<N>-report.md`.
- Public Combat route count after Phase 2H is exactly 3: `melee-attack`, `respond`, and `pass`.
- Public `combat/start`, `combat/end`, `combat/damage`, and `combat/resolve-damage` remain absent and return 404.
- Bearer session is the only `PlayerId` authority. Public bodies never accept `PlayerId`, owner, host authority, rolls, stats, policy, outcome, damage, HP, turn, or round.
- Public actor identity is an owned investigator `ActorCharacterId`; public target identity is a projected active opponent `ParticipantId`; PvP and same-side attack remain absent.
- `PlayerCombatIntentCoordinator` must not directly depend on `IGameStateStore`, `IDiceRoller`, `ICombatDamageEngine`, `IHpDamageEngine`, `IGameRealtimeNotifier`, SignalR hub context, persistence, or AI.
- Before the first mutation, use the viewer-specific `IGameCoordinator.GetProjectionAsync(roomId, authenticatedPlayerId)` affordance and revision; projection prevalidation never replaces canonical transition validation.
- After Begin use only `BeginOpposedExchangeResult.State`; after Resolve use only `ResolvePendingExchangeResult.State`; final HTTP success is a fresh viewer-specific projection.
- If a necessary canonical fact cannot be obtained from those seams, stop and report the exact missing fact. Do not inject `IGameStateStore`; propose a narrow internal query/result contract for review.
- Preserve separate Begin, Resolve, and Damage commits/revisions and their existing commit-before-viewer-specific SignalR publication. The application coordinator adds no publication.
- Disconnect never resolves, responds, passes, advances, or damages; reconnect is revision-neutral and restores the latest viewer affordance.
- Do not add an `IntentId` registry or automatic intent replay. Stale expected revision causes authoritative resync and a new user decision.
- Keep NPC defender response policy typed, private, and snapshotted at Combat Start. NPC attacker driver, public NPC actor/pass, and Host Gameplay Superuser remain deferred.
- Do not add Firearms, Impaling, weapon switching, movement/range, timeout/forfeit, AI gameplay, Scenario, persistence, DB, or Redis.
- Do not change Single Player rules, fixtures except when an authorized conformance generator deterministically rewrites its own fixture, or `outputs/trpg-dm-assistant.html`.
- Keep every edited text file strict UTF-8 and preserve all Chinese text.
- Never amend, rebase, reset, stash, force-push, or rewrite existing Phase 2F/2G/2H design commits.

---

## Planned File Structure

| File | Responsibility |
| --- | --- |
| `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CombatSessionState.cs` | Private canonical typed NPC response policy snapshot. |
| `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs` | Internal typed Start definition and safe viewer-action DTOs. |
| `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameProjection.cs` | Viewer-specific action and exact human defender response projection. |
| `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/PlayerCombatIntentCoordinator.cs` | New narrow application contracts, errors, and orchestration; no store access. |
| `multiplayer/server/src/Trpg.Multiplayer.Api/GameApi.cs` | Exactly three authenticated routes, outer delivery gate, DTO binding, structured error mapping, final safe snapshot. |
| `multiplayer/server/src/Trpg.Multiplayer.Api/Program.cs` | One shared `GameCoordinator` instance exposed through public/internal interfaces and player-intent registration. |
| `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs` | Canonical policy and viewer projection/privacy tests. |
| `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/PlayerCombatIntentCoordinatorTests.cs` | New application seam, orchestration, stale/partial-commit, and dependency-boundary tests. |
| `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameApiTests.cs` | Authentication, exactly-three-route, structured error, spoofing, and final snapshot tests. |
| `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/SignalRGameDeliveryTests.cs` | Ordered intermediate committed snapshots and no duplicate publication. |
| `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/DisconnectReconnectTests.cs` | Pending affordance recovery and revision-neutral disconnect/reconnect. |
| `multiplayer/client/src/contracts/rooms.ts` | Read-only action projection plus three minimal request/error contracts. |
| `multiplayer/client/src/api/rooms.ts` | Three bearer-authenticated intent methods and snapshot refresh. |
| `multiplayer/client/src/api/client.ts` | Preserve a safe structured error code/current revision without exposing raw response text. |
| `multiplayer/client/src/api/client.test.ts` | Exact route/body/token and structured error sanitization tests. |
| `multiplayer/client/src/components/LobbyView.vue` | Thin selector/buttons driven only by projected affordances; no Combat state machine. |
| `multiplayer/client/src/components/HomeView.test.ts` | Control visibility, payload, resync, and no-authority source assertions. |
| `multiplayer/client/src/state/gameSnapshot.test.ts` | Existing lower-revision rejection exercised with Combat action snapshots. |
| `docs/CURRENT_STATE.md`, `docs/HANDOFF.md`, `docs/ARCHITECTURE.md` | Post-GREEN factual status, limits, and authority boundary only. |

## Planned Feature Commit Structure

1. Task 1: `feat: snapshot npc combat response policy`
2. Task 2: `feat: project player combat intent actions`
3. Task 3: `feat: define player combat intent coordinator`
4. Task 4: `feat: orchestrate player melee attacks`
5. Task 5: `feat: orchestrate player combat responses`
6. Task 6: `feat: orchestrate player combat pass`
7. Task 7: `feat: expose player combat intent routes`
8. Task 8: `test: cover player combat intent delivery`
9. Task 9: `feat: add player combat intent controls`
10. Task 10: `docs: record player combat intent validation`

Each atomic task commit is created only after its focused GREEN command passes and its report records the RED/GREEN evidence. Push non-force only when the implementation authorization explicitly allows pushes. Task reports remain evidence, not a substitute for test output.

---

### Task 1: Snapshot a typed private NPC defender response policy

**Files:**

- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CombatSessionState.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs`
- Test: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`
- Update typed test setup only where compilation requires it: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/SignalRGameDeliveryTests.cs`
- Update typed test setup only where compilation requires it: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/DisconnectReconnectTests.cs`
- Create evidence: `.superpowers/sdd/phase-2h-task-1-report.md`

**Interfaces:**

- Consumes: existing `CombatResponse.Dodge`, `CombatResponse.FightBack`, `OpponentDefinition`, `CombatParticipantState`, and `StartCombatAsync`.
- Produces: `OpponentDefinition.NpcResponsePolicy: CombatResponse` and `CombatParticipantState.NpcResponsePolicy: CombatResponse?` for later Begin-result orchestration.

- [ ] **Step 1: Write failing canonical snapshot tests.**

Add these exact facts to `GameStateTests`:

```csharp
[Fact]
public async Task InternalCombat_StartSnapshotsTypedPrivateNpcPolicy()
{
    var fixture = await CreateCombatFixtureAsync();
    var started = await StartCombatAsync(
        fixture.Coordinator,
        fixture.Room.RoomId,
        fixture.HostId,
        1,
        fixture.CharacterIds,
        CreateOpponentDefinitions(fixture.Coordinator, CombatResponse.FightBack));

    var opponent = Assert.Single(started.State!.Combat!.Participants, participant => participant.Kind == "opponent");
    Assert.Equal(CombatResponse.FightBack, opponent.NpcResponsePolicy);
    Assert.Contains(opponent.NpcResponsePolicy.Value, opponent.AvailableResponses);
}

[Fact]
public async Task InternalCombat_StartRejectsNpcPolicyOutsideSnapshottedResponses()
{
    var fixture = await CreateCombatFixtureAsync();
    var result = await StartCombatAsync(
        fixture.Coordinator,
        fixture.Room.RoomId,
        fixture.HostId,
        1,
        fixture.CharacterIds,
        CreateOpponentDefinitions(
            fixture.Coordinator,
            CombatResponse.FightBack,
            availableResponses: [CombatResponse.Dodge]));

    Assert.False(result.IsSuccess);
    Assert.Equal(GameErrorCode.InvalidCombat, result.Error!.Code);
    Assert.Equal(1, (await fixture.Coordinator.GetProjectionAsync(fixture.Room.RoomId, fixture.HostId)).Value!.Revision);
}
```

Also extend the existing projection JSON privacy test with:

```csharp
Assert.DoesNotContain("NpcResponsePolicy", combatJson, StringComparison.OrdinalIgnoreCase);
```

- [ ] **Step 2: Run RED.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.InternalCombat_StartSnapshotsTypedPrivateNpcPolicy|FullyQualifiedName~GameStateTests.InternalCombat_StartRejectsNpcPolicyOutsideSnapshottedResponses" --nologo -v:minimal
```

Expected RED: compilation fails because the typed `NpcResponsePolicy` member and typed test factory argument do not exist. Record the compiler diagnostics and failed-test count; a passing run means the tests did not establish the missing behavior.

- [ ] **Step 3: Add the minimal typed canonical field and validation.**

Use these exact contract shapes:

```csharp
internal sealed record OpponentDefinition(
    string Label,
    int Dex,
    int Fighting,
    int Dodge,
    IReadOnlyList<CombatResponse> AvailableResponses,
    int ResponseAllowance,
    CombatResponse NpcResponsePolicy,
    int Str,
    int Siz,
    int CurrentHp,
    int MaxHp,
    int FixedArmor,
    CombatWeaponProfile Weapon);

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
    bool Active,
    CombatDamageProfile DamageProfile,
    OpponentVitalityState? OpponentVitality,
    CombatResponse? NpcResponsePolicy);
```

Pass `null` for investigators and `opponent.NpcResponsePolicy` for opponents. Before participant creation, fail with `InvalidCombat` unless both conditions hold:

```csharp
if (!Enum.IsDefined(opponent.NpcResponsePolicy)
    || !opponent.AvailableResponses.Contains(opponent.NpcResponsePolicy))
{
    return GameResult<StartCombatResult>.Failure(GameErrorCode.InvalidCombat);
}
```

Update all internal test factories to pass `CombatResponse.Dodge` or `CombatResponse.FightBack`; do not preserve a free-form runtime policy string or create a public policy DTO.

- [ ] **Step 4: Run GREEN and privacy regression.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.InternalCombat_Start|FullyQualifiedName~GameStateTests.Projection|FullyQualifiedName~SignalRGameDeliveryTests|FullyQualifiedName~DisconnectReconnectTests" --nologo -v:minimal
```

Expected GREEN: all selected tests pass; invalid policy changes no revision; serialized Combat contains no `NpcResponsePolicy` or legacy `ResponsePolicy`.

- [ ] **Step 5: Commit Task 1 after GREEN.**

```powershell
git add multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/CombatSessionState.cs multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameCoordinator.cs multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/SignalRGameDeliveryTests.cs multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/DisconnectReconnectTests.cs .superpowers/sdd/phase-2h-task-1-report.md
git diff --cached --check
git commit -m "feat: snapshot npc combat response policy"
```

**Invariants:** policy is canonical and immutable for the Combat lifetime; only NPC participants hold it; it is a valid member of the same participant's snapshotted responses; no projection or public input contains it.

**Stop/escalate:** stop if adding the field requires changing Single Player, adding persistence, or exposing policy publicly. Stop if canonical Start has no location that can validate the policy before replacement.

---

### Task 2: Project viewer-specific Combat action affordances

**Files:**

- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameProjection.cs`
- Test: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs`
- Test: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameApiTests.cs`
- Create evidence: `.superpowers/sdd/phase-2h-task-2-report.md`

**Interfaces:**

- Consumes: canonical current actor, participant ownership/side/activity, `PendingExchange`, and exact `DamageDispositions` status.
- Produces: `CombatSnapshot.ViewerActions`, `CombatViewerActionsSnapshot`, and `CombatPendingResponseSnapshot` as the sole client legality source.

- [ ] **Step 1: Write failing actor/defender/observer/privacy projection tests.**

Add tests named:

```csharp
Projection_CombatViewerActions_OwnCurrentInvestigatorGetsAttackPassAndActiveOpponents
Projection_CombatViewerActions_NonCurrentOwnerGetsNoActorActions
Projection_CombatViewerActions_ExactHumanDefenderAloneGetsPendingResponse
Projection_CombatViewerActions_AttackerAndOtherParticipantDoNotGetExchangeOrResponses
Projection_CombatViewerActions_PendingDamageSuppressesAttackAndPass
Projection_CombatViewerActions_NonparticipantStillGetsNullCombat
Projection_CombatViewerActions_JsonHasExactSafePropertySetAndNoInternalFields
```

The exact positive assertions are:

```csharp
Assert.Equal(currentCharacterId, actions.ActorCharacterId);
Assert.True(actions.CanMeleeAttack);
Assert.True(actions.CanPass);
Assert.Equal(["opponent:0"], actions.EligibleTargetParticipantIds);
Assert.Null(actions.PendingResponse);

Assert.Equal(pending.ExchangeId, defenderActions.PendingResponse!.ExchangeId);
Assert.Equal(["dodge", "fight_back"], defenderActions.PendingResponse.AvailableResponses);
Assert.Null(attackerActions.PendingResponse);
```

Assert the JSON action property set is exactly `actorCharacterId`, `canMeleeAttack`, `canPass`, `eligibleTargetParticipantIds`, `pendingResponse`; the pending response set is exactly `exchangeId`, `availableResponses`. Assert no policy, allowance, rolls, raw target, stats, registry, schedule, history, source, or provenance.

- [ ] **Step 2: Run RED.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.Projection_CombatViewerActions|FullyQualifiedName~GameApiTests.CombatProjection" --nologo -v:minimal
```

Expected RED: compilation fails because viewer-action DTOs and `CombatSnapshot.ViewerActions` are absent.

- [ ] **Step 3: Add minimal safe DTOs and projection.**

Append an optional final parameter to preserve existing positional callers:

```csharp
public sealed record CombatPendingResponseSnapshot(
    string ExchangeId,
    IReadOnlyList<string> AvailableResponses);

public sealed record CombatViewerActionsSnapshot(
    Guid? ActorCharacterId,
    bool CanMeleeAttack,
    bool CanPass,
    IReadOnlyList<string> EligibleTargetParticipantIds,
    CombatPendingResponseSnapshot? PendingResponse);

public sealed record CombatSnapshot(
    bool Active,
    int Round,
    string? CurrentActorParticipantId,
    IReadOnlyList<CombatParticipantSnapshot> Participants,
    CombatExchangeSnapshot? LastExchange,
    CombatPendingSnapshot? Pending,
    CombatDamageSnapshot? LastDamage = null,
    CombatViewerActionsSnapshot? ViewerActions = null);
```

Build affordances only from canonical state:

```csharp
var hasProgressionBlocker = session.PendingExchange is not null
    || session.DamageDispositions.Values.Any(disposition => disposition.Status == DamageDispositionStatus.Pending);
var currentActor = session.Participants.SingleOrDefault(
    participant => participant.ParticipantId.Value == currentActorParticipantId);
var ownsCurrentInvestigator = currentActor is
{
    Active: true,
    Kind: "investigator",
    CharacterId: Guid,
    OwnerPlayerId: Guid owner
} && owner == viewerPlayerId;
```

When the viewer owns the unblocked current investigator, include only active opposite-side IDs as eligible targets and set `CanMeleeAttack` to `eligibleTargets.Count > 0`, `CanPass` to true. When the viewer owns the exact human pending defender, project its exact exchange and map only:

```csharp
private static string ToCombatResponseWireValue(CombatResponse response) => response switch
{
    CombatResponse.Dodge => "dodge",
    CombatResponse.FightBack => "fight_back",
    _ => throw new CombatDamageStateInvariantException("Unsupported canonical Combat response.")
};
```

Return `ViewerActions = null` when neither actor nor exact defender affordance applies. Never derive NPC response policy into this DTO.

- [ ] **Step 4: Run GREEN and serialization privacy checks.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameStateTests.Projection|FullyQualifiedName~GameApiTests.CombatProjection" --nologo -v:minimal
```

Expected GREEN: all selected tests pass for current actor, non-current owner, exact defender, attacker/observer, pending damage, and nonparticipant.

- [ ] **Step 5: Commit Task 2 after GREEN.**

```powershell
git add multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameContracts.cs multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/GameProjection.cs multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameStateTests.cs multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameApiTests.cs .superpowers/sdd/phase-2h-task-2-report.md
git diff --cached --check
git commit -m "feat: project player combat intent actions"
```

**Invariants:** `Combat = null` remains the nonparticipant boundary; exact response affordance is defender-owner-only; projected affordance is read-only and revision-neutral; raw canonical facts stay absent.

**Stop/escalate:** stop if projection cannot determine the current owned investigator, active opposite-side targets, exact pending owner, or pending damage blocker from `CombatSession`. Do not query storage from projection or broaden the public DTO.

---

### Task 3: Define the narrow player-intent application contract and dependency boundary

**Files:**

- Create: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/PlayerCombatIntentCoordinator.cs`
- Create: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/PlayerCombatIntentCoordinatorTests.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Program.cs`
- Create evidence: `.superpowers/sdd/phase-2h-task-3-report.md`

**Interfaces:**

- Consumes: `IGameCoordinator.GetProjectionAsync` and `IInternalCombatResolutionCoordinator` only.
- Produces: three application methods and narrow error/result contracts used by Tasks 4–7.

- [ ] **Step 1: Write failing contract and dependency-boundary tests.**

```csharp
[Fact]
public void PlayerCombatIntentCoordinator_HasOnlyApprovedStateAndTransitionDependencies()
{
    var constructor = Assert.Single(typeof(PlayerCombatIntentCoordinator).GetConstructors());
    var dependencies = constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

    Assert.Equal([typeof(IGameCoordinator), typeof(IInternalCombatResolutionCoordinator)], dependencies);
    Assert.DoesNotContain(typeof(IGameStateStore), dependencies);
    Assert.DoesNotContain(typeof(IDiceRoller), dependencies);
    Assert.DoesNotContain(typeof(ICombatDamageEngine), dependencies);
    Assert.DoesNotContain(typeof(IHpDamageEngine), dependencies);
    Assert.DoesNotContain(typeof(IGameRealtimeNotifier), dependencies);
}

[Fact]
public void PlayerCombatIntentCoordinator_ExposesExactlyThreePlayerIntents()
{
    Assert.Equal(
        ["MeleeAttackAsync", "PassAsync", "RespondAsync"],
        typeof(IPlayerCombatIntentCoordinator).GetMethods().Select(method => method.Name).Order().ToArray());
}
```

Add a source-boundary assertion that reads only the production coordinator file and rejects `IGameStateStore`, `IDiceRoller`, damage engines, notifier, hub context, persistence, or AI type names.

- [ ] **Step 2: Run RED.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter FullyQualifiedName~PlayerCombatIntentCoordinatorTests --nologo -v:minimal
```

Expected RED: compilation fails because the application coordinator and its contracts do not exist.

- [ ] **Step 3: Add exact commands, result, errors, and interface.**

Create these internal types in `PlayerCombatIntentCoordinator.cs`:

```csharp
internal sealed record PlayerMeleeAttackIntent(
    Guid RoomId,
    Guid PlayerId,
    long ExpectedGameRevision,
    Guid ActorCharacterId,
    string TargetParticipantId);

internal sealed record PlayerRespondIntent(
    Guid RoomId,
    Guid PlayerId,
    long ExpectedGameRevision,
    string ExchangeId,
    CombatResponse Response);

internal sealed record PlayerPassIntent(
    Guid RoomId,
    Guid PlayerId,
    long ExpectedGameRevision,
    Guid ActorCharacterId);

internal enum PlayerCombatIntentErrorCode
{
    InvalidIntent,
    InvalidResponse,
    InvalidSession,
    NotMember,
    RoomNotFound,
    GameNotFound,
    ActorNotOwned,
    DefenderNotOwned,
    StaleGameRevision,
    CombatInactive,
    NotCurrentActor,
    TargetNotEligible,
    ExchangeNotPending,
    ProgressionBlocked,
    CombatConsistencyFailure
}

internal sealed record PlayerCombatIntentError(
    PlayerCombatIntentErrorCode Code,
    long? CurrentGameRevision = null);

internal sealed record PlayerCombatIntentResult(
    GameSnapshot? Snapshot,
    PlayerCombatIntentError? Error)
{
    public bool IsSuccess => Error is null;

    public static PlayerCombatIntentResult Success(GameSnapshot snapshot) => new(snapshot, null);

    public static PlayerCombatIntentResult Failure(
        PlayerCombatIntentErrorCode code,
        long? currentGameRevision = null) => new(null, new PlayerCombatIntentError(code, currentGameRevision));
}

internal interface IPlayerCombatIntentCoordinator
{
    Task<PlayerCombatIntentResult> MeleeAttackAsync(PlayerMeleeAttackIntent intent);
    Task<PlayerCombatIntentResult> RespondAsync(PlayerRespondIntent intent);
    Task<PlayerCombatIntentResult> PassAsync(PlayerPassIntent intent);
}
```

Use a stateless singleton with exactly this constructor:

```csharp
internal sealed class PlayerCombatIntentCoordinator(
    IGameCoordinator games,
    IInternalCombatResolutionCoordinator combat) : IPlayerCombatIntentCoordinator
```

Register one shared canonical coordinator instance, not two stores/coordinators:

```csharp
builder.Services.AddSingleton<GameCoordinator>();
builder.Services.AddSingleton<IGameCoordinator>(services => services.GetRequiredService<GameCoordinator>());
builder.Services.AddSingleton<IInternalCombatResolutionCoordinator>(services => services.GetRequiredService<GameCoordinator>());
builder.Services.AddSingleton<IPlayerCombatIntentCoordinator, PlayerCombatIntentCoordinator>();
```

Do not register `IPlayerCombatIntentCoordinator` with `IGameStateStore` and do not add a second `GameCoordinator` singleton registration.

- [ ] **Step 4: Run GREEN for shape and DI resolution.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~PlayerCombatIntentCoordinatorTests.PlayerCombatIntentCoordinator_HasOnlyApprovedStateAndTransitionDependencies|FullyQualifiedName~PlayerCombatIntentCoordinatorTests.PlayerCombatIntentCoordinator_ExposesExactlyThreePlayerIntents|FullyQualifiedName~PlayerCombatIntentCoordinatorTests.PlayerCombatIntentCoordinator_ResolvesFromDependencyInjection" --nologo -v:minimal
```

Expected GREEN: three tests pass; interface exposes exactly three methods; DI resolves one coordinator; forbidden dependencies are absent.

- [ ] **Step 5: Commit Task 3 after GREEN.**

```powershell
git add multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/PlayerCombatIntentCoordinator.cs multiplayer/server/src/Trpg.Multiplayer.Api/Program.cs multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/PlayerCombatIntentCoordinatorTests.cs .superpowers/sdd/phase-2h-task-3-report.md
git diff --cached --check
git commit -m "feat: define player combat intent coordinator"
```

**Invariants:** this layer stores no canonical/request state in fields; it performs no publication; it never acquires the private `GameCoordinator` room lock; it has exactly two dependencies.

**Stop/escalate:** stop if DI cannot expose the existing `GameCoordinator` through both interfaces as the same instance without changing canonical storage lifetime. Do not instantiate a second coordinator or inject the store.

---

### Task 4: Orchestrate player melee attack through Begin, NPC Resolve, and exact Damage

**Files:**

- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/PlayerCombatIntentCoordinator.cs`
- Test: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/PlayerCombatIntentCoordinatorTests.cs`
- Create evidence: `.superpowers/sdd/phase-2h-task-4-report.md`

**Interfaces:**

- Consumes: `PlayerMeleeAttackIntent`, projected `CombatViewerActionsSnapshot`, `BeginOpposedExchangeResult.State`, private canonical `CombatParticipantState.NpcResponsePolicy`, `ResolvePendingExchangeResult.State`, and exact `DamageDispositions[ExchangeId]`.
- Produces: complete NPC-defender orchestration or a Begin-only human-defender boundary, then a fresh viewer projection.

- [ ] **Step 1: Write failing orchestration and stale-side-effect tests.**

Add exact tests:

```csharp
MeleeAttack_OwnCurrentInvestigatorAgainstProjectedNpc_BeginsResolvesAndConsumesExactDamage
MeleeAttack_HumanDefender_CommitsBeginOnlyAndNeverSelectsResponseOrDamage
MeleeAttack_StaleRevision_PerformsNoBeginResolveDamageDiceIdRevisionOrPublish
MeleeAttack_RejectedActorOrTarget_PerformsNoTransitionOrDice
MeleeAttack_HostCannotActAsUnownedNpc
MeleeAttack_NpcPolicyOutsidePendingResponses_FailsClosedBeforeResolveDice
MeleeAttack_UsesBeginReturnedStateWithoutStoreRead
MeleeAttack_UsesResolveReturnedStateAndLatestRevisionForExactDamage
MeleeAttack_NoHitDoesNotInvokeDamage
MeleeAttack_ReturnsFreshFinalViewerProjection
```

For the NPC damage path, use spies/fakes that return revisions 12 → 13 → 14 → 15 and assert the commands exactly:

```csharp
Assert.Equal(12, beginCommand.ExpectedGameRevision);
Assert.Equal($"character:{actorCharacterId}", beginCommand.AttackerParticipantId);
Assert.Equal("opponent:0", beginCommand.DefenderParticipantId);
Assert.Null(resolveCommand.RequestingPlayerId);
Assert.Equal(13, resolveCommand.ExpectedGameRevision);
Assert.Equal(beginState.Combat.PendingExchange.ExchangeId, resolveCommand.ExchangeId);
Assert.Equal(CombatResponse.Dodge, resolveCommand.Response);
Assert.Equal(14, damageCommand.ExpectedGameRevision);
Assert.Equal(resolveCommand.ExchangeId, damageCommand.ExchangeId);
Assert.Equal(15, result.Snapshot!.Revision);
```

For stale input assert zero calls to Begin/Resolve/Damage and unchanged fake dice/notifier counters. Assert no `ExchangeId` is generated by observing unchanged canonical pending/history/revision.

- [ ] **Step 2: Run RED.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~PlayerCombatIntentCoordinatorTests.MeleeAttack" --nologo -v:minimal
```

Expected RED: melee methods return no behavior or fail assertions because orchestration is absent.

- [ ] **Step 3: Implement projection prevalidation without treating it as mutation authority.**

The first operation must be:

```csharp
var projection = await games.GetProjectionAsync(intent.RoomId, intent.PlayerId);
if (!projection.IsSuccess)
{
    return MapProjectionFailure(projection.Error!.Code);
}

var snapshot = projection.Value!;
if (snapshot.Revision != intent.ExpectedGameRevision)
{
    return PlayerCombatIntentResult.Failure(
        PlayerCombatIntentErrorCode.StaleGameRevision,
        snapshot.Revision);
}

var actions = snapshot.Combat?.ViewerActions;
if (actions?.ActorCharacterId != intent.ActorCharacterId)
{
    return PlayerCombatIntentResult.Failure(PlayerCombatIntentErrorCode.ActorNotOwned, snapshot.Revision);
}

if (!actions.CanMeleeAttack
    || !actions.EligibleTargetParticipantIds.Contains(intent.TargetParticipantId, StringComparer.Ordinal))
{
    return PlayerCombatIntentResult.Failure(PlayerCombatIntentErrorCode.TargetNotEligible, snapshot.Revision);
}
```

Then call canonical Begin, which revalidates revision/current actor/ownership/target/gate before exchange ID generation or dice:

```csharp
var begin = await combat.BeginOpposedExchangeAsync(new BeginOpposedExchangeCommand(
    intent.RoomId,
    intent.PlayerId,
    intent.ExpectedGameRevision,
    $"character:{intent.ActorCharacterId}",
    intent.TargetParticipantId));
```

- [ ] **Step 4: Continue only from returned canonical states.**

Use exact returned facts, never a store re-read or projection for private policy:

```csharp
var beginState = begin.Value!.State;
var pending = beginState.Combat!.PendingExchange
    ?? throw new InvalidOperationException("Committed Begin result omitted its pending exchange.");
var defender = beginState.Combat.Participants.Single(
    participant => participant.ParticipantId == pending.DefenderParticipantId);

if (defender.OwnerPlayerId is null)
{
    var policy = defender.NpcResponsePolicy
        ?? throw new InvalidOperationException("Committed NPC defender omitted its response policy.");
    if (!pending.AvailableResponses.Contains(policy))
    {
        return PlayerCombatIntentResult.Failure(
            PlayerCombatIntentErrorCode.CombatConsistencyFailure,
            beginState.Revision);
    }

    var resolved = await combat.ResolvePendingExchangeAsync(new ResolvePendingExchangeCommand(
        intent.RoomId,
        null,
        beginState.Revision,
        pending.ExchangeId,
        policy));
    if (!resolved.IsSuccess)
    {
        return MapTransitionFailure(resolved.Error!.Code, beginState.Revision);
    }

    var resolvedState = resolved.Value!.State;
    if (resolvedState.Combat!.DamageDispositions.TryGetValue(pending.ExchangeId, out var disposition)
        && disposition.Status == DamageDispositionStatus.Pending)
    {
        var damage = await combat.ResolveCombatDamageAsync(new ResolveCombatDamageCommand(
            intent.RoomId,
            resolvedState.Revision,
            pending.ExchangeId));
        if (!damage.IsSuccess)
        {
            return MapTransitionFailure(damage.Error!.Code, resolvedState.Revision);
        }
    }
}
```

For a human defender, make no Resolve or Damage call. On all successful branches, finish only with:

```csharp
return await GetFinalProjectionAsync(intent.RoomId, intent.PlayerId);
```

Do not infer damage from `LastExchange`, outcome text, or the original HTTP revision. Do not catch `CombatDamageCommitInvariantException` as stale/retryable.

- [ ] **Step 5: Run GREEN including canonical mutation tests.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~PlayerCombatIntentCoordinatorTests.MeleeAttack|FullyQualifiedName~GameStateTests.InternalCombat_Begin|FullyQualifiedName~GameStateTests.ResolveCombatDamage" --nologo -v:minimal
```

Expected GREEN: all selected tests pass; NPC path is Begin + Resolve + optional exact Damage; human path is Begin only; stale/rejected inputs have zero side effects.

- [ ] **Step 6: Commit Task 4 after GREEN.**

```powershell
git add multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/PlayerCombatIntentCoordinator.cs multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/PlayerCombatIntentCoordinatorTests.cs .superpowers/sdd/phase-2h-task-4-report.md
git diff --cached --check
git commit -m "feat: orchestrate player melee attacks"
```

**Invariants:** each internal transition remains final canonical authority; Begin/Resolve/Damage revisions remain separate; no client or host chooses NPC response; final success is not an internal result DTO.

**Stop/escalate:** stop if defender identity/policy is missing from `BeginOpposedExchangeResult.State` or exact damage disposition is missing from `ResolvePendingExchangeResult.State`. Report the missing fact and propose a narrow result contract; never inject `IGameStateStore`.

---

### Task 5: Orchestrate exact human defender response and optional exact Damage

**Files:**

- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/PlayerCombatIntentCoordinator.cs`
- Test: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/PlayerCombatIntentCoordinatorTests.cs`
- Create evidence: `.superpowers/sdd/phase-2h-task-5-report.md`

**Interfaces:**

- Consumes: `PlayerRespondIntent`, projected `PendingResponse`, canonical Resolve, and `ResolvePendingExchangeResult.State`.
- Produces: defender-owned Resolve + optional exact Damage + fresh viewer projection.

- [ ] **Step 1: Write failing response authority/revision/damage tests.**

Add exact tests:

```csharp
Respond_ExactHumanDefenderOwner_ResolvesAndConsumesExactPendingDamage
Respond_ExactHumanDefenderOwner_NoHitDoesNotCallDamage
Respond_WrongPlayerReturnsDefenderNotOwnedBeforeDiceOrMutation
Respond_WrongExchangeReturnsExchangeNotPendingBeforeDiceOrMutation
Respond_UnavailableResponseReturnsInvalidResponseBeforeDiceOrMutation
Respond_StaleRevisionPerformsNoResolveDamageDiceRevisionOrPublish
Respond_UsesResolveReturnedStateAndLatestRevisionForExactDamage
Respond_ReturnsFreshFinalViewerProjection
```

Assert the Resolve and Damage calls:

```csharp
Assert.Equal(defenderPlayerId, resolveCommand.RequestingPlayerId);
Assert.Equal(requestRevision, resolveCommand.ExpectedGameRevision);
Assert.Equal(projectedExchangeId, resolveCommand.ExchangeId);
Assert.Equal(CombatResponse.FightBack, resolveCommand.Response);
Assert.Equal(resolveResult.State.Revision, damageCommand.ExpectedGameRevision);
Assert.Equal(projectedExchangeId, damageCommand.ExchangeId);
```

- [ ] **Step 2: Run RED.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~PlayerCombatIntentCoordinatorTests.Respond" --nologo -v:minimal
```

Expected RED: response orchestration assertions fail because `RespondAsync` has no implementation.

- [ ] **Step 3: Implement exact projection prevalidation and canonical Resolve.**

Use this order:

```csharp
var projection = await games.GetProjectionAsync(intent.RoomId, intent.PlayerId);
if (!projection.IsSuccess)
{
    return MapProjectionFailure(projection.Error!.Code);
}

var snapshot = projection.Value!;
if (snapshot.Revision != intent.ExpectedGameRevision)
{
    return PlayerCombatIntentResult.Failure(
        PlayerCombatIntentErrorCode.StaleGameRevision,
        snapshot.Revision);
}

var pending = snapshot.Combat?.ViewerActions?.PendingResponse;
if (pending is null || !string.Equals(pending.ExchangeId, intent.ExchangeId, StringComparison.Ordinal))
{
    return PlayerCombatIntentResult.Failure(
        PlayerCombatIntentErrorCode.ExchangeNotPending,
        snapshot.Revision);
}

var wireResponse = ToCombatResponseWireValue(intent.Response);
if (!pending.AvailableResponses.Contains(wireResponse, StringComparer.Ordinal))
{
    return PlayerCombatIntentResult.Failure(
        PlayerCombatIntentErrorCode.InvalidResponse,
        snapshot.Revision);
}

var resolved = await combat.ResolvePendingExchangeAsync(new ResolvePendingExchangeCommand(
    intent.RoomId,
    intent.PlayerId,
    intent.ExpectedGameRevision,
    intent.ExchangeId,
    intent.Response));
```

Inspect only `resolved.Value.State.Combat.DamageDispositions[intent.ExchangeId]`; if its status is `Pending`, call Damage with `resolved.Value.State.Revision`. Reuse the same private `ResolveExactPendingDamageAsync` helper as Task 4 so response and attack cannot drift. Return a fresh projection.

- [ ] **Step 4: Run GREEN.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~PlayerCombatIntentCoordinatorTests.Respond|FullyQualifiedName~GameStateTests.InternalCombat_Resolve" --nologo -v:minimal
```

Expected GREEN: all selected tests pass; only the exact owner can resolve; stale/wrong/unavailable inputs roll and mutate zero times; optional Damage uses the post-Resolve revision.

- [ ] **Step 5: Commit Task 5 after GREEN.**

```powershell
git add multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/PlayerCombatIntentCoordinator.cs multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/PlayerCombatIntentCoordinatorTests.cs .superpowers/sdd/phase-2h-task-5-report.md
git diff --cached --check
git commit -m "feat: orchestrate player combat responses"
```

**Invariants:** projected response is safe prevalidation; canonical Resolve revalidates exact exchange, ownership, membership, response, revision, and damage gate; no attacker/observer/host override.

**Stop/escalate:** stop if the exact human defender owner cannot receive `PendingResponse` without revealing it to others, or if canonical Resolve cannot distinguish trusted NPC (`null`) from authenticated human requester.

---

### Task 6: Orchestrate owned current-investigator Pass

**Files:**

- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/PlayerCombatIntentCoordinator.cs`
- Test: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/PlayerCombatIntentCoordinatorTests.cs`
- Create evidence: `.superpowers/sdd/phase-2h-task-6-report.md`

**Interfaces:**

- Consumes: `PlayerPassIntent`, projected `ActorCharacterId`/`CanPass`, and canonical `PassCombatTurnAsync`.
- Produces: exactly one Pass transition and a fresh viewer projection.

- [ ] **Step 1: Write failing Pass authorization and side-effect tests.**

Add exact tests:

```csharp
Pass_OwnCurrentInvestigator_CommitsOnePassAndReturnsFreshProjection
Pass_NonCurrentOrOtherOwnedActor_IsRejectedWithoutMutation
Pass_HostCannotPassForUnownedNpc
Pass_PendingExchangeOrDamageBlocker_IsRejectedWithoutMutation
Pass_StaleRevisionPerformsNoPassRevisionOrPublish
Pass_RoundWrapRemainsCanonicalAndSchedulesDyingOnce
```

Assert the command contains only canonical identities:

```csharp
Assert.Equal(roomId, passCommand.RoomId);
Assert.Equal(playerId, passCommand.RequestingPlayerId);
Assert.Equal(expectedRevision, passCommand.ExpectedGameRevision);
Assert.Equal(expectedRevision + 1, result.Snapshot!.Revision);
```

- [ ] **Step 2: Run RED.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~PlayerCombatIntentCoordinatorTests.Pass" --nologo -v:minimal
```

Expected RED: Pass tests fail because application orchestration is absent.

- [ ] **Step 3: Implement minimal projection prevalidation and canonical Pass.**

```csharp
var projection = await games.GetProjectionAsync(intent.RoomId, intent.PlayerId);
if (!projection.IsSuccess)
{
    return MapProjectionFailure(projection.Error!.Code);
}

var snapshot = projection.Value!;
if (snapshot.Revision != intent.ExpectedGameRevision)
{
    return PlayerCombatIntentResult.Failure(
        PlayerCombatIntentErrorCode.StaleGameRevision,
        snapshot.Revision);
}

var actions = snapshot.Combat?.ViewerActions;
if (actions?.ActorCharacterId != intent.ActorCharacterId)
{
    return PlayerCombatIntentResult.Failure(PlayerCombatIntentErrorCode.ActorNotOwned, snapshot.Revision);
}

if (!actions.CanPass)
{
    return PlayerCombatIntentResult.Failure(PlayerCombatIntentErrorCode.ProgressionBlocked, snapshot.Revision);
}

var passed = await combat.PassCombatTurnAsync(new PassCombatTurnCommand(
    intent.RoomId,
    intent.PlayerId,
    intent.ExpectedGameRevision));
if (!passed.IsSuccess)
{
    return MapTransitionFailure(passed.Error!.Code, snapshot.Revision);
}

return await GetFinalProjectionAsync(intent.RoomId, intent.PlayerId);
```

Do not calculate next actor, wrap, Dying schedule, action counters, or round in the application layer.

- [ ] **Step 4: Run GREEN with canonical round/Dying regressions.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~PlayerCombatIntentCoordinatorTests.Pass|FullyQualifiedName~GameStateTests.InternalCombat_Pass|FullyQualifiedName~GameStateTests.CombatDamageGate" --nologo -v:minimal
```

Expected GREEN: selected tests pass; Pass changes one revision; all rejection paths are revision-neutral; canonical turn/round/Dying behavior is unchanged.

- [ ] **Step 5: Commit Task 6 after GREEN.**

```powershell
git add multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/PlayerCombatIntentCoordinator.cs multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/PlayerCombatIntentCoordinatorTests.cs .superpowers/sdd/phase-2h-task-6-report.md
git diff --cached --check
git commit -m "feat: orchestrate player combat pass"
```

**Invariants:** public host has no NPC pass authority; no pending exchange or pending damage can be bypassed; application layer never selects the next actor.

**Stop/escalate:** stop if Pass requires public NPC authority or application-layer turn/round calculation. Those are deferred designs.

---

### Task 7: Map exactly three authenticated public HTTP routes and structured safe errors

**Files:**

- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/GameApi.cs`
- Modify: `multiplayer/server/src/Trpg.Multiplayer.Api/Program.cs`
- Test: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameApiTests.cs`
- Create evidence: `.superpowers/sdd/phase-2h-task-7-report.md`

**Interfaces:**

- Consumes: bearer-derived `PlayerSessionContext.PlayerId`, one `RoomMutationDeliveryGate` lease, and `IPlayerCombatIntentCoordinator`.
- Produces: exactly three `POST` routes, minimal request DTOs, `CombatIntentErrorResponse`, and latest `GameSnapshot` success bodies.

- [ ] **Step 1: Write failing route-count, auth, body, error, and success tests.**

Add tests named:

```csharp
CombatIntentRoutes_MapExactlyMeleeAttackRespondAndPass
CombatIntentRoutes_StartEndDamageAndResolveDamageRemainNotFound
CombatIntentRoutes_RequireBearerSessionAndMatchingRoom
CombatIntentRoutes_RequestBodiesCannotSpoofPlayerOrCanonicalFacts
CombatIntentRoutes_RejectMalformedIdsResponseAndRevision
CombatIntentRoutes_MapAuthorityAndStateFailuresWithoutInternalLeakage
CombatIntentRoutes_StaleRevisionReturnsConflictAndCurrentSafeRevision
CombatIntentRoutes_SuccessReturnsLatestViewerSpecificGameSnapshot
CombatIntentRoutes_HostHasNoPublicNpcSuperuserAuthority
```

Enumerate endpoint metadata and assert exact Combat paths:

```csharp
Assert.Equal(
    [
        "/api/rooms/{roomId:guid}/game/combat/melee-attack",
        "/api/rooms/{roomId:guid}/game/combat/pass",
        "/api/rooms/{roomId:guid}/game/combat/respond"
    ],
    combatRoutes);
```

POST each forbidden path and assert 404. Send extra `playerId`, `roll`, `target`, `responsePolicy`, `damage`, `nextActor`, and `round` JSON fields and prove they cannot alter the session-derived command or canonical outcome.

- [ ] **Step 2: Run RED.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameApiTests.CombatIntentRoutes" --nologo -v:minimal
```

Expected RED: route enumeration and success tests fail because the three routes are not mapped; existing route-absence tests may also need their approved expectation changed from zero to exactly three.

- [ ] **Step 3: Add exact route and request contracts.**

Map only:

```csharp
app.MapPost("/api/rooms/{roomId:guid}/game/combat/melee-attack", MeleeAttackAsync);
app.MapPost("/api/rooms/{roomId:guid}/game/combat/respond", RespondAsync);
app.MapPost("/api/rooms/{roomId:guid}/game/combat/pass", PassCombatAsync);
```

Use DTOs with no `PlayerId`:

```csharp
public sealed record MeleeAttackRequest(
    long ExpectedGameRevision,
    Guid ActorCharacterId,
    string? TargetParticipantId);

public sealed record CombatRespondRequest(
    long ExpectedGameRevision,
    string? ExchangeId,
    string? Response);

public sealed record CombatPassRequest(
    long ExpectedGameRevision,
    Guid ActorCharacterId);

public sealed record CombatIntentErrorResponse(
    string Code,
    long? CurrentGameRevision = null);
```

Parse response wire values exactly:

```csharp
private static bool TryParseCombatResponse(string? value, out CombatResponse response)
{
    response = value switch
    {
        "dodge" => CombatResponse.Dodge,
        "fight_back" => CombatResponse.FightBack,
        _ => default
    };
    return value is "dodge" or "fight_back";
}
```

- [ ] **Step 4: Keep each handler transport-only.**

Authenticate first, reject token-room mismatch, validate body shape, then hold exactly one outer gate lease:

```csharp
return await mutationGate.RunAsync(roomId, async () =>
{
    var result = await intents.MeleeAttackAsync(new PlayerMeleeAttackIntent(
        roomId,
        session.PlayerId,
        request.ExpectedGameRevision,
        request.ActorCharacterId,
        request.TargetParticipantId.Trim()));
    return result.IsSuccess
        ? Results.Ok(result.Snapshot)
        : ToCombatIntentError(result.Error!);
});
```

Use equivalent thin calls for Respond and Pass. `GameApi` does not call Begin/Resolve/Damage/Pass directly and does not publish. Map application errors exactly:

| Application code | HTTP | Wire code |
| --- | ---: | --- |
| `InvalidIntent` | 400 | `invalid_intent` |
| `InvalidResponse` | 400 | `invalid_response` |
| `InvalidSession` | 401 | `invalid_session` |
| `NotMember`, `ActorNotOwned`, `DefenderNotOwned` | 403 | `not_member`, `actor_not_owned`, `defender_not_owned` |
| `RoomNotFound`, `GameNotFound` | 404 | `room_not_found`, `game_not_found` |
| `StaleGameRevision`, `CombatInactive`, `NotCurrentActor`, `TargetNotEligible`, `ExchangeNotPending`, `ProgressionBlocked` | 409 | approved snake-case code |
| `CombatConsistencyFailure` | 500 | `combat_consistency_failure` |

Include `CurrentGameRevision` only after authenticated room membership is established. Never serialize exception messages or internal `GameErrorCode` names.

- [ ] **Step 5: Run GREEN and exact public-surface regression.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameApiTests.CombatIntentRoutes|FullyQualifiedName~GameApiTests.GameEndpoints|FullyQualifiedName~GameApiTests.InitializeAndGet" --nologo -v:minimal
```

Expected GREEN: exactly three Combat routes; success bodies are viewer-specific final `GameSnapshot`; all auth/error/body tests pass; Start/End/Damage/ResolveDamage remain 404.

- [ ] **Step 6: Commit Task 7 after GREEN.**

```powershell
git add multiplayer/server/src/Trpg.Multiplayer.Api/GameApi.cs multiplayer/server/src/Trpg.Multiplayer.Api/Program.cs multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/GameApiTests.cs .superpowers/sdd/phase-2h-task-7-report.md
git diff --cached --check
git commit -m "feat: expose player combat intent routes"
```

**Invariants:** `PlayerId` comes only from bearer session; one outer delivery gate covers the whole application sequence; internal coordinator names and results never cross HTTP; no public lifecycle or damage route.

**Stop/escalate:** stop if Minimal API binding accepts a body authority field that can affect command construction, if the handler needs `IGameStateStore`, or if a fourth Combat route is proposed.

---

### Task 8: Prove committed realtime ordering, partial-commit truth, and revision-neutral reconnect

**Files:**

- Test: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/SignalRGameDeliveryTests.cs`
- Test: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/DisconnectReconnectTests.cs`
- Test: `multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/PlayerCombatIntentCoordinatorTests.cs`
- Modify production only if a test exposes a defect inside the already-approved seams: `multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/PlayerCombatIntentCoordinator.cs`
- Create evidence: `.superpowers/sdd/phase-2h-task-8-report.md`

**Interfaces:**

- Consumes: existing per-transition notifier, `RoomMutationDeliveryGate`, SignalR `GameSnapshot`, AttachSession, and application failure results.
- Produces: evidence that committed intermediate states remain authoritative and no duplicate application publication exists.

- [ ] **Step 1: Write failing realtime viewer/revision tests.**

Add exact tests:

```csharp
PlayerMeleeAttack_NpcDamage_PublishesBeginResolveDamageOnlyAfterEachCommitInRevisionOrder
PlayerMeleeAttack_NpcNoDamage_PublishesBeginAndResolveExactlyOnce
PlayerMeleeAttack_HumanDefender_PublishesBeginOnlyWithViewerSpecificAffordances
PlayerCombatIntent_NonparticipantReceivesNullCombatAtEveryPublishedRevision
PlayerCombatIntent_RealtimeJsonNeverContainsPolicyAllowanceRollsStatsRegistryScheduleHistorySourceOrProvenance
PlayerCombatIntent_ApplicationCoordinatorAddsNoDuplicatePublish
```

For a damage path collect hub snapshots and assert:

```csharp
Assert.Equal([beginRevision, resolveRevision, damageRevision], received.Select(snapshot => snapshot.Revision));
Assert.All(received, snapshot => Assert.True(snapshot.Revision > requestRevision));
Assert.Equal(3, received.Select(snapshot => snapshot.Revision).Distinct().Count());
Assert.Equal(finalHttpSnapshot.Revision, received[^1].Revision);
```

At each notification, read canonical state and assert its revision equals the payload revision, proving commit-before-publish. Compare actor/defender/nonparticipant JSON separately; nonparticipant `Combat` is null and observers do not receive exact pending response.

- [ ] **Step 2: Write failing reconnect/disconnect tests.**

Add exact tests:

```csharp
PendingHumanResponse_DisconnectReconnectPreservesExactStateAndRestoresOwnerAffordance
Disconnect_DoesNotResolveRespondPassAdvanceRoundAdvanceTurnOrConsumeDamage
Reconnect_DoesNotMutateGameRevisionOrCanonicalCombat
PartialBeginThenResolvePreRngFailure_ReconnectSeesCommittedPendingWithoutWholeIntentRetry
PartialResolveThenDamagePreRngFailure_ReconnectSeesResolvedPendingDispositionWithoutWholeIntentRetry
PostRngDamageInvariantFailure_IsNotMappedRetryableAndNeverRerolls
LostSuccessRetryWithOldRevision_IsConflictWithoutDuplicateMutationOrPublish
```

Capture canonical before disconnect and after reconnect and assert:

```csharp
Assert.Equal(before.Revision, after.Revision);
Assert.Equal(before.Combat!.Round, after.Combat!.Round);
Assert.Equal(before.Combat.TurnIndex, after.Combat.TurnIndex);
Assert.Equal(before.Combat.PendingExchange, after.Combat.PendingExchange);
Assert.Equal(before.Combat.DamageDispositions, after.Combat.DamageDispositions);
Assert.Equal(before.Combat.DyingSchedule, after.Combat.DyingSchedule);
Assert.Equal(before.Combat.ActionCounts, after.Combat.ActionCounts);
Assert.Equal(before.Combat.ResponseCounts, after.Combat.ResponseCounts);
```

The reattached defender snapshot must contain the same `PendingResponse.ExchangeId`/responses; attacker and other viewers must not. Failure-injection tests assert no whole-intent retry and exact transition invocation counts.

- [ ] **Step 3: Run RED.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~SignalRGameDeliveryTests.PlayerCombatIntent|FullyQualifiedName~DisconnectReconnectTests.PendingHumanResponse|FullyQualifiedName~DisconnectReconnectTests.Disconnect_|FullyQualifiedName~DisconnectReconnectTests.Reconnect_|FullyQualifiedName~PlayerCombatIntentCoordinatorTests.Partial|FullyQualifiedName~PlayerCombatIntentCoordinatorTests.PostRng|FullyQualifiedName~PlayerCombatIntentCoordinatorTests.LostSuccess" --nologo -v:minimal
```

Expected RED: new integration/failure-boundary assertions fail until public orchestration is connected; record each failing method and reason.

- [ ] **Step 4: Make only minimal orchestration corrections exposed by RED.**

Allowed correction pattern:

```csharp
if (!resolve.IsSuccess)
{
    // Begin is already canonical. Report failure; never invoke Begin again.
    return MapTransitionFailure(resolve.Error!.Code, beginState.Revision);
}

if (!damage.IsSuccess)
{
    // Resolve is already canonical. Report failure; never invoke Begin or Resolve again.
    return MapTransitionFailure(damage.Error!.Code, resolvedState.Revision);
}
```

Do not add application-level notifier calls, rollback, whole-intent retry, disconnect response, background completion, or exception mapping that turns a post-RNG invariant failure into 409.

- [ ] **Step 5: Run GREEN for realtime/reconnect and stale snapshot monotonicity.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~SignalRGameDeliveryTests|FullyQualifiedName~DisconnectReconnectTests|FullyQualifiedName~PlayerCombatIntentCoordinatorTests.Partial|FullyQualifiedName~PlayerCombatIntentCoordinatorTests.PostRng|FullyQualifiedName~PlayerCombatIntentCoordinatorTests.LostSuccess" --nologo -v:minimal
```

Expected GREEN: all selected tests pass; ordered publications equal canonical committed revisions; partial states survive; reconnect changes no Game revision; no duplicate notifier call or internal data leak.

- [ ] **Step 6: Commit Task 8 after GREEN.**

```powershell
git add multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/PlayerCombatIntentCoordinator.cs multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/SignalRGameDeliveryTests.cs multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Realtime/DisconnectReconnectTests.cs multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Gameplay/PlayerCombatIntentCoordinatorTests.cs .superpowers/sdd/phase-2h-task-8-report.md
git diff --cached --check
git commit -m "test: cover player combat intent delivery"
```

**Invariants:** already committed state is never rolled back; reconnect is a read-only projection; disconnect changes room presence only; post-RNG failure is non-retryable; no automatic action replay.

**Stop/escalate:** stop if satisfying tests requires suppressing intermediate canonical snapshots, adding rollback/transaction aggregation, mutating on disconnect/reconnect, or adding a distributed retry registry. Those change the approved architecture.

---

### Task 9: Add thin Vue controls driven only by projected affordances

**Files:**

- Modify: `multiplayer/client/src/contracts/rooms.ts`
- Modify: `multiplayer/client/src/api/rooms.ts`
- Modify: `multiplayer/client/src/api/client.ts`
- Test: `multiplayer/client/src/api/client.test.ts`
- Modify: `multiplayer/client/src/components/LobbyView.vue`
- Test: `multiplayer/client/src/components/HomeView.test.ts`
- Test: `multiplayer/client/src/state/gameSnapshot.test.ts`
- Create evidence: `.superpowers/sdd/phase-2h-task-9-report.md`

**Interfaces:**

- Consumes: server `CombatViewerActionsSnapshot`, exact participant labels, structured errors, and authoritative `GameSnapshot`.
- Produces: target selector plus Attack/Pass/Dodge/Fight Back controls with no client rules engine.

- [ ] **Step 1: Write failing TypeScript/API contract tests.**

Define fixtures with:

```ts
viewerActions: {
  actorCharacterId: 'character-guid',
  canMeleeAttack: true,
  canPass: true,
  eligibleTargetParticipantIds: ['opponent:0'],
  pendingResponse: null,
}
```

Add exact API tests:

```ts
it('sends only expectedGameRevision actorCharacterId and projected target for melee attack')
it('sends only expectedGameRevision exchangeId and projected response for respond')
it('sends only expectedGameRevision and actorCharacterId for pass')
it('keeps bearer token out of URL and JSON bodies for all combat intents')
it('preserves sanitized structured stale code and current revision without response text')
```

Assert exact paths and JSON equality; reject any `playerId`, roll, target stats, policy, damage, HP, next actor, or round property.

- [ ] **Step 2: Write failing component and monotonic snapshot tests.**

Add exact component tests:

```ts
it('renders Attack and Pass only from projected actor affordances and targets')
it('renders Dodge and Fight Back only from projected pending response values')
it('never renders another viewer response choices or unprojected targets')
it('submits one intent then waits for authoritative snapshot without optimistic combat changes')
it('refreshes on stale conflict and never automatically replays the intent')
it('contains no Start End Damage or NPC actor controls')
it('contains no client combat legality dice opposed damage turn or round calculation')
```

Extend `gameSnapshot.test.ts` with Combat-specific lower-revision rejection:

```ts
expect(shouldAcceptGameSnapshot(
  combatSnapshot(12, { canPass: true }),
  combatSnapshot(11, { canPass: false }),
  'room-1',
)).toBe(false);
```

- [ ] **Step 3: Run RED.**

```powershell
Push-Location multiplayer/client
npm test -- --run src/api/client.test.ts src/components/HomeView.test.ts src/state/gameSnapshot.test.ts
Pop-Location
```

Expected RED: TypeScript/test failures identify missing contracts, three API methods, controls, stale refresh, and Combat-specific monotonic assertion.

- [ ] **Step 4: Add exact read-only TypeScript contracts and API methods.**

Use server-matching names:

```ts
export interface CombatPendingResponseSnapshot {
  exchangeId: string;
  availableResponses: Array<'dodge' | 'fight_back'>;
}

export interface CombatViewerActionsSnapshot {
  actorCharacterId: string | null;
  canMeleeAttack: boolean;
  canPass: boolean;
  eligibleTargetParticipantIds: string[];
  pendingResponse: CombatPendingResponseSnapshot | null;
}

export interface PlayerMeleeAttackRequest {
  expectedGameRevision: number;
  actorCharacterId: string;
  targetParticipantId: string;
}

export interface PlayerCombatRespondRequest {
  expectedGameRevision: number;
  exchangeId: string;
  response: 'dodge' | 'fight_back';
}

export interface PlayerCombatPassRequest {
  expectedGameRevision: number;
  actorCharacterId: string;
}
```

Add `viewerActions?: CombatViewerActionsSnapshot | null` to `CombatSnapshot`. Add `RoomsApi.meleeAttack`, `RoomsApi.respondToCombat`, and `RoomsApi.passCombatTurn`, each returning `Promise<GameSnapshot>` from the exact three routes with bearer header and exact request body.

Extend `ApiRequestError` with optional safe `serverCode` and `currentGameRevision`. Parse JSON only for approved structured error properties and never retain raw body/exception text. A 409 `stale_game_revision` is nonterminal and triggers one `getGame`; it never retries the mutation.

- [ ] **Step 5: Render only projected choices and submit without optimistic state.**

In `LobbyView.vue`, keep local state limited to selected projected target, busy, and safe error. Derive no legality beyond direct field presence:

```vue
<select
  v-if="gameSnapshot.combat?.viewerActions?.canMeleeAttack"
  v-model="selectedTargetParticipantId"
  data-testid="combat-target"
>
  <option
    v-for="participantId in gameSnapshot.combat.viewerActions.eligibleTargetParticipantIds"
    :key="participantId"
    :value="participantId"
  >
    {{ combatParticipantLabel(gameSnapshot, participantId) }}
  </option>
</select>
<button v-if="gameSnapshot.combat?.viewerActions?.canMeleeAttack" data-testid="combat-attack" type="button">Attack</button>
<button v-if="gameSnapshot.combat?.viewerActions?.canPass" data-testid="combat-pass" type="button">Pass</button>
<button
  v-for="response in gameSnapshot.combat?.viewerActions?.pendingResponse?.availableResponses ?? []"
  :key="response"
  :data-testid="`combat-response-${response}`"
  type="button"
>
  {{ response === 'dodge' ? 'Dodge' : 'Fight Back' }}
</button>
```

Handlers submit `gameSnapshot.revision` plus exact projected IDs, set busy, and emit only the returned/refreshed `GameSnapshot`. They do not change participant/order/round/pending/last-exchange locally. After stale conflict, call `getGame`, emit it, display a safe “Combat changed; choose again” message, and stop.

- [ ] **Step 6: Run GREEN and client build.**

```powershell
Push-Location multiplayer/client
npm test -- --run src/api/client.test.ts src/components/HomeView.test.ts src/state/gameSnapshot.test.ts
npm run build
Pop-Location
```

Expected GREEN: selected Vitest files and TypeScript/Vite build pass. Record exact test count and build result.

- [ ] **Step 7: Commit Task 9 after GREEN.**

```powershell
git add multiplayer/client/src/contracts/rooms.ts multiplayer/client/src/api/rooms.ts multiplayer/client/src/api/client.ts multiplayer/client/src/api/client.test.ts multiplayer/client/src/components/LobbyView.vue multiplayer/client/src/components/HomeView.test.ts multiplayer/client/src/state/gameSnapshot.test.ts .superpowers/sdd/phase-2h-task-9-report.md
git diff --cached --check
git commit -m "feat: add player combat intent controls"
```

**Invariants:** UI options are a direct subset of `ViewerActions`; target labels come from safe participant snapshots; no optimistic mutation or automatic stale replay; Start/End/Damage/NPC controls remain absent.

**Stop/escalate:** stop if the UI needs to calculate current actor legality, target side/activity, defender ownership, response availability, dice, opposed result, damage, HP, next actor, or round. The missing fact must be projected by the server after review.

---

### Task 10: Aggregate security, scope, conformance, and full-product validation

**Files:**

- Test/inspect all Phase 2H files listed in the Planned File Structure.
- Modify factual docs only after all implementation gates pass: `docs/CURRENT_STATE.md`
- Modify factual docs only after all implementation gates pass: `docs/HANDOFF.md`
- Modify factual docs only after all implementation gates pass: `docs/ARCHITECTURE.md`
- Create aggregate evidence: `.superpowers/sdd/phase-2h-implementation-report.md`

**Interfaces:**

- Consumes: Tasks 1–9 and all Phase 2F/2G validation gates.
- Produces: audited evidence that Phase 2H added only the approved protocol and thin controls.

- [ ] **Step 1: Add/finalize aggregate scope-guard tests before changing docs.**

Server source-boundary assertions must prove:

```csharp
Assert.Equal(3, combatRoutes.Count);
Assert.DoesNotContain(combatRoutes, route => route.Contains("/start", StringComparison.OrdinalIgnoreCase));
Assert.DoesNotContain(combatRoutes, route => route.Contains("/end", StringComparison.OrdinalIgnoreCase));
Assert.DoesNotContain(combatRoutes, route => route.Contains("damage", StringComparison.OrdinalIgnoreCase));
Assert.DoesNotContain(typeof(PlayerCombatIntentCoordinator).GetConstructors().Single().GetParameters(),
    parameter => parameter.ParameterType == typeof(IGameStateStore));
```

Client source-boundary assertions must reject public Start/End/Damage/NPC actor methods/buttons and reject canonical calculation symbols such as `rollDice`, `parseDice`, `calculateInitiative`, `calculateEligibleTargets`, `resolveOpposed`, `calculateDamage`, `applyDamage`, `advanceTurn`, or `advanceRound`.

- [ ] **Step 2: Run aggregate RED if any scope guard was newly added.**

```powershell
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --filter "FullyQualifiedName~GameApiTests.CombatIntentRoutes|FullyQualifiedName~PlayerCombatIntentCoordinatorTests.PlayerCombatIntentCoordinator_HasOnlyApprovedStateAndTransitionDependencies|FullyQualifiedName~GameStateTests.Projection_CombatViewerActions" --nologo -v:minimal
Push-Location multiplayer/client
npm test -- --run src/api/client.test.ts src/components/HomeView.test.ts src/state/gameSnapshot.test.ts
Pop-Location
```

Expected RED only when a new guard exposes an actual omission. If all behavior was already covered, record that the aggregate guard was added while existing implementation remained GREEN; do not manufacture a failure by weakening code or tests.

- [ ] **Step 3: Make the minimum correction for any genuine aggregate RED.**

Allowed corrections are limited to removing an unintended extra route/control/dependency/calculation or completing a missing approved assertion. Do not add capabilities. The target inventory is exact:

```text
Melee Attack: YES
Respond: YES
Pass: YES
Start: NO
End: NO
Damage: NO
NPC attacker driver: NO
PvP: NO
IntentId registry: NO
Direct PlayerCombatIntentCoordinator → IGameStateStore: NO
```

- [ ] **Step 4: Run clean client install, full tests, and build.**

```powershell
Push-Location multiplayer/client
npm ci
npm test -- --run
npm run build
Pop-Location
```

Expected: clean install, all Vitest tests, and Vite build pass. Record exact client passed/failed/skipped counts.

- [ ] **Step 5: Run full server restore/build/test/format.**

```powershell
dotnet restore multiplayer/server/Trpg.Multiplayer.slnx --nologo
dotnet build multiplayer/server/Trpg.Multiplayer.slnx --no-restore --nologo -v:minimal
dotnet test multiplayer/server/Trpg.Multiplayer.slnx --no-restore --nologo -v:minimal
dotnet format multiplayer/server/Trpg.Multiplayer.slnx --verify-no-changes --no-restore --verbosity minimal
```

Expected: all pass. Record exact server test count, warnings, errors, and format result.

- [ ] **Step 6: Verify conformance fixture counts and Combat Damage determinism.**

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
  if ($json.cases.Count -ne $entry.Value) {
    throw "$($entry.Key): expected $($entry.Value), got $($json.cases.Count)"
  }
}
node multiplayer/server/tests/Fixtures/export-combat-damage-conformance.js
$damageSha1 = (Get-FileHash multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/combat-damage.json -Algorithm SHA256).Hash
node multiplayer/server/tests/Fixtures/export-combat-damage-conformance.js
$damageSha2 = (Get-FileHash multiplayer/server/tests/Trpg.Multiplayer.Api.Tests/Fixtures/combat-damage.json -Algorithm SHA256).Hash
if ($damageSha1 -ne $damageSha2) { throw 'Combat Damage fixture SHA mismatch' }
```

Expected: Check 19, HP 10, Stabilization 21, Combat Opposed 19, Combat Damage 48, and identical Combat Damage SHA. Any fixture diff is a blocker because Phase 2H has no rule-migration authority.

- [ ] **Step 7: Run all 37 authoritative Single Player regressions and legacy alias.**

```powershell
$regressions = @(Get-ChildItem build -File -Filter 'test-*.js' | Sort-Object Name)
if ($regressions.Count -ne 37) { throw "Expected 37 regressions, got $($regressions.Count)" }
foreach ($test in $regressions) {
  node $test.FullName
  if ($LASTEXITCODE -ne 0) { throw "Regression failed: $($test.Name)" }
}
node build/test-protocol-stability.js
if ($LASTEXITCODE -ne 0) { throw 'legacy test-protocol-stability alias failed' }
```

Expected: 37/37 authoritative offline regressions pass and the legacy alias reports PASS. Credentialed real API testing is not required.

- [ ] **Step 8: Run all 69 JavaScript syntax checks.**

```powershell
$scripts = @(Get-ChildItem src,build -Recurse -File -Filter '*.js' | Sort-Object FullName)
if ($scripts.Count -ne 69) { throw "Expected 69 JS files, got $($scripts.Count)" }
foreach ($script in $scripts) {
  node --check $script.FullName
  if ($LASTEXITCODE -ne 0) { throw "Syntax failed: $($script.FullName)" }
}
```

Expected: 69/69 pass.

- [ ] **Step 9: Verify formal HTML remains byte-identical.**

```powershell
$approvedFormalSha = '0A635D94CDD7284B35433092C834D92BCAD44961C14E30DBD565AB48B7E14D4D'
$before = (Get-FileHash outputs/trpg-dm-assistant.html -Algorithm SHA256).Hash
if ($before -ne $approvedFormalSha) { throw "Unexpected formal baseline $before" }
node build/build-single-html.js
node build/verify-single-html.js
$first = (Get-FileHash outputs/trpg-dm-assistant.html -Algorithm SHA256).Hash
node build/build-single-html.js
$second = (Get-FileHash outputs/trpg-dm-assistant.html -Algorithm SHA256).Hash
if ($first -ne $second -or $second -ne $approvedFormalSha) {
  throw "Formal artifact changed: $first / $second"
}
if (@(Get-ChildItem outputs -File -Filter '*.html').Count -ne 1) {
  throw 'Formal output inventory changed'
}
git diff --exit-code -- outputs/trpg-dm-assistant.html
```

Expected: `VERIFY_SINGLE_HTML:PASS`, one HTML output, identical double-build SHA, exact approved SHA, and no artifact diff.

- [ ] **Step 10: Run strict UTF-8, diff, and forbidden-surface audit.**

```powershell
git diff --check
$strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
$changedText = @(git diff --name-only --diff-filter=ACMRT | Where-Object {
  $_ -match '\.(cs|ts|vue|md|json|js|yml|yaml|slnx|csproj)$'
})
foreach ($path in $changedText) {
  [void][IO.File]::ReadAllText((Resolve-Path $path), $strictUtf8)
}
rg -n -S 'IGameStateStore|IDiceRoller|ICombatDamageEngine|IHpDamageEngine|IGameRealtimeNotifier|HubContext' multiplayer/server/src/Trpg.Multiplayer.Api/Gameplay/PlayerCombatIntentCoordinator.cs
rg -n -S 'combat/(start|end|damage|resolve-damage)|Npc.*(Attack|Pass)|HostGameplaySuperuser' multiplayer/server/src multiplayer/client/src
rg -n -S 'rollDice|parseDice|calculateInitiative|calculateEligibleTargets|resolveOpposed|calculateDamage|applyDamage|advanceTurn|advanceRound' multiplayer/client/src
git diff --stat
```

Expected: `git diff --check` and strict UTF-8 pass; forbidden `rg` searches return no production matches; diffs are confined to planned Multiplayer/docs/evidence files; Single Player and formal artifact are unchanged.

- [ ] **Step 11: Update factual documentation and aggregate report.**

Record actual feature SHAs, RED/GREEN commands and counts, server/client/full gates, route count 3, no direct store dependency, policy privacy, Begin/Resolve returned-state use, fresh final projection, stale/no-side-effect evidence, partial-commit recovery, ordered SignalR revisions, reconnect neutrality, and every deferred feature. Do not claim NPC attacker gameplay, PvP, public lifecycle/damage, Firearms/Impaling, persistence, Scenario, or AI gameplay.

- [ ] **Step 12: Commit factual validation documentation and push only when authorized.**

```powershell
git add docs/CURRENT_STATE.md docs/HANDOFF.md docs/ARCHITECTURE.md .superpowers/sdd/phase-2h-implementation-report.md
git diff --cached --check
git diff --cached --name-only
git commit -m "docs: record player combat intent validation"
git push origin HEAD:main
git status -sb
git rev-parse HEAD
git rev-parse origin/main
git rev-list --left-right --count origin/main...HEAD
```

Expected: final approved Phase 2H feature commit only, non-force push, `HEAD == origin/main`, divergence `0 0`, and clean workspace. Stop; do not begin another Combat slice.

**Invariants:** exact public Combat route count 3; no public Start/End/Damage/NPC authority; no direct store access; no client canonical authority; all Phase 2F/2G, conformance, Single Player, syntax, and artifact baselines remain green.

**Stop/escalate:** stop on any failed full gate, fixture/artifact SHA drift, non-UTF-8 file, unexpected pre-existing diff, forbidden dependency/control, or need to broaden scope. Do not hide a failure, update an approved SHA, or push partial/failed work.

---

## Final Self-Review Gate

Before implementation authorization is considered complete, the reviewer must answer YES to every item:

- The coordinator constructor contains exactly `IGameCoordinator` and `IInternalCombatResolutionCoordinator`, never `IGameStateStore`.
- Pre-mutation legality comes from the viewer projection, and every internal transition still revalidates canonical state before mutation/dice.
- NPC defender selection uses only the typed private policy in `BeginOpposedExchangeResult.State`.
- Damage selection uses only the exact disposition in `ResolvePendingExchangeResult.State` and that returned revision.
- Final HTTP success is a fresh viewer-specific `GameSnapshot`, never internal state or a transition result.
- A legal human defender causes Begin only; only its owner receives exact response affordance.
- Stale revision on all three intents produces no mutation, dice, exchange ID, revision, or broadcast.
- Partial commits remain canonical, reconnectable, and are never rolled back or replayed as a whole intent.
- Every canonical changed transition publishes once after commit; the application coordinator publishes zero times.
- Disconnect performs no response/pass/advance/damage and reconnect changes no Game revision.
- Public Combat routes are exactly melee attack/respond/pass; Start/End/Damage/ResolveDamage remain 404.
- Vue displays only projected targets/actions, performs no Combat calculation, and never auto-replays a stale action.
- NPC attacker driver, public NPC actor/pass, Host Gameplay Superuser, PvP, Firearms, Impaling, weapon switching, movement/range, timeout/forfeit, AI gameplay, Scenario, persistence, DB, and Redis remain absent.
- Full server/client/conformance/Single Player/syntax/formal/UTF-8/diff gates pass with actual evidence.

## Execution Handoff

Plan complete at `docs/superpowers/plans/2026-09-08-player-combat-intent-protocol-implementation.md`. After a separate implementation authorization, use subagent-driven development task-by-task with review between tasks, or use executing-plans inline with explicit checkpoints. Do not start Phase 2H implementation from this documentation-only planning task.
