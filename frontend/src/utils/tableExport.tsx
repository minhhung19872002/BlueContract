import type { TableColumnType, TableProps } from 'antd';
import { isValidElement, type ReactNode } from 'react';
import { MemoryRouter } from 'react-router-dom';

export type ExportFormat = 'text' | 'number' | 'money' | 'date' | 'datetime';
type Cell = string | number | null;

const ISO_DATE = /^\d{4}-\d{2}-\d{2}(T|$)/;

type AnyColumn<T> = TableColumnType<T> & { children?: AnyColumn<T>[] };

/** Leaf columns that show data (no action / selection columns, no untitled columns). */
function leafColumns<T>(columns: TableProps<T>['columns']): AnyColumn<T>[] {
  return (columns ?? []).flatMap((c) => {
    const col = c as AnyColumn<T>;
    if (col.children?.length) return leafColumns(col.children);
    const key = String(col.key ?? '');
    if (key === 'actions' || key === 'action' || key === 'select') return [];
    if (typeof col.title === 'function' || !col.title) return [];
    if (!('dataIndex' in col) && !col.render) return [];
    return [col];
  });
}

function rawValue<T>(row: T, col: AnyColumn<T>): unknown {
  const path = (col as { dataIndex?: string | number | (string | number)[] }).dataIndex;
  if (path === undefined) return undefined;
  const keys = Array.isArray(path) ? path : [path];
  return keys.reduce<unknown>(
    (v, k) => (v && typeof v === 'object' ? (v as Record<string | number, unknown>)[k] : undefined),
    row,
  );
}

/** Visible text of what a cell renders ("Có hiệu lực", "1.234.567 ₫", "17/10/2026"). */
export function textOf(node: ReactNode, render: (el: ReactNode) => string): string {
  if (node === null || node === undefined || typeof node === 'boolean') return '';
  if (typeof node === 'string' || typeof node === 'number') return String(node);
  if (Array.isArray(node)) return node.map((n) => textOf(n as ReactNode, render)).join('');
  if (isValidElement(node)) {
    try {
      const html = render(<MemoryRouter>{node}</MemoryRouter>);
      return (new DOMParser().parseFromString(html, 'text/html').body.textContent ?? '')
        .replace(/\s+/g, ' ')
        .trim();
    } catch {
      return '';
    }
  }
  return '';
}

/**
 * Columns + rows of an antd table → cells as the table shows them. Numbers stay numbers (money when shown with ₫),
 * ISO dates become the displayed dd/MM/yyyy [HH:mm]; anything else is the rendered text.
 */
export function toExportTable<T>(
  columns: TableProps<T>['columns'],
  rows: readonly T[],
  render: (el: ReactNode) => string,
): { columns: { header: string; format: ExportFormat }[]; rows: Cell[][] } {
  const cols = leafColumns(columns);
  const formats: (ExportFormat | null)[] = cols.map(() => null);
  const cells = rows.map((row, r) =>
    cols.map((col, c) => {
      const raw = rawValue(row, col);
      const shown = col.render ? (col.render(raw, row, r) as ReactNode) : (raw as ReactNode);
      const text = textOf(shown, render);
      if (typeof raw === 'number' && Number.isFinite(raw)) {
        formats[c] ??= text.includes('₫') ? 'money' : 'number';
        return raw;
      }
      if (typeof raw === 'string' && ISO_DATE.test(raw) && /^\d{2}\/\d{2}\/\d{4}/.test(text)) {
        formats[c] ??= text.includes(':') ? 'datetime' : 'date';
        return text;
      }
      if (text) formats[c] ??= 'text';
      return text || null;
    }),
  );
  return {
    columns: cols.map((col, c) => ({
      header: textOf(col.title as ReactNode, render),
      format: formats[c] ?? 'text',
    })),
    rows: cells,
  };
}
