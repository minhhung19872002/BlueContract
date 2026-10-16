import {
  Alert,
  App,
  Button,
  Card,
  Col,
  DatePicker,
  Descriptions,
  Empty,
  Form,
  Input,
  Popconfirm,
  Row,
  Select,
  Space,
  Table,
  Tabs,
  Tag,
  Typography,
} from 'antd';
import { EditOutlined, SaveOutlined, SendOutlined, StopOutlined } from '@ant-design/icons';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import dayjs from '../../i18n/dayjs';
import { usePagedList } from '../../api/admin';
import type { ContractListItemDto } from '../../api/procurement';
import {
  acceptanceApi,
  useAcceptance,
  useAcceptanceCandidates,
  usePaymentAction,
  type AcceptanceDto,
  type AcceptanceItemDto,
  type AcceptanceReceiptDto,
} from '../../api/payments';
import { useDocumentWorkflow } from '../../api/signing';
import { useAuth } from '../../auth/useAuth';
import { StatusTag } from '../../components/StatusTag';
import { useApiError } from '../../components/common';
import { useFormDraft } from '../../hooks/useFormDraft';
import { DocHeader, StickyActionBar } from '../../components/layout';
import { formatDate, formatDateTime, formatMoney, formatQuantity } from '../../utils/format';
import { WorkflowCard } from '../signing/WorkflowPanel';
import { DocumentPdf } from '../proposals/ProposalDetailPage';
import { ExportableTable } from '../../components/TableExportButton';

/** Hợp đồng có hiệu lực để chọn khi lập biên bản. */
export function EffectiveContractSelect({
  id,
  value,
  onChange,
  disabled,
}: {
  id?: string;
  value?: string;
  onChange?: (v: string) => void;
  disabled?: boolean;
}) {
  const contracts = usePagedList<ContractListItemDto>('/api/contracts', {
    status: 'Effective',
    pageSize: 200,
  });
  return (
    <Select
      id={id}
      showSearch
      disabled={disabled}
      value={value}
      onChange={onChange}
      optionFilterProp="label"
      placeholder="Chọn hợp đồng có hiệu lực"
      options={(contracts.data?.items ?? []).map((c) => ({
        value: c.id,
        label: `${c.number} — ${c.supplierName}`,
      }))}
    />
  );
}

const receiptColumns = [
  { title: 'Số phiếu', dataIndex: 'number' },
  { title: 'Loại', dataIndex: 'type', render: (v: string) => <StatusTag domain="receiptType" value={v} /> },
  { title: 'Ngày', dataIndex: 'date', render: (v: string) => formatDate(v) },
  { title: 'Số hóa đơn', dataIndex: 'invoiceNumber', render: (v?: string | null) => v ?? '—' },
  { title: 'Giá trị', dataIndex: 'amount', align: 'right' as const, render: (v: number) => formatMoney(v) },
];

interface Header {
  contractId?: string;
  period: [dayjs.Dayjs, dayjs.Dayjs];
  periodText?: string;
  conclusion?: string;
}

/** Lập / sửa biên bản nghiệm thu: chọn hợp đồng, kỳ và các phiếu nhập kho đã ký đủ chưa nghiệm thu. */
export function AcceptanceEditorPage() {
  const { id } = useParams<{ id: string }>();
  const existing = useAcceptance(id);
  if (id && !existing.data) return <Card loading />;
  return <AcceptanceEditor key={id ?? 'new'} acceptance={existing.data} />;
}

