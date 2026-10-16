import {
  App,
  Button,
  Checkbox,
  DatePicker,
  Form,
  Modal,
  Radio,
  Select,
  Space,
  Switch,
  Tooltip,
  Typography,
} from 'antd';
import { ApiOutlined, PlusOutlined } from '@ant-design/icons';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import dayjs from '../../i18n/dayjs';
import { useLookup, usePagedList } from '../../api/admin';
import type { ContractListItemDto } from '../../api/procurement';
import {
  RECEIPTS,
  receiptApi,
  useDevHisStatus,
  useReceiptAction,
  type ReceiptListItemDto,
  type SimulateReceiptRequest,
} from '../../api/receipts';
import { useAuth } from '../../auth/useAuth';
import { P } from '../../auth/permissions';
import { DataTable } from '../../components/DataTable';
import { StatusTag, statusOptions } from '../../components/StatusTag';
import { ExportExcelButton, PageHeader, useApiError } from '../../components/common';
import { SupplierSelect } from '../../components/inputs';
import { useTableUrlState } from '../../hooks/useUrlState';
import { formatDate, formatMoney } from '../../utils/format';

const FILTERS = [
  'status',
  'validation',
  'type',
  'source',
  'supplierId',
  'warehouseId',
  'from',
  'to',
  'hasInvoice',
] as const;

/** Phiếu nhập kho (spec §9.1-10; cổng NCC §9.2-5): lọc trạng thái ký, cảnh báo, NCC, kho, ngày. */
export function ReceiptsPage() {
  const { state, update, apiParams } = useTableUrlState(FILTERS);
  const list = usePagedList<ReceiptListItemDto>(RECEIPTS, apiParams);
  const warehouses = useLookup('warehouses');
  const navigate = useNavigate();
  const { isSupplier, can } = useAuth();
  const base = isSupplier ? '/ncc/nhap-kho' : '/nhap-kho';
  const devHis = useDevHisStatus();
  const [simulating, setSimulating] = useState(false);

  return (
    <>
      <PageHeader
        title="Phiếu nhập kho"
        subtitle={
          isSupplier
            ? 'Phiếu nhập kho / hoàn trả của công ty — ký với vai trò người giao hàng, bổ sung hóa đơn điện tử'
            : 'Phiếu từ HIS và lập trên cổng — kiểm tra tự động theo hợp đồng, luồng 5 chữ ký'
        }
        extra={
          !isSupplier && (
            <Space wrap>
              {devHis.data?.enabled && (can(P.ReceiptManage) || can(P.IntegrationManage)) && (
                <Tooltip title="Môi trường demo/kiểm thử: tạo phiếu như HIS gửi sang">
                  <Button icon={<ApiOutlined />} onClick={() => setSimulating(true)}>
                    Giả lập HIS gửi phiếu
                  </Button>
                </Tooltip>
              )}
              {can(P.ReceiptManage) && (
                <Button type="primary" icon={<PlusOutlined />} onClick={() => navigate('/nhap-kho/tao-moi')}>
                  Lập phiếu trên cổng
                </Button>
              )}
            </Space>
          )
        }
      />
      <DataTable<ReceiptListItemDto>
        data={list.data}
        loading={list.isFetching}
        state={state}
        onChange={update}
        searchPlaceholder="Tìm theo số phiếu, số HĐ, số hóa đơn, tên hàng"
        filters={
          <>
            <Select
              allowClear
              placeholder="Trạng thái ký"
              style={{ width: 160 }}
              value={state.filters.status}
              options={statusOptions('receiptStatus')}
              onChange={(v) => update({ filters: { status: v } })}
            />
            <Select
              allowClear
              placeholder="Kiểm tra"
              style={{ width: 150 }}
              value={state.filters.validation}
              options={statusOptions('receiptValidation')}
              onChange={(v) => update({ filters: { validation: v } })}
            />
            <Select
              allowClear
              placeholder="Loại"
              style={{ width: 130 }}
              value={state.filters.type}
              options={statusOptions('receiptType')}
              onChange={(v) => update({ filters: { type: v } })}
            />
            {!isSupplier && (
              <>
                <Select
                  allowClear
                  placeholder="Nguồn"
                  style={{ width: 170 }}
                  value={state.filters.source}
                  options={statusOptions('receiptSource')}
                  onChange={(v) => update({ filters: { source: v } })}
                />
                <SupplierSelect
                  allowClear
                  style={{ width: 240 }}
                  value={state.filters.supplierId}
                  onChange={(v) => update({ filters: { supplierId: v } })}
                />
              </>
            )}
            <Select
              allowClear
              placeholder="Kho"
              style={{ width: 190 }}
              value={state.filters.warehouseId}
              options={(warehouses.data ?? []).map((w) => ({ value: w.id, label: w.name }))}
              onChange={(v) => update({ filters: { warehouseId: v } })}
            />
            <DatePicker.RangePicker
              format="DD/MM/YYYY"
              placeholder={['Từ ngày', 'Đến ngày']}
              value={
                state.filters.from && state.filters.to
                  ? [dayjs(state.filters.from), dayjs(state.filters.to)]
                  : undefined
              }
              onChange={(r) =>
                update({
                  filters: {
                    from: r?.[0]?.format('YYYY-MM-DD'),
                    to: r?.[1]?.format('YYYY-MM-DD'),
                  },
                })
              }
            />
            <Checkbox
              checked={state.filters.hasInvoice === 'false'}
              onChange={(e) => update({ filters: { hasInvoice: e.target.checked ? 'false' : undefined } })}
            >
              Chưa có hóa đơn
            </Checkbox>
          </>
        }
        actions={
          <ExportExcelButton url={`${RECEIPTS}/export`} params={apiParams} fileName="phieu-nhap-kho.xlsx" />
        }
        columns={[
          {
            title: 'Số phiếu',
            dataIndex: 'number',
            render: (v: string, r) => <a onClick={() => navigate(`${base}/${r.id}`)}>{v}</a>,
          },
          {
            title: 'Loại / nguồn',
            key: 'kind',
            render: (_, r) => (
              <Space size={2} wrap>
                <StatusTag domain="receiptType" value={r.type} />
                <StatusTag domain="receiptSource" value={r.source} />
              </Space>
            ),
          },
          { title: 'Ngày', dataIndex: 'receiptDate', render: (v: string) => formatDate(v) },
          { title: 'Kho', dataIndex: 'warehouseName' },
          ...(isSupplier ? [] : [{ title: 'Nhà cung cấp', dataIndex: 'supplierName' }]),
          { title: 'Số HĐ', dataIndex: 'contractNumber', render: (v?: string | null) => v ?? '—' },
          {
            title: 'Hóa đơn',
            dataIndex: 'invoiceNumber',
            render: (v?: string | null) => v ?? <Typography.Text type="warning">Chưa có</Typography.Text>,
          },
          {
            title: 'Giá trị',
            dataIndex: 'totalAmount',
            align: 'right' as const,
            render: (v: number, r: ReceiptListItemDto) => formatMoney(r.type === 'Return' ? -v : v),
          },
          {
            title: 'Kiểm tra',
            dataIndex: 'validationStatus',
            render: (v: string, r: ReceiptListItemDto) => (
              <Space size={2}>
                <StatusTag domain="receiptValidation" value={v} />
                {r.issueCount > 0 && <Typography.Text type="secondary">({r.issueCount})</Typography.Text>}
              </Space>
            ),
          },
          {
            title: 'Trạng thái ký',
            dataIndex: 'status',
            render: (v: string, r: ReceiptListItemDto) => (
              <Space size={2} wrap>
                <StatusTag domain="receiptStatus" value={v} />
                {r.isIncludedInAcceptance && <Typography.Text type="success">Đã nghiệm thu</Typography.Text>}
              </Space>
            ),
          },
        ]}
      />
      <SimulateDialog
        open={simulating}
        onClose={() => setSimulating(false)}
        onOpenReceipt={(id) => navigate(`/nhap-kho/${id}`)}
      />
    </>
  );
}

