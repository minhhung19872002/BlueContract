import type { components } from '../api/schema';
import { formatDate, formatMoney } from './format';

export type ContractInfoField = components['schemas']['ContractInfoField'];
type ContractDto = components['schemas']['ContractDto'];

/** Contract fields a "thay đổi thông tin" appendix may change, with their kind of input. */
export const CONTRACT_INFO_FIELDS: {
  value: ContractInfoField;
  label: string;
  kind: 'text' | 'long' | 'money' | 'date';
}[] = [
  { value: 'Name', label: 'Tên hợp đồng', kind: 'text' },
  { value: 'Duration', label: 'Thời gian thực hiện', kind: 'text' },
  { value: 'PaymentTerms', label: 'Điều khoản thanh toán', kind: 'long' },
  { value: 'DeliveryTerms', label: 'Điều khoản giao hàng', kind: 'long' },
  { value: 'WarrantyTerms', label: 'Điều khoản bảo hành', kind: 'long' },
  { value: 'GuaranteeNumber', label: 'Số bảo lãnh thực hiện', kind: 'text' },
  { value: 'GuaranteeBank', label: 'Ngân hàng bảo lãnh', kind: 'text' },
  { value: 'GuaranteeAmount', label: 'Giá trị bảo lãnh', kind: 'money' },
  { value: 'GuaranteeExpiry', label: 'Hạn bảo lãnh', kind: 'date' },
];

export const contractInfoLabel = (field: string) =>
  CONTRACT_INFO_FIELDS.find((f) => f.value === field)?.label ?? field;

/** Current value of a field on the contract, as the API stores it (amount: number text, date: yyyy-MM-dd). */
export function currentContractInfo(contract: ContractDto, field: ContractInfoField): string | null {
  switch (field) {
    case 'Name':
      return contract.name;
    case 'Duration':
      return contract.duration ?? null;
    case 'PaymentTerms':
      return contract.paymentTerms ?? null;
    case 'DeliveryTerms':
      return contract.deliveryTerms ?? null;
    case 'WarrantyTerms':
      return contract.warrantyTerms ?? null;
    case 'GuaranteeNumber':
      return contract.guaranteeNumber ?? null;
    case 'GuaranteeBank':
      return contract.guaranteeBank ?? null;
    case 'GuaranteeAmount':
      return contract.guaranteeAmount === null || contract.guaranteeAmount === undefined
        ? null
        : String(contract.guaranteeAmount);
    default:
      return contract.guaranteeExpiry ?? null;
  }
}

/** Stored value → display ("150.000.000 ₫", "31/03/2027", "(không có)"). */
export function formatContractInfo(field: string, value: string | null | undefined): string {
  if (value === null || value === undefined || value === '') return '(không có)';
  if (field === 'GuaranteeAmount') return formatMoney(Number(value));
  if (field === 'GuaranteeExpiry') return formatDate(value);
  return value;
}