function AcceptanceEditor({ acceptance }: { acceptance?: AcceptanceDto }) {
  const editing = !!acceptance;
  const [form] = Form.useForm<Header>();
  const [selected, setSelected] = useState<string[]>(acceptance?.receipts.map((r) => r.receiptId) ?? []);
  const navigate = useNavigate();
  const { message } = App.useApp();
  const onError = useApiError();
  const contractId = Form.useWatch('contractId', form) ?? acceptance?.contractId;
  const period = Form.useWatch('period', form);
  const candidates = useAcceptanceCandidates(
    contractId,
    period?.[0]?.format('YYYY-MM-DD'),
    period?.[1]?.format('YYYY-MM-DD'),
    acceptance?.id,
  );
  const rows = candidates.data ?? [];
  const total = rows.filter((r) => selected.includes(r.receiptId)).reduce((s, r) => s + r.amount, 0);

  // Lưu nháp tự động (spec §9.3) for a new acceptance.
  const draft = useFormDraft({
    key: editing ? null : 'acceptance-new',
    form,
    extra: selected,
    onRestoreExtra: setSelected,
  });

  const save = usePaymentAction(async () => {
    const h = await form.validateFields();
    const body = {
      contractId: h.contractId!,
      periodFrom: h.period[0].format('YYYY-MM-DD'),
      periodTo: h.period[1].format('YYYY-MM-DD'),
      periodText: h.periodText || null,
      conclusion: h.conclusion || null,
      receiptIds: selected.filter((s) => rows.some((r) => r.receiptId === s)),
    };
    if (editing) {
      await acceptanceApi.update(acceptance.id, body);
      return acceptance.id;
    }
    return acceptanceApi.create(body);
  });

  return (
    <>
      <DocHeader
        back={{ to: -1, label: 'Quay lại' }}
        plain
        code={editing ? `Sửa biên bản nghiệm thu ${acceptance.number}` : 'Lập biên bản nghiệm thu'}
        sub="Chỉ gồm phiếu nhập kho đã ký đủ, chưa nghiệm thu; phiếu hoàn trả được trừ vào giá trị"
      />
      {draft.banner}
      <Card style={{ marginBottom: 16 }}>
        <Form
          form={form}
          layout="vertical"
          onValuesChange={draft.onValuesChange}
          initialValues={{
            contractId: acceptance?.contractId,
            period: acceptance
              ? [dayjs(acceptance.periodFrom), dayjs(acceptance.periodTo)]
              : [dayjs().subtract(1, 'month').startOf('month'), dayjs().endOf('month')],
            periodText: acceptance?.periodText,
            conclusion: acceptance?.conclusion ?? undefined,
          }}
        >
          <Row gutter={12}>
            <Col xs={24} md={10}>
              <Form.Item name="contractId" label="Hợp đồng" rules={[{ required: true }]}>
                <EffectiveContractSelect disabled={editing} onChange={() => setSelected([])} />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item
                name="period"
                label="Kỳ nghiệm thu (lọc theo ngày phiếu)"
                rules={[{ required: true }]}
              >
                <DatePicker.RangePicker
                  format="DD/MM/YYYY"
                  placeholder={['Từ ngày', 'Đến ngày']}
                  style={{ width: '100%' }}
                />
              </Form.Item>
            </Col>
            <Col xs={24} md={6}>
              <Form.Item name="periodText" label="Tên kỳ (VD: Tháng 10/2026)">
                <Input maxLength={100} placeholder="Tự đặt theo khoảng ngày" />
              </Form.Item>
            </Col>
            <Col span={24}>
              <Form.Item name="conclusion" label="Kết luận nghiệm thu">
                <Input.TextArea
                  rows={2}
                  maxLength={2000}
                  placeholder="Mặc định: hàng hóa giao đúng chủng loại, số lượng, chất lượng; đồng ý nghiệm thu."
                />
              </Form.Item>
            </Col>
          </Row>
        </Form>
      </Card>
      <Card
        title="Phiếu nhập kho đã ký đủ, chưa nghiệm thu"
        extra={<Typography.Text strong>Giá trị nghiệm thu: {formatMoney(total)}</Typography.Text>}
      >
        {!contractId ? (
          <Empty description="Chọn hợp đồng" />
        ) : (
          <Table<AcceptanceReceiptDto>
            rowKey="receiptId"
            size="small"
            loading={candidates.isFetching}
            dataSource={rows}
            pagination={false}
            rowSelection={{
              selectedRowKeys: selected,
              onChange: (keys) => {
                setSelected(keys.map(String));
                draft.touch();
              },
            }}
            locale={{
              emptyText: <Empty description="Không có phiếu nhập kho đã ký nào chưa nghiệm thu trong kỳ" />,
            }}
            columns={receiptColumns}
          />
        )}
      </Card>
      <StickyActionBar
        total={{
          label: 'Giá trị nghiệm thu',
          value: formatMoney(total),
          sub: `${selected.length} phiếu nhập kho được chọn`,
        }}
      >
        <Button
          type="primary"
          icon={<SaveOutlined />}
          disabled={selected.length === 0}
          loading={save.isPending}
          onClick={async () => {
            try {
              const savedId = await save.mutateAsync();
              draft.clear();
              message.success('Đã lưu biên bản — kiểm tra rồi gửi ký');
              navigate(`/thanh-toan/nghiem-thu/${savedId}`);
            } catch (e) {
              onError(e);
            }
          }}
        >
          Lưu biên bản ({selected.length} phiếu)
        </Button>
      </StickyActionBar>
    </>
  );
}

