import { describe, expect, it } from 'vitest';
import type { GameSnapshot } from '../contracts/rooms';
import { shouldAcceptGameSnapshot } from './gameSnapshot';

const snapshot = (revision: number, roomId = 'room-1'): GameSnapshot => ({
  roomId,
  revision,
  status: 'Active',
  createdAt: '2026-08-17T00:00:00Z',
  characters: [],
  lastCheck: null,
});

const combatSnapshot = (revision: number, viewerActions: NonNullable<NonNullable<GameSnapshot['combat']>['viewerActions']>): GameSnapshot => ({
  ...snapshot(revision),
  combat: {
    active: true, round: 1, currentActorParticipantId: 'character-1', participants: [], lastExchange: null, pending: null, viewerActions,
  },
});

describe('game snapshot recovery state', () => {
  it('accepts only current-room snapshots that are not older than the current revision', () => {
    expect(shouldAcceptGameSnapshot(null, snapshot(1), 'room-1')).toBe(true);
    expect(shouldAcceptGameSnapshot(snapshot(2), snapshot(2), 'room-1')).toBe(true);
    expect(shouldAcceptGameSnapshot(snapshot(2), snapshot(1), 'room-1')).toBe(false);
    expect(shouldAcceptGameSnapshot(snapshot(2), snapshot(3, 'room-2'), 'room-1')).toBe(false);
  });

  it('rejects lower-revision combat snapshots regardless of changed projected actions', () => {
    expect(shouldAcceptGameSnapshot(
      combatSnapshot(12, { actorCharacterId: 'character-1', canMeleeAttack: false, canPass: true, eligibleTargetParticipantIds: [], pendingResponse: null }),
      combatSnapshot(11, { actorCharacterId: 'character-1', canMeleeAttack: false, canPass: false, eligibleTargetParticipantIds: [], pendingResponse: null }),
      'room-1',
    )).toBe(false);
  });
});
