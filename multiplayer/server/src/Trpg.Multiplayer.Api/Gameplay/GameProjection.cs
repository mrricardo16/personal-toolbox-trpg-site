namespace Trpg.Multiplayer.Api.Gameplay;

public sealed record CharacterSnapshot(
    Guid CharacterId,
    Guid OwnerPlayerId,
    string Name,
    IReadOnlyDictionary<string, int> CheckValues,
    CharacterHealthSnapshot? Health);

public sealed record CharacterHealthSnapshot(
    int CurrentHp,
    int MaxHp,
    bool MajorWound,
    bool Unconscious,
    bool Dying,
    bool Dead,
    bool Stabilized);

public sealed record GameSnapshot(
    Guid RoomId,
    long Revision,
    string Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<CharacterSnapshot> Characters,
    GameCheckRecord? LastCheck = null,
    CombatSnapshot? Combat = null);

public static class GameProjection
{
    public static GameSnapshot Build(MultiplayerGameState state, Guid viewerPlayerId)
    {
        return new GameSnapshot(
            state.RoomId,
            state.Revision,
            state.Status.ToString(),
            state.CreatedAt,
            state.Characters
                .Select(character => new CharacterSnapshot(
                    character.CharacterId,
                    character.OwnerPlayerId,
                    character.Name,
                    character.OwnerPlayerId == viewerPlayerId
                        ? new Dictionary<string, int>(character.CheckValues, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                    character.OwnerPlayerId == viewerPlayerId
                        ? new CharacterHealthSnapshot(
                            character.Health.CurrentHp,
                            character.Health.MaxHp,
                            character.Health.MajorWound,
                            character.Health.Unconscious,
                            character.Health.Dying,
                            character.Health.Dead,
                            character.Health.Stabilized is not null)
                        : null))
                .ToArray(),
            state.LastCheck,
            BuildCombat(state.Combat, viewerPlayerId));
    }

    private static CombatSnapshot? BuildCombat(CombatSession? session, Guid viewerPlayerId)
    {
        if (session is null)
        {
            return null;
        }

        var viewerParticipantIds = session.Participants
            .Where(participant => participant.OwnerPlayerId == viewerPlayerId)
            .Select(participant => participant.ParticipantId.Value)
            .ToHashSet(StringComparer.Ordinal);
        if (viewerParticipantIds.Count == 0)
        {
            return null;
        }

        var currentActorParticipantId = session.TurnIndex >= 0 && session.TurnIndex < session.Order.Count
            ? session.Order[session.TurnIndex].Value
            : null;
        return new CombatSnapshot(
            session.Active,
            session.Round,
            currentActorParticipantId,
            session.Participants
                .Select(participant => new CombatParticipantSnapshot(
                    participant.ParticipantId.Value,
                    participant.CharacterId,
                    participant.Label,
                    participant.Side,
                    participant.Active,
                    participant.ParticipantId.Value == currentActorParticipantId,
                    participant.OwnerPlayerId == viewerPlayerId,
                    participant.OwnerPlayerId == viewerPlayerId
                        ? new CombatParticipantStatsSnapshot(participant.Dex, participant.Fighting, participant.Dodge)
                        : null))
                .ToArray(),
            session.LastExchange is null
                ? null
                : new CombatExchangeSnapshot(
                    session.LastExchange.Outcome,
                    session.LastExchange.WinnerParticipantId?.Value,
                    session.LastExchange.DamageDisposition?.Pending == true),
            session.PendingExchange is null
                ? null
                : new CombatPendingSnapshot(
                    GetPendingRole(session.PendingExchange, viewerParticipantIds),
                    "awaiting_response"));
    }

    private static string GetPendingRole(
        PendingCombatExchange pendingExchange,
        IReadOnlySet<string> viewerParticipantIds)
    {
        if (viewerParticipantIds.Contains(pendingExchange.AttackerParticipantId.Value))
        {
            return "attacker";
        }

        return viewerParticipantIds.Contains(pendingExchange.DefenderParticipantId.Value)
            ? "defender"
            : "observer";
    }
}
