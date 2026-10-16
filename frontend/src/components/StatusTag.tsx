/** Colour families of the design system (CSS variables --bp-{tone}-fg / -bg / -bd / -solid). */
export type Tone =
  'primary' | 'success' | 'warning' | 'orange' | 'danger' | 'info' | 'purple' | 'indigo' | 'neutral' | 'teal';

interface StatusStyle {
  label: string;
  tone: Tone;
  /** In progress: the dot pulses. */
  running?: boolean;
  /** Dashed border (virtual receipt). */
  dashed?: boolean;
}

/**
 * The single place where status values get their Vietnamese label and colour (spec §9.3). A status always shows its
 * text, never colour alone. Add new domains here as they are introduced.
 */
const STATUS: Record<string, Record<string, StatusStyle>> = {
  flag: {
    Overdue: { label: 'Quá hạn', tone: 'danger' },
  },
  active: {
    true: { label: 'Đang hoạt động', tone: 'success' },
    false: { label: 'Ngừng hoạt động', tone: 'neutral' },
  },
  userStatus: {
    Active: { label: 'Hoạt động', tone: 'success' },
    Inactive: { label: 'Ngừng hoạt động', tone: 'neutral' },
    PendingActivation: { label: 'Chờ kích hoạt', tone: 'warning' },
  },
  userType: {
    Internal: { label: 'Nội bộ', tone: 'primary' },
    Supplier: { label: 'Nhà cung cấp', tone: 'purple' },
  },
  departmentType: {
    Clinical: { label: 'Lâm sàng', tone: 'info' },
    Paraclinical: { label: 'Cận lâm sàng', tone: 'indigo' },
    Functional: { label: 'Phòng chức năng', tone: 'neutral' },
  },
  warehouseType: {
    Drug: { label: 'Kho thuốc', tone: 'primary' },
    MedicalSupply: { label: 'Kho vật tư y tế', tone: 'info' },
    Chemical: { label: 'Kho hóa chất', tone: 'orange' },
    Asset: { label: 'Kho tài sản', tone: 'purple' },
    General: { label: 'Kho chung', tone: 'neutral' },
  },
  delegation: {
    effective: { label: 'Đang hiệu lực', tone: 'success' },
    upcoming: { label: 'Chưa đến hạn', tone: 'primary' },
    expired: { label: 'Hết hạn', tone: 'neutral' },
    revoked: { label: 'Đã thu hồi', tone: 'danger' },
  },
  templateKind: {
    Contract: { label: 'Hợp đồng', tone: 'primary' },
    Appendix: { label: 'Phụ lục', tone: 'indigo' },
    Proposal: { label: 'Phiếu đề xuất', tone: 'info' },
    ProposalSummary: { label: 'Tổng hợp đề xuất', tone: 'info' },
    CallOrder: { label: 'Đơn gọi hàng', tone: 'purple' },
    WarehouseReceipt: { label: 'Phiếu nhập kho', tone: 'orange' },
    Acceptance: { label: 'BB nghiệm thu', tone: 'success' },
    Valuation: { label: 'BB xác định giá trị (08a)', tone: 'success' },
    PaymentRequest: { label: 'Đề nghị thanh toán', tone: 'warning' },
    Liquidation: { label: 'BB thanh lý', tone: 'orange' },
  },
  documentStatus: {
    Draft: { label: 'Bản nháp', tone: 'neutral' },
    InWorkflow: { label: 'Đang trình ký', tone: 'primary', running: true },
    Completed: { label: 'Hoàn thành', tone: 'success' },
    Cancelled: { label: 'Đã hủy', tone: 'danger' },
  },
  documentType: {
    Contract: { label: 'Hợp đồng', tone: 'primary' },
    Appendix: { label: 'Phụ lục hợp đồng', tone: 'indigo' },
    Proposal: { label: 'Phiếu đề xuất', tone: 'info' },
    ProposalSummary: { label: 'Tổng hợp đề xuất', tone: 'info' },
    CallOrder: { label: 'Đơn gọi hàng', tone: 'purple' },
    WarehouseReceipt: { label: 'Phiếu nhập kho', tone: 'orange' },
    Acceptance: { label: 'Biên bản nghiệm thu', tone: 'success' },
    Valuation: { label: 'Biên bản XĐGT (08a)', tone: 'success' },
    PaymentRequest: { label: 'Đề nghị thanh toán', tone: 'warning' },
    Liquidation: { label: 'Biên bản thanh lý', tone: 'orange' },
    TemplateSample: { label: 'Tài liệu thử từ mẫu', tone: 'neutral' },
  },
  bidStatus: {
    Draft: { label: 'Đang nhập liệu', tone: 'neutral' },
    Active: { label: 'Đã ban hành', tone: 'success' },
    Closed: { label: 'Đã đóng', tone: 'danger' },
  },
  itemSource: {
    His: { label: 'Đồng bộ HIS', tone: 'teal' },
    Import: { label: 'Import Excel', tone: 'indigo' },
    Manual: { label: 'Nhập tay', tone: 'neutral' },
  },
  contractStatus: {
    Draft: { label: 'Bản nháp', tone: 'neutral' },
    InReview: { label: 'Tổ rà soát kiểm tra', tone: 'primary', running: true },
    SupplierReview: { label: 'NCC rà soát', tone: 'warning' },
    AccountantReview: { label: 'Kế toán xem thay đổi', tone: 'orange' },
    SupplierSigning: { label: 'NCC ký số', tone: 'purple' },
    AccountantInitialing: { label: 'Kế toán ký nháy', tone: 'indigo' },
    DirectorSigning: { label: 'Giám đốc ký', tone: 'purple' },
    Stamping: { label: 'Văn thư đóng dấu', tone: 'orange' },
    Effective: { label: 'Đang hiệu lực', tone: 'success' },
    Liquidated: { label: 'Đã thanh lý', tone: 'info' },
    Cancelled: { label: 'Đã hủy', tone: 'neutral' },
    Returned: { label: 'Bị trả lại', tone: 'danger' },
  },
  procurementType: {
    Bidding: { label: 'Trong thầu', tone: 'primary' },
    NonBidding: { label: 'Ngoài thầu', tone: 'purple' },
  },
  contractKind: {
    UnitPriceCallOff: { label: 'Theo đơn giá, gọi hàng từng đợt', tone: 'info' },
    LumpSum: { label: 'Trọn gói', tone: 'indigo' },
  },
  appendixType: {
    AddQuantity: { label: 'Bổ sung số lượng', tone: 'primary' },
    PriceReduction: { label: 'Giảm giá', tone: 'success' },
    InfoChange: { label: 'Thay đổi thông tin', tone: 'warning' },
    Extension: { label: 'Gia hạn', tone: 'purple' },
  },
  appendixStatus: {
    Draft: { label: 'Bản nháp', tone: 'neutral' },
    InWorkflow: { label: 'Đang trình ký', tone: 'primary', running: true },
    Effective: { label: 'Đã hiệu lực', tone: 'success' },
    Cancelled: { label: 'Đã hủy', tone: 'neutral' },
  },
  revisionKind: {
    Insertion: { label: 'Thêm', tone: 'success' },
    Deletion: { label: 'Xóa', tone: 'danger' },
    Formatting: { label: 'Định dạng', tone: 'neutral' },
  },
  proposalStatus: {
    Draft: { label: 'Bản nháp', tone: 'neutral' },
    Signing: { label: 'Đang ký', tone: 'primary', running: true },
    Submitted: { label: 'Đã gửi — chờ tổng hợp', tone: 'primary' },
    Aggregated: { label: 'Đã tổng hợp', tone: 'success' },
    Returned: { label: 'Bị trả lại', tone: 'danger' },
    Cancelled: { label: 'Đã hủy', tone: 'neutral' },
  },
  summaryStatus: {
    Draft: { label: 'Bản nháp', tone: 'neutral' },
    AggregatorSigning: { label: 'Người tổng hợp ký', tone: 'primary', running: true },
    DomainHeadReview: { label: 'Tổ trưởng duyệt', tone: 'warning' },
    DirectorSigning: { label: 'Giám đốc ký', tone: 'purple' },
    Approved: { label: 'Đã duyệt', tone: 'success' },
    Returned: { label: 'Bị trả lại', tone: 'danger' },
    Cancelled: { label: 'Đã hủy', tone: 'neutral' },
  },
  callOrderStatus: {
    Sent: { label: 'Chờ NCC xác nhận', tone: 'warning' },
    SupplierConfirmed: { label: 'NCC đã xác nhận', tone: 'success' },
    PartiallyConfirmed: { label: 'Xác nhận một phần', tone: 'orange' },
    Rejected: { label: 'NCC từ chối', tone: 'danger' },
    Delivered: { label: 'Đã giao hàng', tone: 'info' },
    Cancelled: { label: 'Đã hủy', tone: 'neutral' },
  },
  workflowStatus: {
    Running: { label: 'Đang trình ký', tone: 'primary', running: true },
    Completed: { label: 'Đã ký xong', tone: 'success' },
    Returned: { label: 'Bị trả lại', tone: 'danger' },
    Cancelled: { label: 'Đã hủy', tone: 'neutral' },
  },
  stepStatus: {
    Pending: { label: 'Chờ đến lượt', tone: 'neutral' },
    Active: { label: 'Đang xử lý', tone: 'primary', running: true },
    Done: { label: 'Đã xử lý', tone: 'success' },
    Skipped: { label: 'Bỏ qua', tone: 'neutral' },
  },
  workflowAction: {
    Review: { label: 'Kiểm tra', tone: 'primary' },
    Approve: { label: 'Duyệt', tone: 'info' },
    Sign: { label: 'Ký số', tone: 'purple' },
    Initial: { label: 'Ký nháy', tone: 'indigo' },
    OrgSeal: { label: 'Đóng dấu điện tử', tone: 'orange' },
    SupplierReview: { label: 'NCC rà soát', tone: 'warning' },
    SupplierSign: { label: 'NCC ký số', tone: 'purple' },
    Confirm: { label: 'Xác nhận', tone: 'success' },
  },
  assigneeType: {
    Role: { label: 'Theo vai trò', tone: 'primary' },
    User: { label: 'Người cụ thể', tone: 'purple' },
    DocumentSupplier: { label: 'Nhà cung cấp của tài liệu', tone: 'warning' },
    Creator: { label: 'Người lập', tone: 'neutral' },
  },
  workflowEvent: {
    Started: { label: 'Gửi ký', tone: 'primary' },
    StepActivated: { label: 'Chuyển bước', tone: 'neutral' },
    Approved: { label: 'Đồng ý', tone: 'success' },
    SignatureAdded: { label: 'Đã ký', tone: 'purple' },
    Returned: { label: 'Trả lại', tone: 'danger' },
    Skipped: { label: 'Bỏ qua', tone: 'neutral' },
    Commented: { label: 'Ý kiến', tone: 'info' },
    Completed: { label: 'Hoàn tất', tone: 'success' },
    Resubmitted: { label: 'Trình lại', tone: 'primary' },
    Restarted: { label: 'Bắt đầu lại (phiên bản mới)', tone: 'orange' },
    Cancelled: { label: 'Hủy luồng', tone: 'neutral' },
  },
  signatureKind: {
    Personal: { label: 'Chữ ký cá nhân', tone: 'purple' },
    Organization: { label: 'Dấu tổ chức', tone: 'orange' },
    Initial: { label: 'Ký nháy', tone: 'indigo' },
  },
  historyChange: {
    Created: { label: 'Tạo mới', tone: 'success' },
    Updated: { label: 'Cập nhật', tone: 'primary' },
    Deleted: { label: 'Xóa', tone: 'danger' },
  },
  receiptStatus: {
    Draft: { label: 'Chưa gửi ký', tone: 'neutral' },
    Signing: { label: 'Đang ký', tone: 'primary', running: true },
    Returned: { label: 'Bị trả lại', tone: 'danger' },
    Completed: { label: 'Đã ký đủ', tone: 'success' },
    Cancelled: { label: 'Đã hủy', tone: 'neutral' },
  },
  receiptValidation: {
    Valid: { label: 'Hợp lệ', tone: 'success' },
    Warning: { label: 'Cảnh báo', tone: 'warning' },
    Invalid: { label: 'Không hợp lệ', tone: 'danger' },
  },
  receiptType: {
    Receipt: { label: 'Nhập kho', tone: 'primary' },
    Return: { label: 'Hoàn trả', tone: 'orange' },
  },
  receiptSource: {
    His: { label: 'HIS', tone: 'teal' },
    Portal: { label: 'Lập trên cổng', tone: 'indigo' },
    VirtualPortal: { label: 'Phiếu ảo (không qua kho)', tone: 'purple', dashed: true },
  },
  integrationStatus: {
    Success: { label: 'Thành công', tone: 'success' },
    Failed: { label: 'Lỗi', tone: 'danger' },
    Duplicate: { label: 'Gửi trùng', tone: 'neutral' },
    Rejected: { label: 'Bị từ chối', tone: 'orange' },
  },
  paymentRecordStatus: {
    Draft: { label: 'Bản nháp', tone: 'neutral' },
    Signing: { label: 'Đang trình ký', tone: 'primary', running: true },
    Returned: { label: 'Bị trả lại', tone: 'danger' },
    Completed: { label: 'Đã ký xong', tone: 'success' },
    Cancelled: { label: 'Đã hủy', tone: 'neutral' },
  },
  paymentStatus: {
    Unpaid: { label: 'Chưa thanh toán đủ', tone: 'orange' },
    Paid: { label: 'Đã thanh toán', tone: 'success' },
  },
  integrationDirection: {
    Inbound: { label: 'HIS → Cổng', tone: 'teal' },
    Outbound: { label: 'Cổng → HIS', tone: 'indigo' },
  },
};

