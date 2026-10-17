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
  Table,
  Tabs,
  Tag,
  Typography,
  Upload,
} from 'antd';
import {
  CheckCircleOutlined,
  EditOutlined,
  FileTextOutlined,
  ReloadOutlined,
  SendOutlined,
  StopOutlined,
  UploadOutlined,
} from '@ant-design/icons';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import dayjs from '../../i18n/dayjs';
import {
  receiptApi,
  useReceipt,
  useReceiptAction,
  type InvoiceRequest,
  type ReceiptDto,
  type ReceiptLineDto,
} from '../../api/receipts';
import { useDocumentWorkflow } from '../../api/signing';
import { useAuth } from '../../auth/useAuth';
import { StatusTag } from '../../components/StatusTag';
import { useApiError } from '../../components/common';
import { DocHeader } from '../../components/layout';
import { MoneyInput } from '../../components/inputs';
import { formatDate, formatDateTime, formatMoney, formatQuantity } from '../../utils/format';
import { WorkflowCard } from '../signing/WorkflowPanel';
import { DocumentPdf } from '../proposals/ProposalDetailPage';
import { ExportableTable } from '../../components/TableExportButton';
import { DetailState } from '../../components/DetailState';

const TITLE: Record<string, string> = { Receipt: 'Phiếu nhập kho', Return: 'Phiếu hoàn trả' };

