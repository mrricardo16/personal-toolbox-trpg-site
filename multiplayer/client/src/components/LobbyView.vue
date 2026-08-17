<script setup lang="ts">
import type { PlayerSnapshot, RoomSnapshot } from '../contracts/rooms';

defineProps<{
  room: RoomSnapshot;
  currentPlayerId: string;
  busy: boolean;
  errorMessage: string;
}>();

const emit = defineEmits<{
  ready: [];
  leave: [];
}>();

function playerLabel(player: PlayerSnapshot): string {
  return player.isHost ? `${player.nickname} · HOST` : player.nickname;
}
</script>

<template>
  <main class="lobby-shell">
    <header class="lobby-header">
      <div><p class="eyebrow">ACTIVE LOBBY</p><h1>{{ room.inviteCode }}</h1><p class="room-id">{{ room.roomId }}</p></div>
      <div class="header-meta"><span class="status-pill"><span class="signal-dot" /> {{ room.status }}</span><span>REV {{ room.revision }}</span></div>
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
  </main>
</template>
