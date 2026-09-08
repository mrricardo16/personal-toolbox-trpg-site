using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Text.RegularExpressions;
using Trpg.Multiplayer.Api.Gameplay;
using Trpg.Multiplayer.Api.Realtime;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests.Gameplay;

public sealed class PlayerCombatIntentCoordinatorTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
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
            Assert.False(
                pattern.IsMatch(source),
                $"Coordinator source must not reference the forbidden {category} dependency category.");
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
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        "..",
        "src",
        "Trpg.Multiplayer.Api",
        "Gameplay",
        "PlayerCombatIntentCoordinator.cs"));
}
