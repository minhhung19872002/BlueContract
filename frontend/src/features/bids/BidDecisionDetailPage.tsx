import {
  Alert,
  App,
  Button,
  Card,
  Col,
  Form,
  Input,
  Modal,
  Popconfirm,
  Row,
  Select,
  Space,
  Table,
  Tabs,
  Typography,
  Upload,
} from 'antd';
import {
  CheckCircleOutlined,
  CloudSyncOutlined,
  DeleteOutlined,
  EditOutlined,
  FileAddOutlined,
  FilePdfOutlined,
  FileExcelOutlined,
  PlusOutlined,
  StopOutlined,
  UploadOutlined,
} from '@ant-design/icons';
import { useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router-dom';
import {
  bidApi,
  useBidAction,
  useBidDecision,
  useBidItems,
  type BidItemDto,
  type BidItemRequest,
  type BidSupplierDto,
} from '../../api/procurement';
import { useAuth } from '../../auth/useAuth';
import { P } from '../../auth/permissions';
import { PdfViewer } from '../../components/PdfViewer';
import { StatusTag } from '../../components/StatusTag';
import { useApiError } from '../../components/common';
import { DocHeader, DocKpis, InfoGrid, Panel } from '../../components/layout';
import { MoneyInput, QuantityInput, SupplierSelect } from '../../components/inputs';
import { formatDate, formatMoney, formatQuantity } from '../../utils/format';
import { BidForm } from './BidForm';
import { fromBid, toBidRequest, type BidFormValues } from './bidFormValues';
import { ImportItemsDialog } from './ImportItemsDialog';

/** Chi tiết quyết định thầu: thông tin, NCC trúng thầu, danh mục hàng (HIS / Excel / nhập tay), các hợp đồng đã tạo. */
export function BidDecisionDetailPage() {
  const { id } = useParams<{ id: string }>();
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') ?? 'suppliers';
  const supplierFilter = params.get('supplier') ?? undefined;
  const bid = useBidDecision(id);
  const items = useBidItems(id, supplierFilter);
  const navigate = useNavigate();
  const { can } = useAuth();
  const { message, modal } = App.useApp();
  const onError = useApiError();
  const [editing, setEditing] = useState(false);
  const [importing, setImporting] = useState(false);
  const [addingItem, setAddingItem] = useState(false);
  const [viewPdf, setViewPdf] = useState(false);
  const [supplierForm] = Form.useForm<{ supplierId: string; winningValue: number }>();
  const [bidForm] = Form.useForm<BidFormValues>();
  const [itemForm] = Form.useForm<BidItemRequest>();
  const b = bid.data;
  const canManage = can(P.BidManage) && b?.status !== 'Closed';

  const run =
    <T,>(fn: () => Promise<T>, success: (r: T) => string) =>
    async () => {
      try {
        const r = await fn();
        message.success(success(r));
      } catch (e) {
        onError(e);
      }
    };

  const update = useBidAction((v: BidFormValues) => bidApi.update(id!, toBidRequest(v)));
  const sync = useBidAction((supplierId?: string) => bidApi.syncHis(id!, supplierId));
  const upsertSupplier = useBidAction((v: { supplierId: string; winningValue: number }) =>
    bidApi.upsertSupplier(id!, v.supplierId, v.winningValue),
  );
  const removeSupplier = useBidAction((supplierId: string) => bidApi.removeSupplier(id!, supplierId));
  const addItem = useBidAction((v: BidItemRequest) => bidApi.addItem(id!, v));
  const removeItem = useBidAction((itemId: string) => bidApi.removeItem(id!, itemId));
  const attach = useBidAction((file: File) => bidApi.attach(id!, file));
  const activate = useBidAction(() => bidApi.activate(id!));
  const close = useBidAction(() => bidApi.close(id!));

  const syncHis = async (supplierId?: string) => {
    try {
      const r = await sync.mutateAsync(supplierId);
      modal.success({
        title: `Đồng bộ từ ${b?.hisName ?? 'HIS'}`,
        content: (
          <Space direction="vertical">
            <span>
              Thêm mới {r.created}, cập nhật {r.updated}, không đổi {r.unchanged} mặt hàng.
            </span>
            {r.messages.map((m, i) => (
              <Typography.Text key={i} type="secondary">
                {m}
              </Typography.Text>
            ))}
          </Space>
        ),
      });
    } catch (e) {
      onError(e);
    }
  };

  const supplierColumns = [
    { title: 'MST', dataIndex: 'taxCode', width: 130 },
    { title: 'Nhà cung cấp', dataIndex: 'name' },
    {
      title: 'Giá trúng thầu',
      dataIndex: 'winningValue',
      align: 'right' as const,
      render: (v: number) => formatMoney(v),
    },
    { title: 'Mặt hàng', dataIndex: 'itemCount', width: 90, align: 'center' as const },
    {
      title: 'Giá trị danh mục',
      dataIndex: 'itemsValue',
      align: 'right' as const,
      render: (v: number) => formatMoney(v),
    },
    {
      title: 'Đã ký hợp đồng',
      dataIndex: 'contractedValue',
      align: 'right' as const,
      render: (v: number) => formatMoney(v),
    },
    {
      title: '',
      key: 'actions',
      render: (_: unknown, s: BidSupplierDto) => (
        <Space wrap>
          <Button size="small" onClick={() => setParams({ tab: 'items', supplier: s.supplierId })}>
            Danh mục
          </Button>
          {canManage && (
            <Button
              size="small"
              icon={<CloudSyncOutlined />}
              loading={sync.isPending && sync.variables === s.supplierId}
              onClick={() => syncHis(s.supplierId)}
            >
              Đồng bộ HIS
            </Button>
          )}
          {b?.status === 'Active' && can(P.ContractCreate) && (
            <Button
              size="small"
              type="primary"
              icon={<FileAddOutlined />}
              onClick={() => navigate(`/hop-dong/tao-moi?bid=${id}&supplier=${s.supplierId}`)}
            >
              Tạo hợp đồng
            </Button>
          )}
          {canManage && (
            <Popconfirm
              title="Xóa NCC khỏi quyết định?"
              onConfirm={run(
                () => removeSupplier.mutateAsync(s.supplierId),
                () => 'Đã xóa NCC',
              )}
            >
              <Button size="small" danger icon={<DeleteOutlined />} />
            </Popconfirm>
          )}
        </Space>
      ),
    },
  ];

  return (
    <>
      <DocHeader
        back={{ to: '/quyet-dinh-thau', label: 'Quyết định thầu' }}
        plain
        code={
          b ? (
            <>
              Quyết định <span className="mono">{b.number}</span>
            </>
          ) : (
            'Quyết định thầu'
          )
        }
        tags={b ? <StatusTag domain="bidStatus" value={b.status} /> : undefined}
        title={b?.packageName}
        sub={
          b
            ? [
                `Ký ngày ${formatDate(b.signedDate)}`,
                b.categoryName,
                b.validUntil && `Hiệu lực đến ${formatDate(b.validUntil)}`,
              ]
                .filter(Boolean)
                .join(' · ')
            : undefined
        }
        actions={
          b && (
            <>
              {b.attachmentName && (
                <Button icon={<FilePdfOutlined />} onClick={() => setViewPdf(true)}>
                  Xem quyết định
                </Button>
              )}
              {canManage && (
                <Upload
                  accept=".pdf"
                  showUploadList={false}
                  beforeUpload={(f) => {
                    run(
                      () => attach.mutateAsync(f),
                      () => 'Đã đính kèm tệp quyết định',
                    )();
                    return false;
                  }}
                >
                  <Button icon={<UploadOutlined />} loading={attach.isPending}>
                    {b.attachmentName ? 'Thay tệp QĐ' : 'Đính kèm PDF QĐ'}
                  </Button>
                </Upload>
              )}
              {canManage && (
                <Button
                  icon={<EditOutlined />}
                  onClick={() => {
                    bidForm.setFieldsValue(fromBid(b));
                    setEditing(true);
                  }}
                >
                  Sửa
                </Button>
              )}
              {canManage && b.status === 'Draft' && (
                <Popconfirm
                  title="Ban hành quyết định?"
                  description="Sau khi ban hành, số quyết định bị khóa và có thể tạo hợp đồng."
                  onConfirm={run(
                    () => activate.mutateAsync(),
                    () => 'Đã ban hành quyết định thầu',
                  )}
                >
                  <Button type="primary" icon={<CheckCircleOutlined />}>
                    Ban hành
                  </Button>
                </Popconfirm>
              )}
              {canManage && b.status === 'Active' && (
                <Popconfirm
                  title="Đóng quyết định thầu?"
                  onConfirm={run(
                    () => close.mutateAsync(),
                    () => 'Đã đóng quyết định',
                  )}
                >
                  <Button icon={<StopOutlined />}>Đóng</Button>
                </Popconfirm>
              )}
            </>
          )
        }
      />

      {!b ? (
        <Card loading={bid.isLoading} style={{ marginBottom: 16 }} />
      ) : (
        <>
          <DocKpis
            items={[
              { label: 'NCC trúng thầu', value: b.suppliers.length },
              {
                label: 'Giá trị trúng thầu',
                value: formatMoney(b.suppliers.reduce((sum, x) => sum + x.winningValue, 0)),
              },
              {
                label: 'Mặt hàng trong danh mục',
                value: formatQuantity(b.suppliers.reduce((sum, x) => sum + x.itemCount, 0)),
              },
              (() => {
                const won = b.suppliers.reduce((sum, x) => sum + x.winningValue, 0);
                const contracted = b.suppliers.reduce((sum, x) => sum + x.contractedValue, 0);
                return {
                  label: `Đã ký hợp đồng (${b.contracts.length} HĐ)`,
                  value: formatMoney(contracted),
                  percent: won > 0 ? (contracted / won) * 100 : 0,
                  tone: 'success' as const,
                  hint: won > 0 ? `${Math.round((contracted / won) * 100)}% giá trị trúng thầu` : undefined,
                };
              })(),
            ]}
          />
          <Panel title="Thông tin quyết định" className="panel--spaced">
            <InfoGrid
              items={[
                { label: 'Trích yếu', value: b.summary, span: 'full' },
                { label: 'Hình thức LCNT', value: b.selectionMethod },
                { label: 'Nguồn vốn', value: b.fundingSource },
                { label: 'Thời gian thực hiện', value: b.executionPeriod },
                { label: 'Tên trên HIS', value: b.hisName },
                {
                  label: 'Tệp quyết định',
                  value: b.attachmentName ? (
                    <Button type="link" size="small" style={{ padding: 0 }} onClick={() => setViewPdf(true)}>
                      {b.attachmentName}
                    </Button>
                  ) : (
                    'Chưa đính kèm'
                  ),
                },
              ]}
            />
          </Panel>
        </>
      )}

      {b?.status === 'Draft' && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 16 }}
          message="Thêm NCC trúng thầu, lấy danh mục từ HIS (theo Số QĐ + MST) hoặc import Excel, sau đó Ban hành để tạo hợp đồng."
        />
      )}

      <Card>
        <Tabs
          activeKey={tab}
          onChange={(k) => setParams({ tab: k })}
          items={[
            {
              key: 'suppliers',
              label: `NCC trúng thầu (${b?.suppliers.length ?? 0})`,
              children: (
                <>
                  {canManage && (
                    <Form
                      form={supplierForm}
                      layout="inline"
                      style={{ marginBottom: 12, rowGap: 8 }}
                      onFinish={async (v) => {
                        await run(
                          () => upsertSupplier.mutateAsync(v),
                          () => 'Đã cập nhật NCC trúng thầu',
                        )();
                        supplierForm.resetFields();
                      }}
                    >
                      <Form.Item
                        name="supplierId"
                        rules={[{ required: true, message: 'Chọn NCC' }]}
                        style={{ minWidth: 320 }}
                      >
                        <SupplierSelect />
                      </Form.Item>
                      <Form.Item name="winningValue" initialValue={0} style={{ width: 220 }}>
                        <MoneyInput placeholder="Giá trúng thầu" />
                      </Form.Item>
                      <Button htmlType="submit" icon={<PlusOutlined />} loading={upsertSupplier.isPending}>
                        Thêm / cập nhật NCC
                      </Button>
                      <Button
                        icon={<CloudSyncOutlined />}
                        loading={sync.isPending && sync.variables === undefined}
                        onClick={() => syncHis()}
                      >
                        Đồng bộ HIS tất cả NCC
                      </Button>
                    </Form>
                  )}
                  <Table<BidSupplierDto>
                    rowKey="supplierId"
                    size="middle"
                    pagination={false}
                    dataSource={b?.suppliers ?? []}
                    scroll={{ x: 'max-content' }}
                    columns={supplierColumns}
                  />
                </>
              ),
            },
            {
              key: 'items',
              label: 'Danh mục hàng hóa',
              children: (
                <>
                  <Space wrap style={{ marginBottom: 12 }}>
                    <Select
                      allowClear
                      placeholder="Lọc theo NCC"
                      style={{ width: 320 }}
                      value={supplierFilter}
                      options={(b?.suppliers ?? []).map((s) => ({ value: s.supplierId, label: s.name }))}
                      onChange={(v) => setParams(v ? { tab: 'items', supplier: v } : { tab: 'items' })}
                    />
                    {canManage && (
                      <>
                        <Button
                          icon={<CloudSyncOutlined />}
                          loading={sync.isPending}
                          onClick={() => syncHis(supplierFilter)}
                        >
                          Đồng bộ từ HIS
                        </Button>
                        <Button icon={<FileExcelOutlined />} onClick={() => setImporting(true)}>
                          Import Excel
                        </Button>
                        <Button
                          icon={<PlusOutlined />}
                          disabled={!b?.suppliers.length}
                          onClick={() => {
                            itemForm.resetFields();
                            itemForm.setFieldsValue({ supplierId: supplierFilter });
                            setAddingItem(true);
                          }}
                        >
                          Nhập tay
                        </Button>
                      </>
                    )}
                  </Space>
                  <Table<BidItemDto>
                    rowKey="itemId"
                    size="small"
                    loading={items.isFetching}
                    dataSource={items.data ?? []}
                    scroll={{ x: 'max-content' }}
                    pagination={{ pageSize: 50, showTotal: (t) => `${t} mặt hàng` }}
                    columns={[
                      { title: 'Mã key HIS', dataIndex: 'hisKey', width: 100 },
                      { title: 'Mã hàng', dataIndex: 'code', width: 90 },
                      {
                        title: 'Tên hàng',
                        dataIndex: 'name',
                        render: (v: string, r) => (
                          <Space direction="vertical" size={0}>
                            <span>{v}</span>
                            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                              {[r.activeIngredient, r.strength, r.specification].filter(Boolean).join(' · ')}
                            </Typography.Text>
                          </Space>
                        ),
                      },
                      { title: 'ĐVT', dataIndex: 'unit', width: 70 },
                      {
                        title: 'Hãng, nước SX',
                        key: 'maker',
                        render: (_, r) => [r.manufacturer, r.country].filter(Boolean).join(', '),
                      },
                      { title: 'NCC', dataIndex: 'supplierName' },
                      {
                        title: 'SL trúng thầu',
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
                        title: 'Đã vào HĐ',
                        dataIndex: 'contractedQuantity',
                        align: 'right',
                        render: (v: number) => formatQuantity(v),
                      },
                      {
                        title: 'Còn lại',
                        dataIndex: 'remainingQuantity',
                        align: 'right',
                        render: (v: number) => <Typography.Text strong>{formatQuantity(v)}</Typography.Text>,
                      },
                      {
                        title: 'Nguồn',
                        dataIndex: 'source',
                        render: (v: string) => <StatusTag domain="itemSource" value={v} />,
                      },
                      ...(canManage
                        ? [
                            {
                              title: '',
                              key: 'actions',
                              render: (_: unknown, r: BidItemDto) =>
                                r.contractedQuantity === 0 && (
                                  <Popconfirm
                                    title="Xóa mặt hàng khỏi quyết định?"
                                    onConfirm={run(
                                      () => removeItem.mutateAsync(r.itemId),
                                      () => 'Đã xóa',
                                    )}
                                  >
                                    <Button size="small" danger icon={<DeleteOutlined />} />
                                  </Popconfirm>
                                ),
                            },
                          ]
                        : []),
                    ]}
                  />
                </>
              ),
            },
            {
              key: 'contracts',
              label: `Hợp đồng (${b?.contracts.length ?? 0})`,
              children: (
                <Table
                  rowKey="id"
                  size="middle"
                  pagination={false}
                  dataSource={b?.contracts ?? []}
                  columns={[
                    {
                      title: 'Số hợp đồng',
                      dataIndex: 'number',
                      render: (v: string, r: { id: string }) => (
                        <a onClick={() => navigate(`/hop-dong/${r.id}`)}>{v}</a>
                      ),
                    },
                    { title: 'Tên hợp đồng', dataIndex: 'name' },
                    { title: 'Nhà cung cấp', dataIndex: 'supplierName' },
                    {
                      title: 'Giá trị',
                      dataIndex: 'totalAmount',
                      align: 'right',
                      render: (v: number) => formatMoney(v),
                    },
                    {
                      title: 'Trạng thái',
                      dataIndex: 'status',
                      render: (v: string) => <StatusTag domain="contractStatus" value={v} />,
                    },
                  ]}
                />
              ),
            },
          ]}
        />
      </Card>

      <Modal
        title="Sửa quyết định thầu"
        open={editing}
        onCancel={() => setEditing(false)}
        okText="Lưu"
        width={860}
        confirmLoading={update.isPending}
        onOk={async () => {
          const v = await bidForm.validateFields();
          try {
            await update.mutateAsync(v);
            message.success('Đã lưu');
            setEditing(false);
          } catch (e) {
            onError(e);
          }
        }}
        destroyOnHidden
        forceRender
      >
        <BidForm form={bidForm} lockNumber={b?.status !== 'Draft'} />
      </Modal>

      <Modal
        title="Thêm mặt hàng trúng thầu (nhập tay)"
        open={addingItem}
        onCancel={() => setAddingItem(false)}
        okText="Thêm"
        width={760}
        confirmLoading={addItem.isPending}
        onOk={async () => {
          const v = await itemForm.validateFields();
          try {
            await addItem.mutateAsync(v);
            message.success('Đã thêm mặt hàng');
            setAddingItem(false);
          } catch (e) {
            onError(e);
          }
        }}
        destroyOnHidden
      >
        <Form form={itemForm} layout="vertical">
          <Row gutter={12}>
            <Col span={24}>
              <Form.Item name="supplierId" label="Nhà cung cấp" rules={[{ required: true }]}>
                <Select options={(b?.suppliers ?? []).map((s) => ({ value: s.supplierId, label: s.name }))} />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item name="code" label="Mã hàng" rules={[{ required: true, whitespace: true }]}>
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={16}>
              <Form.Item name="name" label="Tên hàng" rules={[{ required: true, whitespace: true }]}>
                <Input />
              </Form.Item>
            </Col>
            <Col xs={12} md={6}>
              <Form.Item name="unit" label="ĐVT" rules={[{ required: true, whitespace: true }]}>
                <Input />
              </Form.Item>
            </Col>
            <Col xs={12} md={6}>
              <Form.Item name="hisKey" label="Mã key HIS">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={12} md={6}>
              <Form.Item name="quantity" label="SL trúng thầu" rules={[{ required: true }]}>
                <QuantityInput min={0.001} />
              </Form.Item>
            </Col>
            <Col xs={12} md={6}>
              <Form.Item name="unitPrice" label="Đơn giá" rules={[{ required: true }]}>
                <MoneyInput />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item name="activeIngredient" label="Hoạt chất / thông số">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item name="specification" label="Quy cách">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item name="manufacturer" label="Hãng sản xuất">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item name="country" label="Nước sản xuất">
                <Input />
              </Form.Item>
            </Col>
            <Col xs={24} md={8}>
              <Form.Item name="registrationNumber" label="Số đăng ký / GPNK">
                <Input />
              </Form.Item>
            </Col>
          </Row>
        </Form>
      </Modal>

      {b && (
        <ImportItemsDialog
          bidId={b.id}
          kind={b.importKind}
          open={importing}
          onClose={() => setImporting(false)}
        />
      )}

      <Modal
        title={b?.attachmentName}
        open={viewPdf}
        onCancel={() => setViewPdf(false)}
        footer={null}
        width={980}
        destroyOnHidden
      >
        {viewPdf && b && (
          <PdfViewer
            load={() => bidApi.attachmentBlob(b.id)}
            cacheKey={`bid-${b.id}-${b.attachmentSha256}`}
          />
        )}
      </Modal>
    </>
  );
}
