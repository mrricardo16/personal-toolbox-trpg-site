import { beforeEach, describe, expect, it } from 'vitest';
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

    savePlayerSession(session);

    expect(sessionStorage.getItem(playerSessionStorageKey)).toContain('session-token');
    expect(restorePlayerSession()).toEqual(session);
    expect(localStorage.length).toBe(0);
  });

  it('clears the session and rejects malformed data', () => {
    sessionStorage.setItem(playerSessionStorageKey, '{invalid');
    expect(restorePlayerSession()).toBeNull();

    savePlayerSession({ roomId: 'room-1', playerId: 'player-1', playerSessionToken: 'token' });
    clearPlayerSession();
    expect(restorePlayerSession()).toBeNull();
  });
});
