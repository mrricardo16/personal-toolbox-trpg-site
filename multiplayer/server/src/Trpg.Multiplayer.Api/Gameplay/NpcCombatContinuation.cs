namespace Trpg.Multiplayer.Api.Gameplay;

internal static class CombatContinuationStateValidator
{
    internal static ValidatedNpcCombatTurn ValidateNpcTurn(MultiplayerGameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var session = state.Combat
            ?? throw new NpcCombatContinuationInvariantException("NPC continuation requires a Combat session.");
        if (!session.Active)
        {
            throw new NpcCombatContinuationInvariantException("NPC continuation requires active Combat.");
        }

        ValidateOrder(session);
        if (session.TurnIndex < 0 || session.TurnIndex >= session.Order.Count)
        {
            throw new NpcCombatContinuationInvariantException("Canonical Combat turn index is invalid.");
        }

        var participantsById = session.Participants.ToDictionary(participant => participant.ParticipantId.Value, StringComparer.Ordinal);
        ValidateParticipantShapes(state, session, participantsById);

        var currentNpcId = session.Order[session.TurnIndex].Value;
        var currentNpc = participantsById[currentNpcId];
        EnsureNpcShape(currentNpc, "current actor");
        if (!currentNpc.Active)
        {
            throw new NpcCombatContinuationInvariantException("Canonical current NPC actor is inactive.");
        }

        var legalTargets = session.Order
            .Select(participantId => participantsById[participantId.Value])
            .Where(participant => participant.Active)
            .Where(participant => participant.Side != currentNpc.Side)
            .Where(participant => participant.Kind == "investigator")
            .ToArray();

        foreach (var target in legalTargets)
        {
            EnsureInvestigatorShape(target, "NPC target");
            if (target.Side == currentNpc.Side)
            {
                throw new NpcCombatContinuationInvariantException("Canonical NPC target has an inconsistent side relation.");
            }
        }

        return new ValidatedNpcCombatTurn(state, session, currentNpc, legalTargets);
    }

    private static void ValidateOrder(CombatSession session)
    {
        if (session.Order.Count == 0 || session.Order.Select(participantId => participantId.Value).Distinct(StringComparer.Ordinal).Count() != session.Order.Count)
        {
            throw new NpcCombatContinuationInvariantException("Canonical Combat order is missing or contains duplicate participants.");
        }

        if (session.Participants.Select(participant => participant.ParticipantId.Value).Distinct(StringComparer.Ordinal).Count() != session.Participants.Count)
        {
            throw new NpcCombatContinuationInvariantException("Canonical Combat participants contain duplicate identities.");
        }

        var orderIds = session.Order.Select(participantId => participantId.Value).ToHashSet(StringComparer.Ordinal);
        var participantIds = session.Participants.Select(participant => participant.ParticipantId.Value).ToHashSet(StringComparer.Ordinal);
        if (!orderIds.SetEquals(participantIds))
        {
            throw new NpcCombatContinuationInvariantException("Canonical Combat order and participants are inconsistent.");
        }
    }

    private static void ValidateParticipantShapes(
        MultiplayerGameState state,
        CombatSession session,
        IReadOnlyDictionary<string, CombatParticipantState> participantsById)
    {
        foreach (var participantId in session.Order)
        {
            var participant = participantsById[participantId.Value];
            if (participant.Kind == "opponent")
            {
                EnsureNpcShape(participant, "Combat participant");
                continue;
            }

            if (participant.Kind != "investigator")
            {
                throw new NpcCombatContinuationInvariantException("Canonical Combat participant kind is invalid.");
            }

            EnsureInvestigatorShape(participant, "Combat participant");
            var canonicalCharacter = state.Characters.SingleOrDefault(character => character.CharacterId == participant.CharacterId);
            if (canonicalCharacter is null || canonicalCharacter.OwnerPlayerId != participant.OwnerPlayerId)
            {
                throw new NpcCombatContinuationInvariantException("Canonical investigator owner or character identity is inconsistent.");
            }
        }

        if (!session.Participants.Any(participant => participant.Active && participant.Kind == "investigator"))
        {
            throw new NpcCombatContinuationInvariantException("Active Combat has no active investigator.");
        }
    }

    private static void EnsureNpcShape(CombatParticipantState participant, string role)
    {
        if (participant.Kind != "opponent" || participant.Side != "opponent" || participant.CharacterId is not null || participant.OwnerPlayerId is not null)
        {
            throw new NpcCombatContinuationInvariantException($"Canonical {role} has an invalid NPC shape.");
        }
    }

    private static void EnsureInvestigatorShape(CombatParticipantState participant, string role)
    {
        if (participant.Kind != "investigator" || participant.Side != "investigator" || participant.CharacterId is null || participant.OwnerPlayerId is null)
        {
            throw new NpcCombatContinuationInvariantException($"Canonical {role} has an invalid investigator shape.");
        }
    }
}

internal sealed class NpcCombatTurnDriver
{
    internal NpcCombatTurnDecision Select(ValidatedNpcCombatTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);

        var target = turn.LegalTargets.FirstOrDefault();
        return target is null
            ? new NpcCombatTurnDecision(NpcCombatActionKind.Pass, turn.CurrentNpc.ParticipantId.Value, null)
            : new NpcCombatTurnDecision(
                NpcCombatActionKind.BeginOpposedExchange,
                turn.CurrentNpc.ParticipantId.Value,
                target.ParticipantId.Value);
    }
}
