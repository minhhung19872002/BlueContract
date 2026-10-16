import { Card, Input, Space, Table, type TableProps } from 'antd';
import type { SorterResult } from 'antd/es/table/interface';
import type { ReactNode } from 'react';
import type { Paged } from '../api/admin';
import type { TableState } from '../hooks/useUrlState';

interface Props<T> {
  data?: Paged<T>;
  loading?: boolean;
  columns: TableProps<T>['columns'];
  state: TableState;
  onChange: (patch: Partial<TableState>, resetPage?: boolean) => void;
  rowKey?: keyof T & string;
  searchPlaceholder?: string;
  /** Extra filter controls shown next to the search box. */
  filters?: ReactNode;
  /** Buttons on the right (create, export...). */
  actions?: ReactNode;
  expandable?: TableProps<T>['expandable'];
}

/** Server-side paged/sorted table with search box; state lives in the URL via useTableUrlState. */
export function DataTable<T extends object>({
  data,
  loading,
  columns,
  state,
  onChange,
  rowKey = 'id' as keyof T & string,
  searchPlaceholder = 'Tìm kiếm...',
  filters,
  actions,
  expandable,
}: Props<T>) {
  const sortedColumns = columns?.map((c) => {
    const key = 'dataIndex' in c ? String(c.dataIndex) : undefined;
    if (!c.sorter || !key) return c;
    return {
      ...c,
      sortOrder: state.sortBy === key ? (state.sortDesc ? 'descend' : 'ascend') : null,
    } as typeof c;
  });

  return (
    <Card styles={{ body: { padding: 16 } }}>
      <Space wrap style={{ width: '100%', justifyContent: 'space-between', marginBottom: 12 }}>
        <Space wrap>
          <Input.Search
            allowClear
            placeholder={searchPlaceholder}
            defaultValue={state.search}
            onSearch={(v) => onChange({ search: v || undefined })}
            style={{ width: 280 }}
          />
          {filters}
        </Space>
        <Space wrap>{actions}</Space>
      </Space>
      <Table<T>
        size="middle"
        rowKey={rowKey}
        loading={loading}
        columns={sortedColumns}
        dataSource={data?.items ?? []}
        expandable={expandable}
        scroll={{ x: 'max-content' }}
        pagination={{
          current: state.page,
          pageSize: state.pageSize,
          total: data?.total ?? 0,
          showSizeChanger: true,
          pageSizeOptions: [10, 20, 50, 100],
          showTotal: (total) => `Tổng ${total.toLocaleString('vi-VN')} bản ghi`,
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
    </Card>
  );
}
