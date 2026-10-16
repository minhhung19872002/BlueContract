import { App, Button, Select, Space, Tabs, Typography } from 'antd';
import { PlusOutlined, ThunderboltOutlined } from '@ant-design/icons';
import { useNavigate } from 'react-router-dom';
import { usePagedList } from '../../api/admin';
import {
  ACCEPTANCES,
  LIQUIDATIONS,
  VALUATIONS,
  acceptanceApi,
  usePaymentAction,
  type AcceptanceListItemDto,
  type LiquidationListItemDto,
  type ValuationListItemDto,
} from '../../api/payments';
import { useAuth } from '../../auth/useAuth';
import { P } from '../../auth/permissions';
import { DataTable } from '../../components/DataTable';
import { StatusTag, statusOptions } from '../../components/StatusTag';
import { PageHeader, useApiError } from '../../components/common';
import { SupplierSelect } from '../../components/inputs';
import { useTableUrlState } from '../../hooks/useUrlState';
import { formatDate, formatDateTime, formatMoney } from '../../utils/format';

const FILTERS = ['tab', 'status', 'supplierId', 'paymentStatus'] as const;

type Tab = 'acceptance' | 'valuation' | 'liquidation';

/** Thanh toán (spec §9.1-11, §9.2-6): Nghiệm thu / Xác định giá trị (08a) / Thanh lý. */
export function PaymentsPage() {
  const { state, update, apiParams } = useTableUrlState(FILTERS);
  const tab = (state.filters.tab as Tab | undefined) ?? 'acceptance';
  const params = {
    ...apiParams,
    tab: undefined,
    paymentStatus: tab === 'valuation' ? state.filters.paymentStatus : undefined,
  };
  const acceptances = usePagedList<AcceptanceListItemDto>(ACCEPTANCES, params);
  const valuations = usePagedList<ValuationListItemDto>(VALUATIONS, params);
  const liquidations = usePagedList<LiquidationListItemDto>(LIQUIDATIONS, params);
  const navigate = useNavigate();
  const { isSupplier, can } = useAuth();
  const base = isSupplier ? '/ncc/thanh-toan' : '/thanh-toan';
  const manage = !isSupplier && can(P.PaymentManage);
  const { message } = App.useApp();
  const onError = useApiError();
  const generate = usePaymentAction(() => acceptanceApi.generate());

  const filters = (
    <>
      <Select
        allowClear
        placeholder="Trạng thái"
        style={{ width: 170 }}
        value={state.filters.status}
        options={statusOptions('paymentRecordStatus')}
        onChange={(v) => update({ filters: { status: v } })}
      />
      {tab === 'valuation' && (
        <Select
          allowClear
          placeholder="Thanh toán"
          style={{ width: 190 }}
          value={state.filters.paymentStatus}
          options={statusOptions('paymentStatus')}
          onChange={(v) => update({ filters: { paymentStatus: v } })}
        />
      )}
      {!isSupplier && (
        <SupplierSelect
          allowClear
          style={{ width: 260 }}
          value={state.filters.supplierId}
          onChange={(v) => update({ filters: { supplierId: v } })}
        />
      )}
    </>
  );
  const supplierColumn = isSupplier ? [] : [{ title: 'Nhà cung cấp', dataIndex: 'supplierName' }];

  return (
    <>
      <PageHeader
        title="Thanh toán"
        subtitle={
          isSupplier
            ? 'Biên bản nghiệm thu, xác định giá trị (08a), ủy nhiệm chi và thanh lý của công ty'
            : 'Nghiệm thu theo kỳ → xác định giá trị khối lượng hoàn thành (08a) + đề nghị thanh toán → thanh lý hợp đồng'
        }
        extra={
          manage && (
            <Space wrap>
              {tab === 'acceptance' && (
                <>
                  <Button
                    icon={<ThunderboltOutlined />}
                    loading={generate.isPending}
                    onClick={() =>
                      generate
                        .mutateAsync()
                        .then(
                          (r) =>
                            message.success(
                              r.created > 0
                                ? `Đã lập ${r.created} biên bản nghiệm thu nháp (${r.periodText}): ${r.numbers.join(', ')}`
                                : 'Không còn phiếu nhập kho đã ký nào chưa nghiệm thu.',
                            ),
                          onError,
                        )
                    }
                  >
                    Lập nghiệm thu tự động
                  </Button>
                  <Button
                    type="primary"
                    icon={<PlusOutlined />}
                    onClick={() => navigate('/thanh-toan/nghiem-thu/tao-moi')}
                  >
                    Lập biên bản nghiệm thu
                  </Button>
                </>
              )}
              {tab === 'valuation' && (
                <Button
                  type="primary"
                  icon={<PlusOutlined />}
                  onClick={() => navigate('/thanh-toan/xac-dinh-gia-tri/tao-moi')}
                >
                  Lập biên bản 08a
                </Button>
              )}
              {tab === 'liquidation' && (
                <Button
                  type="primary"
                  icon={<PlusOutlined />}
                  onClick={() => navigate('/thanh-toan/thanh-ly/tao-moi')}
                >
                  Lập biên bản thanh lý
                </Button>
              )}
            </Space>
          )
        }
      />
      <Tabs
        activeKey={tab}
        onChange={(key) => update({ filters: { tab: key, status: undefined, paymentStatus: undefined } })}
        items={[
          {
            key: 'acceptance',
            label: 'Nghiệm thu',
            children: (
              <DataTable<AcceptanceListItemDto>
                exportAs={{ title: 'Biên bản nghiệm thu', url: ACCEPTANCES, params }}
                data={acceptances.data}
                loading={acceptances.isFetching}
                state={state}
                onChange={update}
                searchPlaceholder="Tìm theo số biên bản, số hợp đồng, kỳ"
                filters={filters}
                columns={[
                  {
                    title: 'Số biên bản',
                    dataIndex: 'number',
                    render: (v: string, r) => (
                      <a onClick={() => navigate(`${base}/nghiem-thu/${r.id}`)}>{v}</a>
                    ),
                  },
                  { title: 'Hợp đồng', dataIndex: 'contractNumber' },
                  ...supplierColumn,
                  { title: 'Kỳ', dataIndex: 'periodText' },
                  { title: 'Số phiếu', dataIndex: 'receiptCount', align: 'center' as const },
                  {
                    title: 'Giá trị',
                    dataIndex: 'totalAmount',
                    align: 'right' as const,
                    render: (v: number) => formatMoney(v),
                  },
                  {
                    title: 'Trạng thái',
                    dataIndex: 'status',
                    render: (v: string, r: AcceptanceListItemDto) => (
                      <Space size={2} wrap>
                        <StatusTag domain="paymentRecordStatus" value={v} />
                        {r.isAutoGenerated && <Typography.Text type="secondary">(tự động)</Typography.Text>}
                      </Space>
                    ),
                  },
                  {
                    title: '08a',
                    dataIndex: 'valuationNumber',
                    render: (v?: string | null) => v ?? '—',
                  },
                  { title: 'Ngày lập', dataIndex: 'createdAt', render: (v: string) => formatDateTime(v) },
                ]}
              />
            ),
          },
          {
            key: 'valuation',
            label: 'Xác định giá trị (08a)',
            children: (
              <DataTable<ValuationListItemDto>
                exportAs={{ title: 'Xác định giá trị và đề nghị thanh toán', url: VALUATIONS, params }}
                data={valuations.data}
                loading={valuations.isFetching}
                state={state}
                onChange={update}
                searchPlaceholder="Tìm theo số biên bản, số đề nghị, số hợp đồng"
                filters={filters}
                columns={[
                  {
                    title: 'Số biên bản',
                    dataIndex: 'number',
                    render: (v: string, r) => (
                      <a onClick={() => navigate(`${base}/xac-dinh-gia-tri/${r.id}`)}>{v}</a>
                    ),
                  },
                  { title: 'Hợp đồng', dataIndex: 'contractNumber' },
                  ...supplierColumn,
                  { title: 'Lần', dataIndex: 'paymentSequence', align: 'center' as const },
                  { title: 'Kỳ', dataIndex: 'periodText' },
                  {
                    title: 'Giá trị kỳ này',
                    dataIndex: 'currentPeriodValue',
                    align: 'right' as const,
                    render: (v: number) => formatMoney(v),
                  },
                  {
                    title: 'Lũy kế (% HĐ)',
                    dataIndex: 'cumulativeToDate',
                    align: 'right' as const,
                    render: (v: number, r: ValuationListItemDto) =>
                      `${formatMoney(v)} (${r.completionPercent}%)`,
                  },
                  {
                    title: 'Đề nghị / đã trả',
                    key: 'paid',
                    align: 'right' as const,
                    render: (_: unknown, r: ValuationListItemDto) => (
                      <Space direction="vertical" size={0} style={{ alignItems: 'flex-end' }}>
                        <span>{formatMoney(r.requestedAmount)}</span>
                        <Typography.Text type="secondary">{formatMoney(r.paidAmount)}</Typography.Text>
                      </Space>
                    ),
                  },
                  {
                    title: 'Trạng thái',
                    dataIndex: 'status',
                    render: (v: string, r: ValuationListItemDto) => (
                      <Space size={2} wrap>
                        <StatusTag domain="paymentRecordStatus" value={v} />
                        {r.status === 'Completed' && (
                          <StatusTag domain="paymentStatus" value={r.paymentStatus} />
                        )}
                      </Space>
                    ),
                  },
                ]}
              />
            ),
          },
          {
            key: 'liquidation',
            label: 'Thanh lý',
            children: (
              <DataTable<LiquidationListItemDto>
                exportAs={{ title: 'Biên bản thanh lý', url: LIQUIDATIONS, params }}
                data={liquidations.data}
                loading={liquidations.isFetching}
                state={state}
                onChange={update}
                searchPlaceholder="Tìm theo số biên bản, số hợp đồng"
                filters={filters}
                columns={[
                  {
                    title: 'Số biên bản',
                    dataIndex: 'number',
                    render: (v: string, r) => <a onClick={() => navigate(`${base}/thanh-ly/${r.id}`)}>{v}</a>,
                  },
                  { title: 'Hợp đồng', dataIndex: 'contractNumber' },
                  ...supplierColumn,
                  {
                    title: 'Giá trị HĐ',
                    dataIndex: 'contractValue',
                    align: 'right' as const,
                    render: (v: number) => formatMoney(v),
                  },
                  {
                    title: 'Thực hiện',
                    dataIndex: 'executedValue',
                    align: 'right' as const,
                    render: (v: number) => formatMoney(v),
                  },
                  {
                    title: 'Đã thanh toán',
                    key: 'paid',
                    align: 'right' as const,
                    render: (_: unknown, r: LiquidationListItemDto) =>
                      formatMoney(r.paidAmount + r.settledAmount),
                  },
                  {
                    title: 'Còn lại',
                    dataIndex: 'remainingAmount',
                    align: 'right' as const,
                    render: (v: number) => formatMoney(v),
                  },
                  {
                    title: 'Trạng thái',
                    dataIndex: 'status',
                    render: (v: string) => <StatusTag domain="paymentRecordStatus" value={v} />,
                  },
                  { title: 'Ngày lập', dataIndex: 'createdAt', render: (v: string) => formatDate(v) },
                ]}
              />
            ),
          },
        ]}
      />
    </>
  );
}
