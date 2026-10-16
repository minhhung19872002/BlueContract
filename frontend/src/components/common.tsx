import { App, Button, Typography, Drawer, Empty } from 'antd';
import { DownloadOutlined, HistoryOutlined } from '@ant-design/icons';
import { useState, type ReactNode } from 'react';
import { ArrowRight } from 'lucide-react';
import { downloadFile, toApiProblem, type ApiProblem } from '../api/http';
import { useGet, type HistoryEntryDto } from '../api/admin';
import { formatDateTime } from '../utils/format';
import { StatusTag, statusTone } from './StatusTag';
import { fieldLabel, isTechnicalField } from '../utils/auditDetails';

/** Page title 24/600, one-line description, actions on the right (design/Page List.dc.html). */
export function PageHeader({
  title,
  subtitle,
  extra,
}: {
  title: ReactNode;
  subtitle?: ReactNode;
  extra?: ReactNode;
}) {
  return (
    <div className="page-header">
      <div className="page-header__text">
        <Typography.Title level={1} className="page-header__title">
          {title}
        </Typography.Title>
        {subtitle && <p className="page-header__subtitle">{subtitle}</p>}
      </div>
      {extra && <div className="page-header__actions">{extra}</div>}
    </div>
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
  FacilityIds: 'Cơ sở thực hiện',
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

/** Readable value of a snapshot field (booleans in Vietnamese, empty as a dash). */
const display = (v: unknown) =>
  v === true ? 'Có' : v === false ? 'Không' : v === null || v === undefined || v === '' ? '—' : String(v);

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
        <div className="history-timeline">
          {(data ?? []).map((h, i) => {
            const snapshot = JSON.parse(h.snapshot) as Record<string, unknown>;
            const previous = data?.[i + 1]
              ? (JSON.parse(data[i + 1]!.snapshot) as Record<string, unknown>)
              : undefined;
            const changed = Object.keys(snapshot).filter(
              (k) =>
                !HIDDEN_FIELDS.has(k) &&
                !isTechnicalField(k) &&
                (!previous || JSON.stringify(previous[k]) !== JSON.stringify(snapshot[k])),
            );
            const tone = statusTone('historyChange', h.changeType);
            return (
              <div key={h.version} className="history-timeline__item">
                <span
                  className="history-timeline__dot"
                  style={{
                    background: `var(--bp-${tone}-solid)`,
                    boxShadow: `0 0 0 3px var(--bp-${tone}-bg)`,
                  }}
                  aria-hidden
                />
                <div className="history-timeline__body">
                  <div className="history-timeline__head">
                    <span className="history-timeline__version">Phiên bản {h.version}</span>
                    <StatusTag domain="historyChange" value={h.changeType} />
                    <span className="history-timeline__meta">
                      {formatDateTime(h.changedAt)} · {h.changedByName}
                    </span>
                  </div>
                  {changed.length > 0 && (
                    <div className="history-diff">
                      {changed.map((k) => (
                        <div key={k} className="history-diff__row">
                          <span className="history-diff__key">{FIELD_LABELS[k] ?? fieldLabel(k)}</span>
                          <span className="history-diff__value">
                            {previous && (
                              <>
                                <span className="history-diff__old">{display(previous[k])}</span>
                                <ArrowRight size={13} aria-hidden />
                              </>
                            )}
                            <span className="history-diff__new">{display(snapshot[k])}</span>
                          </span>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      </Drawer>
    </>
  );
}
