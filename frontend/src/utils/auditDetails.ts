import { statusLabel } from '../components/StatusTag';
import { formatDate, formatMoney } from './format';

/** Enum names in declaration order: the audit trail stores enum values as numbers. */
const ENUMS: Record<
  string,
  { domain: 'contractStatus' | 'documentStatus' | 'appendixStatus'; names: string[] }
> = {
  'Contract.Status': {
    domain: 'contractStatus',
    names: [
      'Draft',
      'InReview',
      'SupplierReview',
      'AccountantReview',
      'SupplierSigning',
      'AccountantInitialing',
      'DirectorSigning',
      'Stamping',
      'Effective',
      'Liquidated',
      'Cancelled',
      'Returned',
    ],
  },
  'Document.Status': { domain: 'documentStatus', names: ['Draft', 'InWorkflow', 'Completed', 'Cancelled'] },
  'ContractAppendix.Status': {
    domain: 'appendixStatus',
    names: ['Draft', 'InWorkflow', 'Effective', 'Cancelled'],
  },
};

const LABELS: Record<string, string> = {
  status: 'Trạng thái',
  number: 'Số',
  name: 'Tên',
  title: 'Tiêu đề',
  code: 'Luồng',
  totalamount: 'Tổng giá trị',
  amountbeforevat: 'Giá trị trước thuế',
  vatamount: 'Thuế GTGT',
  expirydate: 'Hết hạn',
  effectivedate: 'Hiệu lực từ',
  signeddate: 'Ngày ký',
  versionnumber: 'Phiên bản',
  accepted: 'Chấp nhận',
  rejected: 'Từ chối',
  reason: 'Lý do',
  comment: 'Ý kiến',
  currentversionid: '',
};

const MONEY = new Set(['totalamount', 'amountbeforevat', 'vatamount']);

/**
 * Readable summary of an audit entry's JSON (contract "Lịch sử" tab): technical keys (ids, enum numbers, internal
 * flags) are hidden or translated, so users never see raw JSON. Returns at most <c>max</c> "Nhãn: giá trị" parts.
 */
export function describeAuditDetails(subject: string, json: string | null | undefined, max = 4): string[] {
  if (!json) return [];
  let data: unknown;
  try {
    data = JSON.parse(json);
  } catch {
    return [json.length > 160 ? `${json.slice(0, 160)}…` : json];
  }
  if (!data || typeof data !== 'object' || Array.isArray(data)) return [];
  const parts: string[] = [];
  for (const [key, value] of Object.entries(data as Record<string, unknown>)) {
    const k = key.toLowerCase();
    if (k === 'id' || k.endsWith('id') || k.endsWith('key') || k.endsWith('sha256') || LABELS[k] === '')
      continue;
    if (value === null || value === undefined || typeof value === 'object') continue;
    const label = LABELS[k];
    if (!label) continue;
    const enumInfo = ENUMS[`${subject}.${key.charAt(0).toUpperCase()}${key.slice(1)}`];
    let shown: string;
    if (enumInfo && typeof value === 'number') {
      shown = statusLabel(enumInfo.domain, enumInfo.names[value] ?? String(value));
    } else if (MONEY.has(k) && typeof value === 'number') {
      shown = formatMoney(value);
    } else if (typeof value === 'string' && /^\d{4}-\d{2}-\d{2}/.test(value)) {
      shown = formatDate(value);
    } else if (typeof value === 'boolean') {
      shown = value ? 'Có' : 'Không';
    } else {
      shown = String(value);
    }
    parts.push(`${label}: ${shown}`);
    if (parts.length >= max) break;
  }
  return parts;
}
