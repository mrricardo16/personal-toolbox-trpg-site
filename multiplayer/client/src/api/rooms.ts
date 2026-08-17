import type {
  CreateRoomRequest,
  JoinRoomRequest,
  RoomClosedResponse,
  RoomCreatedResponse,
  RoomJoinedResponse,
  RoomSnapshot,
  SetReadyRequest,
} from '../contracts/rooms';
import { ApiClient } from './client';

export class RoomsApi {
  constructor(private readonly client = new ApiClient()) {}

  create(request: CreateRoomRequest): Promise<RoomCreatedResponse> {
    return this.client.request('/api/rooms', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });
  }

  join(request: JoinRoomRequest): Promise<RoomJoinedResponse> {
    return this.client.request('/api/rooms/join', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });
  }

  getSnapshot(roomId: string, token: string): Promise<RoomSnapshot> {
    return this.client.request(`/api/rooms/${roomId}`, {
      headers: { Authorization: `Bearer ${token}` },
    });
  }

  ready(roomId: string, token: string, request: SetReadyRequest): Promise<RoomSnapshot> {
    return this.client.request(`/api/rooms/${roomId}/ready`, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
    });
  }

  leave(roomId: string, token: string): Promise<RoomSnapshot | RoomClosedResponse> {
    return this.client.request(`/api/rooms/${roomId}/leave`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${token}` },
    });
  }
}
