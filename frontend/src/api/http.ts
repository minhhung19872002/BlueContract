import axios, {
  AxiosError,
  type AxiosRequestConfig,
  type AxiosResponse,
  type InternalAxiosRequestConfig,
} from 'axios';
import { authStore } from '../auth/authStore';
import type { components } from './schema';

/** Business error shape returned by the API (RFC 7807 ProblemDetails + stable `code`). */
export interface ApiProblem {
  status: number;
  code: string;
  title: string;
  correlationId?: string;
  errors?: Record<string, string[]>;
}

type LoginResult = components['schemas']['LoginResult'];

export const apiClient = axios.create({
  baseURL: '/',
  timeout: 30_000,
  withCredentials: true,
  headers: { Accept: 'application/json' },
});

const FALLBACK_MESSAGE = 'Không kết nối được máy chủ. Vui lòng kiểm tra mạng và thử lại.';

/** Normalises any request failure into an ApiProblem with a Vietnamese message; raw technical errors never reach the UI. */
export function toApiProblem(error: unknown): ApiProblem {
  if (isApiProblem(error)) return error;
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

export function isApiProblem(value: unknown): value is ApiProblem {
  return (
    typeof value === 'object' && value !== null && 'code' in value && 'title' in value && 'status' in value
  );
}

// ---- Access token handling ----

apiClient.interceptors.request.use((config) => {
  const token = authStore.getAccessToken();
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

let refreshing: Promise<string | null> | null = null;

/** Waits between refresh attempts while the API is unreachable (restart / deploy): 0.5 + 1 + 2 + 4 s. */
export const REFRESH_RETRY_DELAYS_MS = [500, 1000, 2000, 4000];

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/** The server answered and refused the session (expired, revoked…), as opposed to being unreachable. */
const isRefusal = (e: unknown) =>
  e instanceof AxiosError && e.response !== undefined && e.response.status >= 400 && e.response.status < 500;

/**
 * Single-flight refresh: concurrent 401s share one /refresh call (the refresh cookie rotates on every use). While the
 * API restarts (network error / 502 / 503) the call is retried instead of signing the user out; only a refusal from the
 * server ends the session.
 */
export function refreshAccessToken(): Promise<string | null> {
  refreshing ??= (async () => {
    for (let attempt = 0; ; attempt++) {
      try {
        const r = await axios.post<LoginResult>('/api/auth/refresh', null, { withCredentials: true });
        authStore.setSession(r.data.accessToken ?? null, r.data.user ?? null);
        return r.data.accessToken ?? null;
      } catch (e) {
        const delay = REFRESH_RETRY_DELAYS_MS[attempt];
        if (isRefusal(e) || delay === undefined) {
          authStore.clear();
          return null;
        }
        await sleep(delay);
      }
    }
  })().finally(() => {
    refreshing = null;
  });
  return refreshing;
}

type RetriableConfig = InternalAxiosRequestConfig & { _retried?: boolean };

apiClient.interceptors.response.use(
  (response) => response,
  async (error: unknown) => {
    if (error instanceof AxiosError && error.response?.status === 401) {
      const config = error.config as RetriableConfig | undefined;
      const isAuthCall = config?.url?.startsWith('/api/auth/') ?? false;
      if (config && !config._retried && !isAuthCall) {
        config._retried = true;
        const token = await refreshAccessToken();
        if (token) {
          config.headers.Authorization = `Bearer ${token}`;
          return apiClient(config);
        }
      }
    }
    if (error instanceof AxiosError && error.response?.status === 403) {
      const problem = toApiProblem(error);
      if (problem.code === 'PASSWORD_CHANGE_REQUIRED') authStore.requirePasswordChange();
    }
    return Promise.reject(toApiProblem(error));
  },
);

/** Downloads a binary response (Excel/PDF/ZIP) and saves it with the server-provided or fallback name. */
export async function downloadFile(
  url: string,
  fallbackName: string,
  config?: AxiosRequestConfig,
): Promise<void> {
  downloadBlob(await apiClient.get<Blob>(url, { ...config, responseType: 'blob' }), fallbackName);
}

/** Saves a blob response under the name of its Content-Disposition header (or the fallback). */
export function downloadBlob(response: AxiosResponse<Blob>, fallbackName: string) {
  const disposition = String(response.headers['content-disposition'] ?? '');
  const match = /filename\*=UTF-8''([^;]+)|filename="?([^";]+)"?/i.exec(disposition);
  const name = match ? decodeURIComponent(match[1] ?? match[2] ?? fallbackName) : fallbackName;
  const href = URL.createObjectURL(response.data);
  const link = document.createElement('a');
  link.href = href;
  link.download = name;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(href);
}
