import {
  Alert,
  App,
  Button,
  Card,
  Checkbox,
  Col,
  DatePicker,
  Descriptions,
  Empty,
  Form,
  Input,
  Row,
  Select,
  Space,
  Table,
  Tabs,
  Typography,
} from 'antd';
import { CheckOutlined, PhoneOutlined, StopOutlined } from '@ant-design/icons';
import { useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import dayjs from '../../i18n/dayjs';
import { useLookup, usePagedList } from '../../api/admin';
import {
  CALL_ORDERS,
  SUMMARIES,
  callOrderApi,
  domainLabel,
  useCallOrder,
  useProposalAction,
  useSummary,
  type CallOrderLineDto,
  type CallOrderListItemDto,
  type SummaryLineDto,
  type SummaryListItemDto,
  useCallOrderTimeline,
} from '../../api/proposals';
import { useAuth } from '../../auth/useAuth';
import { P } from '../../auth/permissions';
import { DataTable } from '../../components/DataTable';
import { StatusTag, statusOptions, type Tone } from '../../components/StatusTag';
import { Timeline } from '../../components/Timeline';
import { ReasonDialog } from '../../components/ReasonDialog';
import { ExportExcelButton, PageHeader, useApiError } from '../../components/common';
import { useFormDraft } from '../../hooks/useFormDraft';
import { DocHeader } from '../../components/layout';
import { QuantityInput, SupplierSelect } from '../../components/inputs';
import { useTableUrlState } from '../../hooks/useUrlState';
import { formatDate, formatDateTime, formatMoney, formatQuantity } from '../../utils/format';
import { DocumentPdf } from './ProposalDetailPage';

const FILTERS = ['status', 'supplierId', 'overdue', 'tab'] as const;

/** Gọi hàng (spec §9.1-9, §9.2-3): tổng hợp chờ gọi, lịch sử các lần gọi và trạng thái xác nhận của NCC. */
export function CallOrdersPage() {
  const { state, update, apiParams } = useTableUrlState(FILTERS);
  const { isSupplier, can } = useAuth();
  const tab = state.filters.tab ?? 'orders';
  const orders = usePagedList<CallOrderListItemDto>(CALL_ORDERS, { ...apiParams, tab: undefined });
  const callable = usePagedList<SummaryListItemDto>(SUMMARIES, { callable: true, pageSize: 100 });
  const navigate = useNavigate();
  const base = isSupplier ? '/ncc/goi-hang' : '/goi-hang';

  const ordersTable = (
    <DataTable<CallOrderListItemDto>
      data={orders.data}
      loading={orders.isFetching}
      state={state}
      onChange={update}
      searchPlaceholder="Tìm theo số đơn, tên hàng"
      filters={
        <>
          <Select
            allowClear
            placeholder="Trạng thái"
            style={{ width: 190 }}
            value={state.filters.status}
            options={statusOptions('callOrderStatus')}
            onChange={(v) => update({ filters: { status: v } })}
          />
          {!isSupplier && (
            <SupplierSelect
              allowClear
              style={{ width: 260 }}
              value={state.filters.supplierId}
              onChange={(v) => update({ filters: { supplierId: v } })}
            />
          )}
          <Checkbox
            checked={state.filters.overdue === 'true'}
            onChange={(e) => update({ filters: { overdue: e.target.checked ? 'true' : undefined } })}
          >
            Quá hạn chưa xác nhận
          </Checkbox>
        </>
      }
      actions={
        <ExportExcelButton
          url={`${CALL_ORDERS}/export`}
          params={{ ...apiParams, tab: undefined }}
          fileName="goi-hang.xlsx"
        />
      }
      columns={[
        {
          title: 'Số đơn',
          dataIndex: 'number',
          render: (v: string, r) => <a onClick={() => navigate(`${base}/${r.id}`)}>{v}</a>,
        },
        ...(isSupplier ? [] : [{ title: 'Nhà cung cấp', dataIndex: 'supplierName' }]),
        { title: 'Phiếu tổng hợp', dataIndex: 'summaryNumber' },
        { title: 'Kho nhận', dataIndex: 'warehouseName' },
        { title: 'Ngày gọi', dataIndex: 'callDate', render: (v: string) => formatDate(v) },
        {
          title: 'Hạn giao',
          dataIndex: 'deliveryDeadline',
          render: (v: string, r: CallOrderListItemDto) => (
            <Typography.Text type={r.isOverdue ? 'danger' : undefined}>{formatDate(v)}</Typography.Text>
          ),
        },
        { title: 'Mặt hàng', dataIndex: 'lineCount', align: 'center' as const },
        {
          title: 'Giá trị',
          dataIndex: 'totalAmount',
          align: 'right' as const,
          render: (v: number) => formatMoney(v),
        },
        {
          title: 'Trạng thái',
          dataIndex: 'status',
          render: (v: string) => <StatusTag domain="callOrderStatus" value={v} />,
        },
      ]}
    />
  );

  return (
    <>
      <PageHeader
        title="Gọi hàng"
        subtitle={
          isSupplier
            ? 'Đơn gọi hàng của Bệnh viện — xác nhận số lượng giao'
            : 'Gọi hàng từ tổng hợp đề xuất đã duyệt'
        }
      />
      {isSupplier || !can(P.CallOrderCreate) ? (
        ordersTable
      ) : (
        <Tabs
          activeKey={tab}
          onChange={(k) => update({ filters: { tab: k === 'orders' ? undefined : k } })}
          items={[
            { key: 'orders', label: 'Các lần gọi hàng', children: ordersTable },
            {
              key: 'callable',
              label: `Tổng hợp chờ gọi (${callable.data?.total ?? 0})`,
              children: (
                <Table<SummaryListItemDto>
                  rowKey="id"
                  size="middle"
                  loading={callable.isFetching}
                  dataSource={callable.data?.items ?? []}
                  pagination={false}
                  columns={[
                    {
                      title: 'Số phiếu',
                      dataIndex: 'number',
                      render: (v: string, r) => (
                        <a onClick={() => navigate(`/tong-hop-de-xuat/${r.id}`)}>{v}</a>
                      ),
                    },
                    {
                      title: 'Mảng',
                      dataIndex: 'domain',
                      render: (v: SummaryListItemDto['domain']) => domainLabel(v),
                    },
                    { title: 'Kỳ', dataIndex: 'periodText' },
                    {
                      title: 'Giá trị duyệt',
                      dataIndex: 'totalAmount',
                      align: 'right',
                      render: (v: number) => formatMoney(v),
                    },
                    {
                      title: 'Còn được gọi',
                      dataIndex: 'remainingToCallAmount',
                      align: 'right',
                      render: (v: number) => formatMoney(v),
                    },
                    {
                      title: '',
                      key: 'call',
                      render: (_, r) => (
                        <Button
                          type="primary"
                          size="small"
                          icon={<PhoneOutlined />}
                          onClick={() => navigate(`/goi-hang/tao-moi?summary=${r.id}`)}
                        >
                          Gọi hàng
                        </Button>
                      ),
                    },
                  ]}
                />
              ),
            },
          ]}
        />
      )}
    </>
  );
}

/** Tạo lần gọi hàng: SL gọi ≤ SL duyệt còn lại; hệ thống tự tách đơn theo NCC. */
export function CallCreatePage() {
  const [params] = useSearchParams();
  const summaryId = params.get('summary') ?? undefined;
  const summary = useSummary(summaryId);
  const warehouses = useLookup('warehouses');
  const [form] = Form.useForm<{
    warehouseId: string;
    deadline: dayjs.Dayjs;
    contactName?: string;
    note?: string;
  }>();
  const [quantities, setQuantities] = useState<Record<string, number>>({});
  const navigate = useNavigate();
  const { message } = App.useApp();
  const onError = useApiError();
  const { user } = useAuth();
  // Lưu nháp tự động (spec §9.3), one draft per approved summary.
  const draft = useFormDraft({
    key: summaryId ? `call-new.${summaryId}` : null,
    form,
    extra: quantities,
    onRestoreExtra: setQuantities,
  });
  const setCalled = (next: Record<string, number>) => {
    setQuantities(next);
    draft.touch();
  };
  const create = useProposalAction(async () => {
    const v = await form.validateFields();
    return callOrderApi.create({
      summaryId: summaryId!,
      warehouseId: v.warehouseId,
      deliveryDeadline: v.deadline.format('YYYY-MM-DD'),
      contactName: v.contactName ?? null,
      note: v.note ?? null,
      lines: Object.entries(quantities)
        .filter(([, q]) => q > 0)
        .map(([summaryLineId, quantity]) => ({ summaryLineId, quantity })),
    });
  });

  if (!summaryId) return <Empty description="Chọn phiếu tổng hợp đã duyệt để gọi hàng" />;
  if (!summary.data) return <Card loading />;
  const s = summary.data;
  const lines = s.lines.filter((l) => l.remainingToCall > 0);
  const over = lines.some((l) => (quantities[l.id] ?? 0) > l.remainingToCall);
  const suppliers = new Set(lines.filter((l) => (quantities[l.id] ?? 0) > 0).map((l) => l.supplierId)).size;

  return (
    <>
      <DocHeader
        back={{ to: -1, label: 'Quay lại' }}
        plain
        code={
          <>
            Gọi hàng — <span className="mono">{s.number}</span>
          </>
        }
        sub={`${domainLabel(s.domain)} · ${s.periodText}`}
        actions={
          <Button
            type="primary"
            icon={<PhoneOutlined />}
            disabled={over || suppliers === 0}
            loading={create.isPending}
            onClick={async () => {
              try {
                const ids = await create.mutateAsync();
                draft.clear();
                message.success(`Đã tạo ${ids.length} đơn gọi hàng và gửi email cho nhà cung cấp`);
                navigate(ids.length === 1 ? `/goi-hang/${ids[0]}` : '/goi-hang');
              } catch (e) {
                onError(e);
              }
            }}
          >
            Gọi hàng ({suppliers} NCC)
          </Button>
        }
      />
      {draft.banner}
      <Card style={{ marginBottom: 16 }}>
        <Form
          form={form}
          layout="vertical"
          initialValues={{ deadline: dayjs().add(5, 'day'), contactName: user?.fullName }}
          onValuesChange={draft.onValuesChange}
        >
          <Row gutter={12}>
            <Col xs={24} md={8}>
              <Form.Item name="warehouseId" label="Kho nhận" rules={[{ required: true }]}>
                <Select options={(warehouses.data ?? []).map((w) => ({ value: w.id, label: w.name }))} />
              </Form.Item>
            </Col>
            <Col xs={12} md={4}>
              <Form.Item name="deadline" label="Hạn giao" rules={[{ required: true }]}>
                <DatePicker
                  format="DD/MM/YYYY"
                  style={{ width: '100%' }}
                  disabledDate={(d) => d.isBefore(dayjs().startOf('day'))}
                />
              </Form.Item>
            </Col>
            <Col xs={12} md={4}>
              <Form.Item name="contactName" label="Người liên hệ">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item name="note" label="Ghi chú cho NCC">
                <Input />
              </Form.Item>
            </Col>
          </Row>
        </Form>
      </Card>
      <Card
        title="Số lượng gọi"
        extra={
          <Button
            size="small"
            onClick={() => setCalled(Object.fromEntries(lines.map((l) => [l.id, l.remainingToCall])))}
          >
            Gọi toàn bộ số còn lại
          </Button>
        }
      >
        {over && (
          <Alert
            type="error"
            showIcon
            style={{ marginBottom: 12 }}
            message="Có mặt hàng gọi vượt số lượng duyệt còn lại."
          />
        )}
        <Table<SummaryLineDto>
          rowKey="id"
          size="small"
          pagination={false}
          dataSource={lines}
          scroll={{ x: 'max-content' }}
          columns={[
            { title: 'Nhà cung cấp', dataIndex: 'supplierName' },
            { title: 'Số HĐ', dataIndex: 'contractNumber' },
            { title: 'Tên hàng', dataIndex: 'itemName' },
            { title: 'ĐVT', dataIndex: 'unit' },
            {
              title: 'SL duyệt',
              dataIndex: 'approvedQuantity',
              align: 'right',
              render: (v: number) => formatQuantity(v),
            },
            {
              title: 'Đã gọi',
              dataIndex: 'calledQuantity',
              align: 'right',
              render: (v: number) => formatQuantity(v),
            },
            {
              title: 'Còn được gọi',
              dataIndex: 'remainingToCall',
              align: 'right',
              render: (v: number) => <b>{formatQuantity(v)}</b>,
            },
            {
              title: 'SL gọi lần này',
              key: 'qty',
              width: 160,
              render: (_, l) => (
                <QuantityInput
                  size="small"
                  max={l.remainingToCall}
                  value={quantities[l.id]}
                  status={(quantities[l.id] ?? 0) > l.remainingToCall ? 'error' : undefined}
                  onChange={(v) => setCalled({ ...quantities, [l.id]: v ?? 0 })}
                />
              ),
            },
            {
              title: 'Thành tiền',
              key: 'amount',
              align: 'right',
              render: (_, l) => formatMoney(Math.round((quantities[l.id] ?? 0) * l.unitPrice)),
            },
          ]}
        />
      </Card>
    </>
  );
}

/** Chi tiết đơn gọi hàng; NCC xác nhận SL (thiếu phải nêu lý do). */
export function CallOrderDetailPage() {
  const { id } = useParams<{ id: string }>();
  const order = useCallOrder(id);
  const o = order.data;
  const { message } = App.useApp();
  const onError = useApiError();
  const [answers, setAnswers] = useState<Record<string, { qty?: number; reason?: string }>>({});
  const [note, setNote] = useState('');
  const confirm = useProposalAction(() =>
    callOrderApi.confirm(id!, {
      note: note || null,
      lines: (o?.lines ?? []).map((l) => ({
        lineId: l.id,
        confirmedQuantity: answers[l.id]?.qty ?? l.quantity,
        shortageReason: answers[l.id]?.reason ?? null,
      })),
    }),
  );
  const cancel = useProposalAction((reason: string) => callOrderApi.cancel(id!, reason));
  const [cancelling, setCancelling] = useState(false);
  const timeline = useCallOrderTimeline(id);
  const { isSupplier } = useAuth();

  if (!o) return <Card loading={order.isLoading} />;
  const missingReason = o.lines.some(
    (l) => (answers[l.id]?.qty ?? l.quantity) < l.quantity && !answers[l.id]?.reason?.trim(),
  );

  return (
    <>
      <DocHeader
        back={{ to: -1, label: 'Quay lại' }}
        plain
        code={
          <>
            Đơn gọi hàng <span className="mono">{o.number}</span>
          </>
        }
        tags={
          <Space>
            <StatusTag domain="callOrderStatus" value={o.status} />
            {o.isOverdue && <Typography.Text type="danger">Quá hạn giao chưa xác nhận</Typography.Text>}
          </Space>
        }
        actions={
          o.canCancel && (
            <Button danger icon={<StopOutlined />} onClick={() => setCancelling(true)}>
              Hủy đơn
            </Button>
          )
        }
      />
      <Card style={{ marginBottom: 16 }}>
        <Descriptions size="small" column={{ xs: 1, md: 3 }}>
          <Descriptions.Item label="Nhà cung cấp">{o.supplierName}</Descriptions.Item>
          <Descriptions.Item label="Kho nhận">{o.warehouseName}</Descriptions.Item>
          <Descriptions.Item label="Phiếu tổng hợp">{o.summaryNumber}</Descriptions.Item>
          <Descriptions.Item label="Ngày gọi">{formatDate(o.callDate)}</Descriptions.Item>
          <Descriptions.Item label="Hạn giao">{formatDate(o.deliveryDeadline)}</Descriptions.Item>
          <Descriptions.Item label="Người liên hệ">{o.contactName ?? '—'}</Descriptions.Item>
          <Descriptions.Item label="Ghi chú">{o.note ?? '—'}</Descriptions.Item>
          <Descriptions.Item label="Xác nhận">
            {o.confirmedAt ? `${o.confirmedByName} — ${formatDateTime(o.confirmedAt)}` : '—'}
          </Descriptions.Item>
          <Descriptions.Item label="Ý kiến NCC">{o.supplierNote ?? '—'}</Descriptions.Item>
        </Descriptions>
      </Card>
      <Card>
        <Tabs
          items={[
            {
              key: 'lines',
              label: `Mặt hàng (${o.lines.length})`,
              children: (
                <>
                  {o.canConfirm && (
                    <Alert
                      type="info"
                      showIcon
                      style={{ marginBottom: 12 }}
                      message="Xác nhận số lượng giao cho từng mặt hàng (mặc định bằng số lượng gọi). Giao thiếu phải nêu lý do."
                    />
                  )}
                  <Table<CallOrderLineDto>
                    rowKey="id"
                    size="small"
                    pagination={false}
                    dataSource={o.lines}
                    scroll={{ x: 'max-content' }}
                    columns={[
                      { title: 'Số HĐ', dataIndex: 'contractNumber' },
                      { title: 'Tên hàng', dataIndex: 'itemName' },
                      { title: 'ĐVT', dataIndex: 'unit' },
                      {
                        title: 'Đơn giá',
                        dataIndex: 'unitPrice',
                        align: 'right',
                        render: (v: number) => formatMoney(v),
                      },
                      {
                        title: 'SL gọi',
                        dataIndex: 'quantity',
                        align: 'right',
                        render: (v: number) => <b>{formatQuantity(v)}</b>,
                      },
                      {
                        title: 'SL xác nhận',
                        key: 'confirmed',
                        width: 150,
                        render: (_, l) =>
                          o.canConfirm ? (
                            <QuantityInput
                              size="small"
                              max={l.quantity}
                              value={answers[l.id]?.qty ?? l.quantity}
                              onChange={(v) =>
                                setAnswers({ ...answers, [l.id]: { ...answers[l.id], qty: v ?? 0 } })
                              }
                            />
                          ) : (
                            formatQuantity(l.confirmedQuantity)
                          ),
                      },
                      {
                        title: 'Lý do giao thiếu',
                        key: 'reason',
                        width: 240,
                        render: (_, l) =>
                          o.canConfirm
                            ? (answers[l.id]?.qty ?? l.quantity) < l.quantity && (
                                <Input
                                  size="small"
                                  status={!answers[l.id]?.reason?.trim() ? 'error' : undefined}
                                  value={answers[l.id]?.reason}
                                  onChange={(e) =>
                                    setAnswers({
                                      ...answers,
                                      [l.id]: { ...answers[l.id], reason: e.target.value },
                                    })
                                  }
                                />
                              )
                            : l.shortageReason,
                      },
                      {
                        title: 'Đã giao',
                        dataIndex: 'deliveredQuantity',
                        align: 'right',
                        render: (v: number) => formatQuantity(v),
                      },
                      {
                        title: 'Thành tiền',
                        dataIndex: 'amount',
                        align: 'right',
                        render: (v: number) => formatMoney(v),
                      },
                    ]}
                  />
                  {o.canConfirm && (
                    <Space direction="vertical" style={{ width: '100%', marginTop: 12 }}>
                      <Input.TextArea
                        rows={2}
                        placeholder="Ý kiến gửi Bệnh viện (không bắt buộc)"
                        value={note}
                        onChange={(e) => setNote(e.target.value)}
                      />
                      <Button
                        type="primary"
                        icon={<CheckOutlined />}
                        disabled={missingReason}
                        loading={confirm.isPending}
                        onClick={() =>
                          confirm
                            .mutateAsync()
                            .then(() => message.success('Đã gửi xác nhận cho Bệnh viện'), onError)
                        }
                      >
                        Gửi xác nhận
                      </Button>
                    </Space>
                  )}
                </>
              ),
            },
            {
              key: 'pdf',
              label: 'Đơn gọi hàng (PDF)',
              children: o.documentId ? <DocumentPdf documentId={o.documentId} /> : <Empty />,
            },
            {
              key: 'timeline',
              label: 'Diễn biến',
              children: (
                <Timeline
                  items={(timeline.data ?? []).map((e, i) => ({
                    key: i,
                    tone: TIMELINE_TONE[e.kind] ?? 'neutral',
                    who: e.actor ?? (e.kind === 'receipt' ? 'Kho' : null),
                    tag: e.status ? <StatusTag domain="receiptStatus" value={e.status} /> : undefined,
                    context: e.link && !isSupplier ? <Link to={e.link}>{e.title}</Link> : e.title,
                    at: formatDateTime(e.at),
                    note: e.note,
                  }))}
                />
              ),
            },
          ]}
        />
      </Card>
      <ReasonDialog
        open={cancelling}
        title={`Hủy đơn gọi hàng ${o.number}?`}
        description="Số lượng đã gọi được trả lại phiếu tổng hợp; nhà cung cấp nhận thông báo kèm lý do."
        okText="Hủy đơn"
        reasonLabel="Lý do hủy"
        loading={cancel.isPending}
        onClose={() => setCancelling(false)}
        onConfirm={async (reason) => {
          try {
            await cancel.mutateAsync(reason);
            message.success('Đã hủy đơn');
          } catch (e) {
            onError(e);
            throw e;
          }
        }}
      />
    </>
  );
}

const TIMELINE_TONE: Record<string, Tone> = {
  created: 'primary',
  sent: 'info',
  confirmed: 'success',
  shortage: 'warning',
  rejected: 'danger',
  receipt: 'teal',
  overdue: 'orange',
  cancelled: 'danger',
};
