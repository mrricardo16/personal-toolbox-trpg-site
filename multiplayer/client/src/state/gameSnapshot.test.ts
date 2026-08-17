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

describe('game snapshot recovery state', () => {
  it('accepts only current-room snapshots that are not older than the current revision', () => {
    expect(shouldAcceptGameSnapshot(null, snapshot(1), 'room-1')).toBe(true);
    expect(shouldAcceptGameSnapshot(snapshot(2), snapshot(2), 'room-1')).toBe(true);
    expect(shouldAcceptGameSnapshot(snapshot(2), snapshot(1), 'room-1')).toBe(false);
    expect(shouldAcceptGameSnapshot(snapshot(2), snapshot(3, 'room-2'), 'room-1')).toBe(false);
  });
});
