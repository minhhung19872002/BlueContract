import { Button, Input, Segmented, Skeleton, Table, type TableProps } from 'antd';
import type { SorterResult } from 'antd/es/table/interface';
import {
  ChevronDown,
  ChevronUp,
  Inbox,
  RotateCcw,
  Rows3,
  Rows4,
  Search,
  SlidersHorizontal,
  TriangleAlert,
  X,
} from 'lucide-react';
import { useEffect, useState, type Key, type ReactNode } from 'react';
import type { Paged } from '../api/admin';
import type { TableState } from '../hooks/useUrlState';
import './dataTable.css';

export interface TableTab {
  key: string;
  label: string;
  count?: number;
}

export interface ActiveFilter {
  key: string;
  label: string;
  onRemove: () => void;
}

type Density = 'comfortable' | 'compact';
const DENSITY_KEY = 'bp-density';

function savedDensity(): Density {
  try {
    return localStorage.getItem(DENSITY_KEY) === 'compact' ? 'compact' : 'comfortable';
  } catch {
    return 'comfortable';
  }
}

interface Props<T> {
  data?: Paged<T>;
  loading?: boolean;
  /** The query failed: shows an error state with a retry button instead of an empty table. */
  error?: boolean;
  onRetry?: () => void;
  columns: TableProps<T>['columns'];
  state: TableState;
  onChange: (patch: Partial<TableState>, resetPage?: boolean) => void;
  rowKey?: keyof T & string;
  searchPlaceholder?: string;
  /** Filter controls shown next to the search box. */
  filters?: ReactNode;
  /** Less used filters, folded under "Bộ lọc nâng cao". */
  advancedFilters?: ReactNode;
  /** Chips "Đang lọc: …" with a remove button each, and "Xóa lọc". */
  activeFilters?: ActiveFilter[];
  onClearFilters?: () => void;
  /** Buttons on the right of the filter bar (export...). */
  actions?: ReactNode;
  /** Tabs with counters above the table. */
  tabs?: TableTab[];
  activeTab?: string;
  onTabChange?: (key: string) => void;
  /** Row selection: renders the bulk-action bar "Đã chọn N bản ghi" while rows are selected. */
  bulkActions?: (selectedKeys: Key[], clear: () => void) => ReactNode;
  expandable?: TableProps<T>['expandable'];
  onRowClick?: (row: T) => void;
  emptyTitle?: string;
  emptyDescription?: string;
  /** First column stays visible while scrolling horizontally (default on). */
  fixFirstColumn?: boolean;
}

/**
 * Server-side paged/sorted list (design/Data Table + Page List): tabs, filter bar with search, chips, bulk actions,
 * density switch, sticky header, first column fixed. State lives in the URL via useTableUrlState.
 */
