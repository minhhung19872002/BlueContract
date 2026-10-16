import { authStore, type CurrentUser } from './authStore';

const user = {
  id: '1',
  userName: 'admin',
  permissions: ['admin.user'],
  mustChangePassword: false,
} as unknown as CurrentUser;

describe('authStore', () => {
  afterEach(() => authStore.clear());

  it('keeps the access token only in memory and notifies subscribers', () => {
    const listener = vi.fn();
    const unsubscribe = authStore.subscribe(listener);

    authStore.setSession('token-1', user);

    expect(authStore.getAccessToken()).toBe('token-1');
    expect(listener).toHaveBeenCalled();
    expect(window.localStorage.length).toBe(0);
    unsubscribe();
  });

  it('flags a pending password change once', () => {
    authStore.setSession('token', user);
    authStore.requirePasswordChange();
    expect(authStore.getSnapshot().user?.mustChangePassword).toBe(true);
  });

  it('clear() ends the session but stays initialized', () => {
    authStore.setSession('token', user);
    authStore.clear();
    expect(authStore.getSnapshot()).toMatchObject({ accessToken: null, user: null, initialized: true });
  });
});
