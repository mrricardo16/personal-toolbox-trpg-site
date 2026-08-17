<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue';
import HomeView from './components/HomeView.vue';
import LobbyView from './components/LobbyView.vue';
import type { CheckResolvedEvent, GameSnapshot, RoomSnapshot } from './contracts/rooms';
import { ApiRequestError, isTerminalSessionError, safeApiMessage } from './api/client';
import { RoomsApi } from './api/rooms';
import {
  clearPlayerSession,
  restorePlayerSession,
  savePlayerSession,
  type PlayerSession,
} from './state/sessionStorage';
import { createRoomConnection, type RoomConnection, type RoomConnectionStatus } from './realtime/roomConnection';
import { shouldAcceptGameSnapshot } from './state/gameSnapshot';
import './styles.css';

const roomsApi = new RoomsApi();
const room = ref<RoomSnapshot | null>(null);
const gameSnapshot = ref<GameSnapshot | null>(null);
const session = ref<PlayerSession | null>(null);
const busy = ref(false);
const errorMessage = ref('');
const recoveryMessage = ref('');
const connectionStatus = ref<RoomConnectionStatus>('disconnected');
let roomConnection: RoomConnection | null = null;

async function runRequest(action: () => Promise<void>): Promise<void> {
  busy.value = true;
  errorMessage.value = '';
  try {
    await action();
  } catch (error) {
    errorMessage.value = safeApiMessage(error);
  } finally {
    busy.value = false;
  }
}

async function createRoom(request: { nickname: string; maxPlayers: number }): Promise<void> {
  await runRequest(async () => {
    gameSnapshot.value = null;
    const response = await roomsApi.create(request);
    const nextSession = {
      roomId: response.roomId,
      playerId: response.playerId,
      playerSessionToken: response.playerSessionToken,
    };
    if (!savePlayerSession(nextSession)) {
      recoveryMessage.value = 'Session is available in this tab, but browser storage is unavailable.';
    }
    session.value = nextSession;
    room.value = response.room;
    await startRealtime(nextSession);
  });
}

async function joinRoom(request: { inviteCode: string; nickname: string }): Promise<void> {
  await runRequest(async () => {
    gameSnapshot.value = null;
    const response = await roomsApi.join(request);
    const nextSession = {
      roomId: response.room.roomId,
      playerId: response.playerId,
      playerSessionToken: response.playerSessionToken,
    };
    if (!savePlayerSession(nextSession)) {
      recoveryMessage.value = 'Session is available in this tab, but browser storage is unavailable.';
    }
    session.value = nextSession;
    room.value = response.room;
    await startRealtime(nextSession);
  });
}

async function restoreRoom(): Promise<void> {
  const stored = restorePlayerSession();
  if (!stored) {
    return;
  }

  session.value = stored;
  gameSnapshot.value = null;
  busy.value = true;
  errorMessage.value = '';
  try {
    room.value = await roomsApi.getSnapshot(stored.roomId, stored.playerSessionToken);
    await restoreGameSnapshot(stored);
    recoveryMessage.value = 'Session restored from this tab.';
    await startRealtime(stored);
  } catch (error) {
    errorMessage.value = safeApiMessage(error);
    if (isTerminalSessionError(error)) {
      clearPlayerSession();
      session.value = null;
      recoveryMessage.value = 'Saved session is no longer available.';
    } else {
      recoveryMessage.value = 'Session found. Retry when the server is available.';
    }
  } finally {
    busy.value = false;
  }
}

async function restoreGameSnapshot(stored: PlayerSession): Promise<void> {
  try {
    acceptGameSnapshot(await roomsApi.getGame(stored.roomId, stored.playerSessionToken));
  } catch (error) {
    if (!(error instanceof ApiRequestError && error.status === 404)) {
      throw error;
    }
  }
}

function acceptGameSnapshot(snapshot: GameSnapshot): void {
  if (session.value && shouldAcceptGameSnapshot(gameSnapshot.value, snapshot, session.value.roomId)) {
    gameSnapshot.value = snapshot;
  }
}

async function startRealtime(nextSession: PlayerSession): Promise<void> {
  if (roomConnection) {
    await roomConnection.stop();
  }

  roomConnection = createRoomConnection({
    onSnapshot: (snapshot) => { room.value = snapshot; },
    onGameSnapshot: acceptGameSnapshot,
    onCheckResolved: (event: CheckResolvedEvent) => {
      recoveryMessage.value = `Check resolved at game revision ${event.gameRevision}.`;
    },
    onRoomClosed: () => { void handleRoomClosed(); },
    onStatus: (status) => { connectionStatus.value = status; },
  });

  try {
    await roomConnection.start(nextSession.playerSessionToken);
  } catch {
    errorMessage.value = 'Realtime connection failed. You can retry by refreshing the lobby.';
  }
}

async function handleRoomClosed(): Promise<void> {
  await roomConnection?.stop();
  roomConnection = null;
  clearPlayerSession();
  session.value = null;
  room.value = null;
  gameSnapshot.value = null;
  recoveryMessage.value = 'Room closed by host.';
  errorMessage.value = '';
}

async function toggleReady(): Promise<void> {
  if (!session.value || !room.value) return;
  const current = room.value.players.find((player) => player.playerId === session.value?.playerId);
  await runRequest(async () => {
    room.value = await roomsApi.ready(room.value!.roomId, session.value!.playerSessionToken, { isReady: !current?.isReady });
  });
}

async function leaveRoom(): Promise<void> {
  if (!session.value || !room.value) return;
  await runRequest(async () => {
    await roomsApi.leave(room.value!.roomId, session.value!.playerSessionToken);
    await roomConnection?.stop();
    roomConnection = null;
    clearPlayerSession();
    session.value = null;
    room.value = null;
    gameSnapshot.value = null;
  });
}

onMounted(() => { void restoreRoom(); });
onBeforeUnmount(() => { void roomConnection?.stop(); });
</script>

<template>
  <div class="app-frame">
    <div class="topline"><span>TRPG AI HOST ASSISTANT</span><span>PHASE 2B / GAME</span></div>
    <HomeView v-if="!room" :busy="busy" :error-message="errorMessage || recoveryMessage" @create="createRoom" @join="joinRoom" />
    <LobbyView v-else :room="room" :game-snapshot="gameSnapshot" :current-player-id="session!.playerId" :busy="busy" :error-message="errorMessage" :connection-status="connectionStatus" :api="roomsApi" :token="session!.playerSessionToken" @ready="toggleReady" @leave="leaveRoom" @snapshot="room = $event" @game-snapshot="acceptGameSnapshot" />
  </div>
</template>