/** Chi tiết phiếu nhập kho: cảnh báo của validation engine, hóa đơn, chi tiết hàng, PDF và luồng 5 chữ ký. */
export function ReceiptDetailPage() {
  const { id } = useParams<{ id: string }>();
  const receipt = useReceipt(id);
  const r = receipt.data;
  const workflow = useDocumentWorkflow(r?.documentId ?? undefined);
  const navigate = useNavigate();
  const { isSupplier } = useAuth();
  const { message } = App.useApp();
  const onError = useApiError();
  const submit = useReceiptAction(() => receiptApi.submit(id!));
  const revalidate = useReceiptAction(() => receiptApi.revalidate(id!));
  const cancel = useReceiptAction(() => receiptApi.cancel(id!));

  if (!r) return <DetailState query={receipt} />;
  const title = r.source === 'VirtualPortal' ? 'Phiếu xác nhận khối lượng' : TITLE[r.type];
  const lineIssues = new Map<number, string[]>();
  r.issues
    .filter((i) => i.lineNo)
    .forEach((i) => lineIssues.set(i.lineNo!, [...(lineIssues.get(i.lineNo!) ?? []), i.message]));

  return (
    <>
      <DocHeader
        back={{ to: -1, label: 'Quay lại' }}
        plain
        code={
          <>
            {title} <span className="mono">{r.number}</span>
          </>
        }
        tags={
          <Space size={4} wrap>
            <StatusTag domain="receiptType" value={r.type} />
            <StatusTag domain="receiptSource" value={r.source} />
            <StatusTag domain="receiptStatus" value={r.status} />
            <StatusTag domain="receiptValidation" value={r.validationStatus} />
            {r.isIncludedInAcceptance && <Tag color="green">Đã đưa vào nghiệm thu</Tag>}
          </Space>
        }
        actions={
          <>
            {r.canEdit && (
              <Button icon={<EditOutlined />} onClick={() => navigate(`/nhap-kho/${r.id}/sua`)}>
                Sửa phiếu
              </Button>
            )}
            {r.canRevalidate && (
              <Button
                icon={<ReloadOutlined />}
                loading={revalidate.isPending}
                onClick={() =>
                  revalidate.mutateAsync().then(() => message.success('Đã kiểm tra lại phiếu'), onError)
                }
              >
                Kiểm tra lại
              </Button>
            )}
            {r.canSubmit && (
              <Button
                type="primary"
                icon={r.source === 'VirtualPortal' ? <CheckCircleOutlined /> : <SendOutlined />}
                loading={submit.isPending}
                onClick={() =>
                  submit
                    .mutateAsync()
                    .then(
                      () =>
                        message.success(
                          r.source === 'VirtualPortal'
                            ? 'Đã xác nhận khối lượng'
                            : 'Đã gửi ký — thủ kho ký đầu tiên',
                        ),
                      onError,
                    )
                }
              >
                {r.source === 'VirtualPortal'
                  ? 'Xác nhận khối lượng'
                  : r.status === 'Returned'
                    ? 'Gửi ký lại'
                    : 'Gửi ký'}
              </Button>
            )}
            {r.canCancel && (
              <Popconfirm
                title="Hủy phiếu?"
                description="Số lượng của phiếu không còn được tính vào hợp đồng."
                onConfirm={() => cancel.mutateAsync().then(() => message.success('Đã hủy phiếu'), onError)}
              >
                <Button danger icon={<StopOutlined />}>
                  Hủy phiếu
                </Button>
              </Popconfirm>
            )}
          </>
        }
      />

      {r.status === 'Returned' && r.lastReturnReason && (
        <Alert
          type="error"
          showIcon
          style={{ marginBottom: 12 }}
          message="Phiếu bị trả lại"
          description={`${r.lastReturnReason}${r.source === 'His' ? ' — chờ HIS gửi lại phiếu đã sửa, hoặc bổ sung rồi bấm "Gửi ký lại".' : ''}`}
        />
      )}
      {r.issues.length > 0 && (
        <Alert
          type={r.validationStatus === 'Invalid' ? 'error' : 'warning'}
          showIcon
          style={{ marginBottom: 12 }}
          message={
            r.validationStatus === 'Invalid'
              ? 'Phiếu không hợp lệ — không thể ký cho đến khi được sửa'
              : 'Phiếu có cảnh báo — người ký phải nhập lý do chấp nhận'
          }
          description={
            <List
              size="small"
              dataSource={r.issues}
              renderItem={(i) => (
                <List.Item style={{ padding: '2px 0' }}>
                  <Space align="start">
                    <StatusTag domain="receiptValidation" value={i.severity} />
                    <span>{i.message}</span>
                  </Space>
                </List.Item>
              )}
            />
          }
        />
      )}

      <Row gutter={[16, 16]}>
        <Col xs={24} xl={workflow.data ? 16 : 24}>
          <Card style={{ marginBottom: 16 }}>
            <Descriptions size="small" column={{ xs: 1, md: 2, xxl: 3 }}>
              <Descriptions.Item label="Ngày phiếu">{formatDate(r.receiptDate)}</Descriptions.Item>
              <Descriptions.Item label="Kho">{r.warehouseName}</Descriptions.Item>
              <Descriptions.Item label="Mã phiếu HIS">{r.hisReceiptId ?? '—'}</Descriptions.Item>
              <Descriptions.Item label="Nhà cung cấp">
                {r.supplierName} (MST {r.supplierTaxCode})
              </Descriptions.Item>
              <Descriptions.Item label="Số QĐ thầu">{r.bidDecisionNumber ?? '—'}</Descriptions.Item>
              <Descriptions.Item label="Hợp đồng">
                {r.contractId ? (
                  <a onClick={() => navigate(`${isSupplier ? '/ncc' : ''}/hop-dong/${r.contractId}`)}>
                    {r.contractNumber}
                  </a>
                ) : (
                  <Typography.Text type="danger">Chưa xác định</Typography.Text>
                )}
              </Descriptions.Item>
              <Descriptions.Item label="Đơn gọi hàng">
                {r.callOrderId ? (
                  <a onClick={() => navigate(`${isSupplier ? '/ncc' : ''}/goi-hang/${r.callOrderId}`)}>
                    {r.callOrderNumber}
                  </a>
                ) : (
                  '—'
                )}
              </Descriptions.Item>
              <Descriptions.Item label="Người giao hàng">{r.delivererName ?? '—'}</Descriptions.Item>
              <Descriptions.Item label="Hoàn tất">{formatDateTime(r.completedAt) || '—'}</Descriptions.Item>
              <Descriptions.Item label="Ghi chú">{r.note ?? '—'}</Descriptions.Item>
            </Descriptions>
          </Card>
          <Card>
            <Tabs
              items={[
                {
                  key: 'lines',
                  label: `Chi tiết hàng (${r.lines.length})`,
                  children: (
                    <ExportableTable<ReceiptLineDto>
                      exportTitle="Mặt hàng phiếu nhập kho"
                      rowKey="id"
                      size="small"
                      pagination={false}
                      dataSource={r.lines}
                      scroll={{ x: 'max-content' }}
                      onRow={(l) => ({
                        style: lineIssues.has(l.lineNo) ? { background: '#fffbe6' } : undefined,
                      })}
                      summary={() => (
                        <Table.Summary.Row>
                          <Table.Summary.Cell index={0} colSpan={8}>
                            <Typography.Text strong>
                              Tổng cộng (đã gồm VAT {formatMoney(r.vatAmount)})
                            </Typography.Text>
                          </Table.Summary.Cell>
                          <Table.Summary.Cell index={1} align="right">
                            <Typography.Text strong>{formatMoney(r.totalAmount)}</Typography.Text>
                          </Table.Summary.Cell>
                          <Table.Summary.Cell index={2} />
                        </Table.Summary.Row>
                      )}
                      columns={[
                        { title: 'STT', dataIndex: 'lineNo', width: 50, align: 'center' },
                        { title: 'Mã hàng', dataIndex: 'itemCode' },
                        {
                          title: 'Tên hàng',
                          dataIndex: 'itemName',
                          render: (v: string, l) => (
                            <Space direction="vertical" size={0}>
                              <span>{v}</span>
                              {lineIssues.get(l.lineNo)?.map((m) => (
                                <Typography.Text key={m} type="warning" style={{ fontSize: 12 }}>
                                  {m}
                                </Typography.Text>
                              ))}
                            </Space>
                          ),
                        },
                        { title: 'ĐVT', dataIndex: 'unit' },
                        { title: 'Số lô', dataIndex: 'lotNumber' },
                        {
                          title: 'Hạn dùng',
                          dataIndex: 'expiryDate',
                          render: (v?: string | null) => formatDate(v),
                        },
                        {
                          title: 'Số lượng',
                          dataIndex: 'quantity',
                          align: 'right',
                          render: (v: number) => <b>{formatQuantity(v)}</b>,
                        },
                        {
                          title: 'Đơn giá (HĐ)',
                          key: 'price',
                          align: 'right',
                          render: (_, l) => (
                            <Space direction="vertical" size={0} style={{ alignItems: 'flex-end' }}>
                              <span>{formatMoney(l.unitPrice)}</span>
                              {l.contractUnitPrice != null && l.contractUnitPrice !== l.unitPrice && (
                                <Typography.Text type="danger" style={{ fontSize: 12 }}>
                                  HĐ: {formatMoney(l.contractUnitPrice)}
                                </Typography.Text>
                              )}
                            </Space>
                          ),
                        },
                        {
                          title: 'Thành tiền',
                          dataIndex: 'amount',
                          align: 'right',
                          render: (v: number) => formatMoney(v),
                        },
                        {
                          title: 'Còn lại HĐ',
                          dataIndex: 'contractRemaining',
                          align: 'right',
                          render: (v?: number | null) => (v == null ? '—' : formatQuantity(v)),
                        },
                      ]}
                    />
                  ),
                },
                {
                  key: 'pdf',
                  label: `${title} (PDF)`,
                  children: r.documentId ? (
                    <DocumentPdf documentId={r.documentId} />
                  ) : (
                    <Empty description="Chưa sinh phiếu (chưa xác định nhà cung cấp)" />
                  ),
                },
              ]}
            />
          </Card>
        </Col>
        <Col xs={24} xl={workflow.data ? 8 : 24}>
          <InvoiceCard receipt={r} />
          {workflow.data && (
            <div style={{ marginTop: 16 }}>
              <WorkflowCard wf={workflow.data} />
            </div>
          )}
        </Col>
      </Row>
    </>
  );
}

