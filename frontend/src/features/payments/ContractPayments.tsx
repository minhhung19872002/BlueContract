import { Button, Space, Typography } from 'antd';
import { useNavigate } from 'react-router-dom';
import { usePagedList } from '../../api/admin';
import {
  ACCEPTANCES,
  LIQUIDATIONS,
  VALUATIONS,
  type AcceptanceListItemDto,
  type LiquidationListItemDto,
  type ValuationListItemDto,
} from '../../api/payments';
import { StatusTag } from '../../components/StatusTag';
import { formatMoney } from '../../utils/format';
import { ExportableTable } from '../../components/TableExportButton';

/** Tab "Thanh toán" của hợp đồng (spec §9.1-5): nghiệm thu, 08a + UNC, thanh lý. */
export function ContractPayments({
  contractId,
  base,
  canManage,
}: {
  contractId: string;
  base: string;
  canManage: boolean;
}) {
  const params = { contractId, pageSize: 100 };
  const acceptances = usePagedList<AcceptanceListItemDto>(ACCEPTANCES, params);
  const valuations = usePagedList<ValuationListItemDto>(VALUATIONS, params);
  const liquidations = usePagedList<LiquidationListItemDto>(LIQUIDATIONS, params);
  const navigate = useNavigate();
  const link = (path: string) => () => navigate(`${base}/thanh-toan/${path}`);

  return (
    <Space direction="vertical" style={{ width: '100%' }} size="large">
      <div>
        <Typography.Title level={5}>Biên bản nghiệm thu</Typography.Title>
        <ExportableTable<AcceptanceListItemDto>
          exportTitle="Biên bản nghiệm thu của hợp đồng"
          rowKey="id"
          size="small"
          pagination={false}
          loading={acceptances.isFetching}
          dataSource={acceptances.data?.items ?? []}
          onRow={(r) => ({ onClick: link(`nghiem-thu/${r.id}`), style: { cursor: 'pointer' } })}
          columns={[
            { title: 'Số', dataIndex: 'number' },
            { title: 'Kỳ', dataIndex: 'periodText' },
            {
              title: 'Giá trị',
              dataIndex: 'totalAmount',
              align: 'right',
              render: (v: number) => formatMoney(v),
            },
            {
              title: 'Trạng thái',
              dataIndex: 'status',
              render: (v: string) => <StatusTag domain="paymentRecordStatus" value={v} />,
            },
            { title: '08a', dataIndex: 'valuationNumber', render: (v?: string | null) => v ?? '—' },
          ]}
        />
      </div>
      <div>
        <Typography.Title level={5}>Xác định giá trị (08a) & thanh toán</Typography.Title>
        <ExportableTable<ValuationListItemDto>
          exportTitle="Thanh toán của hợp đồng"
          rowKey="id"
          size="small"
          pagination={false}
          loading={valuations.isFetching}
          dataSource={valuations.data?.items ?? []}
          onRow={(r) => ({ onClick: link(`xac-dinh-gia-tri/${r.id}`), style: { cursor: 'pointer' } })}
          columns={[
            { title: 'Số', dataIndex: 'number' },
            { title: 'Lần', dataIndex: 'paymentSequence', align: 'center' },
            {
              title: 'Kỳ này',
              dataIndex: 'currentPeriodValue',
              align: 'right',
              render: (v: number) => formatMoney(v),
            },
            {
              title: 'Lũy kế (% HĐ)',
              dataIndex: 'cumulativeToDate',
              align: 'right',
              render: (v: number, r) => `${formatMoney(v)} (${r.completionPercent}%)`,
            },
            {
              title: 'Đề nghị',
              dataIndex: 'requestedAmount',
              align: 'right',
              render: (v: number) => formatMoney(v),
            },
            {
              title: 'Đã chuyển',
              dataIndex: 'paidAmount',
              align: 'right',
              render: (v: number) => formatMoney(v),
            },
            {
              title: 'Trạng thái',
              key: 'status',
              render: (_, r) => (
                <Space size={2}>
                  <StatusTag domain="paymentRecordStatus" value={r.status} />
                  {r.status === 'Completed' && <StatusTag domain="paymentStatus" value={r.paymentStatus} />}
                </Space>
              ),
            },
          ]}
        />
      </div>
      <div>
        <Space style={{ justifyContent: 'space-between', width: '100%' }}>
          <Typography.Title level={5}>Thanh lý</Typography.Title>
          {canManage && (liquidations.data?.items ?? []).every((l) => l.status === 'Cancelled') && (
            <Button
              size="small"
              onClick={() => navigate(`/thanh-toan/thanh-ly/tao-moi?contract=${contractId}`)}
            >
              Lập biên bản thanh lý
            </Button>
          )}
        </Space>
        <ExportableTable<LiquidationListItemDto>
          exportTitle="Thanh lý của hợp đồng"
          rowKey="id"
          size="small"
          pagination={false}
          loading={liquidations.isFetching}
          dataSource={liquidations.data?.items ?? []}
          onRow={(r) => ({ onClick: link(`thanh-ly/${r.id}`), style: { cursor: 'pointer' } })}
          columns={[
            { title: 'Số', dataIndex: 'number' },
            {
              title: 'Thực hiện',
              dataIndex: 'executedValue',
              align: 'right',
              render: (v: number) => formatMoney(v),
            },
            {
              title: 'Đã thanh toán',
              key: 'paid',
              align: 'right',
              render: (_, r) => formatMoney(r.paidAmount + r.settledAmount),
            },
            {
              title: 'Còn lại',
              dataIndex: 'remainingAmount',
              align: 'right',
              render: (v: number) => formatMoney(v),
            },
            {
              title: 'Trạng thái',
              dataIndex: 'status',
              render: (v: string) => <StatusTag domain="paymentRecordStatus" value={v} />,
            },
          ]}
        />
      </div>
    </Space>
  );
}
