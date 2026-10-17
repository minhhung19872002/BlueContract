import { fireEvent, render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import type { ApiProblem } from '../api/http';
import { DetailState } from './DetailState';

// What the API client rejects with (http.ts turns every Axios error into an ApiProblem).
const problem = (status: number, title: string): ApiProblem => ({ status, code: 'X', title });

function renderState(query: Parameters<typeof DetailState>[0]['query']) {
  return render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter>
        <DetailState query={query} />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DetailState', () => {
  it('shows a skeleton while loading', () => {
    const { container } = renderState({ isError: false, error: null });
    expect(container.querySelector('.ant-skeleton')).not.toBeNull();
  });

  it('says the record was not found (or is not visible to this account) on 404', () => {
    renderState({ isError: true, error: problem(404, 'Không tìm thấy') });
    expect(screen.getByRole('heading', { name: 'Không tìm thấy tài liệu' })).toBeInTheDocument();
    expect(screen.getByText(/không được xem/)).toBeInTheDocument();
  });

  it('says access is denied on 403', () => {
    renderState({ isError: true, error: problem(403, 'Cấm') });
    expect(screen.getByRole('heading', { name: 'Không có quyền truy cập' })).toBeInTheDocument();
  });

  it('shows the server message and retries on other errors', () => {
    const refetch = vi.fn();
    renderState({ isError: true, error: problem(500, 'Lỗi máy chủ, vui lòng thử lại'), refetch });
    expect(screen.getByText('Lỗi máy chủ, vui lòng thử lại')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Thử lại/ }));
    expect(refetch).toHaveBeenCalled();
  });
});

const PAGES = import.meta.glob(['../features/**/*.tsx', '!../**/*.test.tsx'], {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

describe('detail pages', () => {
  it('do not stand in for a missing record with a bare loading card (empty or spinning forever on 404)', () => {
    expect(Object.keys(PAGES).length).toBeGreaterThan(20);
    const offenders = Object.entries(PAGES).flatMap(([file, source]) =>
      [...source.matchAll(/(return|\?)\s*\(?\s*<Card loading(=\{\w+\.isLoading\})?[^>]*\/>/g)].map(
        (m) => `${file}: ${m[0]}`,
      ),
    );
    expect(offenders).toEqual([]);
  });
});
