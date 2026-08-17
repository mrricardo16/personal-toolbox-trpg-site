import { describe, expect, it } from 'vitest';
import { mount } from '@vue/test-utils';
import HomeView from './HomeView.vue';
import LobbyView from './LobbyView.vue';

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
        busy: false,
        errorMessage: '',
        room: {
          roomId: 'room-1', inviteCode: 'NIGHT-42', hostPlayerId: 'player-1', maxPlayers: 4,
          status: 'Open', revision: 3,
          players: [{ playerId: 'player-1', nickname: 'Host', isHost: true, isReady: false, isConnected: true }],
        },
      },
    });

    expect(wrapper.text()).toContain('NIGHT-42');
    expect(wrapper.text()).toContain('Host · HOST');
    expect(wrapper.text()).toContain('Close room');
  });
});
