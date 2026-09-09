<script setup lang="ts">
import { ref } from 'vue';
import type { GameSnapshot, PlayerSnapshot, RoomSnapshot } from '../contracts/rooms';
import type { RoomConnectionStatus } from '../realtime/roomConnection';
import { RoomsApi } from '../api/rooms';
import HostAiConfigPanel from './HostAiConfigPanel.vue';
import { ApiRequestError, safeApiMessage } from '../api/client';

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
const selectedTargetParticipantId = ref('');

function playerLabel(player: PlayerSnapshot): string {
  return player.isHost ? `${player.nickname} · HOST` : player.nickname;
}

function isOwnCharacter(character: GameSnapshot['characters'][number], currentPlayerId: string): boolean {
  return character.ownerPlayerId === currentPlayerId;
}

function combatParticipantLabel(snapshot: GameSnapshot, participantId: string | null): string {
  return snapshot.combat?.participants.find((participant) => participant.participantId === participantId)?.label ?? '—';
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

async function submitCombatIntent(
  props: { room: RoomSnapshot; api: RoomsApi; token: string },
  submit: () => Promise<GameSnapshot>,
): Promise<void> {
  gameBusy.value = true;
  gameError.value = '';
  try {
    emit('gameSnapshot', await submit());
  } catch (error) {
    if (error instanceof ApiRequestError && error.status === 409 && error.serverCode === 'stale_game_revision') {
      try {
        emit('gameSnapshot', await props.api.getGame(props.room.roomId, props.token));
        gameError.value = 'Combat changed; choose again';
      } catch (refreshError) {
        gameError.value = safeApiMessage(refreshError);
      }
    } else {
      gameError.value = safeApiMessage(error);
    }
  } finally {
    gameBusy.value = false;
  }
}

function meleeAttack(props: { room: RoomSnapshot; api: RoomsApi; token: string }, snapshot: GameSnapshot): Promise<void> {
  const actions = snapshot.combat?.viewerActions;
  if (!actions?.actorCharacterId || !actions.eligibleTargetParticipantIds.includes(selectedTargetParticipantId.value)) return Promise.resolve();
  return submitCombatIntent(props, () => props.api.meleeAttack(props.room.roomId, props.token, {
    expectedGameRevision: snapshot.revision,
    actorCharacterId: actions.actorCharacterId as string,
    targetParticipantId: selectedTargetParticipantId.value,
  }));
}

function passCombatTurn(props: { room: RoomSnapshot; api: RoomsApi; token: string }, snapshot: GameSnapshot): Promise<void> {
  const actorCharacterId = snapshot.combat?.viewerActions?.actorCharacterId;
  if (!actorCharacterId) return Promise.resolve();
  return submitCombatIntent(props, () => props.api.passCombatTurn(props.room.roomId, props.token, { expectedGameRevision: snapshot.revision, actorCharacterId }));
}

function respondToCombat(props: { room: RoomSnapshot; api: RoomsApi; token: string }, snapshot: GameSnapshot, response: 'dodge' | 'fight_back'): Promise<void> {
  const exchangeId = snapshot.combat?.viewerActions?.pendingResponse?.exchangeId;
  if (!exchangeId) return Promise.resolve();
  return submitCombatIntent(props, () => props.api.respondToCombat(props.room.roomId, props.token, { expectedGameRevision: snapshot.revision, exchangeId, response }));
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
            <p v-if="character.health" class="character-health" data-testid="character-health">HP {{ character.health.currentHp }} / {{ character.health.maxHp }}<span v-if="character.health.majorWound"> · MAJOR WOUND</span><span v-if="character.health.unconscious"> · UNCONSCIOUS</span><span v-if="character.health.dying"> · DYING</span><span v-if="character.health.dead"> · DEAD</span><span v-if="character.health.stabilized"> · STABILIZED</span></p>
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
        <section v-if="gameSnapshot.combat" class="combat-status" data-testid="combat-status">
          <p class="card-kicker">COMBAT {{ gameSnapshot.combat.active ? 'ACTIVE' : 'INACTIVE' }}</p>
          <p>ROUND {{ gameSnapshot.combat.round }}</p>
          <p>CURRENT {{ combatParticipantLabel(gameSnapshot, gameSnapshot.combat.currentActorParticipantId) }}</p>
          <p>ORDER {{ gameSnapshot.combat.participants.map((participant) => participant.label).join(' → ') }}</p>
          <p v-if="gameSnapshot.combat.lastExchange">LAST {{ gameSnapshot.combat.lastExchange.outcome }} · {{ combatParticipantLabel(gameSnapshot, gameSnapshot.combat.lastExchange.winnerParticipantId) }} · {{ gameSnapshot.combat.lastExchange.dispositionPending ? 'PENDING' : 'COMPLETE' }}</p>
          <p v-if="gameSnapshot.combat.pending">WAITING {{ gameSnapshot.combat.pending.role }} · {{ gameSnapshot.combat.pending.status }}</p>
          <p v-if="gameSnapshot.combat.lastDamage" data-testid="last-damage">LAST DAMAGE {{ combatParticipantLabel(gameSnapshot, gameSnapshot.combat.lastDamage.ownerParticipantId) }} → {{ combatParticipantLabel(gameSnapshot, gameSnapshot.combat.lastDamage.targetParticipantId) }} · {{ gameSnapshot.combat.lastDamage.outcome }} · NET {{ gameSnapshot.combat.lastDamage.netDamage }} · {{ gameSnapshot.combat.lastDamage.targetDefeated ? 'DEFEATED' : 'ACTIVE' }}</p>
          <select v-if="gameSnapshot.combat.viewerActions?.canMeleeAttack" v-model="selectedTargetParticipantId" data-testid="combat-target" :disabled="gameBusy">
            <option disabled value="">Choose target</option>
            <option v-for="participantId in gameSnapshot.combat.viewerActions.eligibleTargetParticipantIds" :key="participantId" :value="participantId">{{ combatParticipantLabel(gameSnapshot, participantId) }}</option>
          </select>
          <button v-if="gameSnapshot.combat.viewerActions?.canMeleeAttack" class="secondary-button" data-testid="combat-attack" :disabled="gameBusy || !gameSnapshot.combat.viewerActions.eligibleTargetParticipantIds.includes(selectedTargetParticipantId)" type="button" @click="meleeAttack({ room, api, token }, gameSnapshot)">Attack</button>
          <button v-if="gameSnapshot.combat.viewerActions?.canPass" class="secondary-button" data-testid="combat-pass" :disabled="gameBusy" type="button" @click="passCombatTurn({ room, api, token }, gameSnapshot)">Pass</button>
          <button v-for="response in gameSnapshot.combat.viewerActions?.pendingResponse?.availableResponses ?? []" :key="response" class="secondary-button" :data-testid="`combat-response-${response}`" :disabled="gameBusy" type="button" @click="respondToCombat({ room, api, token }, gameSnapshot, response)">{{ response === 'dodge' ? 'Dodge' : 'Fight Back' }}</button>
        </section>
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
