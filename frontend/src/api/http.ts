import axios, { AxiosError } from 'axios';

/** Business error shape returned by the API (RFC 7807 ProblemDetails + stable `code`). */
export interface ApiProblem {
  status: number;
  code: string;
  title: string;
  correlationId?: string;
  errors?: Record<string, string[]>;
}

export const apiClient = axios.create({
  baseURL: '/',
  timeout: 30_000,
  headers: { Accept: 'application/json' },
});

const FALLBACK_MESSAGE = 'Không kết nối được máy chủ. Vui lòng kiểm tra mạng và thử lại.';

/** Normalises any request failure into an ApiProblem with a Vietnamese message; raw technical errors never reach the UI. */
export function toApiProblem(error: unknown): ApiProblem {
  if (error instanceof AxiosError) {
    const data = error.response?.data as (Partial<ApiProblem> & { traceId?: string }) | undefined;
    if (data && typeof data === 'object' && typeof data.title === 'string') {
      return {
        status: error.response?.status ?? 0,
        code: data.code ?? 'UNKNOWN',
        title: data.title,
        correlationId: data.correlationId ?? data.traceId,
        errors: data.errors,
      };
    }
    return { status: error.response?.status ?? 0, code: 'NETWORK_ERROR', title: FALLBACK_MESSAGE };
  }
  return { status: 0, code: 'UNKNOWN', title: FALLBACK_MESSAGE };
}

apiClient.interceptors.response.use(
  (response) => response,
  (error: unknown) => Promise.reject(toApiProblem(error)),
);
