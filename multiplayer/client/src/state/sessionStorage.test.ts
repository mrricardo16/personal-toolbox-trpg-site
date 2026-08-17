import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  clearPlayerSession,
  playerSessionStorageKey,
  restorePlayerSession,
  savePlayerSession,
} from './sessionStorage';

describe('player session storage', () => {
  beforeEach(() => sessionStorage.clear());

  it('saves and restores only the session identity fields', () => {
    const session = {
      roomId: 'room-1',
      playerId: 'player-1',
      playerSessionToken: 'session-token',
    };

    expect(savePlayerSession(session)).toBe(true);

    expect(sessionStorage.getItem(playerSessionStorageKey)).toContain('session-token');
    expect(restorePlayerSession()).toEqual(session);
    expect(localStorage.length).toBe(0);
  });

  it('clears the session and rejects malformed data', () => {
    sessionStorage.setItem(playerSessionStorageKey, '{invalid');
    expect(restorePlayerSession()).toBeNull();

    expect(savePlayerSession({ roomId: 'room-1', playerId: 'player-1', playerSessionToken: 'token' })).toBe(true);
    clearPlayerSession();
    expect(restorePlayerSession()).toBeNull();
  });

  it('keeps the in-memory flow alive when sessionStorage is blocked', () => {
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('Blocked', 'SecurityError');
    });

    expect(savePlayerSession({ roomId: 'room-1', playerId: 'player-1', playerSessionToken: 'token' })).toBe(false);
    setItem.mockRestore();
  });
});
