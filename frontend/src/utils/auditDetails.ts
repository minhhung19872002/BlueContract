import { statusLabel } from '../components/StatusTag';
import { formatDate, formatDateTime, formatMoney } from './format';

/** Enum names in declaration order: the audit trail stores enum values as numbers. */
const ENUMS: Record<
  string,
  { domain: 'contractStatus' | 'documentStatus' | 'appendixStatus' | 'procurementType'; names: string[] }
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
  'Contract.ProcurementType': { domain: 'procurementType', names: ['Bidding', 'NonBidding'] },
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
  code: 'Mã',
  totalamount: 'Tổng giá trị',
  amountbeforevat: 'Giá trị trước thuế',
  vatamount: 'Thuế GTGT',
  vatrate: 'Thuế suất GTGT (%)',
  expirydate: 'Hết hạn',
  effectivedate: 'Hiệu lực từ',
  signeddate: 'Ngày ký',
  versionnumber: 'Phiên bản',
  accepted: 'Chấp nhận',
  rejected: 'Từ chối',
  reason: 'Lý do',
  comment: 'Ý kiến',
  duration: 'Thời gian thực hiện',
  paymentterms: 'Điều khoản thanh toán',
  deliveryterms: 'Điều khoản giao hàng',
  warrantyterms: 'Điều khoản bảo hành',
  guaranteenumber: 'Số bảo lãnh',
  guaranteebank: 'Ngân hàng bảo lãnh',
  guaranteeamount: 'Giá trị bảo lãnh',
  guaranteeexpiry: 'Hạn bảo lãnh',
  acceptedquantity: 'SL nghiệm thu',
  actedat: 'Thời điểm xử lý',
  actedbyname: 'Người xử lý',
  actiontype: 'Loại thao tác',
  activatedat: 'Bắt đầu bước',
  activeingredient: 'Hoạt chất',
  address: 'Địa chỉ',
  advanceamount: 'Tạm ứng',
  advancerecovered: 'Thu hồi tạm ứng',
  aggregatorname: 'Người tổng hợp',
  allowedit: 'Cho phép sửa',
  amount: 'Số tiền',
  appliedat: 'Ngày áp dụng',
  approvedat: 'Ngày duyệt',
  approvedquantity: 'SL duyệt',
  archivecompletedat: 'Xác nhận hồ sơ lúc',
  archivecompletedbyname: 'Người xác nhận hồ sơ',
  attachmentname: 'Tệp đính kèm',
  authorizationdocument: 'Giấy ủy quyền',
  bankaccountnumber: 'Số tài khoản',
  bankname: 'Ngân hàng',
  biddecisionnumber: 'Số QĐ thầu',
  bidgroup: 'Nhóm thầu',
  body: 'Nội dung',
  bodyhtml: 'Nội dung email',
  calldate: 'Ngày gọi',
  calledquantity: 'SL đã gọi',
  cancelledat: 'Ngày hủy',
  caption: 'Chức danh ký',
  certificateissuer: 'Tổ chức cấp chứng thư',
  certificateserial: 'Số hiệu chứng thư',
  certificatesubject: 'Chủ thể chứng thư',
  certificatevalidto: 'Chứng thư hết hạn',
  completedat: 'Hoàn tất lúc',
  conclusion: 'Kết luận',
  condition: 'Điều kiện',
  confirmedat: 'Xác nhận lúc',
  confirmedbyname: 'Người xác nhận',
  confirmedquantity: 'SL xác nhận',
  contactname: 'Người liên hệ',
  contractnumber: 'Số hợp đồng',
  contractvalue: 'Giá trị hợp đồng',
  country: 'Nước sản xuất',
  delegatetitle: 'Chức danh người được ủy quyền',
  delegatortitle: 'Chức danh người ủy quyền',
  deliveredquantity: 'SL đã giao',
  deliverername: 'Người giao hàng',
  deliverydeadline: 'Hạn giao',
  description: 'Mô tả',
  documenttype: 'Loại tài liệu',
  domain: 'Mảng',
  dueat: 'Hạn xử lý',
  email: 'Email',
  executedvalue: 'Giá trị thực hiện',
  executionperiod: 'Thời gian thực hiện gói thầu',
  fullname: 'Họ tên',
  fundingsource: 'Nguồn vốn',
  infochanges: 'Nội dung thay đổi',
  invoicedate: 'Ngày hóa đơn',
  invoicenumber: 'Số hóa đơn',
  invoicesymbol: 'Ký hiệu hóa đơn',
  invoicetotalamount: 'Tổng tiền hóa đơn',
  isactive: 'Đang hoạt động',
  isdefault: 'Mặc định',
  isoptional: 'Tùy chọn',
  itemname: 'Tên hàng',
  jobtitle: 'Chức danh',
  kind: 'Loại',
  lastloginat: 'Đăng nhập gần nhất',
  lastreturnreason: 'Lý do trả lại gần nhất',
  lotnumber: 'Số lô',
  manufacturer: 'Hãng sản xuất',
  maxunitprice: 'Đơn giá tối đa',
  mustchangepassword: 'Phải đổi mật khẩu',
  newexpirydate: 'Hạn mới',
  newquantity: 'SL mới',
  newunitprice: 'Đơn giá mới',
  note: 'Ghi chú',
  notificationemail: 'Email nhận thông báo',
  oldexpirydate: 'Hạn cũ',
  oldquantity: 'SL cũ',
  oldunitprice: 'Đơn giá cũ',
  packagename: 'Tên gói thầu',
  paidamount: 'Đã thanh toán',
  paymentrequestnumber: 'Số đề nghị thanh toán',
  paymentsequence: 'Lần thanh toán',
  periodfrom: 'Từ ngày',
  periodtext: 'Kỳ',
  periodto: 'Đến ngày',
  phone: 'Điện thoại',
  procurementtype: 'Hình thức',
  proposedquantity: 'SL đề xuất',
  provider: 'Nhà cung cấp chữ ký',
  quantity: 'Số lượng',
  receiptdate: 'Ngày phiếu',
  receivedquantity: 'SL đã nhập',
  registrationnumber: 'Số đăng ký',
  representativename: 'Người đại diện',
  representativetitle: 'Chức vụ người đại diện',
  requireswarehousereceipt: 'Có nhập kho',
  selectionmethod: 'Hình thức lựa chọn nhà thầu',
  shortname: 'Tên viết tắt',
  shortagereason: 'Lý do thiếu',
  signedat: 'Ký lúc',
  signername: 'Người ký',
  signertitle: 'Chức vụ người ký',
  slahours: 'SLA (giờ)',
  source: 'Nguồn',
  specification: 'Quy cách',
  startedat: 'Bắt đầu lúc',
  startedbyname: 'Người gửi',
  strength: 'Hàm lượng',
  subject: 'Tiêu đề',
  summary: 'Trích yếu',
  suppliername: 'Nhà cung cấp',
  suppliernote: 'Ý kiến NCC',
  suppliertaxcode: 'MST nhà cung cấp',
  taxcode: 'Mã số thuế',
  type: 'Loại',
  unit: 'ĐVT',
  unitprice: 'Đơn giá',
  username: 'Tên đăng nhập',
  usertype: 'Loại tài khoản',
  validfrom: 'Hiệu lực từ',
  validto: 'Hiệu lực đến',
  validuntil: 'Hiệu lực đến',
  value: 'Giá trị',
  valuechange: 'Giá trị thay đổi',
  warehousecode: 'Mã kho',
  winningunitprice: 'Đơn giá trúng thầu',
  winningvalue: 'Giá trị trúng thầu',
  year: 'Năm',
  twofactorenabled: 'Xác thực 2 lớp',
  failedlogincount: 'Số lần đăng nhập sai',
  lockoutend: 'Khóa đến',
  sortorder: 'Thứ tự',
  order: 'Thứ tự',
  stepname: 'Bước',
  rolecode: 'Vai trò',
  currentversionid: '',
};

