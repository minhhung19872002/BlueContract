import { App, Button, Typography, Space, Drawer, Timeline, Empty, Descriptions } from 'antd';
import { DownloadOutlined, HistoryOutlined } from '@ant-design/icons';
import { useState, type ReactNode } from 'react';
import { downloadFile, toApiProblem, type ApiProblem } from '../api/http';
import { useGet, type HistoryEntryDto } from '../api/admin';
import { formatDateTime } from '../utils/format';
import { StatusTag } from './StatusTag';

export function PageHeader({
  title,
  subtitle,
  extra,
}: {
  title: string;
  subtitle?: ReactNode;
  extra?: ReactNode;
}) {
  return (
    <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 16 }} wrap>
      <div>
        <Typography.Title level={3} style={{ margin: 0 }}>
          {title}
        </Typography.Title>
        {subtitle && <Typography.Text type="secondary">{subtitle}</Typography.Text>}
      </div>
      {extra}
    </Space>
  );
}

/** Downloads the current list (with the same filters) as .xlsx (spec §9.3). */
export function ExportExcelButton({
  url,
  params,
  fileName,
}: {
  url: string;
  params?: object;
  fileName: string;
}) {
  const [loading, setLoading] = useState(false);
  const { message } = App.useApp();
  return (
    <Button
      icon={<DownloadOutlined />}
      loading={loading}
      onClick={async () => {
        setLoading(true);
        try {
          await downloadFile(url, fileName, { params: { ...params, page: undefined, pageSize: undefined } });
        } catch (e) {
          message.error(toApiProblem(e).title);
        } finally {
          setLoading(false);
        }
      }}
    >
      Xuất Excel
    </Button>
  );
}

/** Shows an API error as a toast; returns field errors for forms. */
export function useApiError() {
  const { message } = App.useApp();
  return (e: unknown): ApiProblem => {
    const problem = toApiProblem(e);
    const first = problem.errors ? Object.values(problem.errors).flat()[0] : undefined;
    message.error(first ?? problem.title);
    return problem;
  };
}

const FIELD_LABELS: Record<string, string> = {
  Code: 'Mã',
  Name: 'Tên',
  Type: 'Loại',
  FacilityId: 'Cơ sở',
  HisDepartmentCode: 'Mã HIS',
  IsActive: 'Hoạt động',
  IsDeleted: 'Đã xóa',
  TaxCode: 'Mã số thuế',
  ShortName: 'Tên viết tắt',
  Address: 'Địa chỉ',
  RepresentativeName: 'Người đại diện',
  RepresentativeTitle: 'Chức vụ',
  BankAccountNumber: 'Số tài khoản',
  BankName: 'Ngân hàng',
  NotificationEmail: 'Email nhận thông báo',
  Phone: 'Điện thoại',
  UserName: 'Tên đăng nhập',
  FullName: 'Họ tên',
  Email: 'Email',
  JobTitle: 'Chức danh',
  DepartmentId: 'Khoa/phòng',
  Status: 'Trạng thái',
  TwoFactorEnabled: 'Xác thực 2 lớp',
  LastLoginAt: 'Đăng nhập gần nhất',
};

const HIDDEN_FIELDS = new Set([
  'Id',
  'CreatedAt',
  'CreatedBy',
  'UpdatedAt',
  'UpdatedBy',
  'FailedLoginCount',
  'LockoutEnd',
  'MustChangePassword',
]);

/** "Lịch sử cập nhật" drawer for master data (departments, suppliers, users). */
export function HistoryButton({ url, title }: { url: string; title: string }) {
  const [open, setOpen] = useState(false);
  const { data, isLoading } = useGet<HistoryEntryDto[]>(open ? url : null);
  return (
    <>
      <Button size="small" icon={<HistoryOutlined />} onClick={() => setOpen(true)}>
        Lịch sử
      </Button>
      <Drawer
        title={`Lịch sử cập nhật — ${title}`}
        open={open}
        onClose={() => setOpen(false)}
        width={560}
        loading={isLoading}
      >
        {data && data.length === 0 && <Empty description="Chưa có lịch sử" />}
        <Timeline
          items={(data ?? []).map((h, i) => {
            const snapshot = JSON.parse(h.snapshot) as Record<string, unknown>;
            const previous = data?.[i + 1]
              ? (JSON.parse(data[i + 1]!.snapshot) as Record<string, unknown>)
              : undefined;
            const changed = Object.keys(snapshot).filter(
              (k) =>
                !HIDDEN_FIELDS.has(k) &&
                (!previous || JSON.stringify(previous[k]) !== JSON.stringify(snapshot[k])),
            );
            return {
              key: h.version,
              children: (
                <Space direction="vertical" size={4} style={{ width: '100%' }}>
                  <Space wrap>
                    <Typography.Text strong>Phiên bản {h.version}</Typography.Text>
                    <StatusTag domain="historyChange" value={h.changeType} />
                    <Typography.Text type="secondary">
                      {formatDateTime(h.changedAt)} · {h.changedByName}
                    </Typography.Text>
                  </Space>
                  <Descriptions size="small" column={1} bordered>
                    {changed.map((k) => (
                      <Descriptions.Item key={k} label={FIELD_LABELS[k] ?? k}>
                        {previous && (
                          <Typography.Text delete type="secondary">
                            {String(previous[k] ?? '—')}{' '}
                          </Typography.Text>
                        )}
                        {String(snapshot[k] ?? '—')}
                      </Descriptions.Item>
                    ))}
                  </Descriptions>
                </Space>
              ),
            };
          })}
        />
      </Drawer>
    </>
  );
}
