export type FetchLike = typeof fetch;

export class ApiRequestError extends Error {
  constructor(
    public readonly status: number,
    public readonly safeCode: string,
  ) {
    super(safeCode);
    this.name = 'ApiRequestError';
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

  async request<T>(path: string, init: RequestInit = {}): Promise<T> {
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

    if (!response.ok) {
      throw new ApiRequestError(response.status, statusToSafeCode(response.status));
    }

    if (response.status === 204) {
      return undefined as T;
    }

    return await response.json() as T;
  }
}
