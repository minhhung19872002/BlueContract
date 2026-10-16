import { Spin } from 'antd';
import { useEffect, type ReactNode } from 'react';
import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { refreshAccessToken } from '../api/http';
import { authStore } from './authStore';
import { useAuth } from './useAuth';
import { ForbiddenPage } from '../pages/ErrorPages';

/** On first load, try a silent refresh with the httpOnly cookie to restore the session. */
export function SessionBootstrap({ children }: { children: ReactNode }) {
  const { initialized } = useAuth();
  useEffect(() => {
    if (!authStore.getSnapshot().initialized) {
      void refreshAccessToken().finally(() => authStore.markInitialized());
    }
  }, []);

  if (!initialized) {
    return (
      <div style={{ display: 'grid', placeItems: 'center', height: '100vh' }}>
        <Spin size="large" />
      </div>
    );
  }
  return <>{children}</>;
}

export function RequireAuth() {
  const { isAuthenticated, user } = useAuth();
  const location = useLocation();
  if (!isAuthenticated) {
    return (
      <Navigate
        to={`/dang-nhap?returnUrl=${encodeURIComponent(location.pathname + location.search)}`}
        replace
      />
    );
  }
  if (user?.mustChangePassword && location.pathname !== '/doi-mat-khau') {
    return <Navigate to="/doi-mat-khau" replace />;
  }
  return <Outlet />;
}

export function RequirePermission({ any, children }: { any: string[]; children: ReactNode }) {
  const { can } = useAuth();
  if (!can(...any)) {
    return <ForbiddenPage />;
  }
  return <>{children}</>;
}

/** Sends internal users and supplier users to their own home. */
export function HomeRedirect() {
  const { isSupplier } = useAuth();
  return <Navigate to={isSupplier ? '/ncc' : '/tong-quan'} replace />;
}

/** Keeps internal users out of the supplier portal and vice versa. */
export function PortalGuard({ portal, children }: { portal: 'internal' | 'supplier'; children: ReactNode }) {
  const { isSupplier } = useAuth();
  if (portal === 'internal' && isSupplier) return <Navigate to="/ncc" replace />;
  if (portal === 'supplier' && !isSupplier) return <Navigate to="/tong-quan" replace />;
  return <>{children}</>;
}
