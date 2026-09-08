using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Text.RegularExpressions;
using Trpg.Multiplayer.Api.Gameplay;
using Trpg.Multiplayer.Api.Realtime;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests.Gameplay;

public sealed class PlayerCombatIntentCoordinatorTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Pass_OwnCurrentInvestigator_CommitsOnePassAndReturnsFreshProjection()
    {
        var rig = MeleeAttackRig.Create(finalRevision: 13);
        var result = await InvokePassAsync(rig.Coordinator, CreatePassIntent(rig));

        Assert.True(GetProperty<bool>(result, "IsSuccess"));
        var passCommand = Assert.Single(rig.Combat.PassCommands);
        Assert.Equal(rig.RoomId, GetProperty<Guid>(passCommand, "RoomId"));
        Assert.Equal(rig.PlayerId, GetProperty<Guid>(passCommand, "RequestingPlayerId"));
        Assert.Equal(rig.ExpectedRevision, GetProperty<long>(passCommand, "ExpectedGameRevision"));
        Assert.Equal(rig.ExpectedRevision + 1, GetProperty<GameSnapshot>(result, "Snapshot")!.Revision);
    }

    [Fact]
    public async Task Pass_OtherPlayersCharacter_ReturnsActorNotOwnedBeforeMutation()
    {
        var rig = MeleeAttackRig.Create(actorViewerOwned: false);
        var result = await InvokePassAsync(rig.Coordinator, CreatePassIntent(rig));
        AssertPassFailureWithoutMutation(rig, result, "ActorNotOwned", 12);
    }

    [Fact]
    public async Task Pass_OwnButNonCurrentInvestigator_ReturnsNotCurrentActorBeforeMutation()
    {
        var rig = MeleeAttackRig.Create(actorCurrent: false);
        var result = await InvokePassAsync(rig.Coordinator, CreatePassIntent(rig));
        AssertPassFailureWithoutMutation(rig, result, "NotCurrentActor", 12);
    }

    [Fact]
    public async Task Pass_HostCannotPassForUnownedNpc()
    {
        var rig = MeleeAttackRig.Create(actorHasCharacter: false);
        var result = await InvokePassAsync(rig.Coordinator, CreatePassIntent(rig));
        AssertPassFailureWithoutMutation(rig, result, "ActorNotOwned", 12);
    }

    [Fact]
    public async Task Pass_PendingExchangeOrDamageBlocker_IsRejectedWithoutMutation()
    {
        var damageDispositionRig = MeleeAttackRig.Create(hasPendingDamage: true);
        var pendingExchangeRig = MeleeAttackRig.Create(hasPendingExchange: true);
        var damageDispositionResult = await InvokePassAsync(
            damageDispositionRig.Coordinator,
            CreatePassIntent(damageDispositionRig));
        var pendingExchangeResult = await InvokePassAsync(
            pendingExchangeRig.Coordinator,
            CreatePassIntent(pendingExchangeRig));

        Assert.False(damageDispositionRig.InitialProjection.Combat!.ViewerActions!.CanPass);
        Assert.NotEmpty(damageDispositionRig.InitialCanonicalState.Combat!.DamageDispositions);
        AssertPassFailureWithoutMutation(
            damageDispositionRig,
            damageDispositionResult,
            "ProgressionBlocked",
            12);
        Assert.False(pendingExchangeRig.InitialProjection.Combat!.ViewerActions!.CanPass);
        Assert.NotNull(pendingExchangeRig.InitialCanonicalState.Combat!.PendingExchange);
        AssertPassFailureWithoutMutation(
            pendingExchangeRig,
            pendingExchangeResult,
            "ProgressionBlocked",
            12);
    }

    [Fact]
    public async Task Pass_StaleRevisionPerformsNoPassRevisionOrPublish()
    {
        var rig = MeleeAttackRig.Create(projectedRevision: 13, expectedRevision: 12);
        var result = await InvokePassAsync(rig.Coordinator, CreatePassIntent(rig));
        AssertPassFailureWithoutMutation(rig, result, "StaleGameRevision", 13);
    }

    [Fact]
    public async Task Pass_RoundWrapRemainsCanonicalAndSchedulesDyingOnce()
    {
        var rig = MeleeAttackRig.Create(finalRevision: 13);
        var result = await InvokePassAsync(rig.Coordinator, CreatePassIntent(rig));

        Assert.True(GetProperty<bool>(result, "IsSuccess"));
        Assert.Single(rig.Combat.PassCommands);
        Assert.Empty(rig.Combat.BeginCommands);
        Assert.Empty(rig.Combat.ResolveCommands);
        Assert.Empty(rig.Combat.DamageCommands);
        Assert.Equal(13, GetProperty<GameSnapshot>(result, "Snapshot")!.Revision);
    }

    [Fact]
    public async Task MeleeAttack_OwnCurrentInvestigatorAgainstProjectedNpc_BeginsResolvesAndConsumesExactDamage()
    {
        var rig = MeleeAttackRig.Create(hasPendingDamage: true);
        var result = await InvokeMeleeAttackAsync(rig.Coordinator, rig.Intent);

        Assert.True(GetProperty<bool>(result, "IsSuccess"));
        var begin = Assert.Single(rig.Combat.BeginCommands);
        Assert.Equal(12, GetProperty<long>(begin, "ExpectedGameRevision"));
        Assert.Equal($"character:{rig.ActorCharacterId}", GetProperty<string>(begin, "AttackerParticipantId"));
        Assert.Equal("opponent:0", GetProperty<string>(begin, "DefenderParticipantId"));
        var resolve = Assert.Single(rig.Combat.ResolveCommands);
        Assert.Null(GetProperty<Guid?>(resolve, "RequestingPlayerId"));
        Assert.Equal(13, GetProperty<long>(resolve, "ExpectedGameRevision"));
        Assert.Equal(rig.ExchangeId, GetProperty<string>(resolve, "ExchangeId"));
        Assert.Equal(CombatResponse.Dodge, GetProperty<CombatResponse>(resolve, "Response"));
        var damage = Assert.Single(rig.Combat.DamageCommands);
        Assert.Equal(14, GetProperty<long>(damage, "ExpectedGameRevision"));
        Assert.Equal(GetProperty<string>(resolve, "ExchangeId"), GetProperty<string>(damage, "ExchangeId"));
        Assert.Equal(15, GetProperty<GameSnapshot>(result, "Snapshot")!.Revision);
    }

    [Fact]
    public async Task MeleeAttack_HumanDefender_CommitsBeginOnlyAndNeverSelectsResponseOrDamage()
    {
        var rig = MeleeAttackRig.Create(defenderOwnerPlayerId: Guid.NewGuid(), finalRevision: 14);
        var result = await InvokeMeleeAttackAsync(rig.Coordinator, rig.Intent);

        Assert.True(GetProperty<bool>(result, "IsSuccess"));
        Assert.Single(rig.Combat.BeginCommands);
        Assert.Empty(rig.Combat.ResolveCommands);
        Assert.Empty(rig.Combat.DamageCommands);
        Assert.Equal(14, GetProperty<GameSnapshot>(result, "Snapshot")!.Revision);
    }

    [Fact]
    public async Task MeleeAttack_StaleRevision_PerformsNoBeginResolveDamageDiceIdRevisionOrPublish()
    {
        var rig = MeleeAttackRig.Create(projectedRevision: 13, expectedRevision: 12);
        var result = await InvokeMeleeAttackAsync(rig.Coordinator, rig.Intent);
        AssertFailureWithoutMutation(rig, result, "StaleGameRevision", 13);
    }

    [Fact]
    public async Task MeleeAttack_OtherPlayersCharacter_ReturnsActorNotOwnedBeforeDiceOrMutation()
    {
        var rig = MeleeAttackRig.Create(actorViewerOwned: false);
        var result = await InvokeMeleeAttackAsync(rig.Coordinator, rig.Intent);
        AssertFailureWithoutMutation(rig, result, "ActorNotOwned", 12);
    }

    [Fact]
    public async Task MeleeAttack_OwnButNonCurrentInvestigator_ReturnsNotCurrentActorBeforeDiceOrMutation()
    {
        var rig = MeleeAttackRig.Create(actorCurrent: false);
        var result = await InvokeMeleeAttackAsync(rig.Coordinator, rig.Intent);
        AssertFailureWithoutMutation(rig, result, "NotCurrentActor", 12);
    }

    [Fact]
    public async Task MeleeAttack_IneligibleTarget_ReturnsTargetNotEligibleBeforeDiceOrMutation()
    {
        var rig = MeleeAttackRig.Create(eligibleTargets: ["opponent:1"]);
        var result = await InvokeMeleeAttackAsync(rig.Coordinator, rig.Intent);
        AssertFailureWithoutMutation(rig, result, "TargetNotEligible", 12);
    }

    [Fact]
    public async Task MeleeAttack_HostCannotActAsUnownedNpc()
    {
        var rig = MeleeAttackRig.Create(actorHasCharacter: false);
        var result = await InvokeMeleeAttackAsync(rig.Coordinator, rig.Intent);
        AssertFailureWithoutMutation(rig, result, "ActorNotOwned", 12);
    }

    [Fact]
    public async Task MeleeAttack_NpcPolicyOutsidePendingResponses_FailsClosedBeforeResolveDice()
    {
        var rig = MeleeAttackRig.Create(
            npcResponsePolicy: CombatResponse.FightBack,
            pendingResponses: [CombatResponse.Dodge]);
        var result = await InvokeMeleeAttackAsync(rig.Coordinator, rig.Intent);

        Assert.False(GetProperty<bool>(result, "IsSuccess"));
        var error = GetProperty<object>(result, "Error")!;
        Assert.Equal("CombatConsistencyFailure", GetProperty<object>(error, "Code")!.ToString());
        Assert.Equal(13, GetProperty<long?>(error, "CurrentGameRevision"));
        Assert.Single(rig.Combat.BeginCommands);
        Assert.Empty(rig.Combat.ResolveCommands);
        Assert.Empty(rig.Combat.DamageCommands);
        Assert.Equal(0, rig.Combat.DiceRolls);
    }

    [Fact]
    public async Task MeleeAttack_UsesBeginReturnedStateWithoutStoreRead()
    {
        var rig = MeleeAttackRig.Create(beginRevision: 31, resolveRevision: 32, finalRevision: 33);
        await InvokeMeleeAttackAsync(rig.Coordinator, rig.Intent);

        var resolve = Assert.Single(rig.Combat.ResolveCommands);
        Assert.Equal(31, GetProperty<long>(resolve, "ExpectedGameRevision"));
        Assert.Equal(rig.ExchangeId, GetProperty<string>(resolve, "ExchangeId"));
        Assert.Equal(CombatResponse.Dodge, GetProperty<CombatResponse>(resolve, "Response"));
        Assert.Equal(2, rig.Games.ProjectionCalls);
    }

    [Fact]
    public async Task MeleeAttack_UsesResolveReturnedStateAndLatestRevisionForExactDamage()
    {
        var rig = MeleeAttackRig.Create(
            hasPendingDamage: true,
            resolveRevision: 41,
            finalRevision: 42,
            unrelatedPendingExchangeId: "other-exchange");
        await InvokeMeleeAttackAsync(rig.Coordinator, rig.Intent);

        var damage = Assert.Single(rig.Combat.DamageCommands);
        Assert.Equal(41, GetProperty<long>(damage, "ExpectedGameRevision"));
        Assert.Equal(rig.ExchangeId, GetProperty<string>(damage, "ExchangeId"));
    }

    [Fact]
    public async Task MeleeAttack_NoHitDoesNotInvokeDamage()
    {
        var rig = MeleeAttackRig.Create(hasPendingDamage: false);
        var result = await InvokeMeleeAttackAsync(rig.Coordinator, rig.Intent);

        Assert.True(GetProperty<bool>(result, "IsSuccess"));
        Assert.Single(rig.Combat.ResolveCommands);
        Assert.Empty(rig.Combat.DamageCommands);
    }

    [Fact]
    public async Task MeleeAttack_ReturnsFreshFinalViewerProjection()
    {
        var rig = MeleeAttackRig.Create(finalRevision: 99);
        var result = await InvokeMeleeAttackAsync(rig.Coordinator, rig.Intent);

        Assert.Same(rig.FinalProjection, GetProperty<GameSnapshot>(result, "Snapshot"));
        Assert.Equal(2, rig.Games.ProjectionCalls);
    }

    [Fact]
    public async Task Respond_ExactHumanDefenderOwner_ResolvesAndConsumesExactPendingDamage()
    {
        var rig = RespondRig.Create(hasPendingDamage: true);
        var result = await InvokeRespondAsync(rig.Coordinator, rig.Intent);

        Assert.True(GetProperty<bool>(result, "IsSuccess"));
        var resolveCommand = Assert.Single(rig.Combat.ResolveCommands);
        Assert.Equal(rig.DefenderPlayerId, GetProperty<Guid?>(resolveCommand, "RequestingPlayerId"));
        Assert.Equal(12, GetProperty<long>(resolveCommand, "ExpectedGameRevision"));
        Assert.Equal(rig.ExchangeId, GetProperty<string>(resolveCommand, "ExchangeId"));
        Assert.Equal(CombatResponse.FightBack, GetProperty<CombatResponse>(resolveCommand, "Response"));
        var damageCommand = Assert.Single(rig.Combat.DamageCommands);
        Assert.Equal(14, GetProperty<long>(damageCommand, "ExpectedGameRevision"));
        Assert.Equal(rig.ExchangeId, GetProperty<string>(damageCommand, "ExchangeId"));
    }

    [Fact]
    public async Task Respond_ExactHumanDefenderOwner_NoHitDoesNotCallDamage()
    {
        var rig = RespondRig.Create(hasPendingDamage: false);
        var result = await InvokeRespondAsync(rig.Coordinator, rig.Intent);

        Assert.True(GetProperty<bool>(result, "IsSuccess"));
        Assert.Single(rig.Combat.ResolveCommands);
        Assert.Empty(rig.Combat.DamageCommands);
    }

    [Fact]
    public async Task Respond_WrongPlayerReturnsDefenderNotOwnedBeforeDiceOrMutation()
    {
        var rig = RespondRig.Create(includeOwnedPendingResponse: false);
        var result = await InvokeRespondAsync(rig.Coordinator, rig.Intent);

        AssertRespondFailureWithoutMutation(rig, result, "DefenderNotOwned", 12);
        Assert.NotNull(rig.InitialProjection.Combat!.Pending);
        Assert.Null(rig.InitialProjection.Combat.ViewerActions);
        Assert.Null(GetProperty<GameSnapshot>(result, "Snapshot"));
    }

    [Fact]
    public async Task Respond_WrongExchangeReturnsExchangeNotPendingBeforeDiceOrMutation()
    {
        var rig = RespondRig.Create(requestedExchangeId: "exchange-other");
        var result = await InvokeRespondAsync(rig.Coordinator, rig.Intent);

        AssertRespondFailureWithoutMutation(rig, result, "ExchangeNotPending", 12);
    }

    [Fact]
    public async Task Respond_UnavailableResponseReturnsInvalidResponseBeforeDiceOrMutation()
    {
        var rig = RespondRig.Create(availableResponses: ["dodge"]);
        var result = await InvokeRespondAsync(rig.Coordinator, rig.Intent);

        AssertRespondFailureWithoutMutation(rig, result, "InvalidResponse", 12);
    }

    [Fact]
    public async Task Respond_StaleRevisionPerformsNoResolveDamageDiceRevisionOrPublish()
    {
        var rig = RespondRig.Create(expectedRevision: 11);
        var result = await InvokeRespondAsync(rig.Coordinator, rig.Intent);

        AssertRespondFailureWithoutMutation(rig, result, "StaleGameRevision", 12);
    }

    [Fact]
    public async Task Respond_UsesResolveReturnedStateAndLatestRevisionForExactDamage()
    {
        var rig = RespondRig.Create(
            resolveRevision: 41,
            finalRevision: 42,
            hasPendingDamage: true,
            unrelatedPendingExchangeId: "other-exchange");
        await InvokeRespondAsync(rig.Coordinator, rig.Intent);

        var damageCommand = Assert.Single(rig.Combat.DamageCommands);
        Assert.Equal(41, GetProperty<long>(damageCommand, "ExpectedGameRevision"));
        Assert.Equal(rig.ExchangeId, GetProperty<string>(damageCommand, "ExchangeId"));
    }

    [Fact]
    public async Task Respond_ReturnsFreshFinalViewerProjection()
    {
        var rig = RespondRig.Create(finalRevision: 99);
        var result = await InvokeRespondAsync(rig.Coordinator, rig.Intent);

        Assert.Same(rig.FinalProjection, GetProperty<GameSnapshot>(result, "Snapshot"));
        Assert.Equal(2, rig.Games.ProjectionCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Respond_CombatAbsentOrInactiveReturnsCombatInactiveBeforeDiceOrMutation(bool includeInactiveCombat)
    {
        var rig = RespondRig.Create(includeCombat: includeInactiveCombat, combatActive: false);
        var result = await InvokeRespondAsync(rig.Coordinator, rig.Intent);

        AssertRespondFailureWithoutMutation(rig, result, "CombatInactive", 12);
    }

    [Fact]
    public async Task Respond_NoGenericPendingReturnsExchangeNotPendingBeforeDiceOrMutation()
    {
        var rig = RespondRig.Create(includeGenericPending: false, includeOwnedPendingResponse: false);
        var result = await InvokeRespondAsync(rig.Coordinator, rig.Intent);

        AssertRespondFailureWithoutMutation(rig, result, "ExchangeNotPending", 12);
    }

    [Fact]
    public void PlayerCombatIntentCoordinator_HasOnlyApprovedStateAndTransitionDependencies()
    {
        var coordinatorType = GetRequiredGameplayType("PlayerCombatIntentCoordinator");
        var internalCombatType = GetRequiredGameplayType("IInternalCombatResolutionCoordinator");
        var constructor = Assert.Single(coordinatorType.GetConstructors());
        var dependencies = constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        Assert.Equal([typeof(IGameCoordinator), internalCombatType], dependencies);
        Assert.DoesNotContain(typeof(IGameStateStore), dependencies);
        Assert.DoesNotContain(typeof(IDiceRoller), dependencies);
        Assert.DoesNotContain(typeof(ICombatDamageEngine), dependencies);
        Assert.DoesNotContain(typeof(IHpDamageEngine), dependencies);
        Assert.DoesNotContain(typeof(IGameRealtimeNotifier), dependencies);

        var source = File.ReadAllText(GetCoordinatorSourcePath());
        foreach (var (category, pattern) in ForbiddenDependencyPatterns)
        {
            Assert.False(pattern.IsMatch(source), $"Coordinator source must not reference the forbidden {category} dependency category.");
        }
    }

    [Fact]
    public void PlayerCombatIntentCoordinator_ExposesExactlyThreePlayerIntents()
    {
        var contractType = GetRequiredGameplayType("IPlayerCombatIntentCoordinator");
        Assert.Equal(
            ["MeleeAttackAsync", "PassAsync", "RespondAsync"],
            contractType.GetMethods().Select(method => method.Name).Order().ToArray());
    }

    [Fact]
    public void PlayerCombatIntentCoordinator_ResolvesFromDependencyInjection()
    {
        var coordinatorType = GetRequiredGameplayType("IPlayerCombatIntentCoordinator");
        var first = factory.Services.GetRequiredService(coordinatorType);
        var second = factory.Services.GetRequiredService(coordinatorType);
        var games = factory.Services.GetRequiredService<IGameCoordinator>();
        var combat = factory.Services.GetRequiredService(GetRequiredGameplayType("IInternalCombatResolutionCoordinator"));

        Assert.Same(first, second);
        Assert.Same(games, combat);
    }

    private static async Task<object> InvokeMeleeAttackAsync(object coordinator, object intent)
    {
        var task = (Task)GetRequiredGameplayType("PlayerCombatIntentCoordinator")
            .GetMethod("MeleeAttackAsync")!
            .Invoke(coordinator, [intent])!;
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static async Task<object> InvokeRespondAsync(object coordinator, object intent)
    {
        var task = (Task)GetRequiredGameplayType("PlayerCombatIntentCoordinator")
            .GetMethod("RespondAsync")!
            .Invoke(coordinator, [intent])!;
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static async Task<object> InvokePassAsync(object coordinator, object intent)
    {
        var task = (Task)GetRequiredGameplayType("PlayerCombatIntentCoordinator")
            .GetMethod("PassAsync")!
            .Invoke(coordinator, [intent])!;
        await task;
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static object CreatePassIntent(MeleeAttackRig rig) => Activator.CreateInstance(
        GetRequiredGameplayType("PlayerPassIntent"),
        rig.RoomId,
        rig.PlayerId,
        rig.ExpectedRevision,
        rig.ActorCharacterId)!;

    private static T? GetProperty<T>(object instance, string propertyName) =>
        (T?)instance.GetType().GetProperty(propertyName)!.GetValue(instance);

    private static Type GetRequiredGameplayType(string typeName) =>
        typeof(GameCoordinator).Assembly.GetType($"Trpg.Multiplayer.Api.Gameplay.{typeName}")
        ?? throw new InvalidOperationException($"Gameplay type '{typeName}' was not found.");

    private static readonly (string Category, Regex Pattern)[] ForbiddenDependencyPatterns =
    [
        ("store", new Regex(@"\b[A-Za-z0-9_]*Store\b", RegexOptions.CultureInvariant)),
        ("dice", new Regex(@"\b[A-Za-z0-9_]*Dice[A-Za-z0-9_]*\b", RegexOptions.CultureInvariant)),
        ("engine", new Regex(@"\b[A-Za-z0-9_]*Engine\b", RegexOptions.CultureInvariant)),
        ("notifier", new Regex(@"\b[A-Za-z0-9_]*Notifier\b", RegexOptions.CultureInvariant)),
        ("Hub or SignalR", new Regex(@"\b(?:IHubContext|[A-Za-z0-9_]*Hub[A-Za-z0-9_]*|[A-Za-z0-9_]*SignalR[A-Za-z0-9_]*)\b", RegexOptions.CultureInvariant)),
        ("persistence", new Regex(@"\b[A-Za-z0-9_]*(?:Repository|Persistence|DbContext|Database|EntityFramework)[A-Za-z0-9_]*\b", RegexOptions.CultureInvariant)),
        ("AI", new Regex(@"\b(?:I?AI|I?Ai)[A-Za-z0-9_]*\b", RegexOptions.CultureInvariant))
    ];

    private static string GetCoordinatorSourcePath() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src",
        "Trpg.Multiplayer.Api", "Gameplay", "PlayerCombatIntentCoordinator.cs"));

    private static void AssertFailureWithoutMutation(
        MeleeAttackRig rig,
        object result,
        string expectedCode,
        long expectedRevision)
    {
        Assert.False(GetProperty<bool>(result, "IsSuccess"));
        var error = GetProperty<object>(result, "Error")!;
        Assert.Equal(expectedCode, GetProperty<object>(error, "Code")!.ToString());
        Assert.Equal(expectedRevision, GetProperty<long?>(error, "CurrentGameRevision"));
        Assert.Empty(rig.Combat.BeginCommands);
        Assert.Empty(rig.Combat.ResolveCommands);
        Assert.Empty(rig.Combat.DamageCommands);
        Assert.Equal(0, rig.Combat.GeneratedExchangeIds);
        Assert.Equal(0, rig.Combat.DiceRolls);
        Assert.Equal(0, rig.Combat.Publications);
        Assert.Same(rig.InitialCanonicalState, rig.Combat.CurrentState);
        Assert.Equal(expectedRevision, rig.Combat.CurrentState.Revision);
        Assert.Null(rig.Combat.CurrentState.Combat!.PendingExchange);
        Assert.Empty(rig.Combat.CurrentState.Combat.History);
    }

    private static void AssertRespondFailureWithoutMutation(
        RespondRig rig,
        object result,
        string expectedCode,
        long expectedRevision)
    {
        Assert.False(GetProperty<bool>(result, "IsSuccess"));
        var error = GetProperty<object>(result, "Error")!;
        Assert.Equal(expectedCode, GetProperty<object>(error, "Code")!.ToString());
        Assert.Equal(expectedRevision, GetProperty<long?>(error, "CurrentGameRevision"));
        Assert.Empty(rig.Combat.BeginCommands);
        Assert.Empty(rig.Combat.ResolveCommands);
        Assert.Empty(rig.Combat.DamageCommands);
        Assert.Equal(0, rig.Combat.GeneratedExchangeIds);
        Assert.Equal(0, rig.Combat.DiceRolls);
        Assert.Equal(0, rig.Combat.Publications);
        Assert.Same(rig.InitialCanonicalState, rig.Combat.CurrentState);
        Assert.Equal(expectedRevision, rig.Combat.CurrentState.Revision);
        Assert.NotNull(rig.Combat.CurrentState.Combat!.PendingExchange);
        Assert.Empty(rig.Combat.CurrentState.Combat.History);
    }

    private static void AssertPassFailureWithoutMutation(
        MeleeAttackRig rig,
        object result,
        string expectedCode,
        long expectedRevision)
    {
        Assert.False(GetProperty<bool>(result, "IsSuccess"));
        var error = GetProperty<object>(result, "Error")!;
        Assert.Equal(expectedCode, GetProperty<object>(error, "Code")!.ToString());
        Assert.Equal(expectedRevision, GetProperty<long?>(error, "CurrentGameRevision"));
        Assert.Empty(rig.Combat.PassCommands);
        Assert.Empty(rig.Combat.BeginCommands);
        Assert.Empty(rig.Combat.ResolveCommands);
        Assert.Empty(rig.Combat.DamageCommands);
        Assert.Equal(0, rig.Combat.GeneratedExchangeIds);
        Assert.Equal(0, rig.Combat.DiceRolls);
        Assert.Equal(0, rig.Combat.Publications);
        Assert.Same(rig.InitialCanonicalState, rig.Combat.CurrentState);
        Assert.Equal(expectedRevision, rig.Combat.CurrentState.Revision);
        var combat = rig.Combat.CurrentState.Combat!;
        Assert.Equal(0, combat.TurnIndex);
        Assert.Equal(1, combat.Round);
        Assert.Empty(combat.History);
    }

    private sealed class RespondRig
    {
        private RespondRig(
            Guid defenderPlayerId,
            string exchangeId,
            object intent,
            RecordingGameCoordinator games,
            RecordingCombatProxy combat,
            MultiplayerGameState initialCanonicalState,
            GameSnapshot initialProjection,
            GameSnapshot finalProjection,
            object coordinator)
        {
            DefenderPlayerId = defenderPlayerId;
            ExchangeId = exchangeId;
            Intent = intent;
            Games = games;
            Combat = combat;
            InitialCanonicalState = initialCanonicalState;
            InitialProjection = initialProjection;
            FinalProjection = finalProjection;
            Coordinator = coordinator;
        }

        public Guid DefenderPlayerId { get; }
        public string ExchangeId { get; }
        public object Intent { get; }
        public RecordingGameCoordinator Games { get; }
        public RecordingCombatProxy Combat { get; }
        public MultiplayerGameState InitialCanonicalState { get; }
        public GameSnapshot InitialProjection { get; }
        public GameSnapshot FinalProjection { get; }
        public object Coordinator { get; }

        public static RespondRig Create(
            long projectedRevision = 12,
            long expectedRevision = 12,
            long resolveRevision = 14,
            long finalRevision = 15,
            bool includeCombat = true,
            bool combatActive = true,
            bool includeGenericPending = true,
            bool includeOwnedPendingResponse = true,
            IReadOnlyList<string>? availableResponses = null,
            string? requestedExchangeId = null,
            bool hasPendingDamage = false,
            string? unrelatedPendingExchangeId = null)
        {
            var roomId = Guid.NewGuid();
            var attackerPlayerId = Guid.NewGuid();
            var defenderPlayerId = Guid.NewGuid();
            var attackerCharacterId = Guid.NewGuid();
            var defenderCharacterId = Guid.NewGuid();
            const string exchangeId = "exchange-exact";
            var responses = availableResponses ?? ["dodge", "fight_back"];
            CombatSnapshot? initialCombat = includeCombat
                ? CreateProjectionCombat(
                    combatActive,
                    includeGenericPending,
                    includeOwnedPendingResponse,
                    exchangeId,
                    attackerCharacterId,
                    defenderCharacterId,
                    responses)
                : null;
            var initialProjection = new GameSnapshot(
                roomId,
                projectedRevision,
                MultiplayerGameStatus.Active.ToString(),
                DateTimeOffset.UnixEpoch,
                [],
                Combat: initialCombat);
            var finalProjection = new GameSnapshot(
                roomId,
                finalRevision,
                MultiplayerGameStatus.Active.ToString(),
                DateTimeOffset.UnixEpoch,
                [],
                Combat: CreateProjectionCombat(
                    true,
                    false,
                    false,
                    exchangeId,
                    attackerCharacterId,
                    defenderCharacterId,
                    responses));
            var initialCanonicalState = CreateCanonicalState(
                roomId,
                projectedRevision,
                attackerPlayerId,
                defenderPlayerId,
                attackerCharacterId,
                defenderCharacterId,
                exchangeId,
                [],
                true);
            var damageExchangeIds = new List<string>();
            if (hasPendingDamage)
            {
                damageExchangeIds.Add(exchangeId);
            }
            if (unrelatedPendingExchangeId is not null)
            {
                damageExchangeIds.Add(unrelatedPendingExchangeId);
            }
            var resolvedState = CreateCanonicalState(
                roomId,
                resolveRevision,
                attackerPlayerId,
                defenderPlayerId,
                attackerCharacterId,
                defenderCharacterId,
                exchangeId,
                damageExchangeIds,
                false);
            var games = new RecordingGameCoordinator(initialProjection, finalProjection);
            var combatObject = DispatchProxy.Create(
                GetRequiredGameplayType("IInternalCombatResolutionCoordinator"),
                typeof(RecordingCombatProxy));
            var combat = (RecordingCombatProxy)combatObject;
            combat.CurrentState = initialCanonicalState;
            combat.BeginState = initialCanonicalState;
            combat.ResolvedState = resolvedState;
            var coordinator = Activator.CreateInstance(
                GetRequiredGameplayType("PlayerCombatIntentCoordinator"),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                [games, combatObject],
                null)!;
            var intent = Activator.CreateInstance(
                GetRequiredGameplayType("PlayerRespondIntent"),
                roomId,
                defenderPlayerId,
                expectedRevision,
                requestedExchangeId ?? exchangeId,
                CombatResponse.FightBack)!;
            return new RespondRig(
                defenderPlayerId,
                exchangeId,
                intent,
                games,
                combat,
                initialCanonicalState,
                initialProjection,
                finalProjection,
                coordinator);
        }

        private static CombatSnapshot CreateProjectionCombat(
            bool active,
            bool includeGenericPending,
            bool includeOwnedPendingResponse,
            string exchangeId,
            Guid attackerCharacterId,
            Guid defenderCharacterId,
            IReadOnlyList<string> availableResponses) => new(
                active,
                1,
                $"character:{attackerCharacterId}",
                [
                    new CombatParticipantSnapshot(
                        $"character:{attackerCharacterId}", attackerCharacterId, "Attacker", "investigator",
                        true, true, false, null),
                    new CombatParticipantSnapshot(
                        $"character:{defenderCharacterId}", defenderCharacterId, "Defender", "investigator",
                        true, false, true, null)
                ],
                null,
                includeGenericPending ? new CombatPendingSnapshot("defender", "awaiting_response") : null,
                ViewerActions: includeOwnedPendingResponse
                    ? new CombatViewerActionsSnapshot(
                        null,
                        false,
                        false,
                        [],
                        new CombatPendingResponseSnapshot(exchangeId, availableResponses))
                    : null);

        private static MultiplayerGameState CreateCanonicalState(
            Guid roomId,
            long revision,
            Guid attackerPlayerId,
            Guid defenderPlayerId,
            Guid attackerCharacterId,
            Guid defenderCharacterId,
            string exchangeId,
            IReadOnlyList<string> damageExchangeIds,
            bool includePendingExchange)
        {
            var attackerId = new CombatParticipantId($"character:{attackerCharacterId}");
            var defenderId = new CombatParticipantId($"character:{defenderCharacterId}");
            var participants = new[]
            {
                CreateParticipant(attackerId, attackerCharacterId, attackerPlayerId),
                CreateParticipant(defenderId, defenderCharacterId, defenderPlayerId)
            };
            var pending = includePendingExchange
                ? CombatSessionState.CreatePendingExchange(
                    exchangeId,
                    1,
                    0,
                    attackerId,
                    defenderId,
                    defenderPlayerId,
                    [CombatResponse.Dodge, CombatResponse.FightBack],
                    0,
                    revision - 1,
                    DateTimeOffset.UnixEpoch)
                : null;
            var dispositions = damageExchangeIds.ToDictionary(
                id => id,
                id => new DamageDispositionState(
                    new DamageDispositionData(id, attackerId, defenderId, CombatDamageMode.Regular, revision),
                    DamageDispositionStatus.Pending,
                    null),
                StringComparer.Ordinal);
            var session = new CombatSession(
                Guid.NewGuid(),
                true,
                1,
                0,
                [attackerId, defenderId],
                participants,
                new Dictionary<string, int>(),
                new Dictionary<string, int>(),
                pending,
                null,
                [],
                dispositions,
                new Dictionary<Guid, DyingScheduleState>(),
                DateTimeOffset.UnixEpoch,
                null,
                null);
            return new MultiplayerGameState(
                roomId,
                revision,
                MultiplayerGameStatus.Active,
                DateTimeOffset.UnixEpoch,
                [],
                combat: session);
        }

        private static CombatParticipantState CreateParticipant(
            CombatParticipantId participantId,
            Guid characterId,
            Guid ownerPlayerId) => new(
                participantId,
                characterId,
                ownerPlayerId,
                participantId.Value,
                "investigator",
                "investigator",
                50,
                50,
                50,
                [CombatResponse.Dodge, CombatResponse.FightBack],
                1,
                true,
                new CombatDamageProfile(
                    50,
                    50,
                    CocCombatDamageRules.DeriveDamageBonus(50, 50),
                    CocCombatDamageRules.NormalizeWeapon("unarmed", "Unarmed", "1d3", true, "melee_non_impaling"),
                    0),
                null,
                null);
    }

    private sealed class MeleeAttackRig
    {
        private MeleeAttackRig(
            Guid actorCharacterId,
            string exchangeId,
            object intent,
            RecordingGameCoordinator games,
            RecordingCombatProxy combat,
            MultiplayerGameState initialCanonicalState,
            GameSnapshot initialProjection,
            GameSnapshot finalProjection,
            object coordinator)
        {
            ActorCharacterId = actorCharacterId;
            ExchangeId = exchangeId;
            Intent = intent;
            Games = games;
            Combat = combat;
            InitialCanonicalState = initialCanonicalState;
            InitialProjection = initialProjection;
            FinalProjection = finalProjection;
            Coordinator = coordinator;
        }

        public Guid ActorCharacterId { get; }
        public Guid RoomId { get; private init; }
        public Guid PlayerId { get; private init; }
        public long ExpectedRevision { get; private init; }
        public string ExchangeId { get; }
        public object Intent { get; }
        public RecordingGameCoordinator Games { get; }
        public RecordingCombatProxy Combat { get; }
        public MultiplayerGameState InitialCanonicalState { get; }
        public GameSnapshot InitialProjection { get; }
        public GameSnapshot FinalProjection { get; }
        public object Coordinator { get; }

        public static MeleeAttackRig Create(
            long projectedRevision = 12,
            long expectedRevision = 12,
            long beginRevision = 13,
            long resolveRevision = 14,
            long finalRevision = 15,
            bool actorViewerOwned = true,
            bool actorCurrent = true,
            bool actorHasCharacter = true,
            bool canPass = true,
            bool hasPendingExchange = false,
            IReadOnlyList<string>? eligibleTargets = null,
            Guid? defenderOwnerPlayerId = null,
            CombatResponse? npcResponsePolicy = CombatResponse.Dodge,
            IReadOnlyList<CombatResponse>? pendingResponses = null,
            bool hasPendingDamage = false,
            string? unrelatedPendingExchangeId = null)
        {
            var roomId = Guid.NewGuid();
            var playerId = Guid.NewGuid();
            var actorCharacterId = Guid.NewGuid();
            Guid? projectedActorCharacterId = actorHasCharacter ? actorCharacterId : null;
            const string exchangeId = "exchange-exact";
            const string targetParticipantId = "opponent:0";
            var actions = new CombatViewerActionsSnapshot(
                actorCharacterId,
                true,
                canPass && !hasPendingExchange && !hasPendingDamage,
                eligibleTargets ?? [targetParticipantId],
                null);
            var initialProjection = CreateProjection(
                roomId, projectedRevision, actorCharacterId, projectedActorCharacterId,
                actorViewerOwned, actorCurrent, actions);
            var finalProjection = CreateProjection(
                roomId, finalRevision, actorCharacterId, projectedActorCharacterId,
                actorViewerOwned, actorCurrent, null);
            var responses = pendingResponses ?? [CombatResponse.Dodge, CombatResponse.FightBack];
            var initialCanonicalState = CreateCanonicalState(
                roomId, projectedRevision, playerId, actorCharacterId, exchangeId,
                defenderOwnerPlayerId, npcResponsePolicy, responses,
                hasPendingDamage ? [exchangeId] : [], hasPendingExchange);
            var beginState = CreateCanonicalState(
                roomId, beginRevision, playerId, actorCharacterId, exchangeId,
                defenderOwnerPlayerId, npcResponsePolicy, responses, [], true);
            var damageExchangeIds = new List<string>();
            if (hasPendingDamage)
            {
                damageExchangeIds.Add(exchangeId);
            }
            if (unrelatedPendingExchangeId is not null)
            {
                damageExchangeIds.Add(unrelatedPendingExchangeId);
            }
            var resolvedState = CreateCanonicalState(
                roomId, resolveRevision, playerId, actorCharacterId, exchangeId,
                defenderOwnerPlayerId, npcResponsePolicy, responses, damageExchangeIds, false);
            var passState = CreateCanonicalState(
                roomId, finalRevision, playerId, actorCharacterId, exchangeId,
                defenderOwnerPlayerId, npcResponsePolicy, responses, [], false);
            var games = new RecordingGameCoordinator(initialProjection, finalProjection);
            var combatObject = DispatchProxy.Create(
                GetRequiredGameplayType("IInternalCombatResolutionCoordinator"),
                typeof(RecordingCombatProxy));
            var combat = (RecordingCombatProxy)combatObject;
            combat.CurrentState = initialCanonicalState;
            combat.BeginState = beginState;
            combat.ResolvedState = resolvedState;
            combat.PassState = passState;
            var coordinator = Activator.CreateInstance(
                GetRequiredGameplayType("PlayerCombatIntentCoordinator"),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                [games, combatObject],
                null)!;
            var intent = Activator.CreateInstance(
                GetRequiredGameplayType("PlayerMeleeAttackIntent"),
                roomId, playerId, expectedRevision, actorCharacterId, targetParticipantId)!;
            return new MeleeAttackRig(
                actorCharacterId,
                exchangeId,
                intent,
                games,
                combat,
                initialCanonicalState,
                initialProjection,
                finalProjection,
                coordinator)
            {
                RoomId = roomId,
                PlayerId = playerId,
                ExpectedRevision = expectedRevision
            };
        }

        private static GameSnapshot CreateProjection(
            Guid roomId,
            long revision,
            Guid requestedActorCharacterId,
            Guid? projectedActorCharacterId,
            bool actorViewerOwned,
            bool actorCurrent,
            CombatViewerActionsSnapshot? actions)
        {
            var actorParticipantId = projectedActorCharacterId is null
                ? "opponent:actor"
                : $"character:{requestedActorCharacterId}";
            return new GameSnapshot(
                roomId,
                revision,
                MultiplayerGameStatus.Active.ToString(),
                DateTimeOffset.UnixEpoch,
                [],
                Combat: new CombatSnapshot(
                    true,
                    1,
                    actorCurrent ? actorParticipantId : "character:other",
                    [
                        new CombatParticipantSnapshot(
                            actorParticipantId, projectedActorCharacterId, "Actor", "investigator",
                            true, actorCurrent, actorViewerOwned, null),
                        new CombatParticipantSnapshot(
                            "opponent:0", null, "Opponent", "opponent",
                            true, false, false, null)
                    ],
                    null,
                    null,
                    ViewerActions: actions));
        }

        private static MultiplayerGameState CreateCanonicalState(
            Guid roomId,
            long revision,
            Guid playerId,
            Guid actorCharacterId,
            string exchangeId,
            Guid? defenderOwnerPlayerId,
            CombatResponse? npcResponsePolicy,
            IReadOnlyList<CombatResponse> pendingResponses,
            IReadOnlyList<string> damageExchangeIds,
            bool includePendingExchange)
        {
            var actorId = new CombatParticipantId($"character:{actorCharacterId}");
            var defenderId = new CombatParticipantId("opponent:0");
            var participants = new[]
            {
                CreateParticipant(actorId, actorCharacterId, playerId, "investigator", null),
                CreateParticipant(defenderId, null, defenderOwnerPlayerId, "opponent", npcResponsePolicy)
            };
            var pending = includePendingExchange
                ? CombatSessionState.CreatePendingExchange(
                    exchangeId, 1, 0, actorId, defenderId, defenderOwnerPlayerId,
                    pendingResponses, 0, revision - 1, DateTimeOffset.UnixEpoch)
                : null;
            var dispositions = damageExchangeIds.ToDictionary(
                id => id,
                id => new DamageDispositionState(
                    new DamageDispositionData(id, actorId, defenderId, CombatDamageMode.Regular, revision),
                    DamageDispositionStatus.Pending,
                    null),
                StringComparer.Ordinal);
            var session = new CombatSession(
                Guid.NewGuid(), true, 1, 0, [actorId, defenderId], participants,
                new Dictionary<string, int>(), new Dictionary<string, int>(), pending, null, [],
                dispositions, new Dictionary<Guid, DyingScheduleState>(),
                DateTimeOffset.UnixEpoch, null, null);
            return new MultiplayerGameState(
                roomId, revision, MultiplayerGameStatus.Active, DateTimeOffset.UnixEpoch, [], combat: session);
        }

        private static CombatParticipantState CreateParticipant(
            CombatParticipantId participantId,
            Guid? characterId,
            Guid? ownerPlayerId,
            string side,
            CombatResponse? npcResponsePolicy) => new(
                participantId,
                characterId,
                ownerPlayerId,
                participantId.Value,
                characterId is null ? "opponent" : "investigator",
                side,
                50,
                50,
                50,
                [CombatResponse.Dodge, CombatResponse.FightBack],
                1,
                true,
                new CombatDamageProfile(
                    50,
                    50,
                    CocCombatDamageRules.DeriveDamageBonus(50, 50),
                    CocCombatDamageRules.NormalizeWeapon("unarmed", "Unarmed", "1d3", true, "melee_non_impaling"),
                    0),
                characterId is null ? new OpponentVitalityState(10, 10) : null,
                npcResponsePolicy);
    }

    private sealed class RecordingGameCoordinator(params GameSnapshot[] snapshots) : IGameCoordinator
    {
        private readonly Queue<GameSnapshot> snapshots = new(snapshots);
        public int ProjectionCalls { get; private set; }

        public Task<GameResult<GameSnapshot>> GetProjectionAsync(Guid roomId, Guid viewerPlayerId)
        {
            ProjectionCalls++;
            return Task.FromResult(GameResult<GameSnapshot>.Success(snapshots.Dequeue(), changed: false));
        }

        public Task<GameResult<MultiplayerGameState>> InitializeAsync(InitializeGameCommand command) => throw new NotSupportedException();
        public Task<GameResult<CharacterState>> GetCharacterForOwnerAsync(Guid roomId, Guid characterId, Guid playerId) => throw new NotSupportedException();
        public Task<GameResult<GameCheckResult>> ResolveCheckAsync(ResolveCheckCommand command) => throw new NotSupportedException();
        public Task<GameResult<HpDamageResult>> ApplyDamageAsync(ApplyDamageCommand command) => throw new NotSupportedException();
        public Task<GameResult<HealthStabilizationResult>> ResolveDyingRoundAsync(ResolveDyingRoundCommand command) => throw new NotSupportedException();
        public Task<GameResult<HealthStabilizationResult>> ResolveFirstAidAsync(ResolveFirstAidCommand command) => throw new NotSupportedException();
        public Task<bool> RemoveAsync(Guid roomId) => throw new NotSupportedException();
    }

    public class RecordingCombatProxy : DispatchProxy
    {
        public MultiplayerGameState BeginState { get; set; } = null!;
        public MultiplayerGameState ResolvedState { get; set; } = null!;
        public MultiplayerGameState PassState { get; set; } = null!;
        public MultiplayerGameState CurrentState { get; set; } = null!;
        public List<object> BeginCommands { get; } = [];
        public List<object> ResolveCommands { get; } = [];
        public List<object> DamageCommands { get; } = [];
        public List<object> PassCommands { get; } = [];
        public int GeneratedExchangeIds { get; private set; }
        public int DiceRolls { get; private set; }
        public int Publications { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var command = args![0]!;
            return targetMethod!.Name switch
            {
                "BeginOpposedExchangeAsync" => RecordSuccess(
                    BeginCommands, command, "BeginOpposedExchangeResult", BeginState, 1, 0),
                "ResolvePendingExchangeAsync" => RecordSuccess(
                    ResolveCommands, command, "ResolvePendingExchangeResult", ResolvedState, 0, 2),
                "ResolveCombatDamageAsync" => RecordSuccess(
                    DamageCommands, command, "ResolveCombatDamageResult", ResolvedState, 0, 1),
                "PassCombatTurnAsync" => RecordSuccess(
                    PassCommands, command, "PassCombatTurnResult", PassState, 0, 0),
                _ => throw new NotSupportedException(targetMethod.Name)
            };
        }

        private object RecordSuccess(
            List<object> commands,
            object command,
            string resultTypeName,
            MultiplayerGameState state,
            int generatedExchangeIds,
            int diceRolls)
        {
            commands.Add(command);
            GeneratedExchangeIds += generatedExchangeIds;
            DiceRolls += diceRolls;
            Publications++;
            CurrentState = resultTypeName switch
            {
                "BeginOpposedExchangeResult" => BeginState,
                "PassCombatTurnResult" => PassState,
                _ => ResolvedState
            };
            var valueType = GetRequiredGameplayType(resultTypeName);
            var value = resultTypeName == "ResolveCombatDamageResult"
                ? Activator.CreateInstance(valueType, state, null)!
                : Activator.CreateInstance(valueType, state)!;
            var gameResultType = typeof(GameResult<>).MakeGenericType(valueType);
            var gameResult = gameResultType.GetMethod("Success")!.Invoke(null, [value, true])!;
            return typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(gameResultType)
                .Invoke(null, [gameResult])!;
        }
    }
}
