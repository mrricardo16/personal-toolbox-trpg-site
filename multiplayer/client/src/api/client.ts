export type FetchLike = typeof fetch;

export class ApiRequestError extends Error {
  constructor(
    public readonly status: number,
    public readonly safeCode: string,
    serverCode?: string,
    currentGameRevision?: number,
  ) {
    super(safeCode);
    this.name = 'ApiRequestError';
    if (serverCode === 'stale_game_revision') {
      this.serverCode = serverCode;
    }
    if (typeof currentGameRevision === 'number' && Number.isFinite(currentGameRevision)) {
      this.currentGameRevision = currentGameRevision;
    }
  }

  declare public readonly serverCode?: 'stale_game_revision';
  declare public readonly currentGameRevision?: number;
}

async function readStructuredError(response: Response): Promise<Pick<ApiRequestError, 'serverCode' | 'currentGameRevision'>> {
  try {
    const body: unknown = await response.json();
    if (typeof body !== 'object' || body === null) return {};
    const record = body as Record<string, unknown>;
    return {
      ...(record.code === 'stale_game_revision' ? { serverCode: record.code } : {}),
      ...(typeof record.currentGameRevision === 'number' && Number.isFinite(record.currentGameRevision) ? { currentGameRevision: record.currentGameRevision } : {}),
    };
  } catch {
    return {};
  }
}

export function safeApiMessage(error: unknown): string {
  if (error instanceof ApiRequestError) {
    return error.safeCode;
  }

  if (error instanceof TypeError) {
    return 'Network error';
  }

  return 'Request failed';
}

export function isTerminalSessionError(error: unknown): boolean {
  return error instanceof ApiRequestError && [401, 403, 404].includes(error.status);
}

function statusToSafeCode(status: number): string {
  switch (status) {
    case 400:
      return 'Invalid request';
    case 401:
      return 'Unauthorized';
    case 403:
      return 'Forbidden';
    case 404:
      return 'Room not found';
    case 409:
      return 'Room unavailable';
    default:
      return status >= 500 ? 'Server unavailable' : 'Request failed';
  }
}

export class ApiClient {
  constructor(private readonly fetcher: FetchLike = fetch.bind(globalThis)) {}

  async request<T>(path: string, init: RequestInit = {}, acceptedStatuses: readonly number[] = []): Promise<T> {
    let response: Response;
    try {
      response = await this.fetcher(path, {
        ...init,
        headers: {
          Accept: 'application/json',
          ...init.headers,
        },
      });
    } catch {
      throw new ApiRequestError(0, 'Network error');
    }

    if (!response.ok && !acceptedStatuses.includes(response.status)) {
      const structured = await readStructuredError(response);
      throw new ApiRequestError(response.status, statusToSafeCode(response.status), structured.serverCode, structured.currentGameRevision);
    }

    if (response.status === 204) {
      return undefined as T;
    }

    return await response.json() as T;
  }
}