const MONEY = new Set(['totalamount', 'amountbeforevat', 'vatamount', 'guaranteeamount']);

/**
 * Readable summary of an audit entry's JSON (contract "Lịch sử" tab): technical keys (ids, enum numbers, internal
 * flags) are hidden or translated, so users never see raw JSON. Returns at most <c>max</c> "Nhãn: giá trị" parts.
 */
export function describeAuditDetails(subject: string, json: string | null | undefined, max = 5): string[] {
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
    // A document's "code" is the code of its signing workflow.
    const label = subject === 'Document' && k === 'code' ? 'Luồng' : LABELS[k];
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

/** Keys that are internal (ids, file keys, hashes, concurrency, secrets): never shown to people. */
export const isTechnicalField = (key: string) => {
  const k = key.toLowerCase();
  return (
    k === 'id' ||
    k.endsWith('id') ||
    k.endsWith('ids') ||
    k.endsWith('key') ||
    k.endsWith('hash') ||
    k.endsWith('sha256') ||
    k.endsWith('ip') ||
    [
      'rowversion',
      'securitystamp',
      'passwordhash',
      'twofactorsecret',
      'protectedpfx',
      'isdeleted',
      'createdby',
      'updatedby',
      'datatosign',
      'documentdigest',
      'snapshot',
    ].includes(k) ||
    LABELS[k] === ''
  );
};

/** Vietnamese name of a recorded field; unknown business fields read as "Thông tin khác". */
export const fieldLabel = (key: string) => LABELS[key.toLowerCase()] || 'Thông tin khác';

/** A recorded value as people read it: money, dates, yes/no, enum names; objects summarised, never raw JSON. */
export function formatAuditValue(subject: string, key: string, value: unknown): string {
  if (value === null || value === undefined || value === '') return '—';
  const k = key.toLowerCase();
  const enumInfo = ENUMS[`${subject}.${key.charAt(0).toUpperCase()}${key.slice(1)}`];
  if (enumInfo && typeof value === 'number')
    return statusLabel(enumInfo.domain, enumInfo.names[value] ?? String(value));
  if (typeof value === 'number')
    return MONEY.has(k) || k.endsWith('amount') || k.endsWith('value')
      ? formatMoney(value)
      : value.toLocaleString('vi-VN');
  if (typeof value === 'boolean') return value ? 'Có' : 'Không';
  if (typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(value)) return formatDateTime(value);
  if (typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value)) return formatDate(value);
  if (Array.isArray(value)) return `${value.length} mục`;
  if (typeof value === 'object') return 'Dữ liệu chi tiết';
  return String(value);
}
