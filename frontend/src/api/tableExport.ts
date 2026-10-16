import type { TableProps } from 'antd';
import { apiClient, downloadBlob } from './http';
import { toExportTable } from '../utils/tableExport';

const MAX_ROWS = 10_000;
const PAGE = 200;

/** Every row of a server-paged list (same filters), up to 10.000. */
export async function fetchAllRows<T>(url: string, params: object = {}): Promise<T[]> {
  const rows: T[] = [];
  for (let page = 1; rows.length < MAX_ROWS; page++) {
    const { data } = await apiClient.get<{ items: T[]; total: number }>(url, {
      params: { ...params, page, pageSize: PAGE },
    });
    rows.push(...data.items);
    if (data.items.length < PAGE || rows.length >= data.total) break;
  }
  return rows.slice(0, MAX_ROWS);
}

/** Lays the table out as .xlsx on the server (typed cells) and downloads it. */
export async function exportTable<T>(options: {
  title: string;
  fileName?: string;
  columns: TableProps<T>['columns'];
  rows: readonly T[];
}) {
  const { renderToStaticMarkup } = await import('react-dom/server');
  const table = toExportTable(options.columns, options.rows, renderToStaticMarkup);
  const response = await apiClient.post<Blob>(
    '/api/export/table',
    { title: options.title, fileName: options.fileName ?? options.title, ...table },
    { responseType: 'blob' },
  );
  downloadBlob(response, `${options.fileName ?? 'danh-sach'}.xlsx`);
}