/** "Giả lập HIS" (POST /api/dev/his/simulate-receipt): push ngay hoặc lập phiếu chờ job kéo về. */
function SimulateDialog({
  open,
  onClose,
  onOpenReceipt,
}: {
  open: boolean;
  onClose: () => void;
  onOpenReceipt: (id: string) => void;
}) {
  const [form] = Form.useForm<SimulateReceiptRequest>();
  const contracts = usePagedList<ContractListItemDto>('/api/contracts', {
    status: 'Effective',
    pageSize: 100,
  });
  const { message } = App.useApp();
  const onError = useApiError();
  const simulate = useReceiptAction((body: SimulateReceiptRequest) => receiptApi.simulate(body));

  return (
    <Modal
      title="Giả lập HIS gửi phiếu nhập kho"
      open={open}
      onCancel={onClose}
      okText="Gửi phiếu"
      okButtonProps={{ loading: simulate.isPending }}
      destroyOnHidden
      onOk={async () => {
        const v = await form.validateFields();
        try {
          const result = await simulate.mutateAsync({ ...v, contractId: v.contractId ?? null });
          onClose();
          if (result.queued) {
            message.success(
              `HIS đã lập phiếu ${result.payload.receiptNumber} — bấm "Kéo phiếu từ HIS" ở màn hình Tích hợp HIS hoặc chờ job định kỳ.`,
            );
          } else if (result.result) {
            message.success(`Đã nhận phiếu ${result.result.receiptNumber} từ HIS`);
            onOpenReceipt(result.result.receiptId);
          }
        } catch (e) {
          onError(e);
        }
      }}
    >
      <Form
        form={form}
        layout="vertical"
        initialValues={{ type: 'Receipt', scenario: 'Valid', mode: 'Push', withInvoice: true, maxLines: 3 }}
      >
        <Form.Item name="contractId" label="Hợp đồng (bỏ trống = tự chọn hợp đồng có đơn gọi hàng)">
          <Select
            allowClear
            showSearch
            optionFilterProp="label"
            options={(contracts.data?.items ?? []).map((c) => ({
              value: c.id,
              label: `${c.number} — ${c.supplierName}`,
            }))}
          />
        </Form.Item>
        <Form.Item name="type" label="Loại phiếu">
          <Radio.Group options={statusOptions('receiptType')} />
        </Form.Item>
        <Form.Item name="scenario" label="Kịch bản">
          <Radio.Group
            options={[
              { value: 'Valid', label: 'Hợp lệ' },
              { value: 'Warning', label: 'Có cảnh báo' },
              { value: 'Invalid', label: 'Không hợp lệ' },
            ]}
          />
        </Form.Item>
        <Form.Item name="mode" label="Cách gửi">
          <Radio.Group
            options={[
              { value: 'Push', label: 'HIS đẩy qua API (ngay)' },
              { value: 'Pull', label: 'HIS lập phiếu, cổng kéo về' },
            ]}
          />
        </Form.Item>
        <Space size="large">
          <Form.Item name="withInvoice" label="Kèm hóa đơn" valuePropName="checked">
            <Switch />
          </Form.Item>
          <Form.Item name="maxLines" label="Số mặt hàng tối đa">
            <Select style={{ width: 100 }} options={[1, 2, 3, 5].map((n) => ({ value: n, label: n }))} />
          </Form.Item>
        </Space>
      </Form>
    </Modal>
  );
}
