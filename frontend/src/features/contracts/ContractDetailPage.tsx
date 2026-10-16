import {
  Alert,
  App,
  Button,
  Card,
  Descriptions,
  Empty,
  Form,
  Input,
  Modal,
  Popconfirm,
  Progress,
  Space,
  Table,
  Tabs,
  Typography,
} from 'antd';
import {
  EditOutlined,
  FileAddOutlined,
  FolderOpenOutlined,
  SendOutlined,
  StopOutlined,
} from '@ant-design/icons';
import { useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import {
  fetchDocumentFile,
  useDocument,
  useStartEditSession,
  type EditorMode,
  type EditorSessionDto,
} from '../../api/documents';
import {
  contractApi,
  useContract,
  useContractAction,
  type ContractAppendixDto,
  type ContractItemDto,
} from '../../api/procurement';
import { useDocumentWorkflow, useInvalidateWorkflow } from '../../api/signing';
import { useAuth } from '../../auth/useAuth';
import { DocEditor } from '../../components/DocEditor';
import { PdfViewer } from '../../components/PdfViewer';
import { StatusTag } from '../../components/StatusTag';
import { PageHeader, useApiError } from '../../components/common';
import { formatDate, formatDateTime, formatMoney, formatQuantity } from '../../utils/format';
import { WorkflowCard, WorkflowHistory } from '../signing/WorkflowPanel';
import { AppendixDialog } from './AppendixDialog';
import { AccountantReviewPanel, SupplierReviewPanel } from './ContractReviewPanels';

const pct = (part: number, whole: number) => (whole > 0 ? Math.round((part * 1000) / whole) / 10 : 0);

/** Current PDF of the contract dossier with a link to the full version history. */
function DocumentTab({ documentId, base }: { documentId: string; base: string }) {
  const doc = useDocument(documentId);
  const navigate = useNavigate();
  const current = doc.data?.versions.find((v) => v.isCurrent);
  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      <Space wrap>
        <Button icon={<FolderOpenOutlined />} onClick={() => navigate(`${base}/tai-lieu/${documentId}`)}>
          Mở hồ sơ tài liệu (phiên bản, tải DOCX/PDF, xác minh chữ ký)
        </Button>
        {current && (
          <Typography.Text type="secondary">
            Phiên bản hiện tại v{current.versionNumber} — {current.reason} (
            {formatDateTime(current.createdAt)})
          </Typography.Text>
        )}
      </Space>
      {current?.hasPdf ? (
        <PdfViewer
          load={() => fetchDocumentFile(documentId, current.id, 'pdf')}
          cacheKey={`${current.id}:${current.pdfSha256}`}
          height="70vh"
        />
      ) : (
        <Empty description="Phiên bản hiện tại chưa có bản PDF (được tạo khi chốt bản để ký)" />
      )}
    </Space>
  );
}

