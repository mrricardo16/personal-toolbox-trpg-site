<script setup lang="ts">
import { ref } from 'vue';
import type { CreateRoomRequest, JoinRoomRequest } from '../contracts/rooms';

defineProps<{ busy: boolean; errorMessage: string }>();
const emit = defineEmits<{
  create: [request: CreateRoomRequest];
  join: [request: JoinRoomRequest];
}>();

const createNickname = ref('');
const maxPlayers = ref(4);
const inviteCode = ref('');
const joinNickname = ref('');
const localError = ref('');

function submitCreate(): void {
  localError.value = '';
  if (!createNickname.value.trim() || maxPlayers.value < 1) {
    localError.value = 'Enter a nickname and a valid player limit.';
    return;
  }

  emit('create', { nickname: createNickname.value.trim(), maxPlayers: maxPlayers.value });
}

function submitJoin(): void {
  localError.value = '';
  if (!inviteCode.value.trim() || !joinNickname.value.trim()) {
    localError.value = 'Enter an invite code and a nickname.';
    return;
  }

  emit('join', { inviteCode: inviteCode.value.trim(), nickname: joinNickname.value.trim() });
}
</script>

<template>
  <main class="home-shell">
    <section class="hero-panel">
      <p class="eyebrow">TRPG / MULTIPLAYER LOBBY</p>
      <h1>Gather the table.<br /><span>Keep the story shared.</span></h1>
      <p class="hero-copy">Create a room or join one with an invite code. The server remains the source of truth for every lobby change.</p>
      <div class="principle-note"><span class="signal-dot" /> Server-authoritative foundation</div>
    </section>

    <section class="form-grid" aria-label="Room actions">
      <form class="card form-card" @submit.prevent="submitCreate">
        <div class="card-heading"><span class="step-mark">01</span><div><p class="card-kicker">START A ROOM</p><h2>Create room</h2></div></div>
        <label for="create-nickname">Nickname</label>
        <input id="create-nickname" v-model="createNickname" autocomplete="nickname" placeholder="Keeper or player name" />
        <label for="max-players">Max players</label>
        <input id="max-players" v-model.number="maxPlayers" type="number" min="1" max="12" />
        <button class="primary-button" :disabled="busy" type="submit">Create room <span>↗</span></button>
      </form>

      <form class="card form-card" @submit.prevent="submitJoin">
        <div class="card-heading"><span class="step-mark muted-mark">02</span><div><p class="card-kicker">JOIN A TABLE</p><h2>Join room</h2></div></div>
        <label for="invite-code">Invite code</label>
        <input id="invite-code" v-model="inviteCode" autocomplete="off" placeholder="e.g. NIGHT-42" />
        <label for="join-nickname">Nickname</label>
        <input id="join-nickname" v-model="joinNickname" autocomplete="nickname" placeholder="Your player name" />
        <button class="secondary-button" :disabled="busy" type="submit">Join room <span>→</span></button>
      </form>
    </section>

    <p v-if="localError || errorMessage" class="error-banner" role="alert">{{ localError || errorMessage }}</p>
  </main>
</template>
