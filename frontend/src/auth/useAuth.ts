import { useCallback, useMemo, useSyncExternalStore } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { authStore } from './authStore';
import { logoutRequest } from '../api/auth';

export function useAuth() {
  const state = useSyncExternalStore(authStore.subscribe, authStore.getSnapshot);
  const queryClient = useQueryClient();

  const logout = useCallback(async () => {
    try {
      await logoutRequest();
    } finally {
      authStore.clear();
      queryClient.clear();
    }
  }, [queryClient]);

  const permissions = useMemo(() => state.user?.permissions ?? [], [state.user]);
  const can = useCallback(
    (...required: string[]) => required.some((p) => permissions.includes(p)),
    [permissions],
  );

  return {
    user: state.user,
    initialized: state.initialized,
    isAuthenticated: !!state.accessToken && !!state.user,
    isSupplier: state.user?.userType === 'Supplier',
    can,
    logout,
  };
}
