<script setup lang="ts">
import { ref, watch } from 'vue';
import type { AiConnectionTestResult, RoomAiConfiguration, RoomSnapshot } from '../contracts/rooms';
import { safeApiMessage } from '../api/client';
import { RoomsApi } from '../api/rooms';

const defaultProvider = 'deepseek';
const defaultEndpoint = 'https://api.deepseek.com/v1/chat/completions';

const props = defineProps<{
  api: RoomsApi;
  roomId: string;
  token: string;
  configuration?: RoomAiConfiguration | null;
}>();

const emit = defineEmits<{ snapshot: [snapshot: RoomSnapshot] }>();
const provider = ref(defaultProvider);
const endpoint = ref(defaultEndpoint);
const model = ref('deepseek-chat');
const apiKey = ref('');
const busy = ref(false);
const testBusy = ref(false);
const errorMessage = ref('');
const resultMessage = ref('');

watch(() => props.configuration, (configuration) => {
  if (!configuration) return;
  provider.value = configuration.provider;
  endpoint.value = configuration.endpoint;
  model.value = configuration.model;
}, { immediate: true });

function applyProviderPreset(): void {
  if (provider.value === defaultProvider && !endpoint.value) {
    endpoint.value = defaultEndpoint;
  }
}

function connectionResultMessage(result: AiConnectionTestResult): string {
  if (result.success) {
    return result.latencyMs == null ? 'Connection accepted by provider.' : `Connection accepted · ${result.latencyMs} ms`;
  }

  switch (result.code) {
    case 'TEST_BUSY': return 'Connection test already in progress.';
    case 'ENDPOINT_REJECTED': return 'Endpoint was rejected by server security policy.';
    case 'CREDENTIAL_MISSING': return 'Save a credential before testing the connection.';
    case 'PROVIDER_UNAUTHORIZED': return 'Provider rejected the credential.';
    case 'TIMEOUT': return 'Provider connection timed out.';
    default: return result.code ? `Connection test failed: ${result.code}` : 'Connection test failed.';
  }
}

async function saveConfiguration(): Promise<void> {
  errorMessage.value = '';
  resultMessage.value = '';
  if (!provider.value.trim() || !endpoint.value.trim() || !model.value.trim()) {
    errorMessage.value = 'Provider, endpoint, and model are required.';
    return;
  }

  let submitted = false;
  busy.value = true;
  try {
    await props.api.updateAiConfiguration(props.roomId, props.token, {
      provider: provider.value.trim(),
      endpoint: endpoint.value.trim(),
      model: model.value.trim(),
      ...(apiKey.value ? { apiKey: apiKey.value } : {}),
    });
    submitted = true;
    emit('snapshot', await props.api.getSnapshot(props.roomId, props.token));
    resultMessage.value = 'AI configuration saved.';
  } catch (error) {
    errorMessage.value = safeApiMessage(error);
    submitted = true;
  } finally {
    if (submitted) apiKey.value = '';
    busy.value = false;
  }
}

async function removeCredential(): Promise<void> {
  if (!window.confirm('Remove the room API key?')) return;
  errorMessage.value = '';
  resultMessage.value = '';
  busy.value = true;
  try {
    await props.api.removeCredential(props.roomId, props.token);
    emit('snapshot', await props.api.getSnapshot(props.roomId, props.token));
    resultMessage.value = 'API key removed.';
  } catch (error) {
    errorMessage.value = safeApiMessage(error);
  } finally {
    apiKey.value = '';
    busy.value = false;
  }
}

async function testConnection(): Promise<void> {
  errorMessage.value = '';
  resultMessage.value = '';
  testBusy.value = true;
  try {
    resultMessage.value = connectionResultMessage(await props.api.testAiConnection(props.roomId, props.token));
  } catch (error) {
    errorMessage.value = safeApiMessage(error);
  } finally {
    testBusy.value = false;
  }
}
</script>

<template>
  <section class="card ai-card">
    <div class="section-heading"><div><p class="card-kicker">HOST CONTROL</p><h2>AI configuration</h2></div><span class="credential-state">Credential configured: <strong>{{ configuration?.credentialPresent ? 'YES' : 'NO' }}</strong></span></div>
    <div class="ai-form-grid">
      <div><label for="ai-provider">Provider</label><select id="ai-provider" v-model="provider" @change="applyProviderPreset"><option value="deepseek">DeepSeek</option><option value="openai-compatible">OpenAI-compatible</option></select></div>
      <div><label for="ai-model">Model</label><input id="ai-model" v-model="model" autocomplete="off" /></div>
    </div>
    <label for="ai-endpoint">Endpoint</label>
    <input id="ai-endpoint" v-model="endpoint" autocomplete="url" spellcheck="false" />
    <label for="ai-key">API key <span class="label-note">never saved in this browser</span></label>
    <input id="ai-key" v-model="apiKey" type="password" autocomplete="new-password" spellcheck="false" placeholder="Enter a key to set or replace it" />
    <div class="ai-actions">
      <button class="primary-button" :disabled="busy" type="button" @click="saveConfiguration">Save configuration <span>↗</span></button>
      <button class="secondary-button" :disabled="testBusy" type="button" @click="testConnection">Test connection <span>→</span></button>
      <button class="quiet-button" :disabled="busy" type="button" @click="removeCredential">Remove API key</button>
    </div>
    <p v-if="resultMessage" class="success-banner" role="status">{{ resultMessage }}</p>
    <p v-if="errorMessage" class="error-banner" role="alert">{{ errorMessage }}</p>
  </section>
</template>
