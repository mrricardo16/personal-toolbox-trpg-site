import { describe, expect, it, vi } from 'vitest';
import { ApiClient, ApiRequestError, isTerminalSessionError } from './client';
import { RoomsApi } from './rooms';

describe('RoomsApi', () => {
  it('sends typed create and authorized ready requests', async () => {
    const fetcher = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ roomId: 'room-1' }), { status: 201 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ revision: 2 }), { status: 200 }));
    const api = new RoomsApi(new ApiClient(fetcher));

    await api.create({ nickname: 'Host', maxPlayers: 4 });
    await api.ready('room-1', 'session-token', { isReady: true });

    expect(fetcher.mock.calls[0][0]).toBe('/api/rooms');
    expect(fetcher.mock.calls[0][1]).toMatchObject({ method: 'POST' });
    expect(JSON.parse(fetcher.mock.calls[0][1].body)).toEqual({ nickname: 'Host', maxPlayers: 4 });
    expect(fetcher.mock.calls[1][1].headers).toMatchObject({ Authorization: 'Bearer session-token' });
  });

  it('gets a snapshot with the token in Authorization, never in the URL', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify({ roomId: 'room-1' }), { status: 200 }));
    const api = new RoomsApi(new ApiClient(fetcher));

    await api.getSnapshot('room-1', 'session-token');

    expect(fetcher).toHaveBeenCalledWith('/api/rooms/room-1', expect.objectContaining({
      headers: expect.objectContaining({ Authorization: 'Bearer session-token' }),
    }));
    expect(fetcher.mock.calls[0][0]).not.toContain('session-token');
  });

  it('maps HTTP failures to safe messages without exposing response text', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response('provider secret body', { status: 403 }));
    const api = new ApiClient(fetcher);

    await expect(api.request('/api/rooms/room-1')).rejects.toEqual(
      new ApiRequestError(403, 'Forbidden'),
    );
  });

  it('accepts the connection-test busy response as a sanitized result', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(
      JSON.stringify({ success: false, code: 'TEST_BUSY' }),
      { status: 409 },
    ));
    const api = new RoomsApi(new ApiClient(fetcher));

    await expect(api.testAiConnection('room-1', 'session-token')).resolves.toMatchObject({
      success: false,
      code: 'TEST_BUSY',
    });
  });

  it('only treats identity failures as terminal session errors', () => {
    expect(isTerminalSessionError(new ApiRequestError(404, 'Room not found'))).toBe(true);
    expect(isTerminalSessionError(new ApiRequestError(503, 'Server unavailable'))).toBe(false);
    expect(isTerminalSessionError(new TypeError('network'))).toBe(false);
  });

  it('sends only characterId and checkKey for a game check', async () => {
    const fetcher = vi.fn(async (_input: RequestInfo | URL, _init?: RequestInit) => new Response(
      JSON.stringify({ snapshot: {}, check: {} }),
      { status: 200, headers: { 'Content-Type': 'application/json' } },
    ));
    const api = new RoomsApi(new ApiClient(fetcher));

    await api.resolveCheck('room-1', 'session-token', {
      characterId: 'character-1',
      checkKey: 'spotHidden',
    });

    const body = JSON.parse(String(fetcher.mock.calls[0][1]?.body));
    expect(body).toEqual({ characterId: 'character-1', checkKey: 'spotHidden' });
    expect(body).not.toHaveProperty('roll');
    expect(body).not.toHaveProperty('target');
  });

  it('sends only expectedGameRevision actorCharacterId and projected target for melee attack', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify({ revision: 2 }), { status: 200 }));
    const api = new RoomsApi(new ApiClient(fetcher));

    await api.meleeAttack('room-1', 'session-token', {
      expectedGameRevision: 1,
      actorCharacterId: 'character-guid',
      targetParticipantId: 'opponent:0',
    });

    expect(fetcher).toHaveBeenCalledWith('/api/rooms/room-1/game/combat/melee-attack', expect.objectContaining({
      method: 'POST', headers: expect.objectContaining({ Authorization: 'Bearer session-token' }),
    }));
    const body = JSON.parse(String(fetcher.mock.calls[0][1]?.body));
    expect(body).toEqual({ expectedGameRevision: 1, actorCharacterId: 'character-guid', targetParticipantId: 'opponent:0' });
    expect(body).not.toMatchObject({ playerId: expect.anything(), roll: expect.anything(), targetStats: expect.anything(), policy: expect.anything(), damage: expect.anything(), hp: expect.anything(), nextActor: expect.anything(), round: expect.anything() });
  });

  it('sends only expectedGameRevision exchangeId and projected response for respond', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify({ revision: 2 }), { status: 200 }));
    const api = new RoomsApi(new ApiClient(fetcher));

    await api.respondToCombat('room-1', 'session-token', { expectedGameRevision: 1, exchangeId: 'exchange-1', response: 'dodge' });

    expect(fetcher.mock.calls[0][0]).toBe('/api/rooms/room-1/game/combat/respond');
    expect(JSON.parse(String(fetcher.mock.calls[0][1]?.body))).toEqual({ expectedGameRevision: 1, exchangeId: 'exchange-1', response: 'dodge' });
  });

  it('sends only expectedGameRevision and actorCharacterId for pass', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(JSON.stringify({ revision: 2 }), { status: 200 }));
    const api = new RoomsApi(new ApiClient(fetcher));

    await api.passCombatTurn('room-1', 'session-token', { expectedGameRevision: 1, actorCharacterId: 'character-guid' });

    expect(fetcher.mock.calls[0][0]).toBe('/api/rooms/room-1/game/combat/pass');
    expect(JSON.parse(String(fetcher.mock.calls[0][1]?.body))).toEqual({ expectedGameRevision: 1, actorCharacterId: 'character-guid' });
  });

  it('keeps bearer token out of URL and JSON bodies for all combat intents', async () => {
    const fetcher = vi.fn().mockImplementation(() => new Response(JSON.stringify({ revision: 2 }), { status: 200 }));
    const api = new RoomsApi(new ApiClient(fetcher));

    await api.meleeAttack('room-1', 'session-token', { expectedGameRevision: 1, actorCharacterId: 'character-guid', targetParticipantId: 'opponent:0' });
    await api.respondToCombat('room-1', 'session-token', { expectedGameRevision: 1, exchangeId: 'exchange-1', response: 'fight_back' });
    await api.passCombatTurn('room-1', 'session-token', { expectedGameRevision: 1, actorCharacterId: 'character-guid' });

    for (const [url, init] of fetcher.mock.calls) {
      expect(String(url)).not.toContain('session-token');
      expect(String(init?.body)).not.toContain('session-token');
      expect(init?.headers).toMatchObject({ Authorization: 'Bearer session-token' });
    }
  });

  it('preserves sanitized structured stale code and current revision without response text', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(
      JSON.stringify({ code: 'stale_game_revision', currentGameRevision: 9, detail: 'provider secret body' }),
      { status: 409 },
    ));
    const api = new ApiClient(fetcher);

    let error: unknown;
    try {
      await api.request('/api/rooms/room-1');
    } catch (caught) {
      error = caught;
    }

    expect(error).toMatchObject({ status: 409, safeCode: 'Room unavailable', serverCode: 'stale_game_revision', currentGameRevision: 9 });
    expect(Object.keys(error as ApiRequestError).sort()).toEqual(['currentGameRevision', 'name', 'safeCode', 'serverCode', 'status']);
    expect((error as ApiRequestError).message).toBe('Room unavailable');
    expect(error).not.toHaveProperty('detail');
    expect(error).not.toHaveProperty('rawBody');
    expect(JSON.stringify(error)).not.toContain('provider secret body');
  });

  it('drops unapproved structured error values and malformed current revisions', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(
      JSON.stringify({ code: 'provider_error', currentGameRevision: '9', detail: 'provider secret body' }),
      { status: 409 },
    ));
    const api = new ApiClient(fetcher);

    let error: unknown;
    try {
      await api.request('/api/rooms/room-1');
    } catch (caught) {
      error = caught;
    }

    expect(error).toMatchObject({ status: 409, safeCode: 'Room unavailable' });
    expect(Object.keys(error as ApiRequestError).sort()).toEqual(['name', 'safeCode', 'status']);
    expect(error).not.toHaveProperty('serverCode');
    expect(error).not.toHaveProperty('currentGameRevision');
    expect(JSON.stringify(error)).not.toContain('provider_error');
    expect(JSON.stringify(error)).not.toContain('provider secret body');
  });

  it('keeps the safe stale code but drops an infinite current revision', async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(
      '{"code":"stale_game_revision","currentGameRevision":1e999}',
      { status: 409 },
    ));
    const api = new ApiClient(fetcher);

    let error: unknown;
    try {
      await api.request('/api/rooms/room-1');
    } catch (caught) {
      error = caught;
    }

    expect(error).toMatchObject({ status: 409, safeCode: 'Room unavailable', serverCode: 'stale_game_revision' });
    expect(error).not.toHaveProperty('currentGameRevision');
    expect(Object.keys(error as ApiRequestError).sort()).toEqual(['name', 'safeCode', 'serverCode', 'status']);
  });
});
