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
});