/** Hóa đơn: NCC tải XML hóa đơn điện tử (tự đọc), PDF bản thể hiện, hoặc nhập tay. */
function InvoiceCard({ receipt: r }: { receipt: ReceiptDto }) {
  const [editing, setEditing] = useState(false);
  const [form] = Form.useForm<Omit<InvoiceRequest, 'date'> & { date: dayjs.Dayjs }>();
  const { message } = App.useApp();
  const onError = useApiError();
  const uploadFile = useReceiptAction((file: File) => receiptApi.uploadInvoiceFile(r.id, file));
  const save = useReceiptAction((body: InvoiceRequest) => receiptApi.setInvoice(r.id, body));
  const i = r.invoice;
  const open = (format: 'xml' | 'pdf') =>
    receiptApi.invoiceLink(r.id, format).then((link) => window.open(link.url, '_blank', 'noopener'), onError);

  return (
    <Card
      title={
        <Space>
          <FileTextOutlined />
          Hóa đơn
          {!i.number && <Tag color="gold">Chưa có</Tag>}
        </Space>
      }
      extra={
        r.canEditInvoice && (
          <Space>
            <Upload
              accept=".xml,.pdf"
              showUploadList={false}
              customRequest={({ file }) =>
                uploadFile
                  .mutateAsync(file as File)
                  .then(
                    (updated) =>
                      message.success(
                        (file as File).name.toLowerCase().endsWith('.xml')
                          ? `Đã đọc hóa đơn ${updated.invoice.symbol} số ${updated.invoice.number}`
                          : 'Đã đính kèm PDF hóa đơn',
                      ),
                    onError,
                  )
              }
            >
              <Button type="primary" size="small" icon={<UploadOutlined />} loading={uploadFile.isPending}>
                Tải XML/PDF
              </Button>
            </Upload>
            <Button
              size="small"
              onClick={() => {
                form.setFieldsValue({
                  symbol: i.symbol ?? '',
                  number: i.number ?? '',
                  date: i.date ? dayjs(i.date) : dayjs(),
                  amountBeforeVat: i.amountBeforeVat ?? null,
                  vatAmount: i.vatAmount ?? null,
                  totalAmount: i.totalAmount ?? r.totalAmount,
                  sellerTaxCode: i.sellerTaxCode ?? r.supplierTaxCode,
                });
                setEditing(true);
              }}
            >
              Nhập tay
            </Button>
          </Space>
        )
      }
    >
      {i.number ? (
        <Descriptions size="small" column={1}>
          <Descriptions.Item label="Ký hiệu">{i.symbol}</Descriptions.Item>
          <Descriptions.Item label="Số">{i.number}</Descriptions.Item>
          <Descriptions.Item label="Ngày">{formatDate(i.date)}</Descriptions.Item>
          <Descriptions.Item label="Tiền hàng">
            {i.amountBeforeVat != null ? formatMoney(i.amountBeforeVat) : '—'}
          </Descriptions.Item>
          <Descriptions.Item label="Tiền thuế">
            {i.vatAmount != null ? formatMoney(i.vatAmount) : '—'}
          </Descriptions.Item>
          <Descriptions.Item label="Tổng thanh toán">
            <b>{formatMoney(i.totalAmount)}</b>
          </Descriptions.Item>
          <Descriptions.Item label="MST người bán">{i.sellerTaxCode ?? '—'}</Descriptions.Item>
          <Descriptions.Item label="Cập nhật">
            {i.updatedByName} {formatDateTime(i.updatedAt)}
          </Descriptions.Item>
          <Descriptions.Item label="Tệp">
            <Space>
              {i.hasXml && <a onClick={() => open('xml')}>XML hóa đơn điện tử</a>}
              {i.hasPdf && <a onClick={() => open('pdf')}>PDF bản thể hiện</a>}
              {!i.hasXml && !i.hasPdf && '—'}
            </Space>
          </Descriptions.Item>
        </Descriptions>
      ) : (
        <Typography.Text type="secondary">
          Bắt buộc có thông tin hóa đơn trước khi người giao hàng (NCC) ký. Tải file XML hóa đơn điện tử để tự
          điền ký hiệu, số, ngày, tổng tiền.
        </Typography.Text>
      )}
      <Modal
        title="Thông tin hóa đơn"
        open={editing}
        onCancel={() => setEditing(false)}
        okText="Lưu hóa đơn"
        okButtonProps={{ loading: save.isPending }}
        destroyOnHidden
        onOk={async () => {
          const v = await form.validateFields();
          try {
            await save.mutateAsync({ ...v, date: v.date.format('YYYY-MM-DD') });
            message.success('Đã lưu thông tin hóa đơn');
            setEditing(false);
          } catch (e) {
            onError(e);
          }
        }}
      >
        <Form form={form} layout="vertical">
          <Row gutter={12}>
            <Col span={12}>
              <Form.Item
                name="symbol"
                label="Ký hiệu (VD: 1C26TAA)"
                rules={[{ required: true, whitespace: true }]}
              >
                <Input maxLength={20} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="number" label="Số hóa đơn" rules={[{ required: true, whitespace: true }]}>
                <Input maxLength={20} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="date" label="Ngày hóa đơn" rules={[{ required: true }]}>
                <DatePicker format="DD/MM/YYYY" style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item name="sellerTaxCode" label="MST người bán">
                <Input maxLength={20} />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="amountBeforeVat" label="Tiền hàng">
                <MoneyInput style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="vatAmount" label="Tiền thuế">
                <MoneyInput style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col span={8}>
              <Form.Item name="totalAmount" label="Tổng thanh toán" rules={[{ required: true }]}>
                <MoneyInput style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          </Row>
        </Form>
      </Modal>
    </Card>
  );
}
