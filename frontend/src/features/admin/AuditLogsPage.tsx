import { DatePicker, Descriptions, Select, Table, Tag, Tooltip, Typography } from 'antd';
import dayjs from 'dayjs';
import { resources, useGet, useLookup, usePagedList, type AuditLogDto } from '../../api/admin';
import { DataTable } from '../../components/DataTable';
import { ExportExcelButton, PageHeader } from '../../components/common';
import { useTableUrlState } from '../../hooks/useUrlState';
import { formatDateTime } from '../../utils/format';
import { fieldLabel, formatAuditValue, isTechnicalField } from '../../utils/auditDetails';

const FILTERS = ['userId', 'action', 'entityType', 'entityId', 'from', 'to'] as const;

type AuditOption = { value: string; label: string };

/** Tag colour by the kind of action (the Vietnamese name comes from the API). */
function actionColor(action: string) {
  if (/Cancel|Delete|Remove|Locked|Reuse|Failed|Return/.test(action)) return 'red';
  if (/Create|Register|Install|Enabled|Effective|Completed|Sign|Applied/.test(action)) return 'green';
  if (/Login|Logout|View|Download/.test(action)) return 'default';
  return 'blue';
}

function parse(json?: string | null): Record<string, unknown> {
  if (!json) return {};
  try {
    return JSON.parse(json) as Record<string, unknown>;
  } catch {
    return {};
  }
}

/** Before/after of an audit entry in words: Vietnamese field names, formatted values, technical fields hidden. */
function Diff({ log }: { log: AuditLogDto }) {
  const before = parse(log.oldValues);
  const after = parse(log.newValues);
  const keys = [...new Set([...Object.keys(before), ...Object.keys(after)])].filter(
    (k) => !isTechnicalField(k),
  );
  if (keys.length === 0) return <Typography.Text type="secondary">Không có dữ liệu chi tiết</Typography.Text>;
  return (
    <Table
      size="small"
      pagination={false}
      rowKey="key"
      dataSource={keys.map((k) => ({ key: k, before: before[k], after: after[k] }))}
      columns={[
        { title: 'Thông tin', dataIndex: 'key', width: 220, render: (k: string) => fieldLabel(k) },
        {
          title: 'Trước',
          dataIndex: 'before',
          render: (v: unknown, r) =>
            v === undefined ? (
              ''
            ) : (
              <Typography.Text delete={log.action === 'Update'}>
                {formatAuditValue(log.entityType, r.key, v)}
              </Typography.Text>
            ),
        },
        {
          title: 'Sau',
          dataIndex: 'after',
          render: (v: unknown, r) => (v === undefined ? '' : formatAuditValue(log.entityType, r.key, v)),
        },
      ]}
    />
  );
}

export function AuditLogsPage() {
  const { state, update, apiParams } = useTableUrlState(FILTERS, 50);
  const list = usePagedList<AuditLogDto>(resources.auditLogs, apiParams);
  const entityTypes = useGet<AuditOption[]>(`${resources.auditLogs}/entity-types`, [
    resources.auditLogs,
    'types',
  ]);
  const actions = useGet<AuditOption[]>(`${resources.auditLogs}/actions`, [resources.auditLogs, 'actions']);
  const users = useLookup('internal-users');

  return (
    <>
      <PageHeader
        title="Nhật ký hoạt động"
        subtitle="Nhật ký chỉ được ghi thêm — không ai sửa hay xóa được, kể cả quản trị viên"
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
              showSearch
              optionFilterProp="label"
              options={actions.data ?? []}
              onChange={(v) => update({ filters: { action: v } })}
            />
            <Select
              allowClear
              placeholder="Đối tượng"
              style={{ width: 170 }}
              value={state.filters.entityType}
              options={entityTypes.data ?? []}
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
            dataIndex: 'userFullName',
            width: 180,
            render: (v: string | null | undefined, log) =>
              v ? (
                <Tooltip title={log.userName ? `Tên đăng nhập: ${log.userName}` : undefined}>{v}</Tooltip>
              ) : (
                <Typography.Text type="secondary">Hệ thống</Typography.Text>
              ),
          },
          {
            title: 'Hành động',
            dataIndex: 'actionLabel',
            width: 220,
            render: (v: string, log) => <Tag color={actionColor(log.action)}>{v}</Tag>,
          },
          { title: 'Đối tượng', dataIndex: 'entityLabel', width: 170 },
          { title: 'Mã đối tượng', dataIndex: 'entityId', ellipsis: true },
          { title: 'Địa chỉ IP', dataIndex: 'ipAddress', width: 130 },
        ]}
      />
    </>
  );
}
