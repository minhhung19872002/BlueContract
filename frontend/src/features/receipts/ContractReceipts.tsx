import { Typography } from 'antd';
import { useNavigate } from 'react-router-dom';
import { usePagedList } from '../../api/admin';
import { RECEIPTS, type ReceiptListItemDto } from '../../api/receipts';
import { StatusTag } from '../../components/StatusTag';
import { formatDate, formatMoney } from '../../utils/format';
import { ExportableTable } from '../../components/TableExportButton';

/** Tab "Nhập kho" của hợp đồng (spec §9.1-5): các phiếu nhập/hoàn trả của hợp đồng và trạng thái ký. */
export function ContractReceipts({ contractId, base }: { contractId: string; base: string }) {
  const list = usePagedList<ReceiptListItemDto>(RECEIPTS, { contractId, pageSize: 200 });
  const navigate = useNavigate();
  const rows = list.data?.items ?? [];
  const net = rows
    .filter((r) => r.status !== 'Cancelled' && r.validationStatus !== 'Invalid')
    .reduce((s, r) => s + (r.type === 'Return' ? -r.totalAmount : r.totalAmount), 0);
  return (
    <ExportableTable<ReceiptListItemDto>
      exportTitle="Phiếu nhập kho của hợp đồng"
      rowKey="id"
      size="small"
      loading={list.isFetching}
      dataSource={rows}
      pagination={false}
      scroll={{ x: 'max-content' }}
      footer={() => (
        <Typography.Text strong>Giá trị nhập kho (trừ hoàn trả): {formatMoney(net)}</Typography.Text>
      )}
      columns={[
        {
          title: 'Số phiếu',
          dataIndex: 'number',
          render: (v: string, r) => <a onClick={() => navigate(`${base}/nhap-kho/${r.id}`)}>{v}</a>,
        },
        {
          title: 'Loại',
          dataIndex: 'type',
          render: (v: string) => <StatusTag domain="receiptType" value={v} />,
        },
        { title: 'Ngày', dataIndex: 'receiptDate', render: (v: string) => formatDate(v) },
        { title: 'Kho', dataIndex: 'warehouseName' },
        { title: 'Hóa đơn', dataIndex: 'invoiceNumber', render: (v?: string | null) => v ?? '—' },
        { title: 'Giá trị', dataIndex: 'totalAmount', align: 'right', render: (v: number) => formatMoney(v) },
        {
          title: 'Kiểm tra',
          dataIndex: 'validationStatus',
          render: (v: string) => <StatusTag domain="receiptValidation" value={v} />,
        },
        {
          title: 'Trạng thái ký',
          dataIndex: 'status',
          render: (v: string) => <StatusTag domain="receiptStatus" value={v} />,
        },
      ]}
    />
  );
}
