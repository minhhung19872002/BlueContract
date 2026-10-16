import { App, Button, Card, Descriptions, Drawer, Select, Space, Typography } from 'antd';
import {
  BookOpen,
  CalendarClock,
  Copy,
  FlaskConical,
  Gauge,
  Hospital,
  KeyRound,
  Network,
  Plug,
  type LucideIcon,
} from 'lucide-react';
import { CloudDownloadOutlined, RedoOutlined } from '@ant-design/icons';
import { useState, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { usePagedList } from '../../api/admin';
import {
  INTEGRATION,
  integrationApi,
  useIntegrationConfig,
  useIntegrationLog,
  useReceiptAction,
  type IntegrationLogListItemDto,
} from '../../api/receipts';
import { DataTable } from '../../components/DataTable';
import { StatusTag, statusOptions, type Tone } from '../../components/StatusTag';
import { PageHeader, useApiError } from '../../components/common';
import { useTableUrlState } from '../../hooks/useUrlState';
import { formatDateTime } from '../../utils/format';
import './integration.css';

const FILTERS = ['direction', 'status', 'operation'] as const;

const OPERATIONS: Record<string, string> = {
  PushWarehouseReceipt: 'HIS đẩy phiếu nhập kho',
  PullWarehouseReceipts: 'Cổng kéo phiếu từ HIS',
  GetContracts: 'HIS lấy hợp đồng',
  GetProposalSummaries: 'HIS lấy tổng hợp đề xuất',
  GetItems: 'HIS lấy danh mục hàng hóa',
};

interface HisRow {
  k: string;
  v: ReactNode;
  mono?: boolean;
  copy?: boolean;
}

/** Configuration card of design/Tich hop HIS.dc.html: icon, title, optional badge, key/value rows. */
function HisCard({
  icon: Icon,
  tone,
  title,
  badge,
  rows,
}: {
  icon: LucideIcon;
  tone: Tone;
  title: string;
  badge?: { label: string; tone: Tone };
  rows: HisRow[];
}) {
  const { message } = App.useApp();
  return (
    <section className="his-card">
      <div className="his-card__head">
        <span
          className="his-card__icon"
          style={{ background: `var(--bp-${tone}-bg)`, color: `var(--bp-${tone}-fg)` }}
          aria-hidden
        >
          <Icon size={16} strokeWidth={1.75} />
        </span>
        <span className="his-card__title">{title}</span>
        {badge && (
          <span
            className="his-card__badge"
            style={{
              borderColor: `var(--bp-${badge.tone}-bd)`,
              background: `var(--bp-${badge.tone}-bg)`,
              color: `var(--bp-${badge.tone}-fg)`,
            }}
          >
            {badge.label}
          </span>
        )}
      </div>
      {rows.map((r) => (
        <div key={r.k} className="his-card__row">
          <span className="his-card__key">{r.k}</span>
          <span
            className={r.mono ? 'his-card__value mono' : 'his-card__value'}
            title={typeof r.v === 'string' ? r.v : undefined}
          >
            {r.v}
          </span>
          {r.copy && typeof r.v === 'string' && (
            <button
              type="button"
              className="his-card__copy"
              aria-label={`Sao chép ${r.k}`}
              onClick={() =>
                navigator.clipboard
                  ?.writeText(r.v as string)
                  .then(() => message.success('Đã sao chép'))
                  .catch(() => message.info(r.v as string))
              }
            >
              <Copy size={13} aria-hidden />
            </button>
          )}
        </div>
      ))}
    </section>
  );
}

const pretty = (text?: string | null) => {
  if (!text) return '—';
  try {
    return JSON.stringify(JSON.parse(text), null, 2);
  } catch {
    return text;
  }
};

/** Cấu hình tích hợp HIS + nhật ký request/response, gửi lại, kéo phiếu ngay (spec §7.2, §9.1-14). */
export function IntegrationPage() {
  const { state, update, apiParams } = useTableUrlState(FILTERS);
  const logs = usePagedList<IntegrationLogListItemDto>(`${INTEGRATION}/logs`, apiParams);
  const config = useIntegrationConfig();
  const [selected, setSelected] = useState<string | null>(null);
  const { message } = App.useApp();
  const onError = useApiError();
  const pull = useReceiptAction(() => integrationApi.pull());
  const c = config.data;

  return (
    <>
      <PageHeader
        title="Tích hợp HIS"
        subtitle="API cho HIS (API key + chữ ký HMAC-SHA256), kéo phiếu định kỳ và nhật ký tích hợp"
        extra={
          <Button
            type="primary"
            icon={<CloudDownloadOutlined />}
            loading={pull.isPending}
            onClick={() =>
              pull
                .mutateAsync()
                .then(
                  (r) =>
                    message.success(
                      `Kéo ${r.received} phiếu: ${r.created} mới, ${r.updated} cập nhật, ${r.duplicates} trùng, ${r.failed} lỗi`,
                    ),
                  onError,
                )
            }
          >
            Kéo phiếu từ HIS ngay
          </Button>
        }
      />
      {!c ? (
        <Card loading={config.isLoading} style={{ marginBottom: 16 }} />
      ) : (
        <div className="his-cards">
          <HisCard
            icon={Hospital}
            tone="teal"
            title="Hệ thống HIS"
            badge={
              c.provider === 'Mock'
                ? { label: 'Mock', tone: 'warning' }
                : { label: c.provider, tone: 'neutral' }
            }
            rows={[
              { k: 'Hệ thống', v: c.displayName },
              { k: 'Mã nhà cung cấp', v: c.provider, mono: true },
            ]}
          />
          <HisCard
            icon={Plug}
            tone="primary"
            title="API cho HIS"
            badge={
              c.inboundEnabled
                ? { label: 'Đang bật', tone: 'success' }
                : { label: 'Chưa cấu hình', tone: 'neutral' }
            }
            rows={[
              { k: 'Đường dẫn', v: `${window.location.origin}/api/integration/his`, mono: true, copy: true },
              { k: 'Xác thực', v: 'API key + HMAC-SHA256' },
              { k: 'Header', v: 'X-Api-Key, X-Timestamp, X-Signature', mono: true },
            ]}
          />
          <HisCard
            icon={KeyRound}
            tone="purple"
            title="Ứng dụng được cấp key"
            rows={
              c.clients.length
                ? c.clients.map((name, i) => ({ k: `Ứng dụng ${i + 1}`, v: name, mono: true }))
                : [{ k: 'Ứng dụng', v: 'Chưa cấp key nào' }]
            }
          />
          <HisCard
            icon={Network}
            tone="indigo"
            title="IP được phép"
            rows={
              c.allowedIps.filter(Boolean).length
                ? c.allowedIps.filter(Boolean).map((ip, i) => ({ k: `Dải ${i + 1}`, v: ip, mono: true }))
                : [{ k: 'Phạm vi', v: 'Mọi IP' }]
            }
          />
          <HisCard
            icon={Gauge}
            tone="orange"
            title="Giới hạn (rate limit)"
            rows={[
              { k: 'Mỗi phút', v: `${c.rateLimitPerMinute.toLocaleString('vi-VN')} yêu cầu / ứng dụng` },
            ]}
          />
          <HisCard
            icon={CalendarClock}
            tone="success"
            title="Kéo phiếu định kỳ"
            badge={c.pullEnabled ? { label: 'Đang bật', tone: 'success' } : { label: 'Tắt', tone: 'neutral' }}
            rows={[
              { k: 'Lịch (cron)', v: c.pullCron, mono: true },
              { k: 'Khoảng kéo', v: `${c.pullLookbackDays} ngày gần nhất` },
            ]}
          />
          <HisCard
            icon={FlaskConical}
            tone="warning"
            title="Giả lập HIS (dev)"
            badge={c.devEndpoints ? { label: 'DEV', tone: 'warning' } : { label: 'Tắt', tone: 'neutral' }}
            rows={[
              {
                k: 'Kịch bản',
                v: c.devEndpoints
                  ? 'Phiếu hợp lệ / lệch hóa đơn / gửi trùng'
                  : 'Chỉ bật ở môi trường phát triển',
              },
            ]}
          />
          <HisCard
            icon={BookOpen}
            tone="neutral"
            title="Tài liệu"
            rows={[
              { k: 'Đặc tả', v: 'docs/HIS_INTEGRATION.md', mono: true },
              { k: 'Nội dung', v: 'Endpoint, cách ký request, payload mẫu, mã lỗi' },
            ]}
          />
        </div>
      )}
      <DataTable<IntegrationLogListItemDto>
        data={logs.data}
        loading={logs.isFetching}
        state={state}
        onChange={update}
        searchPlaceholder="Tìm theo mã phiếu HIS, đường dẫn, mã lỗi"
        filters={
          <>
            <Select
              allowClear
              placeholder="Chiều"
              style={{ width: 150 }}
              value={state.filters.direction}
              options={statusOptions('integrationDirection')}
              onChange={(v) => update({ filters: { direction: v } })}
            />
            <Select
              allowClear
              placeholder="Kết quả"
              style={{ width: 150 }}
              value={state.filters.status}
              options={statusOptions('integrationStatus')}
              onChange={(v) => update({ filters: { status: v } })}
            />
            <Select
              allowClear
              placeholder="Nghiệp vụ"
              style={{ width: 230 }}
              value={state.filters.operation}
              options={Object.entries(OPERATIONS).map(([value, label]) => ({ value, label }))}
              onChange={(v) => update({ filters: { operation: v } })}
            />
          </>
        }
        columns={[
          { title: 'Thời điểm', dataIndex: 'startedAt', render: (v: string) => formatDateTime(v) },
          {
            title: 'Chiều',
            dataIndex: 'direction',
            render: (v: string) => <StatusTag domain="integrationDirection" value={v} />,
          },
          {
            title: 'Nghiệp vụ',
            dataIndex: 'operation',
            render: (v: string, r) => <a onClick={() => setSelected(r.id)}>{OPERATIONS[v] ?? v}</a>,
          },
          { title: 'Mã phiếu HIS', dataIndex: 'externalId', render: (v?: string | null) => v ?? '—' },
          {
            title: 'Kết quả',
            dataIndex: 'status',
            render: (v: string, r) => (
              <Space size={2}>
                <StatusTag domain="integrationStatus" value={v} />
                {r.responseStatus && (
                  <Typography.Text type="secondary">HTTP {r.responseStatus}</Typography.Text>
                )}
              </Space>
            ),
          },
          { title: 'Mã lỗi', dataIndex: 'errorCode', render: (v?: string | null) => v ?? '' },
          { title: 'Lần', dataIndex: 'attempts', align: 'center' as const },
          { title: 'Nguồn', key: 'client', render: (_, r) => r.clientName ?? r.remoteIp ?? '—' },
          {
            title: 'Thời gian xử lý',
            dataIndex: 'durationMs',
            align: 'right' as const,
            render: (v: number) => `${v} ms`,
          },
        ]}
      />
      <LogDrawer id={selected} onClose={() => setSelected(null)} />
    </>
  );
}

function LogDrawer({ id, onClose }: { id: string | null; onClose: () => void }) {
  const log = useIntegrationLog(id);
  const l = log.data;
  const navigate = useNavigate();
  const { message } = App.useApp();
  const onError = useApiError();
  const retry = useReceiptAction(() => integrationApi.retry(id!));

  return (
    <Drawer
      title="Chi tiết lần gọi"
      width={760}
      open={!!id}
      onClose={onClose}
      extra={
        l?.canRetry && (
          <Button
            type="primary"
            icon={<RedoOutlined />}
            loading={retry.isPending}
            onClick={() =>
              retry
                .mutateAsync()
                .then((r) => message.success(`Đã xử lý lại: phiếu ${r.receiptNumber}`), onError)
            }
          >
            Gửi lại
          </Button>
        )
      }
    >
      {l && (
        <Space direction="vertical" style={{ width: '100%' }}>
          <Descriptions size="small" column={2} bordered>
            <Descriptions.Item label="Nghiệp vụ" span={2}>
              {OPERATIONS[l.operation] ?? l.operation}
            </Descriptions.Item>
            <Descriptions.Item label="Yêu cầu" span={2}>
              <Typography.Text code>
                {l.method} {l.path}
              </Typography.Text>
            </Descriptions.Item>
            <Descriptions.Item label="Kết quả">
              <StatusTag domain="integrationStatus" value={l.status} />{' '}
              {l.responseStatus && `HTTP ${l.responseStatus}`}
            </Descriptions.Item>
            <Descriptions.Item label="Số lần xử lý">{l.attempts}</Descriptions.Item>
            <Descriptions.Item label="Bắt đầu">{formatDateTime(l.startedAt)}</Descriptions.Item>
            <Descriptions.Item label="Lần gần nhất">{formatDateTime(l.lastAttemptAt)}</Descriptions.Item>
            <Descriptions.Item label="Nguồn">
              {l.clientName ?? '—'} {l.remoteIp && `(${l.remoteIp})`}
            </Descriptions.Item>
            <Descriptions.Item label="Phiếu">
              {l.receiptId ? (
                <a onClick={() => navigate(`/nhap-kho/${l.receiptId}`)}>Mở phiếu nhập kho</a>
              ) : (
                '—'
              )}
            </Descriptions.Item>
            {l.errorCode && (
              <Descriptions.Item label="Lỗi" span={2}>
                <Typography.Text type="danger">
                  {l.errorCode}: {l.errorMessage}
                </Typography.Text>
              </Descriptions.Item>
            )}
          </Descriptions>
          <Typography.Title level={5}>Nội dung gửi (request)</Typography.Title>
          <pre className="his-payload">{pretty(l.requestBody)}</pre>
          <Typography.Title level={5}>Kết quả trả về (response)</Typography.Title>
          <pre className="his-payload">{pretty(l.responseBody)}</pre>
        </Space>
      )}
    </Drawer>
  );
}
