import {
  Alert,
  App,
  Button,
  Card,
  Col,
  DatePicker,
  Empty,
  Form,
  Input,
  Radio,
  Row,
  Select,
  Switch,
  Table,
  Typography,
} from 'antd';
import { SaveOutlined } from '@ant-design/icons';
import { useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import dayjs from '../../i18n/dayjs';
import { useLookup, usePagedList } from '../../api/admin';
import { useContract, type ContractItemDto, type ContractListItemDto } from '../../api/procurement';
import {
  receiptApi,
  useReceipt,
  useReceiptAction,
  type ReceiptDto,
  type ReceiptType,
} from '../../api/receipts';
import { useFormDraft } from '../../hooks/useFormDraft';
import { useApiError } from '../../components/common';
import { DocHeader, StickyActionBar } from '../../components/layout';
import { MoneyInput, QuantityInput } from '../../components/inputs';
import { formatMoney, formatQuantity } from '../../utils/format';

interface Header {
  type: ReceiptType;
  virtual: boolean;
  contractId?: string;
  warehouseId?: string;
  receiptDate: dayjs.Dayjs;
  delivererName?: string;
  note?: string;
}

type Entry = { qty?: number; lot?: string; expiry?: dayjs.Dayjs | null; price?: number };

/** Lập phiếu nhập kho / hoàn trả trên cổng, hoặc phiếu ảo xác nhận khối lượng (HĐ không nhập kho). */
export function ReceiptCreatePage() {
  const { id } = useParams<{ id: string }>();
  const existing = useReceipt(id);
  if (id && !existing.data) return <Card loading />;
  return <ReceiptEditor key={id ?? 'new'} receipt={existing.data} />;
}

function ReceiptEditor({ receipt }: { receipt?: ReceiptDto }) {
  const editing = !!receipt;
  const [form] = Form.useForm<Header>();
  const [entries, setEntries] = useState<Record<string, Entry>>(() =>
    Object.fromEntries(
      (receipt?.lines ?? [])
        .filter((l) => l.contractItemId)
        .map((l) => [
          l.contractItemId!,
          {
            qty: l.quantity,
            lot: l.lotNumber ?? undefined,
            expiry: l.expiryDate ? dayjs(l.expiryDate) : null,
            price: l.unitPrice,
          },
        ]),
    ),
  );
  const navigate = useNavigate();
  const { message } = App.useApp();
  const onError = useApiError();
  const warehouses = useLookup('warehouses');
  const contracts = usePagedList<ContractListItemDto>('/api/contracts', {
    status: 'Effective',
    pageSize: 200,
  });
  const contractId = Form.useWatch('contractId', form) ?? receipt?.contractId ?? undefined;
  const isVirtual = Form.useWatch('virtual', form) ?? receipt?.source === 'VirtualPortal';
  const type = Form.useWatch('type', form) ?? receipt?.type ?? 'Receipt';
  const contract = useContract(contractId);
  const items = contract.data?.items ?? [];

  // Lưu nháp tự động (spec §9.3) for a new receipt.
  const draft = useFormDraft({
    key: editing ? null : 'receipt-new',
    form,
    extra: entries,
    onRestoreExtra: setEntries,
  });

  const save = useReceiptAction(async () => {
    const h = await form.validateFields();
    const body = {
      type: h.type,
      virtual: h.virtual,
      contractId: h.contractId!,
      warehouseId: h.virtual ? (h.warehouseId ?? null) : h.warehouseId!,
      receiptDate: h.receiptDate.format('YYYY-MM-DD'),
      callOrderId: null,
      delivererName: h.delivererName ?? null,
      note: h.note ?? null,
      lines: Object.entries(entries)
        .filter(([, e]) => (e.qty ?? 0) > 0)
        .map(([contractItemId, e]) => ({
          contractItemId,
          quantity: e.qty!,
          lotNumber: e.lot ?? null,
          expiryDate: e.expiry ? e.expiry.format('YYYY-MM-DD') : null,
          unitPrice: e.price ?? null,
        })),
    };
    if (body.lines.length === 0) throw new Error('Nhập số lượng cho ít nhất một mặt hàng.');
    if (editing) {
      await receiptApi.update(receipt.id, body);
      return receipt.id;
    }
    return receiptApi.create(body);
  });

  const set = (key: string, patch: Entry) => {
    setEntries({ ...entries, [key]: { ...entries[key], ...patch } });
    draft.touch();
  };
  const total = items.reduce(
    (s, i) => s + Math.round((entries[i.id]?.qty ?? 0) * (entries[i.id]?.price ?? i.unitPrice)),
    0,
  );

  return (
    <>
      <DocHeader
        back={{ to: -1, label: 'Quay lại' }}
        plain
        code={editing ? `Sửa phiếu ${receipt.number}` : 'Lập phiếu trên cổng'}
        sub="Phiếu được kiểm tra tự động theo hợp đồng trước khi gửi ký"
      />
      {draft.banner}
      <Card style={{ marginBottom: 16 }}>
        <Form
          form={form}
          onValuesChange={draft.onValuesChange}
          layout="vertical"
          initialValues={{
            type: receipt?.type ?? 'Receipt',
            virtual: receipt?.source === 'VirtualPortal',
            contractId: receipt?.contractId ?? undefined,
            warehouseId: receipt?.warehouseId ?? undefined,
            receiptDate: receipt ? dayjs(receipt.receiptDate) : dayjs(),
            delivererName: receipt?.delivererName ?? undefined,
            note: receipt?.note ?? undefined,
          }}
        >
          <Row gutter={12}>
            <Col xs={24} md={6}>
              <Form.Item name="type" label="Loại phiếu">
                <Radio.Group
                  disabled={editing}
                  options={[
                    { value: 'Receipt', label: 'Nhập kho' },
                    { value: 'Return', label: 'Hoàn trả', disabled: isVirtual },
                  ]}
                />
              </Form.Item>
            </Col>
            <Col xs={24} md={6}>
              <Form.Item
                name="virtual"
                label="Phiếu xác nhận khối lượng (không qua kho)"
                valuePropName="checked"
                tooltip="Dùng cho hợp đồng dịch vụ/sửa chữa không nhập kho nhưng cần nghiệm thu — hoàn thành không cần ký"
              >
                <Switch
                  disabled={editing || type === 'Return'}
                  onChange={() => {
                    form.setFieldValue('contractId', undefined);
                    setEntries({});
                  }}
                />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item name="contractId" label="Hợp đồng" rules={[{ required: true }]}>
                <Select
                  showSearch
                  disabled={editing}
                  optionFilterProp="label"
                  onChange={() => setEntries({})}
                  options={(contracts.data?.items ?? [])
                    // Phiếu ảo bỏ qua 5 chữ ký: chỉ cho hợp đồng không nhập kho.
                    .filter((c) => !isVirtual || !c.requiresWarehouseReceipt)
                    .map((c) => ({
                      value: c.id,
                      label: `${c.number} — ${c.supplierName}`,
                    }))}
                />
              </Form.Item>
            </Col>
            <Col xs={24} md={6}>
              <Form.Item name="warehouseId" label="Kho" rules={[{ required: !isVirtual }]}>
                <Select
                  allowClear={isVirtual}
                  options={(warehouses.data ?? []).map((w) => ({ value: w.id, label: w.name }))}
                />
              </Form.Item>
            </Col>
            <Col xs={24} md={6}>
              <Form.Item name="receiptDate" label="Ngày phiếu" rules={[{ required: true }]}>
                <DatePicker format="DD/MM/YYYY" style={{ width: '100%' }} />
              </Form.Item>
            </Col>
            <Col xs={24} md={12}>
              <Form.Item name="delivererName" label="Người giao hàng (NCC)">
                <Input maxLength={200} />
              </Form.Item>
            </Col>
            <Col span={24}>
              <Form.Item name="note" label="Ghi chú">
                <Input.TextArea rows={2} maxLength={2000} />
              </Form.Item>
            </Col>
          </Row>
        </Form>
      </Card>
      <Card
        title="Mặt hàng"
        extra={
          <Typography.Text strong>
            Tổng giá trị: {formatMoney(type === 'Return' ? -total : total)}
          </Typography.Text>
        }
      >
        {isVirtual && (
          <Alert
            type="info"
            showIcon
            style={{ marginBottom: 12 }}
            message='Phiếu xác nhận khối lượng hoàn thành khi bấm "Xác nhận khối lượng", không qua luồng ký, và được dùng để lập biên bản nghiệm thu.'
          />
        )}
        {!contractId ? (
          <Empty description="Chọn hợp đồng để tải danh sách mặt hàng" />
        ) : (
          <Table<ContractItemDto>
            rowKey="id"
            size="small"
            loading={contract.isFetching}
            dataSource={items}
            pagination={false}
            scroll={{ x: 'max-content' }}
            columns={[
              { title: 'Mã hàng', dataIndex: 'itemCode', width: 100 },
              { title: 'Tên hàng', dataIndex: 'itemName', width: 260 },
              { title: 'ĐVT', dataIndex: 'unit', width: 70 },
              {
                title: 'SL HĐ',
                dataIndex: 'quantity',
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
                title: 'Đã nhập',
                dataIndex: 'receivedQuantity',
                align: 'right',
                render: (v: number) => formatQuantity(v),
              },
              {
                title: 'Còn lại',
                dataIndex: 'remainingQuantity',
                align: 'right',
                render: (v: number) => <b>{formatQuantity(v)}</b>,
              },
              {
                title: type === 'Return' ? 'SL hoàn trả' : 'SL nhập',
                key: 'qty',
                width: 140,
                render: (_, i) => (
                  <QuantityInput
                    size="small"
                    value={entries[i.id]?.qty}
                    status={
                      type === 'Receipt' && (entries[i.id]?.qty ?? 0) > i.remainingQuantity
                        ? 'error'
                        : undefined
                    }
                    onChange={(v) => set(i.id, { qty: v ?? undefined })}
                  />
                ),
              },
              {
                title: 'Đơn giá',
                key: 'price',
                width: 150,
                render: (_, i) => (
                  <MoneyInput
                    size="small"
                    value={entries[i.id]?.price ?? i.unitPrice}
                    onChange={(v) => set(i.id, { price: v ?? undefined })}
                  />
                ),
              },
              {
                title: 'Số lô',
                key: 'lot',
                width: 120,
                render: (_, i) => (
                  <Input
                    size="small"
                    maxLength={60}
                    value={entries[i.id]?.lot}
                    onChange={(e) => set(i.id, { lot: e.target.value })}
                  />
                ),
              },
              {
                title: 'Hạn dùng',
                key: 'expiry',
                width: 140,
                render: (_, i: ContractItemDto) => (
                  <DatePicker
                    size="small"
                    format="DD/MM/YYYY"
                    value={entries[i.id]?.expiry ?? null}
                    onChange={(d) => set(i.id, { expiry: d })}
                  />
                ),
              },
            ]}
          />
        )}
      </Card>
      <StickyActionBar total={{ label: 'Tổng tiền phiếu', value: formatMoney(total) }}>
        <Button
          type="primary"
          icon={<SaveOutlined />}
          loading={save.isPending}
          onClick={async () => {
            try {
              const savedId = await save.mutateAsync();
              draft.clear();
              message.success('Đã lưu phiếu — kiểm tra cảnh báo rồi gửi ký');
              navigate(`/nhap-kho/${savedId}`);
            } catch (e) {
              if (e instanceof Error && !('code' in e) && !('response' in e)) message.error(e.message);
              else onError(e);
            }
          }}
        >
          Lưu phiếu
        </Button>
      </StickyActionBar>
    </>
  );
}
