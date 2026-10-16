import { DatePicker, Descriptions, Select, Table, Tag, Typography } from 'antd';
import dayjs from 'dayjs';
import { resources, useGet, useLookup, usePagedList, type AuditLogDto } from '../../api/admin';
import { DataTable } from '../../components/DataTable';
import { ExportExcelButton, PageHeader } from '../../components/common';
import { useTableUrlState } from '../../hooks/useUrlState';
import { formatDateTime } from '../../utils/format';

const FILTERS = ['userId', 'action', 'entityType', 'entityId', 'from', 'to'] as const;

const ACTION_LABELS: Record<string, { label: string; color: string }> = {
  Create: { label: 'Tạo mới', color: 'green' },
  Update: { label: 'Cập nhật', color: 'blue' },
  Delete: { label: 'Xóa', color: 'red' },
  Login: { label: 'Đăng nhập', color: 'cyan' },
  LoginFailed: { label: 'Đăng nhập lỗi', color: 'orange' },
  Logout: { label: 'Đăng xuất', color: 'default' },
  LockedOut: { label: 'Khóa tài khoản', color: 'red' },
  PasswordChanged: { label: 'Đổi mật khẩu', color: 'purple' },
  PasswordReset: { label: 'Đặt lại mật khẩu', color: 'purple' },
  TwoFactorEnabled: { label: 'Bật 2FA', color: 'green' },
  TwoFactorDisabled: { label: 'Tắt 2FA', color: 'orange' },
  RefreshTokenReuse: { label: 'Phát hiện dùng lại phiên', color: 'red' },
};

function parse(json?: string | null): Record<string, unknown> {
  if (!json) return {};
  try {
    return JSON.parse(json) as Record<string, unknown>;
  } catch {
    return {};
  }
}

/** Before/after diff of an audit entry. */
function Diff({ log }: { log: AuditLogDto }) {
  const before = parse(log.oldValues);
  const after = parse(log.newValues);
  const keys = [...new Set([...Object.keys(before), ...Object.keys(after)])];
  if (keys.length === 0) return <Typography.Text type="secondary">Không có dữ liệu chi tiết</Typography.Text>;
  return (
    <Table
      size="small"
      pagination={false}
      rowKey="key"
      dataSource={keys.map((k) => ({ key: k, before: before[k], after: after[k] }))}
      columns={[
        { title: 'Trường', dataIndex: 'key', width: 220 },
        {
          title: 'Trước',
          dataIndex: 'before',
          render: (v: unknown) =>
            v === undefined ? (
              ''
            ) : (
              <Typography.Text delete={log.action === 'Update'}>{JSON.stringify(v)}</Typography.Text>
            ),
        },
        {
          title: 'Sau',
          dataIndex: 'after',
          render: (v: unknown) => (v === undefined ? '' : JSON.stringify(v)),
        },
      ]}
    />
  );
}

export function AuditLogsPage() {
  const { state, update, apiParams } = useTableUrlState(FILTERS, 50);
  const list = usePagedList<AuditLogDto>(resources.auditLogs, apiParams);
  const entityTypes = useGet<string[]>(`${resources.auditLogs}/entity-types`, [resources.auditLogs, 'types']);
  const users = useLookup('internal-users');

  return (
    <>
      <PageHeader
        title="Nhật ký hoạt động"
        subtitle="Bảng ghi chỉ thêm (append-only) — không ai sửa/xóa được, kể cả quản trị viên"
      />
      <DataTable<AuditLogDto>
        data={list.data}
        loading={list.isFetching}
        state={state}
        onChange={update}
        searchPlaceholder="Tìm theo người dùng, mã đối tượng, ghi chú"
        filters={
          <>
            <Select
              allowClear
              showSearch
              optionFilterProp="label"
              placeholder="Người dùng"
              style={{ width: 200 }}
              value={state.filters.userId}
              options={(users.data ?? []).map((u) => ({ value: u.id, label: u.name }))}
              onChange={(v) => update({ filters: { userId: v } })}
            />
            <Select
              allowClear
              placeholder="Hành động"
              style={{ width: 170 }}
              value={state.filters.action}
              options={Object.entries(ACTION_LABELS).map(([value, a]) => ({ value, label: a.label }))}
              onChange={(v) => update({ filters: { action: v } })}
            />
            <Select
              allowClear
              placeholder="Đối tượng"
              style={{ width: 170 }}
              value={state.filters.entityType}
              options={(entityTypes.data ?? []).map((t) => ({ value: t, label: t }))}
              onChange={(v) => update({ filters: { entityType: v } })}
            />
            <DatePicker.RangePicker
              format="DD/MM/YYYY"
              value={
                state.filters.from && state.filters.to
                  ? [dayjs(state.filters.from), dayjs(state.filters.to).subtract(1, 'day')]
                  : null
              }
              onChange={(range) =>
                update({
                  filters: {
                    from: range?.[0]?.startOf('day').toISOString(),
                    to: range?.[1]?.add(1, 'day').startOf('day').toISOString(),
                  },
                })
              }
            />
          </>
        }
        actions={
          <ExportExcelButton
            url={`${resources.auditLogs}/export`}
            params={apiParams}
            fileName="nhat-ky-hoat-dong.xlsx"
          />
        }
        expandable={{
          expandedRowRender: (log) => (
            <>
              <Descriptions size="small" column={{ xs: 1, md: 3 }} style={{ marginBottom: 8 }}>
                <Descriptions.Item label="Mã yêu cầu">{log.correlationId}</Descriptions.Item>
                <Descriptions.Item label="Trình duyệt">{log.userAgent}</Descriptions.Item>
                <Descriptions.Item label="Ghi chú">{log.note}</Descriptions.Item>
              </Descriptions>
              <Diff log={log} />
            </>
          ),
        }}
        columns={[
          {
            title: 'Thời gian',
            dataIndex: 'occurredAt',
            width: 160,
            render: (v: string) => formatDateTime(v),
          },
          {
            title: 'Người dùng',
            dataIndex: 'userName',
            width: 140,
            render: (v?: string) => v ?? <Typography.Text type="secondary">Hệ thống</Typography.Text>,
          },
          {
            title: 'Hành động',
            dataIndex: 'action',
            width: 170,
            render: (v: string) => <Tag color={ACTION_LABELS[v]?.color}>{ACTION_LABELS[v]?.label ?? v}</Tag>,
          },
          { title: 'Đối tượng', dataIndex: 'entityType', width: 150 },
          { title: 'Mã đối tượng', dataIndex: 'entityId', ellipsis: true },
          { title: 'IP', dataIndex: 'ipAddress', width: 130 },
        ]}
      />
    </>
  );
}
