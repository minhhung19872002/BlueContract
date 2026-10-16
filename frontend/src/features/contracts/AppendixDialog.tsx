import { App, Alert, Button, Checkbox, DatePicker, Form, Input, Modal, Select, Table } from 'antd';
import { Plus, Trash2 } from 'lucide-react';
import { useState } from 'react';
import './appendix.css';
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
import { useFormDraft } from '../../hooks/useFormDraft';
import { MoneyInput, QuantityInput } from '../../components/inputs';
import { formatDate, formatMoney, formatQuantity } from '../../utils/format';
import {
  CONTRACT_INFO_FIELDS,
  currentContractInfo,
  formatContractInfo,
  type ContractInfoField,
} from '../../utils/contractInfo';

interface FormValues {
  type: AppendixType;
  number?: string;
  reason: string;
  infoChanges?: string;
  newExpiryDate?: dayjs.Dayjs;
  infoFields?: { field?: ContractInfoField; text?: string; amount?: number; date?: dayjs.Dayjs }[];
}

/** Form row → API value (amount as a number string, date yyyy-MM-dd, empty = remove the value). */
function infoValue(row: NonNullable<FormValues['infoFields']>[number]): string | null {
  const kind = CONTRACT_INFO_FIELDS.find((f) => f.value === row.field)?.kind;
  if (kind === 'money') return row.amount === undefined || row.amount === null ? null : String(row.amount);
  if (kind === 'date') return row.date ? row.date.format('YYYY-MM-DD') : null;
  return row.text?.trim() ? row.text.trim() : null;
}

/** One "field: current → new" row of a "thay đổi thông tin" appendix. */
function InfoFieldRow({
  name,
  contract,
  onRemove,
}: {
  name: number;
  contract: ContractDto;
  onRemove: () => void;
}) {
  const form = Form.useFormInstance<FormValues>();
  const field = Form.useWatch(['infoFields', name, 'field'], form) as ContractInfoField | undefined;
  const chosen = (Form.useWatch('infoFields', form) ?? []).map((f) => f?.field).filter(Boolean);
  const kind = CONTRACT_INFO_FIELDS.find((f) => f.value === field)?.kind;
  return (
    <div className="appendix-info__row">
      <Form.Item
        name={[name, 'field']}
        rules={[{ required: true, message: 'Chọn thông tin' }]}
        className="appendix-info__field"
      >
        <Select
          placeholder="Thông tin"
          options={CONTRACT_INFO_FIELDS.map((f) => ({
            value: f.value,
            label: f.label,
            disabled: f.value !== field && chosen.includes(f.value),
          }))}
        />
      </Form.Item>
      <div className="appendix-info__value">
        {field && (
          <div className="appendix-info__current">
            Hiện tại: {formatContractInfo(field, currentContractInfo(contract, field))}
          </div>
        )}
        {kind === 'money' ? (
          <Form.Item name={[name, 'amount']}>
            <MoneyInput style={{ width: '100%' }} placeholder="Giá trị mới (để trống = bỏ)" />
          </Form.Item>
        ) : kind === 'date' ? (
          <Form.Item name={[name, 'date']}>
            <DatePicker format="DD/MM/YYYY" placeholder="Ngày mới (để trống = bỏ)" />
          </Form.Item>
        ) : (
          <Form.Item name={[name, 'text']}>
            <Input.TextArea
              rows={kind === 'long' ? 3 : 1}
              autoSize={{ minRows: kind === 'long' ? 2 : 1, maxRows: 6 }}
              placeholder="Giá trị mới (để trống = bỏ)"
            />
          </Form.Item>
        )}
      </div>
      <Button
        type="text"
        danger
        icon={<Trash2 size={15} aria-hidden />}
        aria-label="Bỏ dòng"
        onClick={onRemove}
      />
    </div>
  );
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
  // Lưu nháp tự động (spec §9.3), one draft per contract.
  const draft = useFormDraft({
    key: `appendix-new.${contract.id}`,
    form,
    extra: lines,
    onRestoreExtra: setLines,
  });
  const editLines = (next: typeof lines) => {
    setLines(next);
    draft.touch();
  };
  const create = useContractAction((v: FormValues) =>
    contractApi.createAppendix(contract.id, {
      number: v.number ?? null,
      type: v.type,
      reason: v.reason,
      infoChanges: v.infoChanges ?? null,
      newExpiryDate: v.newExpiryDate ? v.newExpiryDate.format('YYYY-MM-DD') : null,
      templateId: null,
      infoFields:
        v.type === 'InfoChange'
          ? (v.infoFields ?? [])
              .filter((f) => f.field)
              .map((f) => ({ field: f.field!, newValue: infoValue(f) }))
          : null,
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
          draft.clear();
          setLines({});
          onClose();
        } catch (e) {
          onError(e);
        }
      }}
    >
      {draft.banner}
      <Form
        form={form}
        layout="vertical"
        initialValues={{ type: 'AddQuantity' }}
        onValuesChange={draft.onValuesChange}
      >
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
          <>
            <div className="appendix-info">
              <div className="appendix-info__title">Thông tin hợp đồng cần thay đổi</div>
              <div className="appendix-info__hint">
                Khi phụ lục được ký đủ, các giá trị mới được ghi vào hợp đồng; giá trị cũ được lưu trong phụ
                lục và lịch sử.
              </div>
              <Form.List name="infoFields">
                {(rows, { add, remove }) => (
                  <>
                    {rows.map(({ key, name }) => (
                      <InfoFieldRow key={key} name={name} contract={contract} onRemove={() => remove(name)} />
                    ))}
                    <Button
                      type="dashed"
                      icon={<Plus size={14} aria-hidden />}
                      onClick={() => add({})}
                      disabled={rows.length >= CONTRACT_INFO_FIELDS.length}
                    >
                      Thêm thông tin cần đổi
                    </Button>
                  </>
                )}
              </Form.List>
            </div>
            <Form.Item
              name="infoChanges"
              label="Nội dung thay đổi khác (ghi vào phụ lục)"
              dependencies={['infoFields']}
              rules={[
                ({ getFieldValue }) => ({
                  validator: (_, value?: string) =>
                    value?.trim() ||
                    (getFieldValue('infoFields') ?? []).some((f?: { field?: string }) => f?.field)
                      ? Promise.resolve()
                      : Promise.reject(
                          new Error('Chọn ít nhất một thông tin cần đổi hoặc nhập nội dung thay đổi.'),
                        ),
                }),
              ]}
            >
              <Input.TextArea rows={2} placeholder="VD: Thay đổi người đại diện của Bên B thành ..." />
            </Form.Item>
          </>
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
                      editLines(next);
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
                      onChange={(v) => editLines({ ...lines, [r.id]: { qty: v ?? undefined } })}
                    />
                  ) : (
                    <MoneyInput
                      size="small"
                      max={r.unitPrice}
                      onChange={(v) => editLines({ ...lines, [r.id]: { price: v ?? undefined } })}
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
