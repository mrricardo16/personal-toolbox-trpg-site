import {
  HubConnectionBuilder,
  type HubConnection,
} from '@microsoft/signalr';
import type { RoomSnapshot } from '../contracts/rooms';

export type RoomConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

export interface HubConnectionLike {
  start(): Promise<void>;
  stop(): Promise<void>;
  invoke(methodName: string, ...args: unknown[]): Promise<unknown>;
  on(methodName: string, callback: (...args: any[]) => void): void;
  onreconnecting(callback: (error?: Error) => void): void;
  onreconnected(callback: (connectionId?: string) => void): void;
  onclose(callback: (error?: Error) => void): void;
}

export interface HubConnectionFactory {
  create(): HubConnectionLike;
}

export interface RoomConnectionCallbacks {
  onSnapshot(snapshot: RoomSnapshot): void;
  onRoomClosed(): void;
  onStatus(status: RoomConnectionStatus): void;
}

class SignalRHubConnectionFactory implements HubConnectionFactory {
  create(): HubConnectionLike {
    return new HubConnectionBuilder()
      .withUrl('/hubs/room')
      .withAutomaticReconnect([0, 2_000, 5_000, 10_000])
      .build();
  }
}

export class RoomConnection {
  private connection: HubConnectionLike | null = null;
  private sessionToken: string | null = null;
  private intentionallyStopped = false;

  constructor(
    private readonly callbacks: RoomConnectionCallbacks,
    private readonly factory: HubConnectionFactory = new SignalRHubConnectionFactory(),
  ) {}

  async start(sessionToken: string): Promise<RoomSnapshot> {
    this.sessionToken = sessionToken;
    this.intentionallyStopped = false;
    const connection = this.factory.create();
    this.connection = connection;
    this.registerHandlers(connection);
    this.callbacks.onStatus('connecting');
    await connection.start();
    this.callbacks.onStatus('connected');
    return this.attachSession();
  }

  async stop(): Promise<void> {
    this.intentionallyStopped = true;
    const connection = this.connection;
    this.connection = null;
    this.sessionToken = null;
    if (connection) {
      await connection.stop();
    }
    this.callbacks.onStatus('disconnected');
  }

  private registerHandlers(connection: HubConnectionLike): void {
    connection.on('RoomSnapshot', (snapshot: RoomSnapshot) => this.callbacks.onSnapshot(snapshot));
    connection.on('MemberJoined', (event: { snapshot?: RoomSnapshot }) => this.acceptEventSnapshot(event));
    connection.on('MemberLeft', (event: { snapshot?: RoomSnapshot }) => this.acceptEventSnapshot(event));
    connection.on('ReadyChanged', (event: { snapshot?: RoomSnapshot }) => this.acceptEventSnapshot(event));
    connection.on('MemberConnectionChanged', (event: { snapshot?: RoomSnapshot }) => this.acceptEventSnapshot(event));
    connection.on('RoomClosed', () => this.callbacks.onRoomClosed());
    connection.onreconnecting(() => this.callbacks.onStatus('reconnecting'));
    connection.onreconnected(() => {
      void this.reattachAfterTransportReconnect();
    });
    connection.onclose((error?: Error) => {
      if (!this.intentionallyStopped || error) {
        this.callbacks.onStatus('disconnected');
      }
    });
  }

  private acceptEventSnapshot(event: { snapshot?: RoomSnapshot }): void {
    if (event.snapshot) {
      this.callbacks.onSnapshot(event.snapshot);
    }
  }

  private async reattachAfterTransportReconnect(): Promise<void> {
    try {
      await this.attachSession();
      this.callbacks.onStatus('connected');
    } catch {
      this.callbacks.onStatus('disconnected');
    }
  }

  private async attachSession(): Promise<RoomSnapshot> {
    if (!this.connection || !this.sessionToken) {
      throw new Error('Room session is not available.');
    }

    const snapshot = await this.connection.invoke('AttachSession', this.sessionToken) as RoomSnapshot;
    this.callbacks.onSnapshot(snapshot);
    return snapshot;
  }
}

export function createRoomConnection(callbacks: RoomConnectionCallbacks): RoomConnection {
  return new RoomConnection(callbacks);
}

export type ProductionHubConnection = HubConnection;
