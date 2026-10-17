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
  List,
  Modal,
  Popconfirm,
  Row,
  Space,
  Statistic,
  Table,
  Tabs,
  Tag,
  Typography,
  Upload,
} from 'antd';
import {
  DeleteOutlined,
  PaperClipOutlined,
  PlusOutlined,
  SaveOutlined,
  SendOutlined,
  StopOutlined,
} from '@ant-design/icons';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import dayjs from '../../i18n/dayjs';
import { usePagedList } from '../../api/admin';
import {
  ACCEPTANCES,
  usePaymentAction,
  useValuation,
  valuationApi,
  type AcceptanceListItemDto,
  type ValuationDto,
  type ValuationItemDto,
} from '../../api/payments';
import { useDocumentWorkflow } from '../../api/signing';
import { useAuth } from '../../auth/useAuth';
import { StatusTag } from '../../components/StatusTag';
import { useApiError } from '../../components/common';
import { useFormDraft } from '../../hooks/useFormDraft';
import { DocHeader, StickyActionBar } from '../../components/layout';
import { MoneyInput } from '../../components/inputs';
import { formatDate, formatDateTime, formatMoney, formatQuantity } from '../../utils/format';
import { WorkflowCard } from '../signing/WorkflowPanel';
import { DocumentPdf } from '../proposals/ProposalDetailPage';
import { EffectiveContractSelect } from './AcceptancePages';
import { ExportableTable } from '../../components/TableExportButton';
import { DetailState } from '../../components/DetailState';

interface Header {
  contractId?: string;
  periodText?: string;
  advanceAmount: number;
  advanceRecovered: number;
}

/** Lập biên bản xác định giá trị (08a): chọn các biên bản nghiệm thu đã ký, chưa đưa vào thanh toán. */
export function ValuationCreatePage() {
  const [form] = Form.useForm<Header>();
  const [selected, setSelected] = useState<string[]>([]);
  const navigate = useNavigate();
  const { message } = App.useApp();
  const onError = useApiError();
  const contractId = Form.useWatch('contractId', form);
  const available = usePagedList<AcceptanceListItemDto>(ACCEPTANCES, {
    contractId,
    available: true,
    pageSize: 200,
  });
  const rows = contractId ? (available.data?.items ?? []) : [];
  const total = rows.filter((r) => selected.includes(r.id)).reduce((s, r) => s + r.totalAmount, 0);
  // Lưu nháp tự động (spec §9.3).
  const draft = useFormDraft({ key: 'valuation-new', form, extra: selected, onRestoreExtra: setSelected });
  const create = usePaymentAction(async () => {
    const h = await form.validateFields();
    return valuationApi.create({
      contractId: h.contractId!,
      acceptanceIds: selected,
      periodText: h.periodText || null,
      advanceAmount: h.advanceAmount ?? 0,
      advanceRecovered: h.advanceRecovered ?? 0,
    });
  });

  return (
    <>
      <DocHeader
        back={{ to: -1, label: 'Quay lại' }}
        plain
        code="Lập biên bản xác định giá trị khối lượng hoàn thành (Mẫu 08a)"
        sub="Hệ thống tính giá trị kỳ này, lũy kế đến kỳ trước / kỳ này, % so với hợp đồng và sinh kèm Đề nghị thanh toán"
      />
      {draft.banner}
      <Card style={{ marginBottom: 16 }}>
        <Form
          form={form}
          layout="vertical"
          initialValues={{ advanceAmount: 0, advanceRecovered: 0 }}
          onValuesChange={draft.onValuesChange}
        >
          <Row gutter={12}>
            <Col xs={24} md={10}>
              <Form.Item name="contractId" label="Hợp đồng" rules={[{ required: true }]}>
                <EffectiveContractSelect onChange={() => setSelected([])} />
              </Form.Item>
            </Col>
            <Col xs={24} md={6}>
              <Form.Item name="periodText" label="Kỳ thanh toán">
                <Input maxLength={100} placeholder="VD: Quý IV/2026 (mặc định: Lần n)" />
              </Form.Item>
            </Col>
            <Col xs={12} md={4}>
              <Form.Item name="advanceAmount" label="Giá trị tạm ứng theo HĐ">
                <MoneyInput style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={12} md={4}>
              <Form.Item name="advanceRecovered" label="Thu hồi tạm ứng kỳ này">
                <MoneyInput style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
        </Form>
      </Card>
      <Card
        title="Biên bản nghiệm thu đã ký, chưa đưa vào thanh toán"
        extra={<Typography.Text strong>Giá trị kỳ này: {formatMoney(total)}</Typography.Text>}
      >
        {!contractId ? (
          <Empty description="Chọn hợp đồng" />
        ) : (
          <Table<AcceptanceListItemDto>
            rowKey="id"
            size="small"
            loading={available.isFetching}
            dataSource={rows}
            pagination={false}
            rowSelection={{
              selectedRowKeys: selected,
              onChange: (keys) => {
                setSelected(keys.map(String));
                draft.touch();
              },
            }}
            locale={{ emptyText: <Empty description="Không có biên bản nghiệm thu nào sẵn sàng" /> }}
            columns={[
              { title: 'Số biên bản', dataIndex: 'number' },
              { title: 'Kỳ', dataIndex: 'periodText' },
              { title: 'Số phiếu', dataIndex: 'receiptCount', align: 'center' },
              {
                title: 'Giá trị',
                dataIndex: 'totalAmount',
                align: 'right',
                render: (v: number) => formatMoney(v),
              },
            ]}
          />
        )}
      </Card>
      <StickyActionBar total={{ label: 'Giá trị kỳ này', value: formatMoney(total) }}>
        <Button
          type="primary"
          icon={<SaveOutlined />}
          disabled={selected.length === 0}
          loading={create.isPending}
          onClick={() =>
            create.mutateAsync().then((v) => {
              draft.clear();
              message.success(`Đã lập biên bản ${v.number} và đề nghị thanh toán ${v.paymentRequestNumber}`);
              navigate(`/thanh-toan/xac-dinh-gia-tri/${v.id}`);
            }, onError)
          }
        >
          Lập 08a ({selected.length} biên bản)
        </Button>
      </StickyActionBar>
    </>
  );
}

