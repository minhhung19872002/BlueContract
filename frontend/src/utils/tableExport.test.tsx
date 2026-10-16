import type { TableProps } from 'antd';
import { renderToStaticMarkup } from 'react-dom/server';
import { Link } from 'react-router-dom';
import { StatusTag } from '../components/StatusTag';
import { formatDate, formatDateTime, formatMoney } from './format';
import { toExportTable } from './tableExport';

interface Row {
  id: string;
  number: string;
  status: string;
  total: number;
  quantity: number;
  signedDate: string;
  sentAt: string;
  supplier: { name: string };
}

const columns: TableProps<Row>['columns'] = [
  {
    title: 'Số HĐ',
    dataIndex: 'number',
    render: (v: string, r) => <Link to={`/hop-dong/${r.id}`}>{v}</Link>,
  },
  {
    title: 'Trạng thái',
    dataIndex: 'status',
    render: (v: string) => <StatusTag domain="contractStatus" value={v} />,
  },
  { title: 'Giá trị', dataIndex: 'total', render: (v: number) => formatMoney(v) },
  { title: 'Số lượng', dataIndex: 'quantity' },
  { title: 'Ngày ký', dataIndex: 'signedDate', render: (v: string) => formatDate(v) },
  { title: 'Gửi lúc', dataIndex: 'sentAt', render: (v: string) => formatDateTime(v) },
  { title: 'NCC', dataIndex: ['supplier', 'name'] },
  { title: '', key: 'actions', render: () => <button type="button">Sửa</button> },
];

const rows: Row[] = [
  {
    id: '1',
    number: '01/2026/HĐ',
    status: 'Effective',
    total: 1234567,
    quantity: 12.5,
    signedDate: '2026-10-16',
    sentAt: '2026-10-16T18:30:00Z',
    supplier: { name: 'Công ty Minh An' },
  },
];

describe('toExportTable', () => {
  it('exports what the table shows, with typed numbers and dates', () => {
    const table = toExportTable(columns, rows, renderToStaticMarkup);
    expect(table.columns.map((c) => c.header)).toEqual([
      'Số HĐ',
      'Trạng thái',
      'Giá trị',
      'Số lượng',
      'Ngày ký',
      'Gửi lúc',
      'NCC',
    ]);
    expect(table.columns.map((c) => c.format)).toEqual([
      'text',
      'text',
      'money',
      'number',
      'date',
      'datetime',
      'text',
    ]);
    const [cells] = table.rows;
    expect(cells![0]).toBe('01/2026/HĐ');
    expect(cells![1]).toBe('Đang hiệu lực');
    expect(cells![2]).toBe(1234567);
    expect(cells![3]).toBe(12.5);
    expect(cells![4]).toBe('16/10/2026');
    expect(cells![5]).toBe('17/10/2026 01:30');
    expect(cells![6]).toBe('Công ty Minh An');
  });
});
