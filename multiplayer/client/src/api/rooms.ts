import type {
  AiConnectionTestResult,
  CreateRoomRequest,
  JoinRoomRequest,
  RoomClosedResponse,
  RoomCreatedResponse,
  RoomJoinedResponse,
  RoomAiConfiguration,
  RoomSnapshot,
  SetReadyRequest,
  UpdateRoomAiConfigurationRequest,
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

  updateAiConfiguration(roomId: string, token: string, request: UpdateRoomAiConfigurationRequest): Promise<RoomAiConfiguration> {
    return this.client.request(`/api/rooms/${roomId}/ai-config`, {
      method: 'PUT',
      headers: {
        Authorization: `Bearer ${token}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
    });
  }

  removeCredential(roomId: string, token: string): Promise<RoomAiConfiguration> {
    return this.client.request(`/api/rooms/${roomId}/credential`, {
      method: 'DELETE',
      headers: { Authorization: `Bearer ${token}` },
    });
  }

  testAiConnection(roomId: string, token: string): Promise<AiConnectionTestResult> {
    return this.client.request(`/api/rooms/${roomId}/ai-config/test`, {
      method: 'POST',
      headers: { Authorization: `Bearer ${token}` },
    }, [409]);
  }
}
