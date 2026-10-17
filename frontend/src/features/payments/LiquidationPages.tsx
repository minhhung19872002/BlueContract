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
  Modal,
  Popconfirm,
  Row,
  Select,
  Space,
  Statistic,
  Table,
  Tabs,
  Typography,
  Upload,
} from 'antd';
import {
  CheckCircleOutlined,
  DeleteOutlined,
  PaperClipOutlined,
  SaveOutlined,
  SendOutlined,
  StopOutlined,
} from '@ant-design/icons';
import { useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import dayjs from '../../i18n/dayjs';
import {
  liquidationApi,
  useLiquidation,
  useLiquidationPreview,
  usePaymentAction,
  type LiquidationDto,
  type LiquidationSettlementDto,
  type LiquidationValuationDto,
} from '../../api/payments';
import { useDocumentWorkflow } from '../../api/signing';
import { useAuth } from '../../auth/useAuth';
import { statusLabel, StatusTag } from '../../components/StatusTag';
import { useApiError } from '../../components/common';
import { DocHeader, StickyActionBar } from '../../components/layout';
import { MoneyInput } from '../../components/inputs';
import { formatDate, formatDateTime, formatMoney } from '../../utils/format';
import { WorkflowCard } from '../signing/WorkflowPanel';
import { DocumentPdf } from '../proposals/ProposalDetailPage';
import { EffectiveContractSelect } from './AcceptancePages';
import { ExportableTable } from '../../components/TableExportButton';
import { DetailState } from '../../components/DetailState';

const valuationColumns = [
  { title: 'Biên bản 08a', dataIndex: 'number' },
  { title: 'Ngày ký', dataIndex: 'date', render: (v: string) => formatDate(v) },
  {
    title: 'Giá trị thực hiện',
    dataIndex: 'amount',
    align: 'right' as const,
    render: (v: number) => formatMoney(v),
  },
  {
    title: 'Đã thanh toán',
    dataIndex: 'paidAmount',
    align: 'right' as const,
    render: (v: number) => formatMoney(v),
  },
  {
    title: 'Còn lại',
    dataIndex: 'openAmount',
    align: 'right' as const,
    render: (v: number) => (
      <Typography.Text type={v > 0 ? 'danger' : undefined}>{formatMoney(v)}</Typography.Text>
    ),
  },
  { title: 'Số UNC', dataIndex: 'paymentOrderNumbers', render: (v?: string | null) => v ?? '—' },
];

function Figures({
  x,
}: {
  x: {
    contractValue: number;
    executedValue: number;
    paidAmount: number;
    remainingAmount: number;
    reductionAmount: number;
  };
}) {
  return (
    <Row gutter={[16, 16]}>
      <Col xs={12} md={5}>
        <Statistic title="Giá trị hợp đồng" value={formatMoney(x.contractValue)} />
      </Col>
      <Col xs={12} md={5}>
        <Statistic title="Giá trị thực hiện" value={formatMoney(x.executedValue)} />
      </Col>
      <Col xs={12} md={5}>
        <Statistic
          title="Đã thanh toán"
          value={formatMoney(x.paidAmount)}
          valueStyle={{ color: '#389e0d' }}
        />
      </Col>
      <Col xs={12} md={5}>
        <Statistic
          title="Còn phải thanh toán"
          value={formatMoney(x.remainingAmount)}
          valueStyle={{ color: x.remainingAmount > 0 ? '#cf1322' : undefined }}
        />
      </Col>
      <Col xs={12} md={4}>
        <Statistic title="Giảm trừ / không thực hiện" value={formatMoney(x.reductionAmount)} />
      </Col>
    </Row>
  );
}

/** Lập biên bản thanh lý: chọn hợp đồng → bảng đối chiếu → ghi chú → lập. */
export function LiquidationCreatePage() {
  const [params] = useSearchParams();
  const [contractId, setContractId] = useState<string | undefined>(params.get('contract') ?? undefined);
  const [notes, setNotes] = useState('');
  const preview = useLiquidationPreview(contractId);
  const p = preview.data;
  const navigate = useNavigate();
  const { message } = App.useApp();
  const onError = useApiError();
  const create = usePaymentAction(() =>
    liquidationApi.create({ contractId: contractId!, notes: notes || null }),
  );

  return (
    <>
      <DocHeader
        back={{ to: -1, label: 'Quay lại' }}
        plain
        code="Lập biên bản thanh lý hợp đồng"
        sub="Tổng hợp các biên bản 08a đã ký; khoản còn thiếu được chốt công nợ trước khi trình ký"
      />
      <Card style={{ marginBottom: 16 }}>
        <Row gutter={12}>
          <Col xs={24} md={10}>
            <Typography.Text>Hợp đồng</Typography.Text>
            <EffectiveContractSelect value={contractId} onChange={setContractId} />
          </Col>
          <Col xs={24} md={14}>
            <Typography.Text>Các nội dung khác (ghi vào biên bản)</Typography.Text>
            <Input.TextArea
              rows={2}
              maxLength={4000}
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
            />
          </Col>
        </Row>
      </Card>
      {!contractId ? (
        <Card>
          <Empty description="Chọn hợp đồng để xem bảng đối chiếu" />
        </Card>
      ) : (
        <Card
          loading={preview.isLoading}
          title={p ? `Bảng đối chiếu — ${p.contractNumber} (${p.supplierName})` : undefined}
        >
          {p && (
            <Space direction="vertical" style={{ width: '100%' }}>
              {p.blockingCode && (
                <Alert
                  type="error"
                  showIcon
                  message={p.blockingMessage ?? p.blockingCode}
                  action={
                    p.existingLiquidationId && (
                      <Button
                        size="small"
                        onClick={() => navigate(`/thanh-toan/thanh-ly/${p.existingLiquidationId}`)}
                      >
                        Mở biên bản
                      </Button>
                    )
                  }
                />
              )}
              {p.warnings.map((w) => (
                <Alert key={w} type="warning" showIcon message={w} />
              ))}
              <Figures x={p} />
              <Table<LiquidationValuationDto>
                rowKey="valuationId"
                size="small"
                pagination={false}
                dataSource={p.valuations}
                columns={valuationColumns}
              />
            </Space>
          )}
        </Card>
      )}
      <StickyActionBar>
        <Button
          type="primary"
          icon={<SaveOutlined />}
          disabled={!p || !!p.blockingCode}
          loading={create.isPending}
          onClick={() =>
            create.mutateAsync().then((id) => {
              message.success('Đã lập biên bản thanh lý — chốt công nợ (nếu còn) rồi gửi ký');
              navigate(`/thanh-toan/thanh-ly/${id}`);
            }, onError)
          }
        >
          Lập biên bản thanh lý
        </Button>
      </StickyActionBar>
    </>
  );
}

/** Chi tiết biên bản thanh lý: đối chiếu, chốt công nợ (đánh dấu đã thanh toán + chứng từ), PDF, luồng ký. */
export function LiquidationDetailPage() {
  const { id } = useParams<{ id: string }>();
  const liquidation = useLiquidation(id);
  const l = liquidation.data;
  const workflow = useDocumentWorkflow(l?.documentId ?? undefined);
  const navigate = useNavigate();
  const { isSupplier } = useAuth();
  const { message } = App.useApp();
  const onError = useApiError();
  const submit = usePaymentAction(() => liquidationApi.submit(id!));
  const cancel = usePaymentAction(() => liquidationApi.cancel(id!));
  const refresh = usePaymentAction(() =>
    liquidationApi.update(id!, { contractId: l!.contractId, notes: l!.notes ?? null }),
  );

  if (!l) return <DetailState query={liquidation} />;
  const base = isSupplier ? '/ncc' : '';

  return (
    <>
      <DocHeader
        back={{ to: -1, label: 'Quay lại' }}
        plain
        code={
          <>
            Biên bản thanh lý <span className="mono">{l.number}</span>
          </>
        }
        tags={<StatusTag domain="paymentRecordStatus" value={l.status} />}
        actions={
          <>
            {l.canEdit && (
              <Button
                loading={refresh.isPending}
                onClick={() =>
                  refresh.mutateAsync().then(() => message.success('Đã cập nhật số liệu'), onError)
                }
              >
                Lấy lại số liệu
              </Button>
            )}
            {l.canSubmit && (
              <Button
                type="primary"
                icon={<SendOutlined />}
                loading={submit.isPending}
                onClick={() =>
                  submit.mutateAsync().then(() => message.success('Đã gửi ký biên bản thanh lý'), onError)
                }
              >
                {l.status === 'Returned' ? 'Trình ký lại' : 'Gửi ký'}
              </Button>
            )}
            {l.canCancel && (
              <Popconfirm
                title="Hủy biên bản thanh lý?"
                onConfirm={() => cancel.mutateAsync().then(() => message.success('Đã hủy'), onError)}
              >
                <Button danger icon={<StopOutlined />}>
                  Hủy
                </Button>
              </Popconfirm>
            )}
          </>
        }
      />
      {l.status === 'Completed' && (
        <Alert
          type="success"
          showIcon
          icon={<CheckCircleOutlined />}
          style={{ marginBottom: 12 }}
          message="Hợp đồng đã thanh lý — khóa mọi phát sinh mới."
        />
      )}
      {l.status === 'Returned' && l.lastReturnReason && (
        <Alert
          type="error"
          showIcon
          style={{ marginBottom: 12 }}
          message="Biên bản bị trả lại"
          description={l.lastReturnReason}
        />
      )}
      {l.canEdit && l.remainingAmount > 0 && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12 }}
          message={`Còn ${formatMoney(l.remainingAmount)} chưa thanh toán — bổ sung ủy nhiệm chi ở biên bản 08a rồi "Lấy lại số liệu", hoặc đánh dấu "đã thanh toán" kèm chứng từ để chốt công nợ.`}
        />
      )}
      <Card style={{ marginBottom: 16 }}>
        <Descriptions size="small" column={{ xs: 1, md: 2 }} style={{ marginBottom: 12 }}>
          <Descriptions.Item label="Hợp đồng">
            <a onClick={() => navigate(`${base}/hop-dong/${l.contractId}`)}>{l.contractNumber}</a> —{' '}
            {l.contractName}
          </Descriptions.Item>
          <Descriptions.Item label="Nhà cung cấp">{l.supplierName}</Descriptions.Item>
          <Descriptions.Item label="Ghi chú" span={2}>
            {l.notes ?? '—'}
          </Descriptions.Item>
        </Descriptions>
        <Figures x={{ ...l, paidAmount: l.paidAmount + l.settledAmount }} />
      </Card>
      <Row gutter={[16, 16]}>
        <Col xs={24} xl={workflow.data ? 16 : 24}>
          <Card>
            <Tabs
              items={[
                {
                  key: 'valuations',
                  label: `Các lần thanh toán (${l.valuations.length})`,
                  children: (
                    <ExportableTable<LiquidationValuationDto>
                      exportTitle="Bảng đối chiếu thanh toán"
                      rowKey="valuationId"
                      size="small"
                      pagination={false}
                      dataSource={l.valuations}
                      onRow={(v) => ({
                        onClick: () => navigate(`${base}/thanh-toan/xac-dinh-gia-tri/${v.valuationId}`),
                        style: { cursor: 'pointer' },
                      })}
                      columns={valuationColumns}
                    />
                  ),
                },
                {
                  key: 'settlements',
                  label: `Chốt công nợ (${l.settlements.length})`,
                  children: <Settlements liquidation={l} />,
                },
                {
                  key: 'pdf',
                  label: 'Biên bản (PDF)',
                  children: l.documentId ? <DocumentPdf documentId={l.documentId} /> : <Empty />,
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

function Settlements({ liquidation: l }: { liquidation: LiquidationDto }) {
  const [open, setOpen] = useState(false);
  const [file, setFile] = useState<File | null>(null);
  const [form] = Form.useForm<{
    valuationId?: string;
    description: string;
    amount: number;
    voucherNumber?: string;
    voucherDate?: dayjs.Dayjs;
  }>();
  const { message } = App.useApp();
  const onError = useApiError();
  const add = usePaymentAction(
    (x: {
      valuationId?: string | null;
      description: string;
      amount: number;
      voucherNumber?: string | null;
      voucherDate?: string | null;
    }) => liquidationApi.addSettlement(l.id, x, file),
  );
  const remove = usePaymentAction((settlementId: string) =>
    liquidationApi.removeSettlement(l.id, settlementId),
  );
  const open_ = l.valuations.filter((v) => v.openAmount > 0);

  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      {l.canEdit && l.remainingAmount > 0 && (
        <Button
          type="primary"
          onClick={() => {
            form.setFieldsValue({
              valuationId: open_[0]?.valuationId,
              description: 'Khoản còn lại được hai bên xác nhận đã thanh toán',
              amount: open_[0]?.openAmount ?? l.remainingAmount,
              voucherDate: dayjs(),
            });
            setFile(null);
            setOpen(true);
          }}
        >
          Đánh dấu đã thanh toán
        </Button>
      )}
      <ExportableTable<LiquidationSettlementDto>
        exportTitle="Khoản đã chốt thanh toán"
        rowKey="id"
        size="small"
        pagination={false}
        dataSource={l.settlements}
        locale={{ emptyText: 'Chưa có khoản chốt công nợ' }}
        columns={[
          { title: 'Nội dung', dataIndex: 'description' },
          {
            title: '08a',
            dataIndex: 'valuationId',
            render: (v?: string | null) => l.valuations.find((x) => x.valuationId === v)?.number ?? '—',
          },
          { title: 'Số tiền', dataIndex: 'amount', align: 'right', render: (v: number) => formatMoney(v) },
          {
            title: 'Chứng từ',
            key: 'voucher',
            render: (_, s) =>
              `${s.voucherNumber ?? '—'}${s.voucherDate ? ` ngày ${formatDate(s.voucherDate)}` : ''}${s.hasFile ? ' (có file)' : ''}`,
          },
          {
            title: 'Người đánh dấu',
            key: 'by',
            render: (_, s) => `${s.markedByName ?? ''} — ${formatDateTime(s.markedAt)}`,
          },
          {
            title: '',
            key: 'actions',
            render: (_, s) =>
              l.canEdit && (
                <Popconfirm
                  title="Bỏ khoản này?"
                  onConfirm={() => remove.mutateAsync(s.id).then(() => message.success('Đã bỏ'), onError)}
                >
                  <a>
                    <DeleteOutlined />
                  </a>
                </Popconfirm>
              ),
          },
        ]}
      />
      <Modal
        title="Đánh dấu khoản chưa thanh toán là đã thanh toán"
        open={open}
        onCancel={() => setOpen(false)}
        okText="Lưu"
        okButtonProps={{ loading: add.isPending }}
        destroyOnHidden
        onOk={async () => {
          const x = await form.validateFields();
          try {
            await add.mutateAsync({
              valuationId: x.valuationId ?? null,
              description: x.description,
              amount: x.amount,
              voucherNumber: x.voucherNumber ?? null,
              voucherDate: x.voucherDate ? x.voucherDate.format('YYYY-MM-DD') : null,
            });
            message.success('Đã chốt công nợ');
            setOpen(false);
          } catch (e) {
            onError(e);
          }
        }}
      >
        <Form form={form} layout="vertical">
          <Form.Item name="valuationId" label="Thuộc biên bản 08a">
            <Select
              allowClear
              options={open_.map((v) => ({
                value: v.valuationId,
                label: `${v.number} — còn ${formatMoney(v.openAmount)}`,
              }))}
            />
          </Form.Item>
          <Form.Item name="description" label="Nội dung" rules={[{ required: true, whitespace: true }]}>
            <Input maxLength={500} />
          </Form.Item>
          <Form.Item name="amount" label="Số tiền" rules={[{ required: true }]}>
            <MoneyInput style={{ width: '100%' }} />
          </Form.Item>
          <Row gutter={12}>
            <Col span={12}>
              <Form.Item name="voucherNumber" label="Số chứng từ">
                <Input maxLength={50} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="voucherDate" label="Ngày chứng từ">
                <DatePicker format="DD/MM/YYYY" style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
          <Upload
            accept=".pdf,.jpg,.jpeg,.png"
            maxCount={1}
            beforeUpload={(f) => {
              setFile(f);
              return false;
            }}
            onRemove={() => setFile(null)}
          >
            <Button icon={<PaperClipOutlined />}>Đính kèm chứng từ (tùy chọn)</Button>
          </Upload>
          <Typography.Paragraph type="secondary" style={{ marginTop: 8, marginBottom: 0 }}>
            Trạng thái hiện tại: {statusLabel('paymentRecordStatus', l.status)}. Người đánh dấu và thời điểm
            được lưu vào biên bản.
          </Typography.Paragraph>
        </Form>
      </Modal>
    </Space>
  );
}
