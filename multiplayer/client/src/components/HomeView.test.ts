import { describe, expect, it, vi } from 'vitest';
import { mount } from '@vue/test-utils';
import type { GameSnapshot } from '../contracts/rooms';
import HomeView from './HomeView.vue';
import LobbyView from './LobbyView.vue';
import { RoomsApi } from '../api/rooms';

describe('lobby views', () => {
  it('validates create and join forms before emitting requests', async () => {
    const wrapper = mount(HomeView, { props: { busy: false, errorMessage: '' } });
    await wrapper.get('form').trigger('submit');
    expect(wrapper.text()).toContain('Enter a nickname');
    expect(wrapper.emitted('create')).toBeUndefined();

    await wrapper.get('#create-nickname').setValue('Host');
    await wrapper.get('form').trigger('submit');
    expect(wrapper.emitted('create')?.[0]).toEqual([{ nickname: 'Host', maxPlayers: 4 }]);
  });

  it('renders server snapshot players and distinguishes host controls', () => {
    const wrapper = mount(LobbyView, {
      props: {
      currentPlayerId: 'player-1',
        gameSnapshot: null,
        busy: false,
        errorMessage: '',
        connectionStatus: 'connected',
        api: {} as RoomsApi,
        token: 'session-token',
        room: {
          roomId: 'room-1', inviteCode: 'NIGHT-42', hostPlayerId: 'player-1', maxPlayers: 4,
          status: 'Open', revision: 3,
          players: [{ playerId: 'player-1', nickname: 'Host', isHost: true, isReady: false, isConnected: true }],
          aiConfiguration: null,
        },
      },
    });

    expect(wrapper.text()).toContain('NIGHT-42');
    expect(wrapper.text()).toContain('Host · HOST');
    expect(wrapper.text()).toContain('Close room');
  });

  it('does not render host AI controls for a member', () => {
    const wrapper = mount(LobbyView, {
      props: {
      currentPlayerId: 'player-2',
        gameSnapshot: null,
        busy: false,
        errorMessage: '',
        connectionStatus: 'connected',
        api: {} as RoomsApi,
        token: 'session-token',
        room: {
          roomId: 'room-1', inviteCode: 'NIGHT-42', hostPlayerId: 'player-1', maxPlayers: 4,
          status: 'Lobby', revision: 3,
          players: [{ playerId: 'player-2', nickname: 'Member', isHost: false, isReady: false, isConnected: true }],
          aiConfiguration: null,
        },
      },
    });

    expect(wrapper.text()).not.toContain('AI configuration');
  });

  it('shows only the own check action and sends the minimal check intent', async () => {
    const resolveCheck = vi.fn().mockResolvedValue({
      snapshot: {
        roomId: 'room-1', revision: 2, status: 'Active', createdAt: '2026-08-17T00:00:00Z',
        characters: [
          { characterId: 'character-1', ownerPlayerId: 'player-1', name: 'Host Character', checkValues: { spotHidden: 60 } },
          { characterId: 'character-2', ownerPlayerId: 'player-2', name: 'Member Character', checkValues: {} },
        ],
        lastCheck: { checkId: 'check-1', playerId: 'player-1', characterId: 'character-1', checkKey: 'spotHidden', target: 60, roll: 41, successLevel: 'regular', passed: true, gameRevision: 2, createdAt: '2026-08-17T00:00:00Z' },
      },
      check: { target: 60, roll: 41, successLevel: 'regular', passed: true },
    });
    const api = Object.assign({ resolveCheck }, {} as RoomsApi) as RoomsApi;
    const wrapper = mount(LobbyView, {
      props: {
        currentPlayerId: 'player-1', gameSnapshot: {
          roomId: 'room-1', revision: 1, status: 'Active', createdAt: '2026-08-17T00:00:00Z',
          characters: [
            { characterId: 'character-1', ownerPlayerId: 'player-1', name: 'Host Character', checkValues: { spotHidden: 60 } },
            { characterId: 'character-2', ownerPlayerId: 'player-2', name: 'Member Character', checkValues: {} },
          ], lastCheck: null,
        },
        busy: false, errorMessage: '', connectionStatus: 'connected', api, token: 'session-token',
        room: {
          roomId: 'room-1', inviteCode: 'NIGHT-42', hostPlayerId: 'player-1', maxPlayers: 2, status: 'Open', revision: 3,
          players: [
            { playerId: 'player-1', nickname: 'Host', isHost: true, isReady: false, isConnected: true },
            { playerId: 'player-2', nickname: 'Member', isHost: false, isReady: false, isConnected: true },
          ], aiConfiguration: null,
        },
      },
    });

    expect(wrapper.findAll('[data-testid="check-spotHidden"]')).toHaveLength(1);
    expect(wrapper.text()).toContain('Other players\' checks are resolved from their own views.');
    await wrapper.get('[data-testid="check-spotHidden"]').trigger('click');
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(resolveCheck).toHaveBeenCalledWith('room-1', 'session-token', {
      characterId: 'character-1', checkKey: 'spotHidden',
    });
    const result = await resolveCheck.mock.results[0].value as { snapshot: GameSnapshot };
    await wrapper.setProps({ gameSnapshot: result.snapshot });
    expect(wrapper.get('[data-testid="last-check"]').text()).toContain('regular');
  });
});
