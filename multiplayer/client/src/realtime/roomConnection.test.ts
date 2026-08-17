import { describe, expect, it, vi } from 'vitest';
import type { CheckResolvedEvent, GameSnapshot, RoomSnapshot } from '../contracts/rooms';
import { RoomConnection, type HubConnectionFactory, type HubConnectionLike } from './roomConnection';

const snapshot = (revision: number): RoomSnapshot => ({
  roomId: 'room-1', inviteCode: 'NIGHT-42', hostPlayerId: 'player-1', maxPlayers: 4,
  status: 'Lobby', revision,
  players: [{ playerId: 'player-1', nickname: 'Host', isHost: true, isReady: false, isConnected: true }],
  aiConfiguration: null,
});

class FakeHubConnection implements HubConnectionLike {
  readonly invoke = vi.fn(async () => snapshot(2));
  private handlers = new Map<string, (...args: any[]) => void>();
  private reconnectingHandler: (() => void) | undefined;
  private reconnectedHandler: (() => void) | undefined;
  private closeHandler: ((error?: Error) => void) | undefined;

  start = vi.fn(async () => undefined);
  stop = vi.fn(async () => undefined);
  on(methodName: string, callback: (...args: any[]) => void): void { this.handlers.set(methodName, callback); }
  onreconnecting(callback: () => void): void { this.reconnectingHandler = callback; }
  onreconnected(callback: () => void): void { this.reconnectedHandler = callback; }
  onclose(callback: (error?: Error) => void): void { this.closeHandler = callback; }
  emit(methodName: string, payload?: unknown): void { this.handlers.get(methodName)?.(payload); }
  async reconnect(): Promise<void> { this.reconnectingHandler?.(); this.reconnectedHandler?.(); }
  close(error?: Error): void { this.closeHandler?.(error); }
}

describe('RoomConnection', () => {
  it('starts, attaches the same session token, and reattaches after transport reconnect', async () => {
    const fake = new FakeHubConnection();
    const factory: HubConnectionFactory = { create: () => fake };
    const snapshots: number[] = [];
    const statuses: string[] = [];
    const connection = new RoomConnection({
      onSnapshot: (value) => snapshots.push(value.revision),
      onGameSnapshot: vi.fn(),
      onCheckResolved: vi.fn(),
      onRoomClosed: vi.fn(),
      onStatus: (value) => statuses.push(value),
    }, factory);

    await connection.start('session-token');
    await fake.reconnect();
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(fake.invoke).toHaveBeenNthCalledWith(1, 'AttachSession', 'session-token');
    expect(fake.invoke).toHaveBeenNthCalledWith(2, 'AttachSession', 'session-token');
    expect(snapshots).toEqual([2, 2]);
    expect(statuses).toEqual(['connecting', 'connected', 'reconnecting', 'connected']);
  });

  it('uses snapshots carried by semantic events and handles RoomClosed', () => {
    const fake = new FakeHubConnection();
    const onSnapshot = vi.fn();
    const onRoomClosed = vi.fn();
    const connection = new RoomConnection({ onSnapshot, onGameSnapshot: vi.fn(), onCheckResolved: vi.fn(), onRoomClosed, onStatus: vi.fn() }, { create: () => fake });

    void connection.start('session-token');
    fake.emit('ReadyChanged', { snapshot: snapshot(7) });
    fake.emit('RoomClosed');

    expect(onSnapshot).toHaveBeenCalledWith(snapshot(7));
    expect(onRoomClosed).toHaveBeenCalledOnce();
  });

  it('delivers game snapshots and check events from the hub', () => {
    const fake = new FakeHubConnection();
    const onGameSnapshot = vi.fn();
    const onCheckResolved = vi.fn();
    const connection = new RoomConnection({
      onSnapshot: vi.fn(),
      onGameSnapshot,
      onCheckResolved,
      onRoomClosed: vi.fn(),
      onStatus: vi.fn(),
    }, { create: () => fake });
    const gameSnapshot = { roomId: 'room-1', revision: 1 } as GameSnapshot;
    const checkEvent = { roomId: 'room-1', gameRevision: 2 } as CheckResolvedEvent;

    void connection.start('session-token');
    fake.emit('GameSnapshot', gameSnapshot);
    fake.emit('CheckResolved', checkEvent);

    expect(onGameSnapshot).toHaveBeenCalledWith(gameSnapshot);
    expect(onCheckResolved).toHaveBeenCalledWith(checkEvent);
    void connection;
  });
});
