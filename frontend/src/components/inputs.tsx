import { InputNumber, Select, type InputNumberProps } from 'antd';
import { useLookup } from '../api/admin';

const thousands = (value: string | number | undefined) => {
  if (value === undefined || value === null || value === '') return '';
  const [int, dec] = String(value).split('.');
  const grouped = (int ?? '').replace(/\B(?=(\d{3})+(?!\d))/g, '.');
  return dec !== undefined ? `${grouped},${dec}` : grouped;
};

const parseVi = (text: string | undefined) =>
  (text ?? '')
    .replace(/\./g, '')
    .replace(',', '.')
    .replace(/[^\d.-]/g, '');

/** Tiền VNĐ: hiển thị "1.234.567", không phần lẻ (spec §9.3). */
export function MoneyInput(props: InputNumberProps<number>) {
  return (
    <InputNumber<number>
      min={0}
      precision={0}
      style={{ width: '100%' }}
      addonAfter="₫"
      formatter={(v) => thousands(v)}
      parser={(t) => Number(parseVi(t)) || 0}
      {...props}
    />
  );
}

/** Số lượng: tối đa 3 chữ số thập phân, phân cách hàng nghìn kiểu Việt Nam. */
export function QuantityInput(props: InputNumberProps<number>) {
  return (
    <InputNumber<number>
      min={0}
      precision={3}
      style={{ width: '100%' }}
      formatter={(v) => thousands(v === undefined ? undefined : Number(v).toString())}
      parser={(t) => Number(parseVi(t)) || 0}
      {...props}
    />
  );
}

/** Chọn nhà cung cấp (theo MST hoặc tên). */
export function SupplierSelect({
  value,
  onChange,
  allowClear,
  disabled,
  placeholder = 'Chọn nhà cung cấp',
  style,
}: {
  value?: string;
  onChange?: (v: string | undefined) => void;
  allowClear?: boolean;
  disabled?: boolean;
  placeholder?: string;
  style?: React.CSSProperties;
}) {
  const suppliers = useLookup('suppliers');
  return (
    <Select
      showSearch
      allowClear={allowClear}
      disabled={disabled}
      placeholder={placeholder}
      optionFilterProp="label"
      loading={suppliers.isLoading}
      value={value}
      onChange={onChange}
      style={{ width: '100%', ...style }}
      options={(suppliers.data ?? []).map((s) => ({ value: s.id, label: `${s.code} — ${s.name}` }))}
    />
  );
}