export function DataTable<T extends object>({
  data,
  loading,
  error,
  onRetry,
  columns,
  state,
  onChange,
  rowKey = 'id' as keyof T & string,
  searchPlaceholder = 'Tìm kiếm…',
  filters,
  advancedFilters,
  activeFilters,
  onClearFilters,
  actions,
  tabs,
  activeTab,
  onTabChange,
  bulkActions,
  expandable,
  onRowClick,
  emptyTitle = 'Chưa có dữ liệu',
  emptyDescription = 'Dữ liệu sẽ hiển thị tại đây khi có bản ghi.',
  fixFirstColumn = true,
}: Props<T>) {
  const [density, setDensity] = useState<Density>(savedDensity);
  const [advancedOpen, setAdvancedOpen] = useState(false);
  const [selected, setSelected] = useState<Key[]>([]);
  const [search, setSearch] = useState(state.search ?? '');

  // Keep the box in sync when the URL changes (back button, "Xóa lọc").
  const urlSearch = state.search ?? '';
  const [lastUrlSearch, setLastUrlSearch] = useState(urlSearch);
  if (urlSearch !== lastUrlSearch) {
    setLastUrlSearch(urlSearch);
    setSearch(urlSearch);
  }

  useEffect(() => {
    try {
      localStorage.setItem(DENSITY_KEY, density);
    } catch {
      // Not remembered; the choice still applies on this page.
    }
  }, [density]);

  const sortedColumns = columns?.map((c, i) => {
    const key = 'dataIndex' in c ? String(c.dataIndex) : undefined;
    const withSort =
      c.sorter && key
        ? { ...c, sortOrder: state.sortBy === key ? (state.sortDesc ? 'descend' : 'ascend') : null }
        : c;
    return (
      i === 0 && fixFirstColumn && withSort.fixed === undefined ? { ...withSort, fixed: 'left' } : withSort
    ) as typeof c;
  });

  const applySearch = (value: string) => {
    if ((value || undefined) !== (state.search || undefined)) onChange({ search: value || undefined });
  };

  const firstLoad = loading && !data;
  const clear = () => setSelected([]);
  const chips = activeFilters ?? [];

  return (
    <div className="data-table-page">
      {tabs && tabs.length > 0 && (
        <div role="tablist" className="data-table__tabs">
          {tabs.map((t) => {
            const on = t.key === activeTab;
            return (
              <button
                key={t.key}
                type="button"
                role="tab"
                aria-selected={on}
                className={`data-table__tab${on ? ' data-table__tab--active' : ''}`}
                onClick={() => {
                  clear();
                  onTabChange?.(t.key);
                }}
              >
                {t.label}
                {t.count !== undefined && (
                  <span className="data-table__tab-count">{t.count.toLocaleString('vi-VN')}</span>
                )}
              </button>
            );
          })}
        </div>
      )}
      <section className="data-table-card">
        <div className="data-table__filters">
          <div className="data-table__filter-row">
            <Input
              allowClear
              className="data-table__search"
              prefix={<Search size={16} strokeWidth={1.75} aria-hidden />}
              placeholder={searchPlaceholder}
              aria-label="Tìm kiếm"
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                if (!e.target.value) applySearch('');
              }}
              onPressEnter={() => applySearch(search.trim())}
              onBlur={() => applySearch(search.trim())}
            />
            {filters}
            {advancedFilters && (
              <button
                type="button"
                className="data-table__advanced-toggle"
                onClick={() => setAdvancedOpen((o) => !o)}
                aria-expanded={advancedOpen}
              >
                <SlidersHorizontal size={15} strokeWidth={1.75} aria-hidden />
                Bộ lọc nâng cao
                {advancedOpen ? <ChevronUp size={14} aria-hidden /> : <ChevronDown size={14} aria-hidden />}
              </button>
            )}
            <div className="data-table__spacer" />
            {actions}
            <Segmented
              size="small"
              aria-label="Mật độ"
              value={density}
              onChange={(v) => setDensity(v as Density)}
              options={[
                {
                  value: 'comfortable',
                  label: 'Thoải mái',
                  icon: <Rows3 size={14} strokeWidth={1.75} aria-hidden />,
                },
                { value: 'compact', label: 'Gọn', icon: <Rows4 size={14} strokeWidth={1.75} aria-hidden /> },
              ]}
            />
          </div>
          {advancedFilters && advancedOpen && <div className="data-table__advanced">{advancedFilters}</div>}
          {chips.length > 0 && (
            <div className="data-table__chips">
              <span className="data-table__chips-label">Đang lọc:</span>
              {chips.map((c) => (
                <span key={c.key} className="data-table__chip">
                  {c.label}
                  <button type="button" aria-label={`Bỏ lọc ${c.label}`} onClick={c.onRemove}>
                    <X size={12} aria-hidden />
                  </button>
                </span>
              ))}
              {onClearFilters && (
                <button type="button" className="data-table__clear" onClick={onClearFilters}>
                  <RotateCcw size={13} aria-hidden />
                  Xóa lọc
                </button>
              )}
            </div>
          )}
        </div>
        {bulkActions && selected.length > 0 && (
          <div className="data-table__bulk">
            <span className="data-table__bulk-count">Đã chọn {selected.length} bản ghi</span>
            {bulkActions(selected, clear)}
            <Button size="small" type="text" onClick={clear}>
              Bỏ chọn
            </Button>
          </div>
        )}
        {error ? (
          <div className="data-table__state" role="alert">
            <span className="data-table__state-icon data-table__state-icon--danger">
              <TriangleAlert size={24} strokeWidth={1.75} aria-hidden />
            </span>
            <div className="data-table__state-title">Không tải được dữ liệu</div>
            <div className="data-table__state-desc">
              Kết nối tới máy chủ bị gián đoạn hoặc máy chủ đang bận.
            </div>
            {onRetry && <Button onClick={onRetry}>Thử lại</Button>}
          </div>
        ) : firstLoad ? (
          <div className="data-table__skeleton" aria-busy>
            {Array.from({ length: 6 }, (_, i) => (
              <Skeleton key={i} active title={false} paragraph={{ rows: 1, width: '100%' }} />
            ))}
          </div>
        ) : (
          <Table<T>
            className="data-table"
            size={density === 'compact' ? 'small' : 'middle'}
            rowKey={rowKey}
            loading={loading}
            columns={sortedColumns}
            dataSource={data?.items ?? []}
            expandable={expandable}
            scroll={{ x: 'max-content' }}
            sticky={{ offsetHeader: 56 }}
            rowSelection={
              bulkActions
                ? { selectedRowKeys: selected, onChange: setSelected, columnWidth: 44, fixed: true }
                : undefined
            }
            onRow={
              onRowClick
                ? (row) => ({ onClick: () => onRowClick(row), style: { cursor: 'pointer' } })
                : undefined
            }
            locale={{
              emptyText: (
                <div className="data-table__state">
                  <span className="data-table__state-icon">
                    <Inbox size={24} strokeWidth={1.75} aria-hidden />
                  </span>
                  <div className="data-table__state-title">
                    {chips.length || state.search ? 'Không có kết quả phù hợp' : emptyTitle}
                  </div>
                  <div className="data-table__state-desc">
                    {chips.length || state.search
                      ? 'Thử bỏ bớt điều kiện lọc hoặc đổi từ khóa tìm kiếm.'
                      : emptyDescription}
                  </div>
                </div>
              ),
            }}
            pagination={{
              current: state.page,
              pageSize: state.pageSize,
              total: data?.total ?? 0,
              showSizeChanger: true,
              pageSizeOptions: [10, 20, 50, 100],
              showTotal: (total) => (
                <span className="data-table__total">
                  Tổng <strong>{total.toLocaleString('vi-VN')}</strong> bản ghi
                </span>
              ),
            }}
            onChange={(pagination, _filters, sorter) => {
              const s = (Array.isArray(sorter) ? sorter[0] : sorter) as SorterResult<T> | undefined;
              const sortBy = s?.order ? String(s.field ?? s.columnKey) : undefined;
              onChange(
                {
                  page: pagination.current ?? 1,
                  pageSize: pagination.pageSize ?? state.pageSize,
                  sortBy,
                  sortDesc: s?.order === 'descend',
                },
                false,
              );
            }}
          />
        )}
      </section>
    </div>
  );
}
