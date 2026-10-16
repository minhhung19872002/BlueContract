import { useCallback, useMemo } from 'react';
import { useSearchParams } from 'react-router-dom';

export interface TableState {
  page: number;
  pageSize: number;
  search?: string;
  sortBy?: string;
  sortDesc?: boolean;
  filters: Record<string, string | undefined>;
}

/** Table paging/sort/filter state persisted in the URL so filters survive reload and can be shared (spec §9.3). */
export function useTableUrlState(filterKeys: readonly string[] = [], defaultPageSize = 20) {
  const [params, setParams] = useSearchParams();

  const state = useMemo<TableState>(() => {
    const filters: Record<string, string | undefined> = {};
    filterKeys.forEach((k) => (filters[k] = params.get(k) ?? undefined));
    return {
      page: Number(params.get('page') ?? 1) || 1,
      pageSize: Number(params.get('pageSize') ?? defaultPageSize) || defaultPageSize,
      search: params.get('search') ?? undefined,
      sortBy: params.get('sortBy') ?? undefined,
      sortDesc: params.get('sortDesc') === 'true',
      filters,
    };
  }, [params, filterKeys, defaultPageSize]);

  const update = useCallback(
    (
      patch: Partial<Omit<TableState, 'filters'>> & { filters?: Record<string, string | undefined> },
      resetPage = true,
    ) => {
      setParams(
        (prev) => {
          const next = new URLSearchParams(prev);
          const set = (k: string, v: string | number | boolean | undefined | null) =>
            v === undefined || v === null || v === '' || v === false
              ? next.delete(k)
              : next.set(k, String(v));
          if ('page' in patch) set('page', patch.page === 1 ? undefined : patch.page);
          if ('pageSize' in patch)
            set('pageSize', patch.pageSize === defaultPageSize ? undefined : patch.pageSize);
          if ('search' in patch) set('search', patch.search);
          if ('sortBy' in patch) set('sortBy', patch.sortBy);
          if ('sortDesc' in patch) set('sortDesc', patch.sortDesc);
          Object.entries(patch.filters ?? {}).forEach(([k, v]) => set(k, v));
          if (resetPage && !('page' in patch)) next.delete('page');
          return next;
        },
        { replace: true },
      );
    },
    [setParams, defaultPageSize],
  );

  /** Flat params for the API call. */
  const apiParams = useMemo(
    () => ({
      page: state.page,
      pageSize: state.pageSize,
      search: state.search,
      sortBy: state.sortBy,
      sortDesc: state.sortDesc || undefined,
      ...state.filters,
    }),
    [state],
  );

  return { state, update, apiParams };
}