/** Chi tiết biên bản nghiệm thu: phiếu, tổng hợp hàng hóa, PDF, luồng ký. */
export function AcceptanceDetailPage() {
  const { id } = useParams<{ id: string }>();
  const acceptance = useAcceptance(id);
  const a = acceptance.data;
  const workflow = useDocumentWorkflow(a?.documentId ?? undefined);
  const navigate = useNavigate();
  const { isSupplier } = useAuth();
  const { message } = App.useApp();
  const onError = useApiError();
  const submit = usePaymentAction(() => acceptanceApi.submit(id!));
  const cancel = usePaymentAction(() => acceptanceApi.cancel(id!));

  if (!a) return <Card loading={acceptance.isLoading} />;
  const base = isSupplier ? '/ncc' : '';

  return (
    <>
      <DocHeader
        back={{ to: -1, label: 'Quay lại' }}
        plain
        code={
          <>
            Biên bản nghiệm thu <span className="mono">{a.number}</span>
          </>
        }
        tags={
          <Space size={4} wrap>
            <StatusTag domain="paymentRecordStatus" value={a.status} />
            {a.isAutoGenerated && <Tag>Lập tự động</Tag>}
            {a.isIncludedInValuation && <Tag color="green">Đã đưa vào thanh toán</Tag>}
          </Space>
        }
        actions={
          <>
            {a.canEdit && (
              <Button icon={<EditOutlined />} onClick={() => navigate(`/thanh-toan/nghiem-thu/${a.id}/sua`)}>
                Sửa
              </Button>
            )}
            {a.canSubmit && (
              <Button
                type="primary"
                icon={<SendOutlined />}
                loading={submit.isPending}
                onClick={() =>
                  submit
                    .mutateAsync()
                    .then(() => message.success('Đã gửi ký — Tổ rà soát kiểm tra trước'), onError)
                }
              >
                {a.status === 'Returned' ? 'Trình ký lại' : 'Gửi ký'}
              </Button>
            )}
            {a.canCancel && (
              <Popconfirm
                title="Hủy biên bản nghiệm thu?"
                description="Các phiếu nhập kho được trả lại danh sách chưa nghiệm thu."
                onConfirm={() => cancel.mutateAsync().then(() => message.success('Đã hủy biên bản'), onError)}
              >
                <Button danger icon={<StopOutlined />}>
                  Hủy
                </Button>
              </Popconfirm>
            )}
          </>
        }
      />
      {a.status === 'Returned' && a.lastReturnReason && (
        <Alert
          type="error"
          showIcon
          style={{ marginBottom: 12 }}
          message="Biên bản bị trả lại"
          description={a.lastReturnReason}
        />
      )}
      <Row gutter={[16, 16]}>
        <Col xs={24} xl={workflow.data ? 16 : 24}>
          <Card style={{ marginBottom: 16 }}>
            <Descriptions size="small" column={{ xs: 1, md: 2 }}>
              <Descriptions.Item label="Hợp đồng">
                <a onClick={() => navigate(`${base}/hop-dong/${a.contractId}`)}>{a.contractNumber}</a> —{' '}
                {a.contractName}
              </Descriptions.Item>
              <Descriptions.Item label="Nhà cung cấp">{a.supplierName}</Descriptions.Item>
              <Descriptions.Item label="Kỳ nghiệm thu">
                {a.periodText} ({formatDate(a.periodFrom)} – {formatDate(a.periodTo)})
              </Descriptions.Item>
              <Descriptions.Item label="Giá trị nghiệm thu">
                <b>{formatMoney(a.totalAmount)}</b>
              </Descriptions.Item>
              <Descriptions.Item label="Biên bản 08a">
                {a.valuationId ? (
                  <a onClick={() => navigate(`${base}/thanh-toan/xac-dinh-gia-tri/${a.valuationId}`)}>
                    {a.valuationNumber}
                  </a>
                ) : (
                  '—'
                )}
              </Descriptions.Item>
              <Descriptions.Item label="Ký xong">{formatDateTime(a.completedAt) || '—'}</Descriptions.Item>
              <Descriptions.Item label="Kết luận" span={2}>
                {a.conclusion ?? '—'}
              </Descriptions.Item>
            </Descriptions>
          </Card>
          <Card>
            <Tabs
              items={[
                {
                  key: 'receipts',
                  label: `Phiếu nhập kho (${a.receipts.length})`,
                  children: (
                    <ExportableTable<AcceptanceReceiptDto>
                      exportTitle="Phiếu nhập kho trong biên bản nghiệm thu"
                      rowKey="receiptId"
                      size="small"
                      pagination={false}
                      dataSource={a.receipts}
                      onRow={(r) => ({
                        onClick: () => navigate(`${base}/nhap-kho/${r.receiptId}`),
                        style: { cursor: 'pointer' },
                      })}
                      columns={receiptColumns}
                    />
                  ),
                },
                {
                  key: 'items',
                  label: `Tổng hợp hàng hóa (${a.items.length})`,
                  children: (
                    <ExportableTable<AcceptanceItemDto>
                      exportTitle="Mặt hàng nghiệm thu"
                      rowKey="contractItemId"
                      size="small"
                      pagination={false}
                      dataSource={a.items}
                      columns={[
                        { title: 'Mã hàng', dataIndex: 'itemCode' },
                        { title: 'Tên hàng', dataIndex: 'itemName' },
                        { title: 'ĐVT', dataIndex: 'unit' },
                        {
                          title: 'Số lượng',
                          dataIndex: 'quantity',
                          align: 'right',
                          render: (v: number) => formatQuantity(v),
                        },
                        {
                          title: 'Đơn giá',
                          dataIndex: 'unitPrice',
                          align: 'right',
                          render: (v: number) => formatMoney(v),
                        },
                        {
                          title: 'Thành tiền',
                          dataIndex: 'amount',
                          align: 'right',
                          render: (v: number) => formatMoney(v),
                        },
                      ]}
                    />
                  ),
                },
                {
                  key: 'pdf',
                  label: 'Biên bản (PDF)',
                  children: a.documentId ? <DocumentPdf documentId={a.documentId} /> : <Empty />,
                },
              ]}
            />
          </Card>
        </Col>
        {workflow.data && (
          <Col xs={24} xl={8}>
            <WorkflowCard wf={workflow.data} />
          </Col>
        )}
      </Row>
    </>
  );
}
