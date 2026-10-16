import { Tag } from 'antd';

/**
 * The single place where status values get their Vietnamese label and colour (spec §9.3).
 * Add new domains (contract, proposal, receipt...) here as they are introduced.
 */
const STATUS: Record<string, Record<string, { label: string; color: string }>> = {
  active: {
    true: { label: 'Đang hoạt động', color: 'green' },
    false: { label: 'Ngừng hoạt động', color: 'default' },
  },
  userStatus: {
    Active: { label: 'Hoạt động', color: 'green' },
    Inactive: { label: 'Ngừng hoạt động', color: 'default' },
    PendingActivation: { label: 'Chờ kích hoạt', color: 'gold' },
  },
  userType: {
    Internal: { label: 'Nội bộ', color: 'blue' },
    Supplier: { label: 'Nhà cung cấp', color: 'purple' },
  },
  departmentType: {
    Clinical: { label: 'Lâm sàng', color: 'cyan' },
    Paraclinical: { label: 'Cận lâm sàng', color: 'geekblue' },
    Functional: { label: 'Phòng chức năng', color: 'default' },
  },
  warehouseType: {
    Drug: { label: 'Kho thuốc', color: 'blue' },
    MedicalSupply: { label: 'Kho vật tư y tế', color: 'cyan' },
    Chemical: { label: 'Kho hóa chất', color: 'orange' },
    Asset: { label: 'Kho tài sản', color: 'purple' },
    General: { label: 'Kho chung', color: 'default' },
  },
  delegation: {
    effective: { label: 'Đang hiệu lực', color: 'green' },
    upcoming: { label: 'Chưa đến hạn', color: 'blue' },
    expired: { label: 'Hết hạn', color: 'default' },
    revoked: { label: 'Đã thu hồi', color: 'red' },
  },
  templateKind: {
    Contract: { label: 'Hợp đồng', color: 'blue' },
    Appendix: { label: 'Phụ lục', color: 'geekblue' },
    Proposal: { label: 'Phiếu đề xuất', color: 'cyan' },
    ProposalSummary: { label: 'Tổng hợp đề xuất', color: 'cyan' },
    CallOrder: { label: 'Đơn gọi hàng', color: 'purple' },
    WarehouseReceipt: { label: 'Phiếu nhập kho', color: 'orange' },
    Acceptance: { label: 'BB nghiệm thu', color: 'green' },
    Valuation: { label: 'BB xác định giá trị (08a)', color: 'green' },
    PaymentRequest: { label: 'Đề nghị thanh toán', color: 'gold' },
    Liquidation: { label: 'BB thanh lý', color: 'volcano' },
  },
  documentStatus: {
    Draft: { label: 'Bản nháp', color: 'default' },
    InWorkflow: { label: 'Đang trình ký', color: 'processing' },
    Completed: { label: 'Hoàn thành', color: 'green' },
    Cancelled: { label: 'Đã hủy', color: 'red' },
  },
  documentType: {
    Contract: { label: 'Hợp đồng', color: 'blue' },
    Appendix: { label: 'Phụ lục hợp đồng', color: 'geekblue' },
    Proposal: { label: 'Phiếu đề xuất', color: 'cyan' },
    ProposalSummary: { label: 'Tổng hợp đề xuất', color: 'cyan' },
    CallOrder: { label: 'Đơn gọi hàng', color: 'purple' },
    WarehouseReceipt: { label: 'Phiếu nhập kho', color: 'orange' },
    Acceptance: { label: 'Biên bản nghiệm thu', color: 'green' },
    Valuation: { label: 'Biên bản XĐGT (08a)', color: 'green' },
    PaymentRequest: { label: 'Đề nghị thanh toán', color: 'gold' },
    Liquidation: { label: 'Biên bản thanh lý', color: 'volcano' },
    TemplateSample: { label: 'Tài liệu thử từ mẫu', color: 'default' },
  },
  bidStatus: {
    Draft: { label: 'Đang nhập liệu', color: 'default' },
    Active: { label: 'Đã ban hành', color: 'green' },
    Closed: { label: 'Đã đóng', color: 'red' },
  },
  itemSource: {
    His: { label: 'Đồng bộ HIS', color: 'blue' },
    Import: { label: 'Import Excel', color: 'purple' },
    Manual: { label: 'Nhập tay', color: 'default' },
  },
  contractStatus: {
    Draft: { label: 'Bản nháp', color: 'default' },
    InReview: { label: 'Tổ rà soát kiểm tra', color: 'processing' },
    SupplierReview: { label: 'NCC rà soát', color: 'gold' },
    AccountantReview: { label: 'Kế toán xem thay đổi', color: 'orange' },
    SupplierSigning: { label: 'NCC ký số', color: 'purple' },
    AccountantInitialing: { label: 'Kế toán ký nháy', color: 'geekblue' },
    DirectorSigning: { label: 'Giám đốc ký', color: 'purple' },
    Stamping: { label: 'Văn thư đóng dấu', color: 'volcano' },
    Effective: { label: 'Đang hiệu lực', color: 'green' },
    Liquidated: { label: 'Đã thanh lý', color: 'cyan' },
    Cancelled: { label: 'Đã hủy', color: 'default' },
    Returned: { label: 'Bị trả lại', color: 'red' },
  },
  procurementType: {
    Bidding: { label: 'Trong thầu', color: 'blue' },
    NonBidding: { label: 'Ngoài thầu', color: 'orange' },
  },
  contractKind: {
    UnitPriceCallOff: { label: 'Theo đơn giá, gọi hàng từng đợt', color: 'cyan' },
    LumpSum: { label: 'Trọn gói', color: 'geekblue' },
  },
  appendixType: {
    AddQuantity: { label: 'Bổ sung số lượng', color: 'blue' },
    PriceReduction: { label: 'Giảm giá', color: 'green' },
    InfoChange: { label: 'Thay đổi thông tin', color: 'gold' },
    Extension: { label: 'Gia hạn', color: 'purple' },
  },
  appendixStatus: {
    Draft: { label: 'Bản nháp', color: 'default' },
    InWorkflow: { label: 'Đang trình ký', color: 'processing' },
    Effective: { label: 'Đã hiệu lực', color: 'green' },
    Cancelled: { label: 'Đã hủy', color: 'default' },
  },
  revisionKind: {
    Insertion: { label: 'Thêm', color: 'green' },
    Deletion: { label: 'Xóa', color: 'red' },
    Formatting: { label: 'Định dạng', color: 'default' },
  },
  proposalStatus: {
    Draft: { label: 'Bản nháp', color: 'default' },
    Signing: { label: 'Đang ký', color: 'processing' },
    Submitted: { label: 'Đã gửi — chờ tổng hợp', color: 'blue' },
    Aggregated: { label: 'Đã tổng hợp', color: 'green' },
    Returned: { label: 'Bị trả lại', color: 'red' },
    Cancelled: { label: 'Đã hủy', color: 'default' },
  },
  summaryStatus: {
    Draft: { label: 'Bản nháp', color: 'default' },
    AggregatorSigning: { label: 'Người tổng hợp ký', color: 'processing' },
    DomainHeadReview: { label: 'Tổ trưởng duyệt', color: 'gold' },
    DirectorSigning: { label: 'Giám đốc ký', color: 'purple' },
    Approved: { label: 'Đã duyệt', color: 'green' },
    Returned: { label: 'Bị trả lại', color: 'red' },
    Cancelled: { label: 'Đã hủy', color: 'default' },
  },
  callOrderStatus: {
    Sent: { label: 'Chờ NCC xác nhận', color: 'gold' },
    SupplierConfirmed: { label: 'NCC đã xác nhận', color: 'green' },
    PartiallyConfirmed: { label: 'Xác nhận một phần', color: 'orange' },
    Rejected: { label: 'NCC từ chối', color: 'red' },
    Delivered: { label: 'Đã giao hàng', color: 'cyan' },
    Cancelled: { label: 'Đã hủy', color: 'default' },
  },
  workflowStatus: {
    Running: { label: 'Đang trình ký', color: 'processing' },
    Completed: { label: 'Đã ký xong', color: 'green' },
    Returned: { label: 'Bị trả lại', color: 'red' },
    Cancelled: { label: 'Đã hủy', color: 'default' },
  },
  stepStatus: {
    Pending: { label: 'Chờ đến lượt', color: 'default' },
    Active: { label: 'Đang xử lý', color: 'processing' },
    Done: { label: 'Đã xử lý', color: 'green' },
    Skipped: { label: 'Bỏ qua', color: 'default' },
  },
  workflowAction: {
    Review: { label: 'Kiểm tra', color: 'blue' },
    Approve: { label: 'Duyệt', color: 'cyan' },
    Sign: { label: 'Ký số', color: 'purple' },
    Initial: { label: 'Ký nháy', color: 'geekblue' },
    OrgSeal: { label: 'Đóng dấu điện tử', color: 'volcano' },
    SupplierReview: { label: 'NCC rà soát', color: 'gold' },
    SupplierSign: { label: 'NCC ký số', color: 'orange' },
    Confirm: { label: 'Xác nhận', color: 'green' },
  },
  assigneeType: {
    Role: { label: 'Theo vai trò', color: 'blue' },
    User: { label: 'Người cụ thể', color: 'purple' },
    DocumentSupplier: { label: 'Nhà cung cấp của tài liệu', color: 'gold' },
    Creator: { label: 'Người lập', color: 'default' },
  },
  workflowEvent: {
    Started: { label: 'Gửi ký', color: 'blue' },
    StepActivated: { label: 'Chuyển bước', color: 'default' },
    Approved: { label: 'Đồng ý', color: 'green' },
    SignatureAdded: { label: 'Đã ký', color: 'purple' },
    Returned: { label: 'Trả lại', color: 'red' },
    Skipped: { label: 'Bỏ qua', color: 'default' },
    Commented: { label: 'Ý kiến', color: 'cyan' },
    Completed: { label: 'Hoàn tất', color: 'green' },
    Resubmitted: { label: 'Trình lại', color: 'blue' },
    Restarted: { label: 'Bắt đầu lại (phiên bản mới)', color: 'orange' },
    Cancelled: { label: 'Hủy luồng', color: 'default' },
  },
  signatureKind: {
    Personal: { label: 'Chữ ký cá nhân', color: 'purple' },
    Organization: { label: 'Dấu tổ chức', color: 'volcano' },
    Initial: { label: 'Ký nháy', color: 'geekblue' },
  },
  historyChange: {
    Created: { label: 'Tạo mới', color: 'green' },
    Updated: { label: 'Cập nhật', color: 'blue' },
    Deleted: { label: 'Xóa', color: 'red' },
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

export function StatusTag({
  domain,
  value,
}: {
  domain: StatusDomain;
  value: string | boolean | null | undefined;
}) {
  if (value === null || value === undefined) return null;
  const s = STATUS[domain]?.[String(value)];
  return <Tag color={s?.color ?? 'default'}>{s?.label ?? String(value)}</Tag>;
}
