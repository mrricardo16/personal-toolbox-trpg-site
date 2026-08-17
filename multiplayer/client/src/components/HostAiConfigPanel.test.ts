import { describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import type { RoomSnapshot } from '../contracts/rooms';
import { RoomsApi } from '../api/rooms';
import HostAiConfigPanel from './HostAiConfigPanel.vue';

const currentSnapshot: RoomSnapshot = {
  roomId: 'room-1', inviteCode: 'NIGHT-42', hostPlayerId: 'player-1', maxPlayers: 4,
  status: 'Lobby', revision: 2,
  players: [{ playerId: 'player-1', nickname: 'Host', isHost: true, isReady: false, isConnected: true }],
  aiConfiguration: {
    provider: 'deepseek', endpoint: 'https://api.deepseek.com/v1/chat/completions', model: 'deepseek-chat', credentialPresent: true,
  },
};

function fakeApi(overrides: Partial<Record<keyof RoomsApi, ReturnType<typeof vi.fn>>> = {}): RoomsApi {
  return Object.assign(Object.create(RoomsApi.prototype), {
    updateAiConfiguration: vi.fn().mockResolvedValue(currentSnapshot.aiConfiguration),
    getSnapshot: vi.fn().mockResolvedValue(currentSnapshot),
    removeCredential: vi.fn().mockResolvedValue(currentSnapshot.aiConfiguration),
    testAiConnection: vi.fn().mockResolvedValue({ success: true, latencyMs: 12 }),
    ...overrides,
  }) as RoomsApi;
}

describe('HostAiConfigPanel', () => {
  it('clears the API key input after submitting and never writes browser storage', async () => {
    sessionStorage.clear();
    localStorage.clear();
    const api = fakeApi();
    const wrapper = mount(HostAiConfigPanel, {
      props: { api, roomId: 'room-1', token: 'session-token', configuration: currentSnapshot.aiConfiguration },
    });

    await wrapper.get('#ai-key').setValue('TEST-ONLY-SECRET-DO-NOT-STORE');
    await wrapper.get('button').trigger('click');
    await flushPromises();

    expect(api.updateAiConfiguration).toHaveBeenCalledWith('room-1', 'session-token', expect.objectContaining({ apiKey: 'TEST-ONLY-SECRET-DO-NOT-STORE' }));
    expect((wrapper.get('#ai-key').element as HTMLInputElement).value).toBe('');
    expect(sessionStorage.length).toBe(0);
    expect(localStorage.length).toBe(0);
  });

  it('renders sanitized busy and endpoint rejection messages', async () => {
    const api = fakeApi({ testAiConnection: vi.fn().mockResolvedValue({ success: false, code: 'TEST_BUSY' }) });
    const wrapper = mount(HostAiConfigPanel, {
      props: { api, roomId: 'room-1', token: 'session-token', configuration: currentSnapshot.aiConfiguration },
    });

    await wrapper.findAll('button')[1].trigger('click');
    await flushPromises();
    expect(wrapper.text()).toContain('Connection test already in progress.');
  });
});
