<script setup lang="ts">
import { ref } from 'vue';
import type { GameSnapshot, PlayerSnapshot, RoomSnapshot } from '../contracts/rooms';
import type { RoomConnectionStatus } from '../realtime/roomConnection';
import { RoomsApi } from '../api/rooms';
import HostAiConfigPanel from './HostAiConfigPanel.vue';
import { safeApiMessage } from '../api/client';

defineProps<{
  room: RoomSnapshot;
  gameSnapshot: GameSnapshot | null;
  currentPlayerId: string;
  busy: boolean;
  errorMessage: string;
  connectionStatus: RoomConnectionStatus;
  api: RoomsApi;
  token: string;
}>();

const emit = defineEmits<{
  ready: [];
  leave: [];
  snapshot: [snapshot: RoomSnapshot];
  gameSnapshot: [snapshot: GameSnapshot];
}>();

const gameBusy = ref(false);
const gameError = ref('');

function playerLabel(player: PlayerSnapshot): string {
  return player.isHost ? `${player.nickname} · HOST` : player.nickname;
}

function isOwnCharacter(character: GameSnapshot['characters'][number], currentPlayerId: string): boolean {
  return character.ownerPlayerId === currentPlayerId;
}

async function initializeGame(props: { room: RoomSnapshot; api: RoomsApi; token: string }): Promise<void> {
  gameBusy.value = true;
  gameError.value = '';
  try {
    const snapshot = await props.api.initializeGame(props.room.roomId, props.token, {
      characters: props.room.players.map((player) => ({
        playerId: player.playerId,
        name: `${player.nickname} Character`,
        checkValues: { spotHidden: 60 },
        health: { currentHp: 12, maxHp: 12, con: 60 },
      })),
    });
    emit('gameSnapshot', snapshot);
  } catch (error) {
    gameError.value = safeApiMessage(error);
  } finally {
    gameBusy.value = false;
  }
}

async function resolveCheck(
  props: { room: RoomSnapshot; api: RoomsApi; token: string },
  characterId: string,
  checkKey: string,
): Promise<void> {
  gameBusy.value = true;
  gameError.value = '';
  try {
    const result = await props.api.resolveCheck(props.room.roomId, props.token, { characterId, checkKey });
    emit('gameSnapshot', result.snapshot);
  } catch (error) {
    gameError.value = safeApiMessage(error);
  } finally {
    gameBusy.value = false;
  }
}
</script>

<template>
  <main class="lobby-shell">
    <header class="lobby-header">
      <div><p class="eyebrow">ACTIVE LOBBY</p><h1>{{ room.inviteCode }}</h1><p class="room-id">{{ room.roomId }}</p></div>
      <div class="header-meta"><span class="status-pill"><span class="signal-dot" :class="{ reconnecting: connectionStatus !== 'connected' }" /> {{ connectionStatus }}</span><span>REV {{ room.revision }}</span></div>
    </header>

    <section class="lobby-grid">
      <div class="card roster-card">
        <div class="section-heading"><div><p class="card-kicker">ROOM ROSTER</p><h2>{{ room.players.length }} / {{ room.maxPlayers }} players</h2></div><span class="revision-stamp">SERVER SNAPSHOT</span></div>
        <ul class="player-list">
          <li v-for="player in room.players" :key="player.playerId" :class="{ current: player.playerId === currentPlayerId }">
            <span class="avatar">{{ player.nickname.slice(0, 1).toUpperCase() }}</span>
            <span class="player-name">{{ playerLabel(player) }}<small v-if="player.playerId === currentPlayerId">YOU</small></span>
            <span class="player-state" :class="{ ready: player.isReady }">{{ player.isReady ? 'READY' : 'WAITING' }} · {{ player.isConnected ? 'ONLINE' : 'OFFLINE' }}</span>
          </li>
        </ul>
      </div>

      <aside class="card control-card">
        <p class="card-kicker">TABLE CONTROL</p>
        <h2>Ready when you are.</h2>
        <p class="muted-copy">Lobby changes are accepted by the server and returned as a fresh snapshot.</p>
        <button class="primary-button" :disabled="busy" type="button" @click="emit('ready')">Toggle ready <span>↗</span></button>
        <button class="quiet-button" :disabled="busy" type="button" @click="emit('leave')">{{ room.hostPlayerId === currentPlayerId ? 'Close room' : 'Leave room' }}</button>
        <p v-if="errorMessage" class="error-banner" role="alert">{{ errorMessage }}</p>
      </aside>
    </section>
    <HostAiConfigPanel
      v-if="room.hostPlayerId === currentPlayerId"
      :api="api"
      :room-id="room.roomId"
      :token="token"
      :configuration="room.aiConfiguration"
      @snapshot="emit('snapshot', $event)"
    />

    <section class="card game-card">
      <div class="section-heading"><div><p class="card-kicker">CHECK GAMEPLAY</p><h2>Shared game state</h2></div><span v-if="gameSnapshot" class="revision-stamp">GAME REV {{ gameSnapshot.revision }}</span></div>
      <template v-if="gameSnapshot">
        <ul class="character-list">
          <li v-for="character in gameSnapshot.characters" :key="character.characterId" :class="{ current: isOwnCharacter(character, currentPlayerId) }">
            <div class="character-heading"><strong>{{ character.name }}</strong><span>{{ isOwnCharacter(character, currentPlayerId) ? 'YOUR CHARACTER' : 'OTHER CHARACTER' }}</span></div>
            <p v-if="character.health" class="character-health" data-testid="character-health">HP {{ character.health.currentHp }} / {{ character.health.maxHp }}<span v-if="character.health.majorWound"> · MAJOR WOUND</span><span v-if="character.health.unconscious"> · UNCONSCIOUS</span><span v-if="character.health.dying"> · DYING</span><span v-if="character.health.dead"> · DEAD</span></p>
            <p v-else class="muted-copy character-note">Health details are private to the character owner.</p>
            <div v-if="isOwnCharacter(character, currentPlayerId)" class="check-list">
              <button v-for="(_target, checkKey) in character.checkValues" :key="checkKey" class="secondary-button check-button" :data-testid="`check-${checkKey}`" :disabled="gameBusy" type="button" @click="resolveCheck({ room, api, token }, character.characterId, checkKey)">
                Roll {{ checkKey }} · {{ _target }} <span>→</span>
              </button>
            </div>
            <p v-else class="muted-copy character-note">Other players' checks are resolved from their own views.</p>
          </li>
        </ul>
        <p v-if="gameSnapshot.lastCheck" class="last-check" data-testid="last-check">Last check: {{ gameSnapshot.lastCheck.checkKey }} · {{ gameSnapshot.lastCheck.successLevel }} · {{ gameSnapshot.lastCheck.passed ? 'PASS' : 'FAIL' }} · roll {{ gameSnapshot.lastCheck.roll }}</p>
      </template>
      <template v-else>
        <p class="muted-copy">The host can initialize the minimal shared roster when the table is ready.</p>
        <button v-if="room.hostPlayerId === currentPlayerId" class="primary-button" :disabled="busy || gameBusy" type="button" @click="initializeGame({ room, api, token })">Initialize check game <span>→</span></button>
        <p v-else class="muted-copy">Waiting for the host to initialize the check game.</p>
      </template>
      <p v-if="gameError" class="error-banner" role="alert">{{ gameError }}</p>
    </section>
  </main>
</template>
