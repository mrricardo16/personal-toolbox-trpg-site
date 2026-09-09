import { describe, expect, it, vi } from 'vitest';
import { mount } from '@vue/test-utils';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import type { GameSnapshot } from '../contracts/rooms';
import HomeView from './HomeView.vue';
import LobbyView from './LobbyView.vue';
import { RoomsApi } from '../api/rooms';
import { ApiRequestError } from '../api/client';

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
          { characterId: 'character-1', ownerPlayerId: 'player-1', name: 'Host Character', checkValues: { spotHidden: 60 }, health: { currentHp: 12, maxHp: 12, majorWound: false, unconscious: false, dying: false, dead: false, stabilized: false } },
          { characterId: 'character-2', ownerPlayerId: 'player-2', name: 'Member Character', checkValues: {}, health: null },
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
            { characterId: 'character-1', ownerPlayerId: 'player-1', name: 'Host Character', checkValues: { spotHidden: 60 }, health: { currentHp: 7, maxHp: 12, majorWound: true, unconscious: false, dying: false, dead: false, stabilized: false } },
            { characterId: 'character-2', ownerPlayerId: 'player-2', name: 'Member Character', checkValues: {}, health: null },
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

  it('renders only server-projected health and never creates a damage control', () => {
    const wrapper = mount(LobbyView, {
      props: {
        currentPlayerId: 'player-1', busy: false, errorMessage: '', connectionStatus: 'connected', api: {} as RoomsApi, token: 'session-token',
        room: { roomId: 'room-1', inviteCode: 'NIGHT-42', hostPlayerId: 'player-1', maxPlayers: 2, status: 'Open', revision: 1, players: [], aiConfiguration: null },
        gameSnapshot: {
          roomId: 'room-1', revision: 2, status: 'Active', createdAt: '2026-08-17T00:00:00Z', lastCheck: null,
          characters: [{ characterId: 'character-1', ownerPlayerId: 'player-1', name: 'Host Character', checkValues: { spotHidden: 60 }, health: { currentHp: 7, maxHp: 12, majorWound: true, unconscious: false, dying: false, dead: false, stabilized: false } }],
        },
      },
    });

    expect(wrapper.get('[data-testid="character-health"]').text()).toContain('HP 7 / 12 · MAJOR WOUND');
    expect(wrapper.findAll('button').map(button => button.text()).join(' ')).not.toMatch(/damage|heal|kill/i);
  });

  it('renders exact read-only stabilized, dying, dead, and unconscious labels from server snapshots', () => {
    const resolveCheck = vi.fn();
    const api = Object.assign({ resolveCheck }, {} as RoomsApi) as RoomsApi;
    const wrapper = mount(LobbyView, {
      props: {
        currentPlayerId: 'player-1', busy: false, errorMessage: '', connectionStatus: 'connected', api, token: 'session-token',
        room: { roomId: 'room-1', inviteCode: 'NIGHT-42', hostPlayerId: 'player-1', maxPlayers: 2, status: 'Open', revision: 1, players: [], aiConfiguration: null },
        gameSnapshot: {
          roomId: 'room-1', revision: 3, status: 'Active', createdAt: '2026-08-17T00:00:00Z', lastCheck: null,
          characters: [
            { characterId: 'stabilized', ownerPlayerId: 'player-1', name: 'Stabilized', checkValues: {}, health: { currentHp: 7, maxHp: 12, majorWound: false, unconscious: false, dying: false, dead: false, stabilized: true } },
            { characterId: 'dying', ownerPlayerId: 'player-1', name: 'Dying', checkValues: {}, health: { currentHp: 0, maxHp: 12, majorWound: true, unconscious: true, dying: true, dead: false, stabilized: false } },
            { characterId: 'dead', ownerPlayerId: 'player-1', name: 'Dead', checkValues: {}, health: { currentHp: 0, maxHp: 12, majorWound: true, unconscious: false, dying: false, dead: true, stabilized: false } },
            { characterId: 'unconscious', ownerPlayerId: 'player-1', name: 'Unconscious', checkValues: {}, health: { currentHp: 7, maxHp: 12, majorWound: true, unconscious: true, dying: false, dead: false, stabilized: false } },
            { characterId: 'other', ownerPlayerId: 'player-2', name: 'Other', checkValues: {}, health: null },
          ],
        },
      },
    });

    const characterItems = wrapper.findAll('.character-list > li');
    expect(characterItems[0].text()).toContain('HP 7 / 12 · STABILIZED');
    expect(characterItems[1].text()).toContain('HP 0 / 12 · MAJOR WOUND · UNCONSCIOUS · DYING');
    expect(characterItems[2].text()).toContain('HP 0 / 12 · MAJOR WOUND · DEAD');
    expect(characterItems[3].text()).toContain('HP 7 / 12 · MAJOR WOUND · UNCONSCIOUS');
    expect(characterItems[4].text()).toContain('Health details are private to the character owner.');
    expect(characterItems[4].text()).not.toMatch(/STABILIZED|DYING|DEAD|UNCONSCIOUS/);

    const forbiddenActions = /First Aid|Dying Round|Stabilize|Heal|Medicine|Damage|Kill/i;
    const buttonText = wrapper.findAll('button').map((button) => button.text()).join(' ');
    expect(buttonText).not.toMatch(forbiddenActions);
    expect(Object.keys(api).join(' ')).not.toMatch(forbiddenActions);
    expect(resolveCheck).not.toHaveBeenCalled();
  });

  it('renders Attack and Pass only from projected actor affordances and targets', () => {
    const api = {} as RoomsApi;
    const wrapper = mount(LobbyView, {
      props: {
        currentPlayerId: 'player-1', busy: false, errorMessage: '', connectionStatus: 'connected', api, token: 'session-token',
        room: { roomId: 'room-1', inviteCode: 'NIGHT-42', hostPlayerId: 'player-1', maxPlayers: 2, status: 'Open', revision: 1, players: [], aiConfiguration: null },
        gameSnapshot: {
          roomId: 'room-1', revision: 4, status: 'Active', createdAt: '2026-08-17T00:00:00Z', characters: [], lastCheck: null,
          combat: {
            active: true, round: 2, currentActorParticipantId: 'character-1',
            participants: [
              { participantId: 'character-1', characterId: 'character-1', label: 'Host', side: 'investigator', active: true, current: true, viewerOwned: true, stats: { dex: 80, fighting: 55, dodge: 45 } },
              { participantId: 'opponent-1', characterId: null, label: 'Cultist', side: 'opponent', active: true, current: false, viewerOwned: false, stats: null },
            ],
            lastExchange: { outcome: 'attacker_hits', winnerParticipantId: 'character-1', dispositionPending: true },
            pending: { role: 'attacker', status: 'awaiting_response' },
            viewerActions: { actorCharacterId: 'character-1', canMeleeAttack: true, canPass: true, eligibleTargetParticipantIds: ['opponent-1'], pendingResponse: null },
          },
        },
      },
    });

    expect(wrapper.text()).toContain('COMBAT ACTIVE');
    expect(wrapper.text()).toContain('ROUND 2');
    expect(wrapper.text()).toContain('CURRENT Host');
    expect(wrapper.text()).toContain('ORDER Host → Cultist');
    expect(wrapper.text()).toContain('LAST attacker_hits · Host · PENDING');
    expect(wrapper.text()).toContain('WAITING attacker · awaiting_response');
    expect(wrapper.get('[data-testid="combat-target"]').text()).toContain('Cultist');
    expect(wrapper.get('[data-testid="combat-attack"]').text()).toBe('Attack');
    expect(wrapper.get('[data-testid="combat-pass"]').text()).toBe('Pass');
    expect(wrapper.findAll('[data-testid^="combat-response-"]')).toHaveLength(0);
  });

  it('renders Dodge and Fight Back only from projected pending response values', () => {
    const wrapper = mount(LobbyView, {
      props: {
        currentPlayerId: 'player-1', busy: false, errorMessage: '', connectionStatus: 'connected', api: {} as RoomsApi, token: 'session-token',
        room: { roomId: 'room-1', inviteCode: 'NIGHT-42', hostPlayerId: 'player-1', maxPlayers: 2, status: 'Open', revision: 1, players: [], aiConfiguration: null },
        gameSnapshot: {
          roomId: 'room-1', revision: 4, status: 'Active', createdAt: '2026-08-17T00:00:00Z', characters: [], lastCheck: null,
          combat: { active: true, round: 2, currentActorParticipantId: 'opponent-1', participants: [], lastExchange: null, pending: null, viewerActions: { actorCharacterId: null, canMeleeAttack: false, canPass: false, eligibleTargetParticipantIds: [], pendingResponse: { exchangeId: 'exchange-1', availableResponses: ['dodge', 'fight_back'] } } },
        },
      },
    });

    expect(wrapper.get('[data-testid="combat-response-dodge"]').text()).toBe('Dodge');
    expect(wrapper.get('[data-testid="combat-response-fight_back"]').text()).toBe('Fight Back');
    expect(wrapper.find('[data-testid="combat-attack"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="combat-pass"]').exists()).toBe(false);
  });

  it('never renders another viewer response choices or unprojected targets', () => {
    const wrapper = mount(LobbyView, {
      props: {
        currentPlayerId: 'player-1', busy: false, errorMessage: '', connectionStatus: 'connected', api: {} as RoomsApi, token: 'session-token',
        room: { roomId: 'room-1', inviteCode: 'NIGHT-42', hostPlayerId: 'player-1', maxPlayers: 2, status: 'Open', revision: 1, players: [], aiConfiguration: null },
        gameSnapshot: {
          roomId: 'room-1', revision: 4, status: 'Active', createdAt: '2026-08-17T00:00:00Z', characters: [], lastCheck: null,
          combat: { active: true, round: 2, currentActorParticipantId: 'opponent-1', participants: [{ participantId: 'opponent-1', characterId: null, label: 'Cultist', side: 'opponent', active: true, current: true, viewerOwned: false, stats: null }], lastExchange: null, pending: null, viewerActions: { actorCharacterId: null, canMeleeAttack: false, canPass: false, eligibleTargetParticipantIds: [], pendingResponse: null } },
        },
      },
    });

    expect(wrapper.find('[data-testid="combat-target"]').exists()).toBe(false);
    expect(wrapper.findAll('[data-testid^="combat-response-"]')).toHaveLength(0);
  });

  it('submits one intent then waits for authoritative snapshot without optimistic combat changes', async () => {
    let resolveAttack: (snapshot: GameSnapshot) => void = () => undefined;
    const meleeAttack = vi.fn().mockImplementation(() => new Promise<GameSnapshot>((resolve) => { resolveAttack = resolve; }));
    const api = Object.assign({ meleeAttack }, {} as RoomsApi) as RoomsApi;
    const gameSnapshot: GameSnapshot = {
      roomId: 'room-1', revision: 4, status: 'Active', createdAt: '2026-08-17T00:00:00Z', characters: [], lastCheck: null,
      combat: { active: true, round: 2, currentActorParticipantId: 'character-1', participants: [{ participantId: 'opponent-1', characterId: null, label: 'Cultist', side: 'opponent', active: true, current: false, viewerOwned: false, stats: null }], lastExchange: null, pending: null, viewerActions: { actorCharacterId: 'character-guid', canMeleeAttack: true, canPass: false, eligibleTargetParticipantIds: ['opponent-1'], pendingResponse: null } },
    };
    const wrapper = mount(LobbyView, { props: { currentPlayerId: 'player-1', gameSnapshot, busy: false, errorMessage: '', connectionStatus: 'connected', api, token: 'session-token', room: { roomId: 'room-1', inviteCode: 'NIGHT-42', hostPlayerId: 'player-1', maxPlayers: 2, status: 'Open', revision: 1, players: [], aiConfiguration: null } } });

    await wrapper.get('[data-testid="combat-target"]').setValue('opponent-1');
    await wrapper.get('[data-testid="combat-attack"]').trigger('click');
    expect(meleeAttack).toHaveBeenCalledWith('room-1', 'session-token', { expectedGameRevision: 4, actorCharacterId: 'character-guid', targetParticipantId: 'opponent-1' });
    expect(wrapper.emitted('gameSnapshot')).toBeUndefined();
    resolveAttack({ ...gameSnapshot, revision: 5 });
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(wrapper.emitted('gameSnapshot')?.[0]).toEqual([{ ...gameSnapshot, revision: 5 }]);
  });

  it('refreshes on stale conflict and never automatically replays the intent', async () => {
    const meleeAttack = vi.fn().mockRejectedValue(new ApiRequestError(409, 'Room unavailable', 'stale_game_revision', 5));
    const getGame = vi.fn().mockResolvedValue({ roomId: 'room-1', revision: 5, status: 'Active', createdAt: '2026-08-17T00:00:00Z', characters: [], lastCheck: null });
    const api = Object.assign({ meleeAttack, getGame }, {} as RoomsApi) as RoomsApi;
    const wrapper = mount(LobbyView, { props: { currentPlayerId: 'player-1', gameSnapshot: { roomId: 'room-1', revision: 4, status: 'Active', createdAt: '2026-08-17T00:00:00Z', characters: [], lastCheck: null, combat: { active: true, round: 1, currentActorParticipantId: 'character-guid', participants: [], lastExchange: null, pending: null, viewerActions: { actorCharacterId: 'character-guid', canMeleeAttack: true, canPass: false, eligibleTargetParticipantIds: ['opponent-1'], pendingResponse: null } } }, busy: false, errorMessage: '', connectionStatus: 'connected', api, token: 'session-token', room: { roomId: 'room-1', inviteCode: 'NIGHT-42', hostPlayerId: 'player-1', maxPlayers: 2, status: 'Open', revision: 1, players: [], aiConfiguration: null } } });

    await wrapper.get('[data-testid="combat-target"]').setValue('opponent-1');
    await wrapper.get('[data-testid="combat-attack"]').trigger('click');
    await new Promise((resolve) => setTimeout(resolve, 0));
    expect(getGame).toHaveBeenCalledTimes(1);
    expect(meleeAttack).toHaveBeenCalledTimes(1);
    expect(wrapper.emitted('gameSnapshot')).toEqual([[{ roomId: 'room-1', revision: 5, status: 'Active', createdAt: '2026-08-17T00:00:00Z', characters: [], lastCheck: null }]]);
    expect(wrapper.text()).toContain('Combat changed; choose again');
  });

  it('does not submit a locally selected target after a newer snapshot removes it from projected eligibility', async () => {
    const meleeAttack = vi.fn().mockResolvedValue({ roomId: 'room-1', revision: 6, status: 'Active', createdAt: '2026-08-17T00:00:00Z', characters: [], lastCheck: null });
    const api = Object.assign({ meleeAttack }, {} as RoomsApi) as RoomsApi;
    const initial: GameSnapshot = {
      roomId: 'room-1', revision: 4, status: 'Active', createdAt: '2026-08-17T00:00:00Z', characters: [], lastCheck: null,
      combat: { active: true, round: 1, currentActorParticipantId: 'character-guid', participants: [], lastExchange: null, pending: null, viewerActions: { actorCharacterId: 'character-guid', canMeleeAttack: true, canPass: false, eligibleTargetParticipantIds: ['opponent-1'], pendingResponse: null } },
    };
    const wrapper = mount(LobbyView, { props: { currentPlayerId: 'player-1', gameSnapshot: initial, busy: false, errorMessage: '', connectionStatus: 'connected', api, token: 'session-token', room: { roomId: 'room-1', inviteCode: 'NIGHT-42', hostPlayerId: 'player-1', maxPlayers: 2, status: 'Open', revision: 1, players: [], aiConfiguration: null } } });

    await wrapper.get('[data-testid="combat-target"]').setValue('opponent-1');
    const updated: GameSnapshot = {
      ...initial,
      revision: 5,
      combat: {
        ...initial.combat!,
        viewerActions: { ...initial.combat!.viewerActions!, eligibleTargetParticipantIds: [] },
      },
    };
    await wrapper.setProps({ gameSnapshot: updated });
    expect(wrapper.get('[data-testid="combat-attack"]').attributes('disabled')).toBeDefined();
    await wrapper.get('[data-testid="combat-attack"]').trigger('click');
    expect(meleeAttack).not.toHaveBeenCalled();
  });

  it('contains no Start End Damage or NPC actor controls', () => {
    const source = readFileSync(resolve(process.cwd(), 'src/components/LobbyView.vue'), 'utf8');
    expect(source).not.toMatch(/combat-(?:start|end|damage|npc)|Start Combat|End Combat|Apply Damage|NPC/i);
  });

  it('contains no client combat legality dice opposed damage turn or round calculation', () => {
    const source = readFileSync(resolve(process.cwd(), 'src/components/LobbyView.vue'), 'utf8');
    expect(source).not.toMatch(/(?:calculate|rollDice|parseDice|opposed|combatLegality|nextActor|advanceRound)/i);
  });

  it('renders only safe combat damage facts without damage authority controls', () => {
    const api = {} as RoomsApi;
    const wrapper = mount(LobbyView, {
      props: {
        currentPlayerId: 'player-1', busy: false, errorMessage: '', connectionStatus: 'connected', api, token: 'session-token',
        room: { roomId: 'room-1', inviteCode: 'NIGHT-42', hostPlayerId: 'player-1', maxPlayers: 2, status: 'Open', revision: 1, players: [], aiConfiguration: null },
        gameSnapshot: {
          roomId: 'room-1', revision: 5, status: 'Active', createdAt: '2026-09-07T00:00:00Z', characters: [
            { characterId: 'character-1', ownerPlayerId: 'player-1', name: 'Host Character', checkValues: {}, health: { currentHp: 7, maxHp: 12, majorWound: true, unconscious: false, dying: false, dead: false, stabilized: false } },
          ], lastCheck: null,
          combat: {
            active: true, round: 3, currentActorParticipantId: 'opponent-1',
            participants: [
              { participantId: 'character-1', characterId: 'character-1', label: 'Host Character', side: 'investigator', active: true, current: false, viewerOwned: true, stats: { dex: 80, fighting: 55, dodge: 45 } },
              { participantId: 'opponent-1', characterId: null, label: 'Cultist', side: 'opponent', active: false, current: true, viewerOwned: false, stats: null },
            ],
            lastExchange: { outcome: 'attacker_hits', winnerParticipantId: 'character-1', dispositionPending: false },
            pending: null,
            lastDamage: { exchangeId: 'exchange-1', ownerParticipantId: 'character-1', targetParticipantId: 'opponent-1', outcome: 'damage_applied', netDamage: 4, targetDefeated: true },
          },
        },
      },
    });

    expect(wrapper.get('[data-testid="last-damage"]').text()).toContain('Host Character → Cultist · damage_applied · NET 4 · DEFEATED');
    expect(wrapper.get('[data-testid="character-health"]').text()).toContain('HP 7 / 12');
    expect(wrapper.get('[data-testid="combat-status"]').text()).toContain('CURRENT Cultist');
    expect(wrapper.get('[data-testid="combat-status"]').text()).toContain('ORDER Host Character → Cultist');

    const forbidden = /Roll Damage|Apply Damage|Damage Input|\bWeapon\b|\bArmor\b|\bAttack\b|\bDodge\b|Fight Back|\bPass\b|Start Combat|End Combat/i;
    expect(wrapper.findAll('button, input, textarea').map((element) => element.text()).join(' ')).not.toMatch(forbidden);
    expect(wrapper.text()).not.toMatch(forbidden);
    expect(Object.keys(api).join(' ')).not.toMatch(forbidden);
    expect(readFileSync(resolve(process.cwd(), 'src/components/LobbyView.vue'), 'utf8')).not.toMatch(/\b(?:rollDice|parseDice|calculateDamage|applyDamage)\b/i);
  });
});
