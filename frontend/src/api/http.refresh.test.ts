import axios, { AxiosError, AxiosHeaders } from 'axios';
import { authStore, type CurrentUser } from '../auth/authStore';
import { refreshAccessToken } from './http';

const user = {
  id: 'u1',
  userName: 'muasam',
  permissions: [],
  mustChangePassword: false,
} as unknown as CurrentUser;

const failure = (status?: number) =>
  new AxiosError(
    'fail',
    status ? 'ERR_BAD_RESPONSE' : 'ERR_NETWORK',
    undefined,
    undefined,
    status
      ? { status, statusText: '', headers: {}, config: { headers: new AxiosHeaders() }, data: {} }
      : undefined,
  );

describe('refreshAccessToken', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
    authStore.clear();
  });

  it('keeps the session while the API restarts (network error, 502) and resumes when it is back', async () => {
    authStore.setSession('old', user);
    const post = vi
      .spyOn(axios, 'post')
      .mockRejectedValueOnce(failure())
      .mockRejectedValueOnce(failure(502))
      .mockResolvedValueOnce({ data: { accessToken: 'new', user } });

    const pending = refreshAccessToken();
    await vi.runAllTimersAsync();

    expect(await pending).toBe('new');
    expect(post).toHaveBeenCalledTimes(3);
    expect(authStore.getSnapshot().user?.id).toBe('u1');
  });

  it('ends the session at once when the server refuses it', async () => {
    authStore.setSession('old', user);
    const post = vi.spyOn(axios, 'post').mockRejectedValue(failure(401));

    const pending = refreshAccessToken();
    await vi.runAllTimersAsync();

    expect(await pending).toBeNull();
    expect(post).toHaveBeenCalledTimes(1);
    expect(authStore.getSnapshot().user).toBeNull();
  });

  it('gives up after the retries when the API stays down', async () => {
    authStore.setSession('old', user);
    const post = vi.spyOn(axios, 'post').mockRejectedValue(failure(503));

    const pending = refreshAccessToken();
    await vi.runAllTimersAsync();

    expect(await pending).toBeNull();
    expect(post).toHaveBeenCalledTimes(5);
  });
});
