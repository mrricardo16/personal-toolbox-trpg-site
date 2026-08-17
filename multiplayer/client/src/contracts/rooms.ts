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

export interface CharacterSnapshot {
  characterId: string;
  ownerPlayerId: string;
  name: string;
  checkValues: Record<string, number>;
}

export interface GameCheckRecord {
  checkId: string;
  playerId: string;
  characterId: string;
  checkKey: string;
  target: number;
  roll: number;
  successLevel: string;
  passed: boolean;
  gameRevision: number;
  createdAt: string;
}

export interface GameSnapshot {
  roomId: string;
  revision: number;
  status: string;
  createdAt: string;
  characters: CharacterSnapshot[];
  lastCheck?: GameCheckRecord | null;
}

export interface CheckResolvedEvent {
  roomId: string;
  checkId: string;
  playerId: string;
  characterId: string;
  checkKey: string;
  gameRevision: number;
}

export interface InitializeGameRequest {
  characters: Array<{
    playerId: string;
    name: string;
    checkValues: Record<string, number>;
  }>;
}

export interface ResolveCheckRequest {
  characterId: string;
  checkKey: string;
}

export interface GameCheckResult {
  snapshot: GameSnapshot;
  check: {
    target: number;
    roll: number;
    successLevel: string;
    passed: boolean;
  };
}
