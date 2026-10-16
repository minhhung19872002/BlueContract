import { App as AntApp, ConfigProvider } from 'antd';
import viVN from 'antd/locale/vi_VN';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from 'react-router-dom';
import { router } from './router';
import { theme } from './theme';
import { SessionBootstrap } from './auth/guards';
import { isApiProblem } from './api/http';
import './i18n/dayjs';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Never retry client errors (403/404/validation) — only transient failures.
      retry: (count, error) =>
        count < 1 && !(isApiProblem(error) && error.status >= 400 && error.status < 500),
      refetchOnWindowFocus: false,
      staleTime: 30_000,
    },
  },
});

export function App() {
  return (
    <ConfigProvider locale={viVN} theme={theme}>
      <AntApp>
        <QueryClientProvider client={queryClient}>
          <SessionBootstrap>
            <RouterProvider router={router} />
          </SessionBootstrap>
        </QueryClientProvider>
      </AntApp>
    </ConfigProvider>
  );
}
