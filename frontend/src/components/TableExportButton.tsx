import { App, Button, Table, type TableProps } from 'antd';
import { DownloadOutlined } from '@ant-design/icons';
import { useState } from 'react';
import { toApiProblem } from '../api/http';
import { exportTable, fetchAllRows } from '../api/tableExport';
import './dataTable.css';

/**
 * "Xuất Excel" of a table exactly as shown (spec §9.3): the rows given, or every page of a server list (`url` +
 * the list's filters). Labels are the displayed ones; numbers, money and dates stay typed in the workbook.
 */
export function TableExportButton<T>({
  title,
  fileName,
  columns,
  rows,
  url,
  params,
  size,
}: {
  title: string;
  fileName?: string;
  columns: TableProps<T>['columns'];
  /** Rows already loaded (detail tables). */
  rows?: readonly T[];
  /** Server-paged list: every page is fetched with these filters. */
  url?: string;
  params?: object;
  size?: 'small' | 'middle';
}) {
  const [busy, setBusy] = useState(false);
  const { message } = App.useApp();
  return (
    <Button
      size={size}
      icon={<DownloadOutlined />}
      loading={busy}
      disabled={!url && !rows?.length}
      onClick={async () => {
        setBusy(true);
        try {
          const data = url ? await fetchAllRows<T>(url, params) : (rows ?? []);
          await exportTable({ title, fileName, columns, rows: data });
        } catch (e) {
          message.error(toApiProblem(e).title);
        } finally {
          setBusy(false);
        }
      }}
    >
      Xuất Excel
    </Button>
  );
}

/** antd Table with a "Xuất Excel" button above it, exporting its own columns and rows (detail tables). */
export function ExportableTable<T extends object>({
  exportTitle,
  exportFileName,
  ...props
}: TableProps<T> & { exportTitle: string; exportFileName?: string }) {
  return (
    <>
      <div className="table-export-bar">
        <TableExportButton<T>
          size="small"
          title={exportTitle}
          fileName={exportFileName}
          columns={props.columns}
          rows={props.dataSource ?? []}
        />
      </div>
      <Table<T> {...props} />
    </>
  );
}
