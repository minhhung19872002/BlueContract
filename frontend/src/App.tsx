import { App as AntApp, ConfigProvider } from 'antd';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from 'react-router-dom';
import { router } from './router';
import { darkTheme, lightTheme } from './theme';
import { ThemeModeProvider, useThemeMode } from './hooks/useThemeMode';
import { SessionBootstrap } from './auth/guards';
import { isApiProblem } from './api/http';
import './i18n/dayjs';
import { antdLocale as locale } from './i18n/antdLocale';

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

function ThemedApp() {
  const { mode } = useThemeMode();
  return (
    <ConfigProvider locale={locale} theme={mode === 'dark' ? darkTheme : lightTheme}>
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

export function App() {
  return (
    <ThemeModeProvider>
      <ThemedApp />
    </ThemeModeProvider>
  );
}
