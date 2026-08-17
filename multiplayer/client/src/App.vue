<script setup lang="ts">
import { onMounted, ref } from 'vue';
import HomeView from './components/HomeView.vue';
import LobbyView from './components/LobbyView.vue';
import type { RoomSnapshot } from './contracts/rooms';
import { safeApiMessage } from './api/client';
import { RoomsApi } from './api/rooms';
import {
  clearPlayerSession,
  restorePlayerSession,
  savePlayerSession,
  type PlayerSession,
} from './state/sessionStorage';
import './styles.css';

const roomsApi = new RoomsApi();
const room = ref<RoomSnapshot | null>(null);
const session = ref<PlayerSession | null>(null);
const busy = ref(false);
const errorMessage = ref('');
const recoveryMessage = ref('');

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
    const response = await roomsApi.create(request);
    const nextSession = {
      roomId: response.roomId,
      playerId: response.playerId,
      playerSessionToken: response.playerSessionToken,
    };
    savePlayerSession(nextSession);
    session.value = nextSession;
    room.value = response.room;
  });
}

async function joinRoom(request: { inviteCode: string; nickname: string }): Promise<void> {
  await runRequest(async () => {
    const response = await roomsApi.join(request);
    const nextSession = {
      roomId: response.room.roomId,
      playerId: response.playerId,
      playerSessionToken: response.playerSessionToken,
    };
    savePlayerSession(nextSession);
    session.value = nextSession;
    room.value = response.room;
  });
}

async function restoreRoom(): Promise<void> {
  const stored = restorePlayerSession();
  if (!stored) {
    return;
  }

  session.value = stored;
  await runRequest(async () => {
    room.value = await roomsApi.getSnapshot(stored.roomId, stored.playerSessionToken);
    recoveryMessage.value = 'Session restored from this tab.';
  });

  if (!room.value) {
    clearPlayerSession();
    session.value = null;
    recoveryMessage.value = 'Saved session is no longer available.';
  }
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
    clearPlayerSession();
    session.value = null;
    room.value = null;
  });
}

onMounted(() => { void restoreRoom(); });
</script>

<template>
  <div class="app-frame">
    <div class="topline"><span>TRPG AI HOST ASSISTANT</span><span>PHASE 1 / LOBBY</span></div>
    <HomeView v-if="!room" :busy="busy" :error-message="errorMessage || recoveryMessage" @create="createRoom" @join="joinRoom" />
    <LobbyView v-else :room="room" :current-player-id="session!.playerId" :busy="busy" :error-message="errorMessage" @ready="toggleReady" @leave="leaveRoom" />
  </div>
</template>
