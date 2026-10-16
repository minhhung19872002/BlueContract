import { App as AntApp, ConfigProvider } from 'antd';
import viVN from 'antd/locale/vi_VN';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from 'react-router-dom';
import { router } from './router';
import { theme } from './theme';
import { SessionBootstrap } from './auth/guards';
import { isApiProblem } from './api/http';
import './i18n/dayjs';

// antd's vi_VN date-picker locale is overwritten by rc-picker's English placeholders: restore Vietnamese ones.
const locale = {
  ...viVN,
  // Same issue with rc-pagination: "20 / page" instead of "20 / trang".
  Pagination: {
    ...viVN.Pagination,
    items_per_page: '/ trang',
    jump_to: 'Đến trang',
    jump_to_confirm: 'xác nhận',
    page: 'Trang',
    prev_page: 'Trang trước',
    next_page: 'Trang sau',
    prev_5: 'Lùi 5 trang',
    next_5: 'Tới 5 trang',
    prev_3: 'Lùi 3 trang',
    next_3: 'Tới 3 trang',
    page_size: 'Số dòng mỗi trang',
  },
  // Empty tables otherwise render rc-table's English "No data".
  Empty: { description: 'Không có dữ liệu' },
  DatePicker: viVN.DatePicker && {
    ...viVN.DatePicker,
    lang: {
      ...viVN.DatePicker.lang,
      placeholder: 'Chọn ngày',
      yearPlaceholder: 'Chọn năm',
      monthPlaceholder: 'Chọn tháng',
      rangePlaceholder: ['Từ ngày', 'Đến ngày'] as [string, string],
    },
  },
};

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
    <ConfigProvider locale={locale} theme={theme}>
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
