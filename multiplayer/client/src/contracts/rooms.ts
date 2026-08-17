export interface PlayerSnapshot {
  playerId: string;
  nickname: string;
  isHost: boolean;
  isReady: boolean;
  isConnected: boolean;
}

export interface RoomAiConfiguration {
  provider: string;
  endpoint: string;
  model: string;
  credentialPresent: boolean;
}

export interface RoomSnapshot {
  roomId: string;
  inviteCode: string;
  hostPlayerId: string;
  maxPlayers: number;
  status: string;
  revision: number;
  players: PlayerSnapshot[];
  aiConfiguration: RoomAiConfiguration | null;
}

export interface CreateRoomRequest {
  nickname: string;
  maxPlayers: number;
}

export interface JoinRoomRequest {
  inviteCode: string;
  nickname: string;
}

export interface SetReadyRequest {
  isReady: boolean;
}

export interface RoomCreatedResponse {
  roomId: string;
  inviteCode: string;
  playerId: string;
  playerSessionToken: string;
  room: RoomSnapshot;
}

export interface RoomJoinedResponse {
  playerId: string;
  playerSessionToken: string;
  room: RoomSnapshot;
}

export interface RoomClosedResponse {
  roomWasClosed: boolean;
}

export interface UpdateRoomAiConfigurationRequest {
  provider: string;
  endpoint: string;
  model: string;
  apiKey?: string;
}

export interface AiConnectionTestResult {
  success: boolean;
  provider?: string | null;
  model?: string | null;
  latencyMs?: number | null;
  code?: string | null;
}
