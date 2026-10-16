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