export type StatusDomain = keyof typeof STATUS;

export function statusLabel(domain: StatusDomain, value: string | boolean | null | undefined): string {
  if (value === null || value === undefined) return '';
  return STATUS[domain]?.[String(value)]?.label ?? String(value);
}

export function statusOptions(domain: StatusDomain) {
  return Object.entries(STATUS[domain] ?? {}).map(([value, s]) => ({ value, label: s.label }));
}

/** Classification tags (type, kind, source…) carry no status dot. */
const CATEGORY_DOMAINS = new Set<string>([
  'procurementType',
  'contractKind',
  'appendixType',
  'workflowAction',
  'workflowEvent',
  'signatureKind',
  'itemSource',
  'receiptType',
  'receiptSource',
  'departmentType',
  'warehouseType',
  'templateKind',
  'documentType',
  'assigneeType',
  'historyChange',
  'revisionKind',
  'userType',
  'integrationDirection',
]);

export function statusTone(domain: StatusDomain, value: string | boolean | null | undefined): Tone {
  if (value === null || value === undefined) return 'neutral';
  return STATUS[domain]?.[String(value)]?.tone ?? 'neutral';
}

export function StatusTag({
  domain,
  value,
  suffix,
}: {
  domain: StatusDomain;
  value: string | boolean | null | undefined;
  /** Appended to the label, e.g. " 3/5" → "Đang ký 3/5". */
  suffix?: string;
}) {
  if (value === null || value === undefined) return null;
  const s = STATUS[domain]?.[String(value)];
  const tone = s?.tone ?? 'neutral';
  const dot = !CATEGORY_DOMAINS.has(domain);
  const className = [
    'status-tag',
    `status-tag--${tone}`,
    s?.running && 'status-tag--running',
    s?.dashed && 'status-tag--dashed',
  ]
    .filter(Boolean)
    .join(' ');
  const style = {
    '--t-fg': `var(--bp-${tone}-fg)`,
    '--t-bg': `var(--bp-${tone}-bg)`,
    '--t-bd': `var(--bp-${tone}-bd)`,
    '--t-solid': `var(--bp-${tone}-solid, var(--bp-${tone}-fg))`,
  } as React.CSSProperties;
  return (
    <span className={className} style={style}>
      {dot && <span className="status-tag__dot" aria-hidden />}
      {s?.label ?? String(value)}
      {suffix}
    </span>
  );
}
