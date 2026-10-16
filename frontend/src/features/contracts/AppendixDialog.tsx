import { App, Alert, Checkbox, DatePicker, Form, Input, Modal, Select, Table } from 'antd';
import { useState } from 'react';
import dayjs from '../../i18n/dayjs';
import {
  contractApi,
  useContractAction,
  type AppendixType,
  type ContractDto,
  type ContractItemDto,
} from '../../api/procurement';
import { statusOptions } from '../../components/StatusTag';
import { useApiError } from '../../components/common';
import { MoneyInput, QuantityInput } from '../../components/inputs';
import { formatDate, formatMoney, formatQuantity } from '../../utils/format';

interface FormValues {
  type: AppendixType;
  number?: string;
  reason: string;
  infoChanges?: string;
  newExpiryDate?: dayjs.Dayjs;
}

/** Tạo phụ lục: bổ sung số lượng / giảm giá / thay đổi thông tin / gia hạn (spec §5.3). */
export function AppendixDialog({
  contract,
  open,
  onClose,
}: {
  contract: ContractDto;
  open: boolean;
  onClose: () => void;
}) {
  const [form] = Form.useForm<FormValues>();
  const [lines, setLines] = useState<Record<string, { qty?: number; price?: number }>>({});
  const type = Form.useWatch('type', form);
  const create = useContractAction((v: FormValues) =>
    contractApi.createAppendix(contract.id, {
      number: v.number ?? null,
      type: v.type,
      reason: v.reason,
      infoChanges: v.infoChanges ?? null,
      newExpiryDate: v.newExpiryDate ? v.newExpiryDate.format('YYYY-MM-DD') : null,
      templateId: null,
      lines: Object.entries(lines).map(([contractItemId, l]) => ({
        contractItemId,
        newQuantity: v.type === 'AddQuantity' ? (l.qty ?? null) : null,
        newUnitPrice: v.type === 'PriceReduction' ? (l.price ?? null) : null,
      })),
    }),
  );
  const { message } = App.useApp();
  const onError = useApiError();
  const withLines = type === 'AddQuantity' || type === 'PriceReduction';

  return (
    <Modal
      title={`Tạo phụ lục — Hợp đồng ${contract.number}`}
      open={open}
      onCancel={onClose}
      okText="Tạo phụ lục và sinh tài liệu"
      confirmLoading={create.isPending}
      width={980}
      destroyOnHidden
      onOk={async () => {
        const v = await form.validateFields();
        if (withLines && Object.keys(lines).length === 0) {
          message.warning('Chọn ít nhất một mặt hàng điều chỉnh.');
          return;
        }
        try {
          await create.mutateAsync(v);
          message.success('Đã tạo phụ lục — kiểm tra tài liệu rồi gửi trình ký');
          setLines({});
          onClose();
        } catch (e) {
          onError(e);
        }
      }}
    >
      <Form form={form} layout="vertical" initialValues={{ type: 'AddQuantity' }}>
        <Form.Item name="type" label="Loại phụ lục" rules={[{ required: true }]}>
          <Select options={statusOptions('appendixType')} onChange={() => setLines({})} />
        </Form.Item>
        <Form.Item name="number" label="Số phụ lục" extra="Để trống: tự đánh số 01, 02...">
          <Input style={{ width: 200 }} />
        </Form.Item>
        <Form.Item name="reason" label="Lý do điều chỉnh" rules={[{ required: true, whitespace: true }]}>
          <Input.TextArea rows={2} />
        </Form.Item>
        {type === 'InfoChange' && (
          <Form.Item
            name="infoChanges"
            label="Nội dung thông tin thay đổi"
            rules={[{ required: true, whitespace: true }]}
          >
            <Input.TextArea rows={3} placeholder="VD: Thay đổi số tài khoản của Bên B thành ..." />
          </Form.Item>
        )}
        {type === 'Extension' && (
          <Form.Item
            name="newExpiryDate"
            label={`Ngày hết hạn mới (hiện tại: ${formatDate(contract.expiryDate) || 'chưa có'})`}
            rules={[{ required: true }]}
          >
            <DatePicker
              format="DD/MM/YYYY"
              disabledDate={(d) => !!contract.expiryDate && !d.isAfter(dayjs(contract.expiryDate))}
            />
          </Form.Item>
        )}
      </Form>
      {withLines && (
        <>
          <Alert
            type="info"
            showIcon
            style={{ marginBottom: 8 }}
            message={
              type === 'AddQuantity'
                ? 'Nhập số lượng mới (lớn hơn số lượng hiện tại).'
                : 'Nhập đơn giá mới (thấp hơn đơn giá hiện tại).'
            }
          />
          <Table<ContractItemDto>
            rowKey="id"
            size="small"
            pagination={false}
            dataSource={contract.items}
            scroll={{ y: 360, x: 'max-content' }}
            columns={[
              {
                title: '',
                key: 'pick',
                width: 40,
                render: (_, r) => (
                  <Checkbox
                    checked={!!lines[r.id]}
                    onChange={(e) => {
                      const next = { ...lines };
                      if (e.target.checked) next[r.id] = {};
                      else delete next[r.id];
                      setLines(next);
                    }}
                  />
                ),
              },
              { title: 'Mặt hàng', dataIndex: 'itemName' },
              {
                title: 'SL hiện tại',
                dataIndex: 'quantity',
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
                title: 'Đơn giá hiện tại',
                dataIndex: 'unitPrice',
                align: 'right',
                render: (v: number) => formatMoney(v),
              },
              {
                title: type === 'AddQuantity' ? 'SL mới' : 'Đơn giá mới',
                key: 'new',
                width: 180,
                render: (_, r) =>
                  lines[r.id] &&
                  (type === 'AddQuantity' ? (
                    <QuantityInput
                      size="small"
                      min={r.quantity}
                      onChange={(v) => setLines({ ...lines, [r.id]: { qty: v ?? undefined } })}
                    />
                  ) : (
                    <MoneyInput
                      size="small"
                      max={r.unitPrice}
                      onChange={(v) => setLines({ ...lines, [r.id]: { price: v ?? undefined } })}
                    />
                  )),
              },
            ]}
          />
        </>
      )}
    </Modal>
  );
}