/** Chi tiết hợp đồng (spec §9.1-5): thông tin, theo dõi thực hiện, tài liệu, luồng ký, phụ lục, lịch sử. */
export function ContractDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') ?? 'info';
  const contract = useContract(id);
  const c = contract.data;
  const workflow = useDocumentWorkflow(c?.documentId ?? undefined);
  const navigate = useNavigate();
  const { isSupplier } = useAuth();
  const base = isSupplier ? '/ncc' : '';
  const { message } = App.useApp();
  const onError = useApiError();
  const invalidate = useInvalidateWorkflow();
  const startEdit = useStartEditSession(c?.documentId ?? '');
  const [session, setSession] = useState<EditorSessionDto | null>(null);
  const [cancelling, setCancelling] = useState(false);
  const [appendixOpen, setAppendixOpen] = useState(false);
  const [form] = Form.useForm<{ reason: string }>();
  const submit = useContractAction(() => contractApi.submit(id!));
  const cancel = useContractAction((reason: string) => contractApi.cancel(id!, reason));
  const submitAppendix = useContractAction((appendixId: string) => contractApi.submitAppendix(appendixId));
  const cancelAppendix = useContractAction((appendixId: string) => contractApi.cancelAppendix(appendixId));

  const openEditor = async (mode: EditorMode) => {
    try {
      setSession(await startEdit.mutateAsync(mode));
    } catch (e) {
      onError(e);
    }
  };

  const closeEditor = () => {
    setSession(null);
    message.info('Trình soạn thảo đang lưu phiên bản mới, danh sách thay đổi sẽ cập nhật sau vài giây.');
    setTimeout(() => void invalidate(), 6_000);
    setTimeout(() => void invalidate(), 15_000);
  };

  if (!c) return <Card loading={contract.isLoading} />;

  const a = c.actions;
  const totals = c.items.reduce(
    (s, i) => ({
      value: s.value + i.amount,
      received: s.received + i.receivedQuantity * i.unitPrice,
      paid: s.paid + i.paidQuantity * i.unitPrice,
    }),
    { value: 0, received: 0, paid: 0 },
  );

  return (
    <>
      <PageHeader
        title={`Hợp đồng ${c.number}`}
        subtitle={
          <Space wrap>
            <StatusTag domain="contractStatus" value={c.status} />
            <StatusTag domain="procurementType" value={c.procurementType} />
            <span>{c.name}</span>
          </Space>
        }
        extra={
          <Space wrap>
            {a.canEdit && (
              <Button icon={<EditOutlined />} onClick={() => navigate(`/hop-dong/${c.id}/sua`)}>
                Sửa
              </Button>
            )}
            {a.canEdit && c.documentId && (
              <Button
                icon={<EditOutlined />}
                loading={startEdit.isPending}
                onClick={() => openEditor('Edit')}
              >
                Sửa trực tiếp DOCX
              </Button>
            )}
            {a.canSubmit && (
              <Popconfirm
                title={c.status === 'Returned' ? 'Trình lại hợp đồng?' : 'Gửi Tổ rà soát kiểm tra?'}
                onConfirm={() => submit.mutateAsync().then(() => message.success('Đã gửi rà soát'), onError)}
              >
                <Button type="primary" icon={<SendOutlined />} loading={submit.isPending}>
                  {c.status === 'Returned' ? 'Trình lại' : 'Gửi rà soát'}
                </Button>
              </Popconfirm>
            )}
            {a.canCreateAppendix && (
              <Button icon={<FileAddOutlined />} onClick={() => setAppendixOpen(true)}>
                Tạo phụ lục
              </Button>
            )}
            {a.canCancel && (
              <Button danger icon={<StopOutlined />} onClick={() => setCancelling(true)}>
                Hủy hợp đồng
              </Button>
            )}
          </Space>
        }
      />

      {c.status === 'Returned' && c.lastReturnReason && (
        <Alert
          type="error"
          showIcon
          style={{ marginBottom: 16 }}
          message="Hợp đồng bị trả lại"
          description={c.lastReturnReason}
        />
      )}
      {a.supplierReviewStepId && (
        <SupplierReviewPanel contract={c} onOpenEditor={() => openEditor('Review')} />
      )}
      {a.accountantReviewStepId && (
        <AccountantReviewPanel contract={c} onOpenEditor={() => openEditor('Edit')} />
      )}

      <Card>
        <Tabs
          activeKey={tab}
          onChange={(k) => setParams({ tab: k })}
          items={[
            {
              key: 'info',
              label: 'Thông tin',
              children: (
                <Descriptions bordered size="small" column={{ xs: 1, md: 2 }}>
                  <Descriptions.Item label="Nhà cung cấp">
                    {c.supplierName} (MST {c.supplierTaxCode})
                  </Descriptions.Item>
                  <Descriptions.Item label="Quyết định thầu">
                    {c.bidDecisionNumber && !isSupplier ? (
                      <a onClick={() => navigate(`/quyet-dinh-thau/${c.bidDecisionId}`)}>
                        {c.bidDecisionNumber}
                      </a>
                    ) : (
                      (c.bidDecisionNumber ?? 'Ngoài thầu')
                    )}
                  </Descriptions.Item>
                  <Descriptions.Item label="Hạng mục">{c.categoryName}</Descriptions.Item>
                  <Descriptions.Item label="Loại hợp đồng">
                    <StatusTag domain="contractKind" value={c.kind} />{' '}
                    {c.requiresWarehouseReceipt ? '' : '(không nhập kho)'}
                  </Descriptions.Item>
                  <Descriptions.Item label="Cơ sở">{c.facilityName ?? 'Toàn bệnh viện'}</Descriptions.Item>
                  <Descriptions.Item label="Thời gian thực hiện">{c.duration ?? '—'}</Descriptions.Item>
                  <Descriptions.Item label="Ngày ký">{formatDate(c.signedDate) || '—'}</Descriptions.Item>
                  <Descriptions.Item label="Hiệu lực">
                    {formatDate(c.effectiveDate) || '—'} – {formatDate(c.expiryDate) || '—'}
                  </Descriptions.Item>
                  <Descriptions.Item label="Giá trị trước thuế">
                    {formatMoney(c.amountBeforeVat)}
                  </Descriptions.Item>
                  <Descriptions.Item label="Thuế GTGT">{formatMoney(c.vatAmount)}</Descriptions.Item>
                  <Descriptions.Item label="Tổng giá trị" span={2}>
                    <Typography.Text strong>{formatMoney(c.totalAmount)}</Typography.Text>
                  </Descriptions.Item>
                  <Descriptions.Item label="Thanh toán" span={2}>
                    {c.paymentTerms ?? '—'}
                  </Descriptions.Item>
                  <Descriptions.Item label="Giao hàng" span={2}>
                    {c.deliveryTerms ?? '—'}
                  </Descriptions.Item>
                  <Descriptions.Item label="Bảo hành" span={2}>
                    {c.warrantyTerms ?? '—'}
                  </Descriptions.Item>
                  <Descriptions.Item label="Bảo lãnh thực hiện" span={2}>
                    {c.guaranteeNumber || c.guaranteeAmount
                      ? `${c.guaranteeNumber ?? ''} ${c.guaranteeBank ? `— ${c.guaranteeBank}` : ''} ${c.guaranteeAmount ? `— ${formatMoney(c.guaranteeAmount)}` : ''} ${c.guaranteeExpiry ? `— đến ${formatDate(c.guaranteeExpiry)}` : ''}`
                      : '—'}
                  </Descriptions.Item>
                  <Descriptions.Item label="Mẫu hợp đồng">{c.templateName ?? '—'}</Descriptions.Item>
                  <Descriptions.Item label="Có hiệu lực lúc">
                    {formatDateTime(c.effectiveAt) || '—'}
                  </Descriptions.Item>
                  {c.note && (
                    <Descriptions.Item label="Ghi chú" span={2}>
                      {c.note}
                    </Descriptions.Item>
                  )}
                </Descriptions>
              ),
            },
            {
              key: 'items',
              label: `Hàng hóa & theo dõi thực hiện (${c.items.length})`,
              children: (
                <>
                  <Space size="large" wrap style={{ marginBottom: 12 }}>
                    <span>
                      Đã nhập kho:{' '}
                      <Progress type="circle" size={44} percent={pct(totals.received, totals.value)} />
                    </span>
                    <span>
                      Đã thanh toán:{' '}
                      <Progress type="circle" size={44} percent={pct(totals.paid, totals.value)} />
                    </span>
                  </Space>
                  <Table<ContractItemDto>
                    rowKey="id"
                    size="small"
                    pagination={false}
                    dataSource={c.items}
                    scroll={{ x: 'max-content' }}
                    summary={() => (
                      <Table.Summary.Row>
                        <Table.Summary.Cell index={0} colSpan={5}>
                          <Typography.Text strong>Tổng cộng</Typography.Text>
                        </Table.Summary.Cell>
                        <Table.Summary.Cell index={1} align="right">
                          <Typography.Text strong>{formatMoney(c.totalAmount)}</Typography.Text>
                        </Table.Summary.Cell>
                        <Table.Summary.Cell index={2} colSpan={8} />
                      </Table.Summary.Row>
                    )}
                    columns={[
                      { title: 'Mã', dataIndex: 'itemCode', width: 90, fixed: 'left' },
                      { title: 'Tên hàng', dataIndex: 'itemName', fixed: 'left' },
                      { title: 'ĐVT', dataIndex: 'unit', width: 60 },
                      {
                        title: 'SL HĐ',
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
                      {
                        title: 'Đã đề xuất',
                        dataIndex: 'proposedQuantity',
                        align: 'right',
                        render: (v: number) => formatQuantity(v),
                      },
                      {
                        title: 'Đã duyệt',
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
                        title: 'Đã nhập kho',
                        dataIndex: 'receivedQuantity',
                        align: 'right',
                        render: (v: number) => formatQuantity(v),
                      },
                      {
                        title: 'Đã nghiệm thu',
                        dataIndex: 'acceptedQuantity',
                        align: 'right',
                        render: (v: number) => formatQuantity(v),
                      },
                      {
                        title: 'Đã thanh toán',
                        dataIndex: 'paidQuantity',
                        align: 'right',
                        render: (v: number) => formatQuantity(v),
                      },
                      {
                        title: 'Còn lại',
                        dataIndex: 'remainingQuantity',
                        align: 'right',
                        render: (v: number, r) => (
                          <Space direction="vertical" size={0} style={{ minWidth: 110 }}>
                            <Typography.Text strong>{formatQuantity(v)}</Typography.Text>
                            <Progress
                              percent={pct(r.receivedQuantity, r.quantity)}
                              size="small"
                              showInfo={false}
                            />
                          </Space>
                        ),
                      },
                    ]}
                  />
                </>
              ),
            },
            {
              key: 'document',
              label: 'Tài liệu & phiên bản',
              children: c.documentId ? <DocumentTab documentId={c.documentId} base={base} /> : <Empty />,
            },
            {
              key: 'workflow',
              label: 'Luồng ký',
              children: workflow.data ? (
                <WorkflowCard wf={workflow.data} />
              ) : (
                <Empty
                  description={c.status === 'Draft' ? 'Hợp đồng chưa gửi rà soát' : 'Chưa có luồng ký'}
                />
              ),
            },
            {
              key: 'appendices',
              label: `Phụ lục (${c.appendices.length})`,
              children: (
                <Table<ContractAppendixDto>
                  rowKey="id"
                  size="middle"
                  pagination={false}
                  dataSource={c.appendices}
                  locale={{ emptyText: <Empty description="Chưa có phụ lục" /> }}
                  expandable={{
                    rowExpandable: (r) => r.lines.length > 0,
                    expandedRowRender: (r) => (
                      <Table
                        rowKey="contractItemId"
                        size="small"
                        pagination={false}
                        dataSource={r.lines}
                        columns={[
                          { title: 'Mặt hàng', dataIndex: 'itemName' },
                          {
                            title: 'Số lượng',
                            key: 'qty',
                            render: (_: unknown, l: ContractAppendixDto['lines'][number]) =>
                              l.newQuantity != null
                                ? `${formatQuantity(l.oldQuantity)} → ${formatQuantity(l.newQuantity)}`
                                : formatQuantity(l.oldQuantity),
                          },
                          {
                            title: 'Đơn giá',
                            key: 'price',
                            render: (_: unknown, l: ContractAppendixDto['lines'][number]) =>
                              l.newUnitPrice != null
                                ? `${formatMoney(l.oldUnitPrice)} → ${formatMoney(l.newUnitPrice)}`
                                : formatMoney(l.oldUnitPrice),
                          },
                          {
                            title: 'Giá trị tăng/giảm',
                            dataIndex: 'valueChange',
                            align: 'right' as const,
                            render: (v: number) => formatMoney(v),
                          },
                        ]}
                      />
                    ),
                  }}
                  columns={[
                    { title: 'Số', dataIndex: 'number', width: 70 },
                    {
                      title: 'Loại',
                      dataIndex: 'type',
                      render: (v: string) => <StatusTag domain="appendixType" value={v} />,
                    },
                    { title: 'Lý do', dataIndex: 'reason' },
                    {
                      title: 'Gia hạn',
                      key: 'expiry',
                      render: (_, r) =>
                        r.newExpiryDate
                          ? `${formatDate(r.oldExpiryDate)} → ${formatDate(r.newExpiryDate)}`
                          : '—',
                    },
                    {
                      title: 'Giá trị tăng/giảm',
                      dataIndex: 'valueChange',
                      align: 'right',
                      render: (v: number) => formatMoney(v),
                    },
                    {
                      title: 'Trạng thái',
                      dataIndex: 'status',
                      render: (v: string) => <StatusTag domain="appendixStatus" value={v} />,
                    },
                    {
                      title: '',
                      key: 'actions',
                      render: (_, r) => (
                        <Space wrap>
                          {r.documentId && (
                            <Button size="small" onClick={() => navigate(`${base}/tai-lieu/${r.documentId}`)}>
                              Tài liệu
                            </Button>
                          )}
                          {r.status === 'Draft' && !isSupplier && (
                            <>
                              <Button
                                size="small"
                                type="primary"
                                loading={submitAppendix.isPending && submitAppendix.variables === r.id}
                                onClick={() =>
                                  submitAppendix
                                    .mutateAsync(r.id)
                                    .then(() => message.success('Đã gửi trình ký phụ lục'), onError)
                                }
                              >
                                Gửi trình ký
                              </Button>
                              <Popconfirm
                                title="Hủy phụ lục?"
                                onConfirm={() =>
                                  cancelAppendix
                                    .mutateAsync(r.id)
                                    .then(() => message.success('Đã hủy phụ lục'), onError)
                                }
                              >
                                <Button size="small" danger>
                                  Hủy
                                </Button>
                              </Popconfirm>
                            </>
                          )}
                        </Space>
                      ),
                    },
                  ]}
                />
              ),
            },
            {
              key: 'history',
              label: 'Lịch sử',
              children: workflow.data ? (
                <WorkflowHistory wf={workflow.data} />
              ) : (
                <Empty description="Chưa có lịch sử xử lý" />
              ),
            },
          ]}
        />
      </Card>

      <Modal
        title="Hủy hợp đồng"
        open={cancelling}
        onCancel={() => setCancelling(false)}
        okText="Hủy hợp đồng"
        okButtonProps={{ danger: true, loading: cancel.isPending }}
        onOk={async () => {
          const v = await form.validateFields();
          try {
            await cancel.mutateAsync(v.reason);
            message.success('Đã hủy hợp đồng');
            setCancelling(false);
          } catch (e) {
            onError(e);
          }
        }}
        destroyOnHidden
      >
        <Form form={form} layout="vertical">
          <Form.Item name="reason" label="Lý do hủy" rules={[{ required: true, whitespace: true }]}>
            <Input.TextArea rows={3} />
          </Form.Item>
        </Form>
      </Modal>

      <AppendixDialog contract={c} open={appendixOpen} onClose={() => setAppendixOpen(false)} />

      <Modal
        title={`Soạn thảo — Hợp đồng ${c.number}`}
        open={!!session}
        onCancel={closeEditor}
        footer={
          <Button type="primary" onClick={closeEditor}>
            Đóng và lưu
          </Button>
        }
        width="96vw"
        style={{ top: 12 }}
        destroyOnHidden
      >
        {session && <DocEditor session={session} />}
      </Modal>
    </>
  );
}
