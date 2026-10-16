import type { components } from '../api/schema';

export type CurrentUser = components['schemas']['CurrentUserDto'];

interface AuthState {
  accessToken: string | null;
  user: CurrentUser | null;
  /** True once the initial silent refresh has finished (success or not). */
  initialized: boolean;
}

type Listener = () => void;

/**
 * Tiny external store for the session. The access token lives only in memory (never localStorage);
 * the refresh token is an httpOnly cookie the browser sends to /api/auth/* automatically.
 */
function createAuthStore() {
  let state: AuthState = { accessToken: null, user: null, initialized: false };
  const listeners = new Set<Listener>();
  const emit = () => listeners.forEach((l) => l());

  return {
    subscribe(listener: Listener) {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    getSnapshot: () => state,
    getAccessToken: () => state.accessToken,
    setSession(accessToken: string | null, user: CurrentUser | null) {
      state = { accessToken, user: user ?? state.user, initialized: true };
      emit();
    },
    setUser(user: CurrentUser) {
      state = { ...state, user };
      emit();
    },
    requirePasswordChange() {
      if (state.user && !state.user.mustChangePassword) {
        state = { ...state, user: { ...state.user, mustChangePassword: true } };
        emit();
      }
    },
    markInitialized() {
      if (!state.initialized) {
        state = { ...state, initialized: true };
        emit();
      }
    },
    clear() {
      state = { accessToken: null, user: null, initialized: true };
      emit();
    },
  };
}

export const authStore = createAuthStore();
