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