/** Chi tiết 08a: bảng lũy kế, đề nghị thanh toán, ủy nhiệm chi, hai luồng ký. */
export function ValuationDetailPage() {
  const { id } = useParams<{ id: string }>();
  const valuation = useValuation(id);
  const v = valuation.data;
  const workflow = useDocumentWorkflow(v?.documentId ?? undefined);
  const requestWorkflow = useDocumentWorkflow(v?.paymentRequestDocumentId ?? undefined);
  const navigate = useNavigate();
  const { isSupplier } = useAuth();
  const { message } = App.useApp();
  const onError = useApiError();
  const submit = usePaymentAction(() => valuationApi.submit(id!));
  const cancel = usePaymentAction(() => valuationApi.cancel(id!));

  if (!v) return <DetailState query={valuation} />;
  const base = isSupplier ? '/ncc' : '';

  return (
    <>
      <DocHeader
        back={{ to: -1, label: 'Quay lại' }}
        plain
        code={
          <>
            Biên bản xác định giá trị <span className="mono">{v.number}</span>
          </>
        }
        tags={
          <Space size={4} wrap>
            <StatusTag domain="paymentRecordStatus" value={v.status} />
            {v.status === 'Completed' && <StatusTag domain="paymentStatus" value={v.paymentStatus} />}
            {v.settledByLiquidation && <Tag color="purple">Chốt công nợ khi thanh lý</Tag>}
            <Typography.Text type="secondary">
              Đề nghị thanh toán {v.paymentRequestNumber}:{' '}
              <StatusTag domain="paymentRecordStatus" value={v.paymentRequestStatus} />
            </Typography.Text>
          </Space>
        }
        actions={
          <>
            {v.canSubmit && (
              <Button
                type="primary"
                icon={<SendOutlined />}
                loading={submit.isPending}
                onClick={() =>
                  submit
                    .mutateAsync()
                    .then(() => message.success('Đã gửi ký biên bản 08a và đề nghị thanh toán'), onError)
                }
              >
                {v.status === 'Returned' ? 'Trình ký lại' : 'Gửi ký'}
              </Button>
            )}
            {v.canCancel && (
              <Popconfirm
                title="Hủy biên bản 08a?"
                description="Các biên bản nghiệm thu được trả lại danh sách chờ thanh toán."
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
      {v.status === 'Returned' && v.lastReturnReason && (
        <Alert
          type="error"
          showIcon
          style={{ marginBottom: 12 }}
          message="Biên bản bị trả lại"
          description={v.lastReturnReason}
        />
      )}
      <Card style={{ marginBottom: 16 }}>
        <Row gutter={[16, 16]}>
          <Col xs={12} md={6}>
            <Statistic title="Giá trị hợp đồng" value={formatMoney(v.contractValue)} />
          </Col>
          <Col xs={12} md={6}>
            <Statistic title="Lũy kế đến cuối kỳ trước" value={formatMoney(v.previousCumulative)} />
          </Col>
          <Col xs={12} md={6}>
            <Statistic
              title={`Thực hiện kỳ này (lần ${v.paymentSequence})`}
              value={formatMoney(v.currentPeriodValue)}
            />
          </Col>
          <Col xs={12} md={6}>
            <Statistic
              title={`Lũy kế đến hết kỳ này (${v.completionPercent}% HĐ)`}
              value={formatMoney(v.cumulativeToDate)}
            />
          </Col>
          <Col xs={12} md={6}>
            <Statistic title="Thu hồi tạm ứng" value={formatMoney(v.advanceRecovered)} />
          </Col>
          <Col xs={12} md={6}>
            <Statistic
              title="Đề nghị thanh toán kỳ này"
              value={formatMoney(v.requestedAmount)}
              valueStyle={{ color: '#1677ff' }}
            />
          </Col>
          <Col xs={12} md={6}>
            <Statistic
              title="Đã chuyển (UNC)"
              value={formatMoney(v.paidAmount)}
              valueStyle={{ color: '#389e0d' }}
            />
          </Col>
          <Col xs={12} md={6}>
            <Descriptions size="small" column={1}>
              <Descriptions.Item label="Hợp đồng">
                <a onClick={() => navigate(`${base}/hop-dong/${v.contractId}`)}>{v.contractNumber}</a>
              </Descriptions.Item>
              <Descriptions.Item label="Kỳ">{v.periodText}</Descriptions.Item>
              <Descriptions.Item label="NCC">{v.supplierName}</Descriptions.Item>
            </Descriptions>
          </Col>
        </Row>
      </Card>
      <Row gutter={[16, 16]}>
        <Col xs={24} xl={16}>
          <Card>
            <Tabs
              items={[
                {
                  key: 'items',
                  label: 'Khối lượng hoàn thành',
                  children: (
                    <ExportableTable<ValuationItemDto>
                      exportTitle="Chi tiết giá trị khối lượng hoàn thành"
                      rowKey="contractItemId"
                      size="small"
                      pagination={false}
                      dataSource={v.items}
                      scroll={{ x: 'max-content' }}
                      columns={[
                        { title: 'Tên hàng hóa / công việc', dataIndex: 'itemName', width: 240 },
                        { title: 'ĐVT', dataIndex: 'unit' },
                        {
                          title: 'KL HĐ',
                          dataIndex: 'contractQuantity',
                          align: 'right',
                          render: (x: number) => formatQuantity(x),
                        },
                        {
                          title: 'Đơn giá',
                          dataIndex: 'unitPrice',
                          align: 'right',
                          render: (x: number) => formatMoney(x),
                        },
                        {
                          title: 'Lũy kế kỳ trước',
                          dataIndex: 'previousQuantity',
                          align: 'right',
                          render: (x: number) => formatQuantity(x),
                        },
                        {
                          title: 'Kỳ này',
                          dataIndex: 'currentQuantity',
                          align: 'right',
                          render: (x: number) => <b>{formatQuantity(x)}</b>,
                        },
                        {
                          title: 'Lũy kế',
                          dataIndex: 'cumulativeQuantity',
                          align: 'right',
                          render: (x: number) => formatQuantity(x),
                        },
                        {
                          title: 'Giá trị kỳ này',
                          dataIndex: 'currentAmount',
                          align: 'right',
                          render: (x: number) => formatMoney(x),
                        },
                        {
                          title: 'Giá trị lũy kế',
                          dataIndex: 'cumulativeAmount',
                          align: 'right',
                          render: (x: number) => formatMoney(x),
                        },
                      ]}
                    />
                  ),
                },
                {
                  key: 'acceptances',
                  label: `Biên bản nghiệm thu (${v.acceptances.length})`,
                  children: (
                    <List
                      dataSource={v.acceptances}
                      renderItem={(a) => (
                        <List.Item
                          actions={[formatMoney(a.amount)]}
                          onClick={() => navigate(`${base}/thanh-toan/nghiem-thu/${a.acceptanceId}`)}
                          style={{ cursor: 'pointer' }}
                        >
                          <List.Item.Meta title={a.number} description={a.periodText} />
                        </List.Item>
                      )}
                    />
                  ),
                },
                {
                  key: 'pdf',
                  label: 'Biên bản 08a (PDF)',
                  children: v.documentId ? <DocumentPdf documentId={v.documentId} /> : <Empty />,
                },
                {
                  key: 'request',
                  label: 'Đề nghị thanh toán (PDF)',
                  children: v.paymentRequestDocumentId ? (
                    <DocumentPdf documentId={v.paymentRequestDocumentId} />
                  ) : (
                    <Empty />
                  ),
                },
              ]}
            />
          </Card>
        </Col>
        <Col xs={24} xl={8}>
          <PaymentOrdersCard valuation={v} />
          {workflow.data && (
            <div style={{ marginTop: 16 }}>
              <Typography.Title level={5}>Ký biên bản 08a</Typography.Title>
              <WorkflowCard wf={workflow.data} />
            </div>
          )}
          {requestWorkflow.data && (
            <div style={{ marginTop: 16 }}>
              <Typography.Title level={5}>Ký đề nghị thanh toán</Typography.Title>
              <WorkflowCard wf={requestWorkflow.data} />
            </div>
          )}
        </Col>
      </Row>
    </>
  );
}

/** Ủy nhiệm chi: số, ngày, số tiền, file — bổ sung được cả sau khi ký. */
function PaymentOrdersCard({ valuation: v }: { valuation: ValuationDto }) {
  const [open, setOpen] = useState(false);
  const [file, setFile] = useState<File | null>(null);
  const [form] = Form.useForm<{ number: string; date: dayjs.Dayjs; amount: number }>();
  const { message } = App.useApp();
  const onError = useApiError();
  const add = usePaymentAction((x: { number: string; date: string; amount: number }) =>
    valuationApi.addPaymentOrder(v.id, x, file),
  );
  const remove = usePaymentAction((orderId: string) => valuationApi.removePaymentOrder(v.id, orderId));
  const outstanding = Math.max(0, v.requestedAmount - v.paidAmount);

  return (
    <Card
      title="Ủy nhiệm chi"
      extra={
        v.canManagePaymentOrders &&
        outstanding > 0 && (
          <Button
            size="small"
            type="primary"
            icon={<PlusOutlined />}
            onClick={() => {
              form.setFieldsValue({ number: '', date: dayjs(), amount: outstanding });
              setFile(null);
              setOpen(true);
            }}
          >
            Thêm UNC
          </Button>
        )
      }
    >
      <List
        dataSource={v.paymentOrders}
        locale={{ emptyText: 'Chưa có ủy nhiệm chi' }}
        renderItem={(p) => (
          <List.Item
            actions={[
              p.hasFile && (
                <a
                  key="file"
                  onClick={() =>
                    valuationApi
                      .paymentOrderLink(v.id, p.id)
                      .then((l) => window.open(l.url, '_blank', 'noopener'), onError)
                  }
                >
                  <PaperClipOutlined /> File
                </a>
              ),
              v.canManagePaymentOrders && (
                <Popconfirm
                  key="del"
                  title="Xóa ủy nhiệm chi?"
                  onConfirm={() => remove.mutateAsync(p.id).then(() => message.success('Đã xóa'), onError)}
                >
                  <a>
                    <DeleteOutlined />
                  </a>
                </Popconfirm>
              ),
            ].filter(Boolean)}
          >
            <List.Item.Meta
              title={`${p.number} — ${formatMoney(p.amount)}`}
              description={`Ngày ${formatDate(p.date)} · ${p.addedByName ?? ''} ${formatDateTime(p.addedAt)}`}
            />
          </List.Item>
        )}
      />
      <Typography.Text type="secondary">
        Đã chuyển {formatMoney(v.paidAmount)} / đề nghị {formatMoney(v.requestedAmount)}
      </Typography.Text>
      <Modal
        title="Thêm ủy nhiệm chi"
        open={open}
        onCancel={() => setOpen(false)}
        okText="Lưu"
        okButtonProps={{ loading: add.isPending }}
        destroyOnHidden
        onOk={async () => {
          const x = await form.validateFields();
          try {
            await add.mutateAsync({ number: x.number, date: x.date.format('YYYY-MM-DD'), amount: x.amount });
            message.success('Đã thêm ủy nhiệm chi');
            setOpen(false);
          } catch (e) {
            onError(e);
          }
        }}
      >
        <Form form={form} layout="vertical">
          <Form.Item name="number" label="Số ủy nhiệm chi" rules={[{ required: true, whitespace: true }]}>
            <Input maxLength={50} />
          </Form.Item>
          <Form.Item name="date" label="Ngày" rules={[{ required: true }]}>
            <DatePicker format="DD/MM/YYYY" style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item
            name="amount"
            label={`Số tiền (còn lại ${formatMoney(outstanding)})`}
            rules={[{ required: true }]}
          >
            <MoneyInput style={{ width: '100%' }} />
          </Form.Item>
          <Upload
            accept=".pdf,.jpg,.jpeg,.png"
            maxCount={1}
            beforeUpload={(f) => {
              setFile(f);
              return false;
            }}
            onRemove={() => setFile(null)}
          >
            <Button icon={<PaperClipOutlined />}>Đính kèm bản scan UNC (tùy chọn)</Button>
          </Upload>
        </Form>
      </Modal>
    </Card>
  );
}
