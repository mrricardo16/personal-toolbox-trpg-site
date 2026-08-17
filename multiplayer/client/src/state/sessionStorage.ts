export const playerSessionStorageKey = 'trpg-multiplayer:player-session';

export interface PlayerSession {
  roomId: string;
  playerId: string;
  playerSessionToken: string;
}

export function savePlayerSession(session: PlayerSession): void {
  sessionStorage.setItem(playerSessionStorageKey, JSON.stringify(session));
}

export function restorePlayerSession(): PlayerSession | null {
  const raw = sessionStorage.getItem(playerSessionStorageKey);
  if (!raw) {
    return null;
  }

  try {
    const parsed = JSON.parse(raw) as Partial<PlayerSession>;
    if (typeof parsed.roomId !== 'string'
      || typeof parsed.playerId !== 'string'
      || typeof parsed.playerSessionToken !== 'string'
      || !parsed.roomId
      || !parsed.playerId
      || !parsed.playerSessionToken) {
      return null;
    }

    return {
      roomId: parsed.roomId,
      playerId: parsed.playerId,
      playerSessionToken: parsed.playerSessionToken,
    };
  } catch {
    return null;
  }
}

export function clearPlayerSession(): void {
  sessionStorage.removeItem(playerSessionStorageKey);
}
